# Day 10 — EF Core change tracker + AsNoTracking

## Task

This exercise demonstrates, against the real Quotes API EF Core model
(`QuotesApi.Data.AppDbContext`, `QuotesApi.Models.Quote`) and a real
10,000-row dataset:

- EF Core change tracking
- identity resolution
- tracked versus non-tracked queries
- `AsNoTracking()`
- 10,000-row read performance
- time/allocation comparison between tracking and `AsNoTracking()`
- a case where `AsNoTracking()` should not be used

The full, runnable program is
[`ChangeTrackerDemo/Program.cs`](./ChangeTrackerDemo/Program.cs). This
README documents what that program does and the actual console output
from running it — nothing below is invented or backfilled from theory.

## Azure SQL Database

The Azure SQL Database reused across Day 7–9 was inspected before
deciding how to run this exercise:

- **Database:** `thinkschool-day7`
- **Resource group:** `thinkschool-rg`
- **Region:** Central India (`centralindia`)

No credentials, passwords, connection strings, access tokens, JWT
signing keys, or other secrets are included anywhere in this document
or in the implementation files.

That database's `dbo.Quotes` table already holds a **different**,
FK-normalized schema from the Day 7 joins/CTE exercises
(`Authors`/`Quotes`/`Tags`/`QuoteTags`, 9 quote rows). Applying the
Quotes API's own EF Core migrations (a flat
`Quotes(Id, Author, Text, IsDeleted)` table) to that same database would
collide with and corrupt that existing table, and provisioning a new
Azure SQL database purely for this exercise would be an unnecessary
Azure resource. So this task's benchmark data lives in the Quotes API's
own real, already-configured SQLite database
(`ConnectionStrings:DefaultConnection` in `appsettings.json`) instead —
seeded with 10,000 real `Quote` rows created through the existing
`Quote.Create` factory. Azure SQL itself was not queried or modified by
this task's code.

## Identity Resolution

**Query used** — the same `Quote` row is returned twice from one SQL
statement (`UNION ALL` of the same `WHERE Id = @p` twice), run once
through a tracking context and once through an `AsNoTracking()`
context:

```csharp
var trackedRows = await trackedContext.Quotes
    .FromSqlInterpolated(
        $"SELECT * FROM Quotes WHERE Id = {sampleId} UNION ALL SELECT * FROM Quotes WHERE Id = {sampleId}")
    .ToListAsync();
```

**With tracking:** the query physically returned 2 rows from SQLite,
but EF Core's identity map resolved them to a single tracked instance —
`ReferenceEquals(row0, row1)` is `True`, and the change tracker holds
exactly 1 entry for that `Id`, not 2.

**Without tracking (`AsNoTracking()`):** each of the 2 rows is
materialized into its own separate `Quote` instance —
`ReferenceEquals(row0, row1)` is `False`, and the change tracker has 0
entries, since nothing was tracked.

**Actual execution result:**

```
=== Identity resolution ===
Tracked query returned 2 rows for Id=1
Tracked: ReferenceEquals(row0, row1) = True
Tracked: ChangeTracker entries for Id=1 = 1
AsNoTracking query returned 2 rows for Id=1
AsNoTracking: ReferenceEquals(row0, row1) = False
AsNoTracking: ChangeTracker entries = 0
```

## Tracking vs AsNoTracking

Two query variants, run against the same row, in the same `DbContext`:

```csharp
var firstTrackedRead = await trackedContext.Quotes.FirstAsync(q => q.Id == id);
var secondTrackedRead = await trackedContext.Quotes.FirstAsync(q => q.Id == id);
```

```csharp
var firstNoTrackingRead = await noTrackingContext.Quotes.AsNoTracking().FirstAsync(q => q.Id == id);
var secondNoTrackingRead = await noTrackingContext.Quotes.AsNoTracking().FirstAsync(q => q.Id == id);
```

**Actual result:**

```
=== Tracked vs AsNoTracking on repeated reads ===
Tracked: ReferenceEquals(firstRead, secondRead) = True
Tracked: ChangeTracker.Entries<Quote>().Count() = 1
AsNoTracking: ReferenceEquals(firstRead, secondRead) = False
AsNoTracking: ChangeTracker.Entries<Quote>().Count() = 0
```

