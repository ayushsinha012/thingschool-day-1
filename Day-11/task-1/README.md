# Day 11 — Task 1: Profile a slow endpoint

## Task

Add and profile a deliberately inefficient endpoint in the existing Week-1
Quotes API. The submission must include baseline p50/p99 latency, the emitted
SQL, an execution plan, and the two largest problems found.

## Implementation

The endpoint is implemented in the existing API at:

```text
GET /api/quotes/performance/author-quotes?authors=50
```

`authors` defaults to 50 and is constrained to 1–100. The endpoint first
loads up to that many distinct author names, then loads quotes separately for
each author. It therefore emits one query for author names plus one query for
each returned author: an N+1 pattern.

The API's `Quote` model has an `Author` string rather than a normalized
Author entity and relationship. This is the closest real equivalent to the
authors-to-quotes N+1 in the exercise; no new production model was introduced
for a performance demonstration.

`Quotes.Author` has no index in the existing EF Core model or migrations, so
each per-author query is also a candidate for a table scan. No migration was
added: the missing index is intentional for this baseline and the task is to
profile it rather than fix it.

The production API keeps its existing SQLite configuration. Its integration
tests retain their existing SQL Server/Testcontainers setup; this task does not
replace either database provider or change migration configuration.

## Files in this directory

- `README.md` — this file.
- `result.md` — the captured baseline, SQL, execution plan, and analysis.
- `seed-performance-data.sh` — (re)generates the synthetic dataset used to
  make the N+1 observable (300 authors × 30 quotes = 9,000 rows by default).
  Idempotent: only touches rows where `Author LIKE 'Synthetic Author%'`.
- `load-test.sh` — runs the load test against the endpoint. Prefers
  `bombardier`, then `k6` (via `load-test.k6.js`), then falls back to Apache
  Bench (`ab`) if neither is installed.
- `load-test.k6.js` — the k6 script `load-test.sh` runs when `k6` is
  available.
- `queries.sql` — the actual SQL emitted by EF Core for this endpoint, as
  captured from command logging.
- `execution-plan.txt` — the actual `EXPLAIN QUERY PLAN` output for that SQL,
  plus the `sqlite_master` check confirming no index exists on `Author`.

The endpoint's production code itself lives in the existing Week-1 API at
`day-1/QuotesApi/Endpoints/QuoteEndpoints.cs` — it is not duplicated here.

## Exercise the endpoint

Seed enough non-deleted quotes spread across many authors to make the N+1
pattern observable, then run the existing API from its project directory:

```bash
cd Day-11/task-1
./seed-performance-data.sh              # defaults: 300 authors x 30 quotes
cd ../../day-1/QuotesApi
dotnet run
```

In a second terminal, make a smoke request:

```bash
curl -i 'http://localhost:5000/api/quotes/performance/author-quotes?authors=50'
```

Use the URL printed by `dotnet run` if it differs from port 5000.

## Collect performance evidence

Capture SQL by starting the API with EF Core command logging enabled:

```bash
cd day-1/QuotesApi
Serilog__MinimumLevel__Override__Microsoft.EntityFrameworkCore.Database.Command=Information dotnet run
```

Make one request and copy the `Executed DbCommand` entries for the endpoint
into `queries.sql`/`result.md`. There should be one distinct-author command
and one author-filtered command per returned author.

With the API running, capture latency with `load-test.sh` (uses bombardier
or k6 if installed, otherwise falls back to `ab`):

```bash
cd Day-11/task-1
./load-test.sh 'http://localhost:5000/api/quotes/performance/author-quotes?authors=50' 10 30s
```

Record its p50 and p99 output in `result.md`. Keep the data volume, URL,
concurrency, duration, machine, and build configuration with the result so it
can be reproduced.

For the configured SQLite database, capture the plan for a representative
per-author command using an author value observed in the SQL log:

```bash
cd day-1/QuotesApi
sqlite3 quotes.db 'EXPLAIN QUERY PLAN SELECT "q"."Id", "q"."Author", "q"."IsDeleted", "q"."Text" FROM "Quotes" AS "q" WHERE NOT ("q"."IsDeleted") AND "q"."Author" = '\''AUTHOR_FROM_LOG'\'';'
```

Replace `AUTHOR_FROM_LOG` with the captured value, then paste the output into
`execution-plan.txt`/`result.md`. The plan should be captured from the same
database state used for the load test.

# Exercise

Paste the baseline p50/p99, the offending SQL, and the plan. State the two
biggest problems you found.

See `result.md` for the actual measured baseline, SQL, execution plan, and
the two biggest problems identified.

## GitHub link

[Add the repository/branch/PR/commit link after publishing the work.]

## Notes for mentor

This endpoint was profiled against the API's existing SQLite database
(local dev configuration) rather than the SQL Server/Testcontainers stack,
which backs integration tests only and was left untouched. No production
behavior was changed; the endpoint is additive.

## What did you learn this session?

That an N+1 pattern and a missing index compound each other rather than
just adding: see `result.md` for the full reasoning from the captured
evidence.

## What would break this?

Growing the `Quotes` table or the requested `authors` count makes both the
round-trip count and the per-query table scan cost worse at the same time.
See `result.md` for details.
