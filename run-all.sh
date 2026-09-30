#!/usr/bin/env bash
# Run all three services and merge their logs into one color-coded view.
set -uo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"

CYAN=$'\e[36m'
GREEN=$'\e[32m'
MAGENTA=$'\e[35m'
NC=$'\e[0m'

PIDS=()

cleanup() {
  echo
  echo "Stopping services..."
  for pid in "${PIDS[@]}"; do
    kill "$pid" 2>/dev/null || true
  done
  # safety net: kill any lingering app hosts
  pkill -f 'OrderService/bin/Debug' 2>/dev/null || true
  pkill -f 'InventoryService/bin/Debug' 2>/dev/null || true
  pkill -f 'FulfillmentService/bin/Debug' 2>/dev/null || true
}
trap cleanup EXIT INT TERM

echo "Ensuring PostgreSQL + RabbitMQ are running..."
docker compose -f "$ROOT/docker-compose.yml" up -d

# Wait until a container reports 'healthy' before starting the services.
wait_healthy() {
  local container="$1"
  until [ "$(docker inspect --format '{{.State.Health.Status}}' "$container" 2>/dev/null)" = "healthy" ]; do
    echo "Waiting for $container to be ready..."
    sleep 2
  done
}
wait_healthy foodics-postgres
wait_healthy foodics-rabbitmq

# Start a service in the background, prefixing every log line with its name.
run() {
  local name="$1" dir="$2" color="$3"
  (
    cd "$ROOT/$dir"
    dotnet run --launch-profile http 2>&1 |
      awk -v p="${color}[${name}]${NC} " '{ print p $0; fflush() }'
  ) &
  PIDS+=("$!")
}

run "OrderService"       "OrderService"       "$CYAN"
run "InventoryService"   "InventoryService"   "$GREEN"
run "FulfillmentService" "FulfillmentService" "$MAGENTA"

echo
echo "All three services are starting. Press Ctrl+C to stop everything."
echo

wait
