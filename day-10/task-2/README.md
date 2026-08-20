# Day 10 — Query translation + projections

## Task

This exercise demonstrates, against the real Quotes API EF Core model
(`QuotesApi.Data.AppDbContext`, `QuotesApi.Models.Quote`) and a real
seeded SQLite database:

- the SQL EF Core generates for a whole-entity query
- the existing EF Core logging approach (`LogTo`), enabled to capture
  that generated SQL
- rewriting that query with `.Select(x => new Dto { ... })` so only the
  required columns are fetched
- the SQL difference between the whole-entity query and the projection
- an accidental client-side evaluation that should not happen on the
  database side
- the fix, so the filtering is translated and executed by EF Core

The full, runnable program is
[`QueryTranslationDemo/Program.cs`](./QueryTranslationDemo/Program.cs).
This README documents what that program does and the actual console
output from running it — nothing below is invented or backfilled. The
full unedited output is saved in [`results.md`](./results.md).

## Database

This task reuses the Quotes API's own real, already-configured EF Core
model instead of provisioning new infrastructure: a local SQLite file
(`day10-task2.db`, gitignored) created by the existing `QuotesApi`
migrations (`Database.MigrateAsync()`) and seeded with 200 real `Quote`
rows through the existing `Quote.Create` factory — 8 authors repeated
across the rows, `"Seneca"` used as the query target throughout. No
Azure resource or other database was needed for this task, and no
credentials, connection strings, or other secrets appear anywhere in
this document or the implementation files.

## Enabling SQL logging

The existing EF Core logging approach — `DbContextOptionsBuilder.LogTo`
filtered to the `Database.Command` category — is used to capture and
print every generated SQL statement:

```csharp
DbContextOptions<AppDbContext> BuildOptions(List<string>? sqlLog = null) =>
    new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(connectionString)
        .LogTo(
            message =>
            {
                Console.WriteLine(message);
                sqlLog?.Add(message);
            },
            new[] { DbLoggerCategory.Database.Command.Name },
            LogLevel.Information)
        .Options;
```

Each demo builds its own `AppDbContext` from this factory with a fresh
`List<string>` so the SQL for each query variant can be captured and
printed separately, in addition to being logged live to the console as
it runs.

## Whole entity query vs projection

**Whole-entity query:**

```csharp
var wholeEntityRows = await wholeEntityContext.Quotes
    .Where(q => q.Author == TargetAuthor)
    .ToListAsync();
```

**Generated SQL (actual, from `results.md`):**

```sql
SELECT "q"."Id", "q"."Author", "q"."IsDeleted", "q"."Text"
FROM "Quotes" AS "q"
WHERE "q"."Author" = 'Seneca'
```

**Projection, rewritten with `.Select(x => new Dto { ... })`** (DTO is
[`Dtos/QuoteSummaryDto.cs`](./QueryTranslationDemo/Dtos/QuoteSummaryDto.cs),
holding only `Id` and `Author`):

```csharp
var projectedRows = await projectionContext.Quotes
    .Where(q => q.Author == TargetAuthor)
    .Select(q => new QuoteSummaryDto { Id = q.Id, Author = q.Author })
    .ToListAsync();
```

**Generated SQL (actual, from `results.md`):**

```sql
SELECT "q"."Id", "q"."Author"
FROM "Quotes" AS "q"
WHERE "q"."Author" = 'Seneca'
```

**The difference:** the whole-entity query selects all four mapped
columns (`Id`, `Author`, `IsDeleted`, `Text`) because it has to be able
to materialize a full `Quote`. The projection selects only `Id` and
`Author` — the two columns the `QuoteSummaryDto` actually needs —
because EF Core translates the `Select` into the column list itself
instead of fetching `IsDeleted` and `Text` and discarding them
client-side. Both queries returned the same 25 rows for
`Author = 'Seneca'`; only the column list in the generated `SELECT`
differs.

## Accidental client-side evaluation, and the fix

**Accidental (the whole table is pulled into memory, then filtered in
client-side LINQ-to-Objects, after the data has already left the
database):**

```csharp
var allRowsFetched = await accidentalContext.Quotes.ToListAsync();
var filteredInMemory = allRowsFetched.Where(q => q.Author == TargetAuthor).ToList();
```

**Generated SQL — no `WHERE` clause at all, because the filter never
reached the query:**

```sql
SELECT "q"."Id", "q"."Author", "q"."IsDeleted", "q"."Text"
FROM "Quotes" AS "q"
```

**Actual result:** 200 rows fetched from the database, then narrowed
to 25 in application memory — every row and every column was
transferred over the wire before 175 of those rows were thrown away.

