#!/usr/bin/env bash
# boot.sh — Start Redis and QuantRocket containers, then verify readiness.
# Usage: ./boot.sh [--pull]
set -euo pipefail

COMPOSE_FILE="$(cd "$(dirname "$0")" && pwd)/docker-compose.yml"

if [[ "${1:-}" == "--pull" ]]; then
  echo "[boot] Pulling latest images..."
  docker compose -f "$COMPOSE_FILE" pull
fi

echo "[boot] Starting Redis and QuantRocket..."
docker compose -f "$COMPOSE_FILE" up -d

echo "[boot] Waiting for Redis on localhost:6379..."
for i in $(seq 1 30); do
  if redis-cli -h 127.0.0.1 -p 6379 ping 2>/dev/null | grep -q PONG; then
    echo "[boot] Redis is up (host)."
    break
  fi
  echo "[boot]   attempt $i/30 — retrying in 2s"
  sleep 2
  if [[ $i -eq 30 ]]; then
    echo "[boot] ERROR: Redis did not become available after 30 attempts." >&2
    exit 1
  fi
done

echo "[boot] Verifying Redis connectivity from within the Docker network..."
docker compose -f "$COMPOSE_FILE" exec -T redis \
  redis-cli -h redis -p 6379 ping

echo "[boot] Checking QuantRocket container status..."
docker compose -f "$COMPOSE_FILE" ps quantrocket

echo "[boot] Stack is ready. QuantRocket API available at http://localhost:1969"
