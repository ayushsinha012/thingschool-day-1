# Day 10 Task 1 — Raw Execution Results

This file is the raw console output from an actual run of
[`ChangeTrackerDemo/Program.cs`](./ChangeTrackerDemo/Program.cs), captured
directly from `dotnet run`. Nothing in this file is estimated or
backfilled — it is copy-pasted terminal output. [`README.md`](./README.md)
summarizes and explains this run; the numbers there are drawn from here.

## Environment

- **SDK:** `dotnet 10.0.302` (net10.0 target)
- **Build configuration:** Debug
- **Database:** SQLite (`day10.db`, seeded with 10,000 `Quote` rows via
  `QuotesApi.Data.AppDbContext` and the real `Quote.Create` factory)
- **Machine:** local development machine (Linux), results are relative,
  not absolute — re-running will produce different but directionally
  similar numbers

## Full console output

```
Seeded 10000 quotes, table now has 10000 rows.

=== Identity resolution ===
Tracked query returned 2 rows for Id=1
Tracked: ReferenceEquals(row0, row1) = True
Tracked: ChangeTracker entries for Id=1 = 1
AsNoTracking query returned 2 rows for Id=1
AsNoTracking: ReferenceEquals(row0, row1) = False
AsNoTracking: ChangeTracker entries = 0

=== Tracked vs AsNoTracking on repeated reads ===
Tracked: ReferenceEquals(firstRead, secondRead) = True
Tracked: ChangeTracker.Entries<Quote>().Count() = 1
AsNoTracking: ReferenceEquals(firstRead, secondRead) = False
AsNoTracking: ChangeTracker.Entries<Quote>().Count() = 0

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

=== When NOT to use AsNoTracking: update through the same context ===
Tracked: SaveChangesAsync() affected 1 row(s) after quote.SoftDelete()
Tracked: reloaded IsDeleted = True
AsNoTracking: SaveChangesAsync() affected 0 row(s) after quote.SoftDelete()
AsNoTracking: reloaded IsDeleted = False
```

## Summary table (from the benchmark section above)

| Query        |  Rows | Avg time (ms) | Avg allocations (bytes/run) |
| ------------ | ----: | ------------: | ---------------------------: |
| Tracking     | 10000 |         78.00 |                     9,895,096 |
| AsNoTracking | 10000 |         39.40 |                     4,332,030 |

- **Time difference:** AsNoTracking was 38.60 ms faster on average
  (~49.5% faster than tracking).
- **Allocation difference:** AsNoTracking allocated 5,563,066 fewer
  bytes per run on average (~56.2% less than tracking).

No credentials, connection strings, tokens, or other secrets are
present in this file or in the run that produced it — the database is
a local SQLite file (`day10.db`, gitignored) seeded with synthetic
benchmark quotes, not real user data.
