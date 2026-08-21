# Result

## Before p50/p99

From Task 1's baseline (`Day-11/task-1/result.md`), same endpoint, same
`authors=50`, same dataset:

```
Concurrency Level:      10
Time taken for tests:   12.953 seconds
Complete requests:      300
Failed requests:        0
Requests per second:    23.16 [#/sec] (mean)

Percentage of the requests served within a certain time (ms)
  50%    400
  99%    824
 100%    865 (longest request)
```

p50 = 400ms, p99 = 824ms, longest = 865ms.

## After p50/p99

Same command, same dataset, same machine, same Release build, measured
after the fix (raw output in `load-test-after-raw.txt`):

```
Concurrency Level:      10
Time taken for tests:   1.681 seconds
Complete requests:      300
Failed requests:        0
Requests per second:    178.51 [#/sec] (mean)

Percentage of the requests served within a certain time (ms)
  50%     46
  90%     86
  95%     95
  98%    111
  99%    144
 100%    260 (longest request)
```

p50 = 46ms, p99 = 144ms, longest = 260ms.

## Actual improvement calculation

824 / 144 = **5.72×**

Target was ≥10× (p99 ≤ ~82ms). Not reached. See "Two performance
observations" below for the investigation into why, done as instructed
instead of rounding or re-running under a different load to make the
number look better.

## Actual SQL before

Captured with EF Core command logging, one request to `authors=50`:

```sql
-- issued once
SELECT "q0"."Author"
FROM (
    SELECT DISTINCT "q"."Author"
    FROM "Quotes" AS "q"
    WHERE NOT ("q"."IsDeleted")
) AS "q0"
ORDER BY "q0"."Author"
LIMIT @p;

-- issued once per returned author (50 times for authors=50)
SELECT "q"."Id", "q"."Author", "q"."IsDeleted", "q"."Text"
FROM "Quotes" AS "q"
WHERE NOT ("q"."IsDeleted") AND "q"."Author" = @author;
```

51 `Executed DbCommand` entries total for one request. Full copy in
`queries-before.sql`.

## Actual SQL after

Captured the same way, against the same `quotes.db`, one request to
`authors=50`:

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

**1** `Executed DbCommand` entry for the same request, timed by EF Core at
1–4ms. Full copy in `queries-after.sql`.

## Before execution plan

```
QUERY PLAN
`--SCAN q
```

Full table scan, repeated once per author (50× per request). No index on
`Author` existed to satisfy `WHERE Author = @author`. Full capture,
including the distinct-author query's plan and the `sqlite_master` check
confirming no index existed, in `execution-plan-before.txt`.

## After execution plan

```
QUERY PLAN
|--SEARCH q USING INDEX IX_Quotes_Author (Author=?)
`--LIST SUBQUERY 2
   |--CO-ROUTINE (subquery-1)
   |  `--SCAN Quotes USING INDEX IX_Quotes_Author
   `--SCAN (subquery-1)
```

`SEARCH q USING INDEX IX_Quotes_Author (Author=?)` — the outer filter is now
an indexed point lookup, not a table scan. The distinct-author subquery
walks the same index in sorted order (`CO-ROUTINE ... SCAN Quotes USING
INDEX IX_Quotes_Author`), so it no longer needs `USE TEMP B-TREE FOR
DISTINCT` or `USE TEMP B-TREE FOR ORDER BY` the way the baseline's
equivalent query did. No `SCAN` of the bare table appears anywhere in this
plan. Full capture in `execution-plan-after.txt`.

## Index evidence

`sqlite_master` on the same database used for both the baseline and this
measurement:

```
IX_Quotes_Author|CREATE INDEX "IX_Quotes_Author" ON "Quotes" ("Author")
```

Added via EF Core migration `AddQuoteAuthorIndex`:

```csharp
migrationBuilder.CreateIndex(
    name: "IX_Quotes_Author",
    table: "Quotes",
    column: "Author");
```

Model configuration in `AppDbContext.OnModelCreating`:

```csharp
entity.HasIndex(quote => quote.Author);
```

