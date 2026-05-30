# Braga-Bielany-czycus

## QuantRocket + Copilot automation baseline

This repository now includes a baseline Copilot/CI setup for QuantRocket-oriented workflows with:

- Copilot cloud agent setup steps (`.github/workflows/copilot-setup-steps.yml`)
- Automated check/build/log workflow with Redis and Docker verification (`.github/workflows/quantrocket-automation.yml`)
- CodeQL scanning on push/pull/schedule (`.github/workflows/codeql.yml`)
- Advanced skill categories for integration, orchestration, and research (`.github/copilot/skills/*`)

### State, checkpoints, and snapshots

Workflows persist run state via artifacts, including:

- Resource and runtime logs
- Redis diagnostics logs
- Checkpoint metadata snapshots for reproducible operations

### Skill categories

1. **QuantRocket Integration Blueprint**: dependency/runtime setup for QuantRocket + ML ecosystem.
2. **Workflow Orchestration and State Logging**: check/build automation, logs, and checkpoints.
3. **Research and Algorithm Design**: Playwright + web search usage for API and algorithm improvements.
