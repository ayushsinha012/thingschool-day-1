# Day 11 — Task 2: Drop p99 by 10×

## Task purpose

Fix the endpoint profiled in Task 1 (`GET /api/quotes/performance/author-quotes`)
using what the profiling found, then re-measure under the exact same load to
see how much the p99 actually drops. Target: at least a 10× reduction in
p99.

## What Task 1's baseline showed

Task 1 (`Day-11/task-1/result.md`) measured this endpoint at `authors=50`
against 9,000 synthetic quotes across 300 authors and found:

- **p50 = 400ms, p99 = 824ms**, longest request 865ms.
- One request produced **51** `Executed DbCommand` log entries: 1
  distinct-author query, then 1 query per returned author.
- The per-author query's plan was `SCAN q` — a full table scan, because
  `Quotes.Author` had no index.

## N+1 problem

The endpoint fetched up to `authors` distinct author names, then looped over
those names doing a separate `await`ed `WHERE Author = @author` query per
name. For `authors=50` that's 51 sequential round trips instead of 1–2, and
because they're awaited one at a time inside a `foreach`, total latency
scaled linearly with `authors`.

## Missing-index problem

`Quotes.Author` had no index in the EF Core model or in any applied
migration, so every one of those 50 per-author queries scanned the entire
`Quotes` table rather than doing an indexed lookup. This compounded the N+1:
50 full table scans instead of 50 cheap indexed point lookups.

## Exact optimization made

All changes are in the existing Week-1 API (`day-1/QuotesApi`), not
duplicated here:

1. **`Data/AppDbContext.cs`** — added `entity.HasIndex(quote => quote.Author)`
   inside the `Quote` entity configuration, and generated the migration
   (see "exact index added" below).
2. **`Endpoints/QuoteEndpoints.cs`**, `/performance/author-quotes` —
   rewritten twice:
   - First pass: replaced the 50 sequential per-author queries with a
     single `WHERE Author IN (authorNames)` query, using the previously
     fetched author-name list. Down from 51 round trips to 2.
   - Second pass (after the first pass measured 4.96×, short of 10×):
     merged the two queries into **one** round trip by passing the
     author-name query itself as an `IQueryable` into `.Contains(...)`, so
     EF translates it to `WHERE Author IN (SELECT DISTINCT Author ...
     ORDER BY Author LIMIT @authorCount)`. Also narrowed the projection to
     `{Author, Text}` instead of full `Quote` entities (`Id`/`IsDeleted`
     were never used by the response), and replaced the `ToLookup` grouping
     with a single linear pass over the now pre-sorted (`ORDER BY Author`)
     result set.
3. **`Program.cs`** — added `ThreadPool.SetMinThreads(Environment.ProcessorCount
   * 4, Environment.ProcessorCount * 4)` to test whether ThreadPool
   injection lag (a known issue when a synchronous ADO.NET driver like
   Microsoft.Data.Sqlite is called from more concurrent requests than the
   default minimum thread count) explained the remaining tail latency.
   Measured as having no effect (see result.md) but kept as a harmless,
   standard mitigation for this endpoint's load profile.

## Exact index added

Migration `AddQuoteAuthorIndex` (applied to both the SQLite migration set
used by the running API and the SQL Server migration set used by
integration tests):

```csharp
migrationBuilder.CreateIndex(
    name: "IX_Quotes_Author",
    table: "Quotes",
    column: "Author");
```

Confirmed present in `sqlite_master` on the same database used for both
Task 1 and this measurement:

```
IX_Quotes_Author|CREATE INDEX "IX_Quotes_Author" ON "Quotes" ("Author")
```

## Resulting query shape

One query per request instead of 51 (verified via EF Core command logging —
exactly one `Executed DbCommand` entry per request against the same
`quotes.db`):

```sql
SELECT "q"."Author", "q"."Text"
FROM "Quotes" AS "q"
WHERE NOT ("q"."IsDeleted") AND "q"."Author" IN (
    SELECT "q0"."Author"
    FROM (
        SELECT DISTINCT "q1"."Author"
        FROM "Quotes" AS "q1"
        WHERE NOT ("q1"."IsDeleted")
        ORDER BY "q1"."Author"
        LIMIT @authorCount
    ) AS "q0"
)
ORDER BY "q"."Author";
```

Full copy in `queries-after.sql`.

## How the task was measured

Same setup as Task 1, reused deliberately so the two results are comparable:

- Build: `~/.dotnet/dotnet build -c Release` (Release, target `net10.0`).
- Run: `bin/Release/net10.0/linux-x64/QuotesApi`, `ASPNETCORE_ENVIRONMENT=Production`,
  `http://localhost:5099`.
