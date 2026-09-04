#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
PROJECT_DIR="$ROOT_DIR/src/ResilienceDemo"
EVIDENCE_DIR="$SCRIPT_DIR/evidence"
BASE_URL="http://localhost:5080"
APP_LOG="$EVIDENCE_DIR/app.log"
DEMO_LOG="$EVIDENCE_DIR/demo-run.log"

mkdir -p "$EVIDENCE_DIR"
: > "$APP_LOG"
: > "$DEMO_LOG"

log() {
  echo "[$(date -u +%H:%M:%S.%3N)] $*" | tee -a "$DEMO_LOG"
}

call() {
  local method="$1" path="$2" body="${3:-}"
  local start end elapsed
  start=$(date +%s%3N)
  if [ -n "$body" ]; then
    resp=$(curl -sS -X "$method" "$BASE_URL$path" -H "Content-Type: application/json" -d "$body" -w '\n%{http_code}')
  else
    resp=$(curl -sS -X "$method" "$BASE_URL$path" -w '\n%{http_code}')
  fi
  end=$(date +%s%3N)
  elapsed=$((end - start))
  local status body_only
  status=$(echo "$resp" | tail -n1)
  body_only=$(echo "$resp" | sed '$d')
  log "$method $path -> http=$status elapsed=${elapsed}ms body=$body_only"
}

control_downstream() {
  local mode="$1" delay="${2:-0}"
  call POST /downstream/control "{\"mode\":\"$mode\",\"delayMs\":$delay}"
}

wait_for_ready() {
  for _ in $(seq 1 50); do
    if curl -sS -o /dev/null "$BASE_URL/demo/status"; then
      return 0
    fi
    sleep 0.2
  done
  echo "app did not become ready" >&2
  exit 1
}

start_app() {
  log "starting ResilienceDemo"
  ( cd "$PROJECT_DIR" && dotnet run --no-launch-profile --no-build ) >> "$APP_LOG" 2>&1 &
  APP_PID=$!
  wait_for_ready
  log "app ready pid=$APP_PID"
}

stop_app() {
  if [ -n "${APP_PID:-}" ]; then
    log "stopping app pid=$APP_PID"
    kill "$APP_PID" 2>/dev/null || true
    wait "$APP_PID" 2>/dev/null || true
  fi
}

trap stop_app EXIT

( cd "$PROJECT_DIR" && dotnet build --nologo -v quiet ) | tee -a "$DEMO_LOG"

start_app

log "=== SCENARIO A: healthy request ==="
call POST /demo/reset
control_downstream healthy
call GET /demo/get/1
call GET /demo/status

log "=== SCENARIO B: sustained failure -> circuit opens ==="
call POST /demo/reset
control_downstream failing
for i in 1 2 3 4 5; do
  call GET "/demo/get/$i"
done
call GET /demo/status
log "extra call while circuit should be OPEN (expect fast fail, no downstream hit)"
call GET /demo/get/99
call GET /downstream/control

log "=== SCENARIO C: recovery OPEN -> HALF-OPEN -> CLOSED ==="
control_downstream healthy
log "waiting for circuit breaker BreakDuration before probing"
sleep 5.5
call GET /demo/get/1
call GET /demo/status
call GET /demo/metrics

log "=== SCENARIO D: timeout ==="
call POST /demo/reset
control_downstream slow 1500
call POST /demo/event
call GET /demo/status

log "=== SCENARIO E: bulkhead under concurrency ==="
call POST /demo/reset
control_downstream slow 500
call POST "/demo/concurrent?count=10"
call GET /demo/status

log "=== FINAL METRICS SNAPSHOT ==="
call GET /demo/metrics

log "demo complete, evidence written to $EVIDENCE_DIR"