**The difference:** with normal tracking, the second read for the same
key never produces a new object — EF hands back the exact instance
already held in the change tracker's identity map, and that instance is
what `SaveChanges()` later inspects for modifications. With
`AsNoTracking()`, every read is independent: EF builds a fresh object
graph from the row data and keeps no reference to it, so there is
nothing for `SaveChanges()` to detect or persist later.

## 10,000-row Performance Comparison

Both queries read the same 10,000-row `Quotes` table, each iteration on
a fresh `DbContext`. Elapsed time was measured with `Stopwatch`;
allocations were measured with
`GC.GetAllocatedBytesForCurrentThread()` (delta across the query call,
after a forced `GC.Collect()` baseline). One untimed warm-up call of
each kind ran first to remove first-call JIT cost; the table below is
the average of 5 timed iterations of each (Debug build, local machine,
SQLite, 10,000 rows):

| Query        |  Rows | Time (avg ms) | Allocations (bytes/run) |
| ------------ | ----: | ------------: | -----------------------: |
| Tracking     | 10000 |         78.00 |                 9,895,096 |
| AsNoTracking | 10000 |         39.40 |                 4,332,030 |

Raw per-iteration output backing the averages above (full output,
including the other sections below, is also saved in
[`results.md`](./results.md)):

```
=== 10,000-row benchmark: tracked vs AsNoTracking ===
Tracked: rows=10000 elapsedMs=91 allocatedBytes=9895096
Tracked: rows=10000 elapsedMs=82 allocatedBytes=9895096
Tracked: rows=10000 elapsedMs=82 allocatedBytes=9895096
Tracked: rows=10000 elapsedMs=66 allocatedBytes=9895096
Tracked: rows=10000 elapsedMs=69 allocatedBytes=9895096
Tracked: avgElapsedMs=78.00 avgAllocatedBytes=9895096
AsNoTracking: rows=10000 elapsedMs=33 allocatedBytes=4332088
AsNoTracking: rows=10000 elapsedMs=54 allocatedBytes=4332016
AsNoTracking: rows=10000 elapsedMs=31 allocatedBytes=4332016
AsNoTracking: rows=10000 elapsedMs=41 allocatedBytes=4332016
AsNoTracking: rows=10000 elapsedMs=38 allocatedBytes=4332016
AsNoTracking: avgElapsedMs=39.40 avgAllocatedBytes=4332030
```

On this run, `AsNoTracking()` was **~49.5% faster** on average (39.40 ms
vs 78.00 ms) and allocated **~56.2% less memory per read** (4,332,030
bytes vs 9,895,096 bytes on average — allocations are nearly identical
every iteration on each side, since no snapshotting or change-tracking
bookkeeping objects are created without tracking). These are the actual
numbers from the run above; absolute values will vary by machine and
build configuration, but the direction of the difference is real and
reproducible.

## Exercise

> Paste the two query variants, the timing/allocation difference, and
> one line on when you would NOT use AsNoTracking.

**1. The tracked query** (run in [`MeasureAsync`](./ChangeTrackerDemo/Program.cs), `tracked: true`):

```csharp
var rows = await context.Quotes.ToListAsync();
```

**2. The `AsNoTracking()` query** (same method, `tracked: false`):

```csharp
var rows = await context.Quotes.AsNoTracking().ToListAsync();
```

**3 & 4. Actual 10,000-row timing and allocation results** (5 timed
iterations per query, after 1 untimed warm-up iteration each, Debug
build, local machine, SQLite, 10,000 rows — full raw per-iteration
output is in [`results.md`](./results.md)):

| Query        |  Rows | Avg time (ms) | Avg allocations (bytes/run) |
| ------------ | ----: | ------------: | ---------------------------: |
| Tracking     | 10000 |         78.00 |                     9,895,096 |
| AsNoTracking | 10000 |         39.40 |                     4,332,030 |

**5. Timing/allocation difference:** `AsNoTracking()` was **38.60 ms
faster on average (~49.5% faster)** and allocated **5,563,066 fewer
bytes on average (~56.2% less)** than the tracked query, for the same
10,000-row read.

