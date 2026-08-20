# Day 10 Task 2 — Raw Execution Results

This file is the raw console output from an actual run of
[`QueryTranslationDemo/Program.cs`](./QueryTranslationDemo/Program.cs),
captured directly from `dotnet run`. Nothing in this file is invented,
estimated, or backfilled — it is copy-pasted terminal output.
[`README.md`](./README.md) explains what this run demonstrates.

## Environment

- **SDK:** `dotnet 10.0.302` (net10.0 target)
- **Build:** `dotnet build` — **Build succeeded, 0 Error(s)** (4 pre-existing
  `SQLitePCLRaw`/NuGet-offline advisory warnings inherited from
  `QuotesApi.csproj`, unrelated to this task)
- **Database:** SQLite (`day10-task2.db`, gitignored, lives next to the
  built binaries), created by the real `QuotesApi` EF Core migrations
  via `Database.MigrateAsync()` and seeded with 200 real `Quote` rows
  through the existing `Quote.Create` factory (8 authors repeated
  across the rows)
- **Run captured below:** `dotnet run --no-build`, 2026-08-20 12:50:52

On this run the database already existed from an earlier run of the
same program, so seeding was skipped idempotently instead of
re-inserting — this is itself real, actual output (`SeedQuotesAsync`
checks `Quotes.CountAsync()` first), not a difference in behavior:

```
SELECT COUNT(*) FROM "sqlite_master" WHERE "name" = '__EFMigrationsLock' AND "type" = 'table';
INSERT OR IGNORE INTO "__EFMigrationsLock"("Id", "Timestamp") VALUES(1, '2026-08-20 07:20:52.3448313+00:00');
SELECT changes();
CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (...)
SELECT COUNT(*) FROM "sqlite_master" WHERE "name" = '__EFMigrationsHistory' AND "type" = 'table';
SELECT "MigrationId", "ProductVersion" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";
DELETE FROM "__EFMigrationsLock";
SELECT COUNT(*)
FROM "Quotes" AS "q"
```

```
Quotes table already has 200 rows, skipping seed.
```

## 1. Original SQL generated for the whole-entity query

**Query:**

```csharp
var wholeEntityRows = await wholeEntityContext.Quotes
    .Where(q => q.Author == TargetAuthor)
    .ToListAsync();
```

**Actual generated SQL (from the run above):**

```
info: 08/20/2026 12:50:54.924 RelationalEventId.CommandExecuted[20101] (Microsoft.EntityFrameworkCore.Database.Command)
      Executed DbCommand (0ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      SELECT "q"."Id", "q"."Author", "q"."IsDeleted", "q"."Text"
      FROM "Quotes" AS "q"
      WHERE "q"."Author" = 'Seneca'
```

**Actual result:** `Whole-entity query returned 25 row(s) for Author=Seneca`

## 2. The projected query — `.Select(x => new Dto { ... })`

**Query** (`QuoteSummaryDto` is
[`Dtos/QuoteSummaryDto.cs`](./QueryTranslationDemo/Dtos/QuoteSummaryDto.cs),
holding only `Id` and `Author`):

```csharp
var projectedRows = await projectionContext.Quotes
    .Where(q => q.Author == TargetAuthor)
    .Select(q => new QuoteSummaryDto { Id = q.Id, Author = q.Author })
    .ToListAsync();
```

## 3. Actual SQL generated for the projected query

```
info: 08/20/2026 12:50:55.083 RelationalEventId.CommandExecuted[20101] (Microsoft.EntityFrameworkCore.Database.Command)
      Executed DbCommand (0ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      SELECT "q"."Id", "q"."Author"
      FROM "Quotes" AS "q"
      WHERE "q"."Author" = 'Seneca'
```

**Actual result:** `Projected query returned 25 row(s) for Author=Seneca`

## 4. The difference — projection fetches only the required columns

| | Whole-entity SQL | Projection SQL |
| --- | --- | --- |
| Columns selected | `Id, Author, IsDeleted, Text` (4) | `Id, Author` (2) |
| Rows returned | 25 | 25 |

Both queries hit the same table with the same `WHERE "q"."Author" = 'Seneca'`
filter and returned the same 25 rows. The only difference is the
column list in the generated `SELECT` — the whole-entity query pulls
`IsDeleted` and `Text` off the disk on every row even though nothing in
that code path uses them; the projection's `SELECT` never asks SQLite
for those two columns at all, because EF Core translated the
`.Select(q => new QuoteSummaryDto { ... })` into the column list
itself instead of materializing a full `Quote` and discarding fields
client-side.

## 5. Accidental client-side evaluation found

**Code (fetches the whole table, then filters the in-memory `List<Quote>`
with LINQ-to-Objects, after the data has already left the database):**

```csharp
var allRowsFetched = await accidentalContext.Quotes.ToListAsync();
var filteredInMemory = allRowsFetched.Where(q => q.Author == TargetAuthor).ToList();
```

**Actual generated SQL — no `WHERE` clause, because the filter never
reached the query:**

```
info: 08/20/2026 12:50:55.092 RelationalEventId.CommandExecuted[20101] (Microsoft.EntityFrameworkCore.Database.Command)
      Executed DbCommand (0ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      SELECT "q"."Id", "q"."Author", "q"."IsDeleted", "q"."Text"
      FROM "Quotes" AS "q"
```

**Actual result:**

