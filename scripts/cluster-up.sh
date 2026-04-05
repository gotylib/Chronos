#!/usr/bin/env bash
set -euo pipefail

AGENTS="${1:-1}"
MASTER_PORT="${MASTER_PORT:-5000}"
AGENT_BASE_PORT="${AGENT_BASE_PORT:-5001}"
DOCKER_COMPOSE_EXECUTABLE="${DOCKER_COMPOSE_EXECUTABLE:-docker-compose}"
DOCKER_EXECUTABLE="${DOCKER_EXECUTABLE:-docker}"

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

echo "Starting Chronos.Master on port ${MASTER_PORT}..."
dotnet run --project "src/Chronos.Master/Chronos.Master.csproj" -- --urls "http://0.0.0.0:${MASTER_PORT}" &
MASTER_PID=$!

sleep 2

for (( i=0; i<AGENTS; i++ )); do
  AGENT_PORT=$((AGENT_BASE_PORT + i))
  export CHRONOS_MASTER_URL="http://localhost:${MASTER_PORT}"
  export CHRONOS_AGENT_BASE_URL="http://localhost:${AGENT_PORT}"
  export CHRONOS_AGENT_DOCKER_COMPOSE_EXECUTABLE="${DOCKER_COMPOSE_EXECUTABLE}"
  export CHRONOS_AGENT_DOCKER_EXECUTABLE="${DOCKER_EXECUTABLE}"
  echo "Starting Chronos.Agent on port ${AGENT_PORT}..."
  dotnet run --project "src/Chronos.Agent/Chronos.Agent.csproj" -- --urls "http://0.0.0.0:${AGENT_PORT}" &
done

echo
echo "Cluster started"
echo "Master UI: http://localhost:${MASTER_PORT}/ui/app"
echo "Agents API: http://localhost:${MASTER_PORT}/agents"
echo
echo "Press Ctrl+C to stop"
wait $MASTER_PID

