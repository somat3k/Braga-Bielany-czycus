using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Collections;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots;

[Robot(AccessRights = AccessRights.FullAccess, AddIndicators = true)]
public class e404_mats0 : Robot
{
    private const string BotLabel = "e404_mats0_ml";
    private readonly ModelBundle _models = new();
    private readonly Queue<DateTime> _executedOrders = new();
    private DateTime _lastOrderTime = DateTime.MinValue;
    private int _lastOrderBarIndex = -1;
    private DateTime _lastGateLogTime = DateTime.MinValue;

    [Parameter("TP (pips)", DefaultValue = 1.0, MinValue = 0.0)]
    public double TakeProfitPips { get; set; }

    [Parameter("SL (pips)", DefaultValue = 12.0, MinValue = 0.0)]
    public double StopLossPips { get; set; }

    [Parameter("Min confidence [0..1]", DefaultValue = 0.01, MinValue = 0.0, MaxValue = 1.0)]
    public double MinConfidence { get; set; }

    [Parameter("Min signal [0..1]", DefaultValue = 0.01, MinValue = 0.0, MaxValue = 1.0)]
    public double MinSignal { get; set; }

    [Parameter("Signal mirroring", DefaultValue = false)]
    public bool SignalMirroring { get; set; }

    [Parameter("Max spread (pips)", DefaultValue = 25, MinValue = 0.0)]
    public double MaxSpreadPips { get; set; }

    [Parameter("Lot size", DefaultValue = 0.02, MinValue = 0.01)]
    public double LotSize { get; set; }

    [Parameter("MMulti sub div", DefaultValue = 100, MinValue = 1)]
    public int MMultiSubdivision { get; set; }

    [Parameter("Models dir path", DefaultValue = "modele")]
    public string ModelDirectoryPath { get; set; }

    [Parameter("Load pattern", DefaultValue = "*")]
    public string ModelLoadPattern { get; set; }

    [Parameter("Include sub dirs", DefaultValue = true)]
    public bool IncludeSubDirectories { get; set; }

    [Parameter("Use closed-bar signal", DefaultValue = true)]
    public bool UseClosedBarSignal { get; set; }

    [Parameter("Min candle body (pips)", DefaultValue = 1.0, MinValue = 0.0)]
    public double MinCandleBodyPips { get; set; }

    [Parameter("Min seconds between trades", DefaultValue = 30, MinValue = 0)]
    public int MinSecondsBetweenTrades { get; set; }

    [Parameter("Min bars between trades", DefaultValue = 1, MinValue = 0)]
    public int MinBarsBetweenTrades { get; set; }

    [Parameter("Max orders / hour", DefaultValue = 6, MinValue = 0)]
    public int MaxOrdersPerHour { get; set; }

    [Parameter("Allow immediate flip", DefaultValue = false)]
    public bool AllowImmediateFlip { get; set; }

    [Parameter("Flip signal factor", DefaultValue = 1.5, MinValue = 1.0)]
    public double FlipSignalFactor { get; set; }

    [Parameter("Debug gate logs", DefaultValue = false)]
    public bool DebugGateLogs { get; set; }

    protected override void OnStart()
    {
        _executedOrders.Clear();
        _lastOrderTime = DateTime.MinValue;
        _lastOrderBarIndex = -1;

        ValidateRuntimeParameters();
        LoadModelBundle();
    }