**6. One line on when you would NOT use `AsNoTracking()`:** don't use
it when the loaded entity needs to be modified and saved back through
that same `DbContext`, because an untracked entity is never in the
change tracker for `SaveChanges()` to detect and persist — this is
demonstrated directly in the
["When I Would NOT Use AsNoTracking"](#when-i-would-not-use-asnotracking)
section below (1 row affected with tracking vs 0 rows affected with
`AsNoTracking()`, for the identical `SoftDelete()` call).

**7. Reference to raw results:** the full, unedited console output this
section is drawn from — including the identity-resolution, tracked-vs-
`AsNoTracking()`, benchmark, and update-through-context sections — is
saved in [`results.md`](./results.md).

**8. Actual execution/validation status:** **PASS** — the queries above
were executed against a real, seeded 10,000-row SQLite `Quotes` table
through the actual `QuotesApi.Data.AppDbContext`, not simulated or
estimated; see the [Actual Execution / Validation](#actual-execution--validation)
table below for the full per-step validation breakdown.

## When I Would NOT Use AsNoTracking

I would not use `AsNoTracking()` when an entity is loaded, modified, and
saved back through the same `DbContext` in one unit of work. This was
verified directly, not just asserted:

```csharp
var quote = await trackedContext.Quotes.FirstAsync(q => q.Id == trackedQuoteId);
quote.SoftDelete();
await trackedContext.SaveChangesAsync();
```

```csharp
var quote = await noTrackingContext.Quotes.AsNoTracking().FirstAsync(q => q.Id == noTrackingQuoteId);
quote.SoftDelete();
await noTrackingContext.SaveChangesAsync();
```

**Actual result:**

```
=== When NOT to use AsNoTracking: update through the same context ===
Tracked: SaveChangesAsync() affected 1 row(s) after quote.SoftDelete()
Tracked: reloaded IsDeleted = True
AsNoTracking: SaveChangesAsync() affected 0 row(s) after quote.SoftDelete()
AsNoTracking: reloaded IsDeleted = False
```

The tracked update persisted (1 row affected, `IsDeleted` confirmed
`True` on reload). The identical `SoftDelete()` call on the
`AsNoTracking()`-loaded entity did **not** persist — `SaveChangesAsync()`
affected 0 rows, and reloading confirmed `IsDeleted` was still `False`.
The change tracker never saw the mutation because the entity was never
attached to it.

## Actual Execution / Validation

| Item | Result |
| --- | --- |
| Project/build validation | **PASS** — `dotnet build` on `ChangeTrackerDemo.csproj`, 0 errors (only a pre-existing `SQLitePCLRaw` advisory inherited from `QuotesApi.csproj`, unrelated to this task) |
| Database/query validation | **PASS** — existing SQLite migrations from `QuotesApi/Migrations` applied via `Database.MigrateAsync()`; 10,000 `Quote` rows seeded and queried through the real `AppDbContext` |
| Identity-resolution validation | **PASS** — tracked context resolved 2 physically duplicated rows to 1 tracked instance (`ReferenceEquals = True`, 1 change-tracker entry); `AsNoTracking()` context kept them as 2 distinct instances (`ReferenceEquals = False`, 0 entries) |
| Tracking query validation | **PASS** — repeated tracked reads of the same `Id` returned the same instance from the change tracker |
| AsNoTracking validation | **PASS** — repeated `AsNoTracking()` reads of the same `Id` always materialized a new instance, and left the change tracker empty |
| 10,000-row measurement | **PASS** — 5 timed iterations per mode after a warm-up iteration; real measured time and allocation numbers recorded above, not estimated |
| Final comparison | **PASS** — on this run, `AsNoTracking()` measured faster (39.40 ms vs 78.00 ms avg) and lower-allocating (4,332,030 vs 9,895,096 bytes/run) than tracking on the same 10,000-row read |
| Raw output | **PASS** — full unedited console output from this run is saved in [`results.md`](./results.md) and matches the numbers in this README |

## Remaining Day 10 Work

Day 10 Task 1 is complete. Any further Day 10 tasks beyond Task 1 have
not been started and are not covered by this README.