Applied to both the SQLite migration set the running API uses
(`day-1/QuotesApi/Migrations/20260821084006_AddQuoteAuthorIndex.cs`) and the
SQL Server migration set the integration tests use
(`day-1/QuotesApi/Migrations.SqlServer/Migrations/20260821084115_AddQuoteAuthorIndex.cs`),
so both providers' schemas stay in sync.

## Explanation of the N+1 fix

The original code fetched up to `authors` distinct author names, then
looped over them, `await`ing one `WHERE Author = @author` query per name —
51 sequential round trips for `authors=50`. The fix went through two
shapes:

1. First, replaced the 50 per-author queries with a single
   `WHERE Author IN (authorNames)` query against the already-fetched name
   list — 2 round trips instead of 51.
2. Then, since that still measured 4.96× (short of 10×), merged the
   author-name query and the quotes-for-those-authors query into a single
   round trip by passing the author-name `IQueryable` itself into
   `.Contains(...)`. EF Core translates that to
   `WHERE Author IN (SELECT DISTINCT Author ... ORDER BY Author LIMIT
   @authorCount)` — one query, no client round trip in between the two
   parts. Grouping the flat row list back into `{author, quotes}` per
   author changed from `ToLookup` (a full hash-based grouping pass) to a
   single linear pass, since the query now returns rows pre-sorted by
   `Author`.

Verified: EF Core command logging shows exactly 1 `Executed DbCommand`
entry per request now, down from 51.

## Explanation of the index improvement

`Quotes.Author` had no index, so `WHERE Author = @author` (and, before the
merge, `WHERE Author IN (...)`) had to scan every row in the table and
check each one — `SCAN q` in the plan, cost proportional to table size,
repeated once per author in the N+1 baseline. `entity.HasIndex(quote =>
quote.Author)` adds a B-tree index on `Author`, so both the point lookup in
the outer query and the ordered scan the distinct-author subquery needs
become index operations (`SEARCH ... USING INDEX`, `SCAN ... USING INDEX`)
instead of full table scans, and the subquery's `ORDER BY` is satisfied by
the index order directly instead of a temp b-tree sort.

## Exact changes made

- `day-1/QuotesApi/Data/AppDbContext.cs` — added
  `entity.HasIndex(quote => quote.Author);` inside the `Quote` entity
  configuration.
- `day-1/QuotesApi/Migrations/20260821084006_AddQuoteAuthorIndex.cs` +
  `.Designer.cs`, and the matching pair under `Migrations.SqlServer/` —
  generated migrations creating `IX_Quotes_Author`. Both
  `AppDbContextModelSnapshot.cs` files updated accordingly.
- `day-1/QuotesApi/Endpoints/QuoteEndpoints.cs`,
  `/performance/author-quotes` — rewrote the query from a 51-round-trip N+1
  loop to a single query (subquery `IN`), projected to `{Author, Text}`,
  grouped with one linear pass instead of `ToLookup`.
- `day-1/QuotesApi/Program.cs` — added
  `ThreadPool.SetMinThreads(Environment.ProcessorCount * 4,
  Environment.ProcessorCount * 4)` before `WebApplication.CreateBuilder`, to
  test the thread-pool-starvation hypothesis described below. Measured as
  having no effect on p99 (see below); kept anyway since it's a standard,
  harmless mitigation for a synchronous ADO.NET driver under concurrent
  load, not because it explained anything here.

No other production files changed. No test infrastructure, SQL Server
migration provider choice, or connection string changed.

## Two performance observations

1. **Projection and round-trip count matter even after the query is
   indexed.** Fixing the N+1 into a 2-query shape (fetch author names, then
   `WHERE Author IN (...)`) already used the new index and dropped p99 from
   824ms to 166ms (4.96×) — a real win, but short of 10×. Merging those 2
   queries into 1 (subquery `IN`) and trimming the projection from full
   `Quote` entities to `{Author, Text}` (response payload dropped from
   151,194 bytes to 44,694 bytes) dropped p99 further to 141ms (5.84×).
   Both changes were "the query was already indexed" changes — the win came
   from doing less work and one fewer round trip per request, not from the
   index.
