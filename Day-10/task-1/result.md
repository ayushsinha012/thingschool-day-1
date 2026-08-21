# Exercise

## Baseline p50/p99

Environment note: the installed system `dotnet` (via `/usr/bin/dotnet`) is
6.0.400, which cannot target this project's `net10.0` TargetFramework. A
.NET 10 SDK (`10.0.302`) was found installed at `~/.dotnet` and was used
directly to build and run the API. Docker was available but not needed —
this endpoint runs against the API's existing SQLite database
(`day-1/QuotesApi/quotes.db`), not the SQL Server/Testcontainers
integration-test stack, which was left untouched.

`bombardier` and `k6` are not installed on this machine and there is no
passwordless package-install access in this environment. Apache Bench
(`ab`, from `apache2-utils`), which was already installed, was used instead
as the lightweight load-testing tool.

Setup used for the measurement:

- Build: `~/.dotnet/dotnet build -c Release` (Release configuration, target `net10.0`)
- Run: `bin/Release/net10.0/linux-x64/QuotesApi.dll`, `ASPNETCORE_ENVIRONMENT=Production`, listening on `http://localhost:5099`
- Data: `quotes.db` seeded with 9,000 synthetic quotes spread across 300 distinct synthetic authors (30 quotes/author, all `IsDeleted = 0`), via a direct SQLite insert script (no real quote content, no production data)
- Machine: 4 logical CPUs, Linux 6.6.9-amd64 (Kali), local disk SQLite, single instance, no other load on the box
- EF Core SQL command logging was **off** during the load test (Production log level, `Warning` for `Microsoft.EntityFrameworkCore.Database.Command`) so logging overhead doesn't skew the latency numbers; SQL was captured separately (see below) from the same build/data with logging turned on for a single request

Command:

```bash
ab -n 300 -c 10 'http://localhost:5099/api/quotes/performance/author-quotes?authors=50'
```

Result (raw `ab` output, 300 requests, concurrency 10, 0 failed requests):

```
Concurrency Level:      10
Time taken for tests:   12.953 seconds
Complete requests:      300
Failed requests:        0
Requests per second:    23.16 [#/sec] (mean)
Time per request:       431.773 [ms] (mean)

Percentage of the requests served within a certain time (ms)
  50%    400
  66%    477
  75%    518
  80%    547
  90%    624
  95%    663
  98%    716
  99%    824
 100%    865 (longest request)
```

**p50 = 400ms, p99 = 824ms** for `GET /api/quotes/performance/author-quotes?authors=50` against 9,000 rows / 300 authors.

## Offending SQL

Captured with EF Core command logging enabled
(`Serilog:MinimumLevel:Override:Microsoft.EntityFrameworkCore.Database.Command=Information`)
for one request to `authors=50` against the same database used for the
load test. The request produced exactly **51** `Executed DbCommand` log
entries: 1 distinct-author query, then 1 additional query per returned
author (50 of them) — a textbook N+1 pattern.

Distinct-author query (issued once):

```sql
SELECT "q0"."Author"
FROM (
    SELECT DISTINCT "q"."Author"
    FROM "Quotes" AS "q"
    WHERE NOT ("q"."IsDeleted")
) AS "q0"
ORDER BY "q0"."Author"
LIMIT @p
```

Per-author query (issued once per returned author — 50 times for this request; parameter shown for `@author = 'Synthetic Author 0001'`, the first author returned):

```sql
SELECT "q"."Id", "q"."Author", "q"."IsDeleted", "q"."Text"
FROM "Quotes" AS "q"
WHERE NOT ("q"."IsDeleted") AND "q"."Author" = @author
```

## Execution plan

Captured with `sqlite3 quotes.db 'EXPLAIN QUERY PLAN ...'` against the same
database used for the load test, using an author value taken from the
captured SQL log above (`'Synthetic Author 0001'`).

Per-author query plan:

