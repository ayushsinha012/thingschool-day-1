#!/usr/bin/env bash
# Seeds (or re-seeds) the synthetic dataset used to make the N+1 pattern in
# GET /api/quotes/performance/author-quotes observable under load.
#
# Idempotent: only touches rows whose Author LIKE 'Synthetic Author%', so it
# is safe to re-run and never deletes real quote data.
#
# Usage: ./seed-performance-data.sh [db_path] [authors] [quotes_per_author]
set -euo pipefail

DB_PATH="${1:-../../day-1/QuotesApi/quotes.db}"
AUTHORS="${2:-300}"
QUOTES_PER_AUTHOR="${3:-30}"
TOTAL=$((AUTHORS * QUOTES_PER_AUTHOR))

if ! command -v sqlite3 >/dev/null 2>&1; then
  echo "sqlite3 is required but not found on PATH." >&2
  exit 1
fi

if [ ! -f "$DB_PATH" ]; then
  echo "Database not found at $DB_PATH." >&2
  echo "Run the API once (dotnet run from day-1/QuotesApi) to create it via migrations first." >&2
  exit 1
fi

sqlite3 "$DB_PATH" <<SQL
DELETE FROM Quotes WHERE Author LIKE 'Synthetic Author%';

WITH RECURSIVE seq(n) AS (
  SELECT 1
  UNION ALL
  SELECT n + 1 FROM seq WHERE n < $TOTAL
)
INSERT INTO Quotes (Author, Text, IsDeleted)
SELECT
  'Synthetic Author ' || substr('000' || ((n - 1) / $QUOTES_PER_AUTHOR + 1), -4, 4),
  'Synthetic quote text #' || n,
  0
FROM seq;
SQL

echo "Seeded $TOTAL synthetic quotes across $AUTHORS authors into $DB_PATH"
sqlite3 "$DB_PATH" \
  "SELECT COUNT(*) || ' total quotes, ' || COUNT(DISTINCT Author) || ' distinct authors' FROM Quotes WHERE Author LIKE 'Synthetic Author%';"