```
Accidental: rows fetched from database = 200
Accidental: rows remaining after in-memory filter = 25
```

All 200 rows and all 4 columns were pulled out of SQLite before 175 of
those rows were discarded in application memory.

## 6. Corrected query/fix

**Fix — the same `.Where(...)` moved before `ToListAsync()`, so it is
part of the `IQueryable` and gets translated into SQL instead of
running after materialization:**

```csharp
var translatedRows = await fixedContext.Quotes
    .Where(q => q.Author == TargetAuthor)
    .ToListAsync();
```

## 7. Actual result/output after the fix

**Actual generated SQL:**

```
info: 08/20/2026 12:50:55.099 RelationalEventId.CommandExecuted[20101] (Microsoft.EntityFrameworkCore.Database.Command)
      Executed DbCommand (0ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      SELECT "q"."Id", "q"."Author", "q"."IsDeleted", "q"."Text"
      FROM "Quotes" AS "q"
      WHERE "q"."Author" = 'Seneca'
```

**Actual result:** `Fixed: rows fetched from database = 25`

## 8. What changed and why

The only code change was moving `.Where(q => q.Author == TargetAuthor)`
from after `ToListAsync()` to before it. That is the difference
between filtering an in-memory `List<Quote>` with LINQ-to-Objects (the
accidental version — no `WHERE` clause, 200 rows and all 4 columns
transferred) and filtering an `IQueryable<Quote>` that EF Core still
gets to translate (the fixed version — `WHERE "q"."Author" = 'Seneca'`
in the actual SQL, 25 rows transferred). The row count returned to the
caller was identical either way (25); what changed is how much data
SQLite had to read and send before that filtering happened — the fix
removes the 175 rows and their `IsDeleted`/`Text` columns that the
accidental version pulled across the wire for no reason.

## Full unedited demo output

```
=== Whole entity query vs projection ===
info: 08/20/2026 12:50:54.924 RelationalEventId.CommandExecuted[20101] (Microsoft.EntityFrameworkCore.Database.Command)
      Executed DbCommand (0ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      SELECT "q"."Id", "q"."Author", "q"."IsDeleted", "q"."Text"
      FROM "Quotes" AS "q"
      WHERE "q"."Author" = 'Seneca'
Whole-entity query returned 25 row(s) for Author=Seneca
info: 08/20/2026 12:50:55.083 RelationalEventId.CommandExecuted[20101] (Microsoft.EntityFrameworkCore.Database.Command)
      Executed DbCommand (0ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      SELECT "q"."Id", "q"."Author"
      FROM "Quotes" AS "q"
      WHERE "q"."Author" = 'Seneca'
Projected query returned 25 row(s) for Author=Seneca

--- Whole-entity SQL ---
info: 08/20/2026 12:50:54.924 RelationalEventId.CommandExecuted[20101] (Microsoft.EntityFrameworkCore.Database.Command)
      Executed DbCommand (0ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      SELECT "q"."Id", "q"."Author", "q"."IsDeleted", "q"."Text"
      FROM "Quotes" AS "q"
      WHERE "q"."Author" = 'Seneca'

--- Projection SQL ---
info: 08/20/2026 12:50:55.083 RelationalEventId.CommandExecuted[20101] (Microsoft.EntityFrameworkCore.Database.Command)
      Executed DbCommand (0ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      SELECT "q"."Id", "q"."Author"
      FROM "Quotes" AS "q"
      WHERE "q"."Author" = 'Seneca'

=== Accidental client-side evaluation vs translated query ===
info: 08/20/2026 12:50:55.092 RelationalEventId.CommandExecuted[20101] (Microsoft.EntityFrameworkCore.Database.Command)
      Executed DbCommand (0ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      SELECT "q"."Id", "q"."Author", "q"."IsDeleted", "q"."Text"
      FROM "Quotes" AS "q"
Accidental: rows fetched from database = 200
Accidental: rows remaining after in-memory filter = 25
info: 08/20/2026 12:50:55.099 RelationalEventId.CommandExecuted[20101] (Microsoft.EntityFrameworkCore.Database.Command)
      Executed DbCommand (0ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      SELECT "q"."Id", "q"."Author", "q"."IsDeleted", "q"."Text"
      FROM "Quotes" AS "q"
      WHERE "q"."Author" = 'Seneca'
Fixed: rows fetched from database = 25

--- Accidental (filter applied in memory, after ToListAsync) SQL ---
info: 08/20/2026 12:50:55.092 RelationalEventId.CommandExecuted[20101] (Microsoft.EntityFrameworkCore.Database.Command)
      Executed DbCommand (0ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      SELECT "q"."Id", "q"."Author", "q"."IsDeleted", "q"."Text"
      FROM "Quotes" AS "q"

--- Fixed (filter translated into SQL) SQL ---
info: 08/20/2026 12:50:55.099 RelationalEventId.CommandExecuted[20101] (Microsoft.EntityFrameworkCore.Database.Command)
      Executed DbCommand (0ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      SELECT "q"."Id", "q"."Author", "q"."IsDeleted", "q"."Text"
      FROM "Quotes" AS "q"
      WHERE "q"."Author" = 'Seneca'
```

No credentials, connection strings, tokens, or other secrets are
present in this file or in the run that produced it — the database is
a local SQLite file (`day10-task2.db`, gitignored) seeded with
synthetic demo quotes, not real user data.