- Data: the same `quotes.db` from Task 1 — 9,000 synthetic quotes, 300
  synthetic authors, unchanged (row/author counts re-verified immediately
  before the final run).
- Machine: same 4-logical-CPU box, no other load.
- `bombardier` and `k6` are still not installed on this machine (checked
  again with `command -v`); `ab` (Apache Bench) was used again, exactly as
  in Task 1.
- EF Core SQL command logging off (Warning level) for every load-test run,
  same as Task 1; turned on separately, outside any `ab` run, only to
  capture the SQL text and per-command timings.

## Exact load used

```bash
ab -n 300 -c 10 'http://localhost:5099/api/quotes/performance/author-quotes?authors=50'
```

Same endpoint, same `authors=50`, same request count, same concurrency as
Task 1's baseline. Not changed.

## Before p50/p99

**p50 = 400ms, p99 = 824ms** (Task 1 baseline, 0 failed / 300 complete).

## After p50/p99

**p50 = 46ms, p99 = 144ms** (this task's final measurement, 0 failed / 300
complete, longest request 260ms, 1.681s test duration, 178.51 req/s).

## Actual improvement factor

824 / 144 = **5.72×**.

This is below the 10× target (p99 ≤ ~82ms was not reached). See
`result.md` for the investigation into why, and why no further SQL/index
change was justified by the evidence.

## Before execution plan

```
QUERY PLAN
`--SCAN q
```

`SCAN q` — full table scan, repeated once per author (50× per request).
Full capture in `execution-plan-before.txt`.

## After execution plan

```
QUERY PLAN
|--SEARCH q USING INDEX IX_Quotes_Author (Author=?)
`--LIST SUBQUERY 2
   |--CO-ROUTINE (subquery-1)
   |  `--SCAN Quotes USING INDEX IX_Quotes_Author
   `--SCAN (subquery-1)
```

No full table scan anywhere — the outer filter is an indexed point lookup
per author, and the author subquery walks the index in sorted order. Full
capture in `execution-plan-after.txt`.

## Files in this directory

- `README.md` — this file.
- `result.md` — the full evidence: before/after SQL, before/after execution
  plans, index evidence, the N+1 and index fixes explained, the exact code
  changes, two performance observations from the investigation, the
  exercise answer, and the reflection questions.
- `queries-before.sql` — the N+1 SQL from Task 1, reproduced here for a
  self-contained before/after comparison.
- `queries-after.sql` — the actual single-query SQL emitted after the fix,
  captured from EF Core command logging.
- `execution-plan-before.txt` — Task 1's `EXPLAIN QUERY PLAN` output,
  reproduced here for a self-contained before/after comparison.
- `execution-plan-after.txt` — `EXPLAIN QUERY PLAN` output for the
  optimized query, against the same database.
- `load-test-after-raw.txt` — the raw `ab` output for the final,
  reported after-optimization measurement.
- `load-test-iteration-raw.txt` — raw `ab` output from the two
  intermediate optimization steps tried before the final one (fixing N+1
  with 2 queries, then merging to 1 query + narrower projection, then
  testing a ThreadPool change), kept as evidence for the investigation
  written up in `result.md`.
- `load-test.sh` — copied from `../task-1/load-test.sh`, unmodified. Prefers
  `bombardier`, then `k6` (via `load-test.k6.js`), then falls back to `ab`.
  Copied rather than referenced so this directory can reproduce its own
  measurement without depending on `task-1/`.
- `load-test.k6.js` — copied from `../task-1/load-test.k6.js`, unmodified;
  the k6 script `load-test.sh` runs if `k6` is available.
- `seed-performance-data.sh` — copied from `../task-1/seed-performance-data.sh`,
  unmodified; (re)generates the same 300 authors × 30 quotes dataset used
  for both Task 1's baseline and this measurement.

The production code changes themselves live in the existing Week-1 API:
`day-1/QuotesApi/Data/AppDbContext.cs`, `day-1/QuotesApi/Endpoints/QuoteEndpoints.cs`,
`day-1/QuotesApi/Program.cs`, and the `AddQuoteAuthorIndex` migrations under
`day-1/QuotesApi/Migrations/` and `day-1/QuotesApi/Migrations.SqlServer/`.
None of that is duplicated here.

## How to reproduce the measurement