    protected override void OnTick()
    {
        if (Bars.Count < 3)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_models.PrimaryOnnxTradingModel))
        {
            return;
        }

        var spreadPips = (Symbol.Ask - Symbol.Bid) / Symbol.PipSize;
        if (spreadPips > MaxSpreadPips)
        {
            RejectTrade($"spread {spreadPips:F2} > max {MaxSpreadPips:F2}");
            return;
        }

        var inference = InferSignalFromOnnx(_models.PrimaryOnnxTradingModel);

        if (SignalMirroring)
        {
            inference = inference with { Signal = -inference.Signal };
        }

        if (inference.Confidence < MinConfidence || Math.Abs(inference.Signal) < MinSignal)
        {
            RejectTrade(
                $"signal/conf gate failed: signal={inference.Signal:F3}, confidence={inference.Confidence:F3}, body={inference.BodyPips:F2}p");
            return;
        }

        var tradeType = inference.Signal > 0 ? TradeType.Buy : TradeType.Sell;

        if (!CanExecuteSignal(tradeType, inference))
        {
            return;
        }

        ExecuteSignal(tradeType, inference);
    }

    protected override void OnStop()
    {
        Print("Stopped.");
    }

    private void ValidateRuntimeParameters()
    {
        if (MinConfidence < 0.05)
        {
            Print($"Warning: MinConfidence={MinConfidence:F2} is very low and may allow noisy entries.");
        }

        if (MinSignal < 0.05)
        {
            Print($"Warning: MinSignal={MinSignal:F2} is very low and may allow frequent flips.");
        }

        if (MaxSpreadPips > 10)
        {
            Print($"Warning: MaxSpreadPips={MaxSpreadPips:F2} is high. Consider a lower spread gate.");
        }

        if (TakeProfitPips > 0 && StopLossPips > 0 && TakeProfitPips < StopLossPips * 0.25)
        {
            Print(
                $"Warning: TP/SL ratio is low (TP={TakeProfitPips:F1}, SL={StopLossPips:F1}). Quick SL churn can increase.");
        }
    }

    private void LoadModelBundle()
    {
        var modelRoot = ResolveModelDirectory(ModelDirectoryPath);
        if (string.IsNullOrWhiteSpace(modelRoot) || !Directory.Exists(modelRoot))
        {
            Print($"Model directory not found: '{ModelDirectoryPath}'.");
            return;
        }

        var searchPattern = string.IsNullOrWhiteSpace(ModelLoadPattern) ? "*" : ModelLoadPattern;
        var searchOption = IncludeSubDirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        _models.Reset();

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(modelRoot, searchPattern, searchOption);
        }
        catch (Exception ex)
        {
            Print($"Failed to scan model directory '{modelRoot}': {ex.Message}");
            return;
        }

        foreach (var filePath in files)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            switch (extension)
            {
                case ".onnx":
                    _models.OnnxTradingModels.Add(filePath);
                    break;

                case ".h5":
                case ".keras":
                case ".pb":
                case ".ckpt":
                    _models.DecisionModels.Add(filePath);
                    break;

                case ".safetensors":
                    _models.ReplayModels.Add(filePath);
                    break;
            }
        }

        _models.PrimaryOnnxTradingModel = _models.OnnxTradingModels.FirstOrDefault() ?? string.Empty;

        Print($"Model root: {modelRoot}");
        Print($"ONNX trading models: {_models.OnnxTradingModels.Count}");
        Print($"H5/Keras/TensorFlow decision models: {_models.DecisionModels.Count}");
        Print($"Safetensors replay models: {_models.ReplayModels.Count}");

        if (string.IsNullOrWhiteSpace(_models.PrimaryOnnxTradingModel))
        {
            Print("No ONNX model found. Trading is disabled until one is available.");
            return;
        }

        Print($"Primary ONNX trading model: {_models.PrimaryOnnxTradingModel}");
    }

    private string ResolveModelDirectory(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
        {
            return configuredPath;
        }

        var baseCandidates = new[]
        {
            Environment.CurrentDirectory,
            AppDomain.CurrentDomain.BaseDirectory
        };

        foreach (var candidate in baseCandidates)
        {
            var combined = Path.GetFullPath(Path.Combine(candidate, configuredPath));
            if (Directory.Exists(combined))
            {
                return combined;
            }
        }

        return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, configuredPath));
    }

    private InferenceOutput InferSignalFromOnnx(string onnxPath)
    {
        // Replace this placeholder with a real ONNX Runtime inference call.
        var signalBarIndex = UseClosedBarSignal
            ? Math.Max(0, Bars.Count - 2)
            : Math.Max(0, Bars.Count - 1);

        var impulse = Bars.ClosePrices[signalBarIndex] - Bars.OpenPrices[signalBarIndex];
        var bodyPips = Symbol.PipSize > 0 ? Math.Abs(impulse) / Symbol.PipSize : 0.0;

        if (bodyPips < MinCandleBodyPips)
        {
            return new InferenceOutput(0.0, 0.0, onnxPath, bodyPips);
        }

        var normalizationWindowPips = Math.Max(4.0, StopLossPips * 0.5);
        var signal = Symbol.PipSize > 0
            ? Math.Clamp(impulse / (Symbol.PipSize * normalizationWindowPips), -1.0, 1.0)
            : 0.0;

        var confidence = Math.Clamp(Math.Abs(signal), 0.0, 1.0);

        // Decision models can be used to adjust confidence or gating.
        if (_models.DecisionModels.Count > 0)
        {
            confidence = Math.Clamp(confidence + 0.05, 0.0, 1.0);
        }

        // Replay/safetensors data can be used to bias signal over time.
        if (_models.ReplayModels.Count > 0)
        {
            var multiplier = 1.0 + 0.1 / Math.Max(1, MMultiSubdivision);
            signal = Math.Clamp(signal * multiplier, -1.0, 1.0);
        }

        return new InferenceOutput(signal, confidence, onnxPath, bodyPips);
    }

    private bool CanExecuteSignal(TradeType tradeType, InferenceOutput inference)
    {
        var now = Server.Time;

        if (MinSecondsBetweenTrades > 0 && _lastOrderTime != DateTime.MinValue)
        {
            var secondsSinceLastTrade = (now - _lastOrderTime).TotalSeconds;
            if (secondsSinceLastTrade < MinSecondsBetweenTrades)
            {
                return RejectTrade(
                    $"time cooldown active: {secondsSinceLastTrade:F1}s < {MinSecondsBetweenTrades}s");
            }
        }

        if (MinBarsBetweenTrades > 0 && _lastOrderBarIndex >= 0)
        {
            var barsSinceLastTrade = (Bars.Count - 1) - _lastOrderBarIndex;
            if (barsSinceLastTrade < MinBarsBetweenTrades)
            {
                return RejectTrade($"bar cooldown active: {barsSinceLastTrade} < {MinBarsBetweenTrades}");
            }
        }

        PruneExecutedOrderWindow(now);
        if (MaxOrdersPerHour > 0 && _executedOrders.Count >= MaxOrdersPerHour)
        {
            return RejectTrade($"hourly order limit reached: {_executedOrders.Count}/{MaxOrdersPerHour}");
        }

        if (HasOpenPosition(tradeType))
        {
            return RejectTrade($"existing {tradeType} position already open");
        }

        var oppositeTradeType = GetOppositeTradeType(tradeType);
        if (HasOpenPosition(oppositeTradeType))
        {
            if (!AllowImmediateFlip)
            {
                return RejectTrade("opposite position open and immediate flip is disabled");
            }

            var requiredFlipSignal = Math.Min(1.0, MinSignal * FlipSignalFactor);
            if (Math.Abs(inference.Signal) < requiredFlipSignal)
            {
                return RejectTrade(
                    $"flip signal too weak: {Math.Abs(inference.Signal):F3} < {requiredFlipSignal:F3}");
            }
        }

        return true;
    }

    private bool RejectTrade(string reason)
    {
        if (!DebugGateLogs)
        {
            return false;
        }

        var now = Server.Time;
        if (_lastGateLogTime == DateTime.MinValue || (now - _lastGateLogTime).TotalSeconds >= 10)
        {
            Print($"Gate reject: {reason}");
            _lastGateLogTime = now;
        }

        return false;
    }

    private void PruneExecutedOrderWindow(DateTime now)
    {
        while (_executedOrders.Count > 0 && (now - _executedOrders.Peek()).TotalHours >= 1)
        {
            _executedOrders.Dequeue();
        }
    }

    private static TradeType GetOppositeTradeType(TradeType tradeType)
    {
        return tradeType == TradeType.Buy ? TradeType.Sell : TradeType.Buy;
    }

    private void RegisterOrderExecution()
    {
        var now = Server.Time;
        _lastOrderTime = now;
        _lastOrderBarIndex = Bars.Count - 1;

        _executedOrders.Enqueue(now);
        PruneExecutedOrderWindow(now);
    }

    private void ExecuteSignal(TradeType tradeType, InferenceOutput output)
    {
        var volumeInUnits = Symbol.QuantityToVolumeInUnits(LotSize);
        if (volumeInUnits <= 0)
        {
            Print($"Invalid volume from lot size: {LotSize}");
            return;
        }

        CloseOppositePositions(tradeType);

        if (HasOpenPosition(tradeType))
        {
            return;
        }

        var result = ExecuteMarketOrder(
            tradeType,
            SymbolName,
            volumeInUnits,
            BotLabel,
            StopLossPips,
            TakeProfitPips);

        if (!result.IsSuccessful)
        {
            Print($"Order failed: {result.Error}");
            return;
        }

        RegisterOrderExecution();

        Print(
            $"{tradeType} opened | model={Path.GetFileName(output.SourceModel)} signal={output.Signal:F3} confidence={output.Confidence:F3} vol={volumeInUnits}");
    }

    private bool HasOpenPosition(TradeType tradeType)
    {
        return Positions.Any(position =>
            position.SymbolName == SymbolName &&
            position.Label == BotLabel &&
            position.TradeType == tradeType);
    }

    private void CloseOppositePositions(TradeType desiredTradeType)
    {
        var oppositeTradeType = GetOppositeTradeType(desiredTradeType);

        var oppositePositions = Positions
            .Where(position =>
                position.SymbolName == SymbolName &&
                position.Label == BotLabel &&
                position.TradeType == oppositeTradeType)
            .ToArray();

        foreach (var position in oppositePositions)
        {
            ClosePosition(position);
        }
    }

    private sealed class ModelBundle
    {
        public List<string> OnnxTradingModels { get; } = new();
        public List<string> DecisionModels { get; } = new();
        public List<string> ReplayModels { get; } = new();

        public string PrimaryOnnxTradingModel { get; set; } = string.Empty;

        public void Reset()
        {
            OnnxTradingModels.Clear();
            DecisionModels.Clear();
            ReplayModels.Clear();
            PrimaryOnnxTradingModel = string.Empty;
        }
    }

    private readonly record struct InferenceOutput(double Signal, double Confidence, string SourceModel, double BodyPips);
}