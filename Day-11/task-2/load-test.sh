#!/usr/bin/env bash
# Runs a load test against GET /api/quotes/performance/author-quotes.
# Prefers bombardier, then k6 (using load-test.k6.js), then falls back to
# Apache Bench (ab) if neither is installed.
#
# Usage: ./load-test.sh [url] [concurrency] [duration_or_requests]
set -euo pipefail

URL="${1:-http://localhost:5099/api/quotes/performance/author-quotes?authors=50}"
CONCURRENCY="${2:-10}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if command -v bombardier >/dev/null 2>&1; then
  DURATION="${3:-30s}"
  echo "Using bombardier: -c $CONCURRENCY -d $DURATION $URL"
  exec bombardier -c "$CONCURRENCY" -d "$DURATION" "$URL"
elif command -v k6 >/dev/null 2>&1; then
  DURATION="${3:-30s}"
  echo "Using k6: vus=$CONCURRENCY duration=$DURATION $URL"
  TARGET_URL="$URL" VUS="$CONCURRENCY" DURATION="$DURATION" exec k6 run "$SCRIPT_DIR/load-test.k6.js"
elif command -v ab >/dev/null 2>&1; then
  REQUESTS="${3:-300}"
  echo "bombardier/k6 not found; falling back to ab: -n $REQUESTS -c $CONCURRENCY $URL"
  exec ab -n "$REQUESTS" -c "$CONCURRENCY" "$URL"
else
  echo "No load-testing tool found (bombardier, k6, or ab). Install one to run this script." >&2
  exit 1
fi