**Fixed — the same `.Where(...)` moved before `ToListAsync()`, so it is
part of the `IQueryable` and gets translated into SQL:**

```csharp
var translatedRows = await fixedContext.Quotes
    .Where(q => q.Author == TargetAuthor)
    .ToListAsync();
```

**Generated SQL:**

```sql
SELECT "q"."Id", "q"."Author", "q"."IsDeleted", "q"."Text"
FROM "Quotes" AS "q"
WHERE "q"."Author" = 'Seneca'
```

**Actual result:** 25 rows fetched from the database — the fix is not
just fewer rows arriving in the app, it's fewer rows leaving SQLite in
the first place, which is the actual cost `AsNoTracking`-style
optimizations can't fix once the whole table has already been read.

(EF Core 3.0+ throws at runtime for most predicates that genuinely
cannot be translated, rather than silently falling back to a client
evaluation warning as EF Core 2.x did. The pattern demonstrated here —
materializing the full table with `ToListAsync()` and then filtering
the resulting `List<Quote>` with LINQ-to-Objects — is the realistic,
current-EF-Core shape of this mistake: syntactically valid, runs
without error or warning, and still moves the entire table's worth of
rows and columns off the database for no reason.)

## Exercise

> Paste: the original SQL EF generated, the projected query + its
> leaner SQL, and the client-eval you caught and fixed.

**1. Original generated SQL** (for
`Quotes.Where(q => q.Author == TargetAuthor).ToListAsync()`, actual
output from [`results.md`](./results.md#1-original-sql-generated-for-the-whole-entity-query)):

```sql
SELECT "q"."Id", "q"."Author", "q"."IsDeleted", "q"."Text"
FROM "Quotes" AS "q"
WHERE "q"."Author" = 'Seneca'
```

**2. Projected query and its leaner generated SQL** (actual output from
[`results.md`](./results.md#2-the-projected-query--selectx--new-dto--)):

```csharp
var projectedRows = await projectionContext.Quotes
    .Where(q => q.Author == TargetAuthor)
    .Select(q => new QuoteSummaryDto { Id = q.Id, Author = q.Author })
    .ToListAsync();
```

```sql
SELECT "q"."Id", "q"."Author"
FROM "Quotes" AS "q"
WHERE "q"."Author" = 'Seneca'
```

**3. Client-side evaluation caught, and the fix** (actual output from
[`results.md`](./results.md#5-accidental-client-side-evaluation-found)):

Caught — the whole table fetched, then filtered in memory, no `WHERE`
clause generated (200 rows fetched, 25 kept after the in-memory
filter):

```csharp
var allRowsFetched = await accidentalContext.Quotes.ToListAsync();
var filteredInMemory = allRowsFetched.Where(q => q.Author == TargetAuthor).ToList();
```

```sql
SELECT "q"."Id", "q"."Author", "q"."IsDeleted", "q"."Text"
FROM "Quotes" AS "q"
```

Fixed — `.Where(...)` moved before `ToListAsync()`, translated into the
SQL `WHERE` clause (25 rows fetched directly):

```csharp
var translatedRows = await fixedContext.Quotes
    .Where(q => q.Author == TargetAuthor)
    .ToListAsync();
```

```sql
SELECT "q"."Id", "q"."Author", "q"."IsDeleted", "q"."Text"
FROM "Quotes" AS "q"
WHERE "q"."Author" = 'Seneca'
```

## Actual Execution / Validation

Full raw, unedited console output for every query and result above —
including the `dotnet run` timestamps — is in
[`results.md`](./results.md).


| Item | Result |
| --- | --- |
| Project/build validation | **PASS** — `dotnet build` on `QueryTranslationDemo.csproj`, 0 errors (only pre-existing `SQLitePCLRaw` / NuGet-offline advisories inherited from `QuotesApi.csproj`, unrelated to this task) |
| Database/query validation | **PASS** — existing SQLite migrations from `QuotesApi/Migrations` applied via `Database.MigrateAsync()`; 200 `Quote` rows seeded and queried through the real `AppDbContext` |
| SQL logging | **PASS** — `LogTo` with `DbLoggerCategory.Database.Command` captured and printed every generated statement live |
| Whole-entity vs projection | **PASS** — real generated SQL differs exactly as described (4 columns vs 2), both returning 25 rows |
| Client-side evaluation demo | **PASS** — the accidental query fetched all 200 rows with no `WHERE` clause, confirmed by the logged SQL and the printed row counts |
| Fix validation | **PASS** — the translated query's logged SQL includes `WHERE "q"."Author" = 'Seneca'` and returned 25 rows directly from the database |
| Raw output | **PASS** — full console output from this run is saved in [`results.md`](./results.md) and matches the SQL and counts in this README |