```
QUERY PLAN
`--SCAN q
```

`SCAN q` means SQLite reads every row of `Quotes` and filters in memory —
a full table scan — because there is no index that can satisfy
`WHERE Author = @author`. This plan repeats once per author returned
(50 times for the default request), each scanning the full table.

Distinct-author query plan (for reference/context — not the primary
offender since it only runs once per request):

```
QUERY PLAN
|--CO-ROUTINE q0
|  |--SCAN q
|  `--USE TEMP B-TREE FOR DISTINCT
|--SCAN q0
`--USE TEMP B-TREE FOR ORDER BY
```

Confirming no index exists on `Quotes` beyond the primary key:

```sql
sqlite> SELECT name, sql FROM sqlite_master WHERE tbl_name='Quotes';
Quotes|CREATE TABLE "Quotes" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Quotes" PRIMARY KEY AUTOINCREMENT,
    "Author" TEXT NOT NULL,
    "Text" TEXT NOT NULL
, "IsDeleted" INTEGER NOT NULL DEFAULT 0)
```

Only the table's `CREATE TABLE` statement is present — no `CREATE INDEX`
row exists for `Quotes`, confirming `Author` (and `IsDeleted`) are unindexed
in both the EF Core model (`AppDbContext.OnModelCreating`) and the applied
migrations.

## Two biggest problems

1. **N+1 query pattern.** The endpoint issues 1 query to fetch up to
   `authors` distinct author names, then loops over those names issuing one
   additional `SELECT ... WHERE Author = @author` per author
   (`Endpoints/QuoteEndpoints.cs`, `/performance/author-quotes`). For the
   default `authors=50` this is 51 sequential round trips to the database
   instead of 1–2. Each round trip pays connection/command overhead and,
   critically, is `await`ed serially inside the `foreach` loop, so total
   latency scales linearly with `authors` — this is the direct cause of the
   ~400ms median / ~824ms p99 measured above at only 300 authors' worth of
   data.
2. **Missing index on `Quotes.Author`.** The `EXPLAIN QUERY PLAN` output
   shows `SCAN q` for the per-author query: with no index on `Author`,
   every one of the 50 per-author queries scans the entire `Quotes` table
   rather than doing an indexed lookup. This compounds problem 1 — instead
   of 50 cheap indexed point lookups, the endpoint does 50 full table
   scans. The cost of each scan grows with table size, so this problem gets
   worse (not just linearly, but per-request) as the `Quotes` table grows,
   independently of the N+1 shape.

## GitHub link

[Add the repository/branch/PR/commit link after publishing the work.]

## Notes for mentor

Baseline was captured on a machine without `bombardier`/`k6` and without
passwordless package installation, so `ab` (already installed) was used as
the lightweight load-testing tool instead — flagging this as a deliberate,
documented substitution rather than a fabricated bombardier/k6 run. The
system default `dotnet` on this machine is 6.0.400 and cannot build this
`net10.0` project; a .NET 10 SDK already present at `~/.dotnet` was used
instead of installing anything new. No production code was changed to
produce this baseline — the endpoint was exercised as implemented, with
9,000 synthetic quotes seeded directly into SQLite (no real content) purely
as load-test fixture data.

## What did you learn this session?

That N+1 and missing indexes compound rather than just add: the same code
with an index on `Author` would still make 51 round trips (N+1), and the
same schema with 1–2 well-shaped queries but no index would still risk a
scan on a large table — but here both problems land on the *same* 50
per-request queries, so their costs multiply rather than stack. The
`EXPLAIN QUERY PLAN` output (`SCAN q`, no `CREATE INDEX` in
`sqlite_master`) was necessary to confirm the missing-index claim instead
of assuming it from the model code alone.

## What would break this?

Increasing `authors` toward its max (100) or growing the `Quotes` table
well past 9,000 rows would make both problems worse simultaneously: more
authors means more sequential round trips (N+1), and a bigger table means
each of those round trips' full table scans gets slower. A get-well-fast
smoke test that only exercises `authors=1` against a near-empty table would
hide both problems, since a single scan over a small table is fast enough
to look fine — which is exactly why this baseline was captured with a
larger, more representative dataset and the default `authors=50` before any
attempt to fix it.