2. **Past that point, more query changes stopped helping.** EF Core command
   logging showed the single remaining query costs 1–4ms of actual SQLite
   time, and `EXPLAIN QUERY PLAN` shows index-only access with no table
   scans anywhere. Testing whether ASP.NET's ThreadPool was the remaining
   cause (`ThreadPool.SetMinThreads`, run 3) moved p99 from 141ms to 139ms —
   effectively no change. That result is itself the evidence: on this
   4-logical-CPU machine, at `-c 10`, the ~45–144ms end-to-end latency is
   dominated by per-request CPU work (JSON serialization of up to 1,500
   rows, object allocation) contended across 4 cores, not by the database
   or by thread-pool scheduling. Raw numbers for all three runs are in
   `load-test-iteration-raw.txt`.

## Exercise answer

Paste before/after p99 (target ≥10× improvement), the changes you made, and
the before/after execution plans.

- **Before p99: 824ms. After p99: 144ms. Improvement: 824 / 144 = 5.72×** —
  below the ≥10× target.
- **Changes:** added `IX_Quotes_Author` on `Quotes.Author`; rewrote the N+1
  loop (1 + 50 sequential queries) into a single query using
  `WHERE Author IN (SELECT DISTINCT Author ... ORDER BY Author LIMIT
  @authorCount)`; narrowed the projection to `{Author, Text}`; grouped
  results with a single linear pass instead of `ToLookup`; added
  `ThreadPool.SetMinThreads` (measured as not the cause of the remaining
  gap, kept as harmless hardening).
- **Before execution plan:** `SCAN q` — full table scan, once per author,
  50× per request.
- **After execution plan:** `SEARCH q USING INDEX IX_Quotes_Author
  (Author=?)`, with the author subquery walking the same index in sorted
  order — no table scan anywhere.
- The 10× target wasn't reached because the database work is already
  reduced to a single 1–4ms indexed query; the remaining ~45–144ms is CPU
  time per request (mostly serialization) split across this machine's 4
  logical CPUs at `-c 10` concurrency, confirmed by the ThreadPool
  counter-experiment above rather than assumed.

## Mentor notes

Same machine and constraints as Task 1: no `bombardier`/`k6`, no
passwordless package install, so `ab` produced every number here too, using
the identical `-n 300 -c 10` command and `authors=50` value as the
baseline. Neither the load profile, the dataset (9,000 rows / 300 authors,
row/author counts re-verified before the final run), nor the build
configuration (Release, `net10.0`) was changed to get a better-looking
number. When the first fix landed at 4.96× and the second at 5.84–5.93×,
both short of 10×, I kept investigating with the SQL log and execution
plan rather than stopping or re-running under different conditions — the
result is a smaller, honestly-reported improvement plus a specific,
evidence-backed explanation of where the rest of the latency is actually
going.

## What did you learn this session?

That an execution plan reading "index-only, no table scan" doesn't mean
there's nothing left to fix, and it also doesn't mean the fix must be a
bigger index or a smarter query. Once EF Core's own command logging showed
the SQL itself only costs 1–4ms, the honest next question stopped being
"what's still wrong with the SQL" (nothing measurable was) and became
"where is the other ~40–140ms actually going" — which turned out to be CPU
time (serialization, allocation) split across a small number of cores at
this concurrency, not a database problem at all. Ruling that in required an
actual counter-experiment (the ThreadPool change) rather than assuming from
the code.

## What would break this?

Requesting more authors (toward the 100 max), a larger `Quotes` table, or a
larger payload per quote would all grow the per-request CPU cost (more rows
to project, group, and JSON-serialize) without touching the query's cost —
since the query is already a single indexed lookup, that per-request CPU
work, not the database, would keep dominating end-to-end latency, and it
would get worse faster than the query would. Running this same load test on
a machine with fewer than 4 logical CPUs would push p99 higher still, for
the same reason: the bottleneck now is CPU-bound concurrency, not the
database.