```bash
cd Day-11/task-2
./seed-performance-data.sh                 # defaults: 300 authors x 30 quotes

cd ../../day-1/QuotesApi
~/.dotnet/dotnet build -c Release
ASPNETCORE_ENVIRONMENT=Production \
ASPNETCORE_URLS=http://localhost:5099 \
Jwt__Key='<any base64 string that decodes to >= 32 bytes>' \
./bin/Release/net10.0/linux-x64/QuotesApi
```

The API requires a JWT signing key at startup (`Jwt:Key`, ≥ 256 bits) even
though this endpoint doesn't require authentication — set it via the
`Jwt__Key` environment variable or `dotnet user-secrets` for local runs; it
is not part of the performance work and isn't committed anywhere.

In a second terminal:

```bash
cd Day-11/task-2
./load-test.sh 'http://localhost:5099/api/quotes/performance/author-quotes?authors=50' 10 300
```

`load-test.sh` prefers `bombardier`, then `k6`, then falls back to `ab -n
300 -c 10` — on the machine used for the recorded measurement, neither
`bombardier` nor `k6` was installed, so the `ab` fallback produced the
numbers in `result.md`.

To re-capture the SQL and execution plan, start the API with EF Core
command logging enabled (`ASPNETCORE_ENVIRONMENT=Development`, or override
`Serilog:MinimumLevel:Override:Microsoft.EntityFrameworkCore.Database.Command`
to `Information`/`Debug`), make one request, and copy the single
`Executed DbCommand` entry. Then, against the same `quotes.db`:

```bash
cd day-1/QuotesApi
sqlite3 quotes.db "EXPLAIN QUERY PLAN <paste the captured SQL, with a real author value in place of the IN-subquery's LIMIT param>;"
```

## Exercise

Paste before/after p99 (target ≥10× improvement), the changes you made, and
the before/after execution plans.

- **Before p99: 824ms. After p99: 144ms. Actual improvement: 824 / 144 =
  5.72×** — short of the ≥10× target.
- **Changes made:** added `IX_Quotes_Author` on `Quotes.Author`; replaced
  the N+1 loop (1 + 50 queries) with a single query using a subquery
  (`WHERE Author IN (SELECT DISTINCT Author ... LIMIT @authorCount)`)
  instead of two round trips; narrowed the projection to `{Author, Text}`
  instead of full `Quote` entities; raised `ThreadPool.SetMinThreads` to
  rule out thread-pool injection lag (it measured as having no effect).
- **Before execution plan:** `SCAN q` (full table scan, repeated once per
  author, 50× per request).
- **After execution plan:** `SEARCH q USING INDEX IX_Quotes_Author
  (Author=?)` for the outer query, with the author subquery walking the
  same index in sorted order — no full table scan anywhere.
- Why short of 10×, and why no further SQL/index change is justified, is in
  `result.md` ("Two performance observations" and "What did you learn this
  session?").

## Notes for mentor

The N+1 and missing index are both fully fixed and verified: one DB command
per request instead of 51, and `EXPLAIN QUERY PLAN` shows indexed searches
with no table scans anywhere in the query. EF Core command logging measured
that single command at 1–4ms. The remaining gap between that and the
44–144ms end-to-end latencies observed under `-c 10` load is CPU time per
request (mostly JSON serialization of up to 1,500 rows) divided across this
machine's 4 logical CPUs, confirmed by a `ThreadPool.SetMinThreads` change
that measured as having no effect. I did not force a 10× number by changing
the load profile, the dataset, or the machine — the load, `authors=50`
value, request count, and concurrency are identical to Task 1's baseline
throughout.

## What did you learn this session?

That an execution plan reading "index-only, no table scan" doesn't mean
there's nothing left to fix, and it also doesn't mean the fix must be a
bigger index or a smarter query. Once EF Core's own command logging showed
the SQL itself only costs 1–4ms, the honest next question stopped being
"what's still wrong with the SQL" (nothing measurable was) and became
"where is the other ~40–140ms actually going" — which turned out to be
CPU time (serialization, allocation) split across a small number of cores
at this concurrency, not a database problem at all. Ruling that in required
an actual counter-experiment (the ThreadPool change) rather than assuming
from the code.

## What would break this?

Requesting more authors (toward the 100 max), a larger `Quotes` table, or a
larger response payload per quote would all grow the per-request CPU cost
(more rows to project, group, and JSON-serialize) without touching the
query's cost — since the query is already a single indexed lookup, that
per-request CPU work, not the database, would keep dominating end-to-end
latency, and it would get worse faster than the query would. Running this
same load test on a machine with fewer than 4 logical CPUs would push p99
higher still, for the same reason: the bottleneck now is CPU-bound
concurrency, not the database.
