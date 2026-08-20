using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

var dbPath = Path.Combine(AppContext.BaseDirectory, "day10.db");
var connectionString = $"Data Source={dbPath}";

DbContextOptions<AppDbContext> BuildOptions() =>
    new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(connectionString)
        .Options;

await using (var setupContext = new AppDbContext(BuildOptions()))
{
    await setupContext.Database.MigrateAsync();
    await SeedQuotesAsync(setupContext, 10_000);
}

await RunIdentityResolutionDemo(BuildOptions);
await RunTrackedVsNoTrackingDemo(BuildOptions);
await RunBenchmark(BuildOptions, iterations: 5);
await RunWhenNotToUseAsNoTrackingDemo(BuildOptions);

static async Task SeedQuotesAsync(AppDbContext context, int targetCount)
{
    var existing = await context.Quotes.CountAsync();
    if (existing >= targetCount)
    {
        Console.WriteLine($"Quotes table already has {existing} rows, skipping seed.");
        return;
    }

    var authors = new[]
    {
        "Marcus Aurelius", "Seneca", "Epictetus", "Lao Tzu",
        "Confucius", "Rumi", "Voltaire", "Mark Twain"
    };

    var toInsert = targetCount - existing;
    for (var i = 0; i < toInsert; i++)
    {
        var author = authors[i % authors.Length];
        var quote = Quote.Create(
            author,
            $"Benchmark quote number {existing + i + 1} attributed to {author}.");
        context.Quotes.Add(quote);

        if ((i + 1) % 1000 == 0)
        {
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
        }
    }

    await context.SaveChangesAsync();
    context.ChangeTracker.Clear();
    Console.WriteLine($"Seeded {toInsert} quotes, table now has {targetCount} rows.");
}

static async Task RunIdentityResolutionDemo(Func<DbContextOptions<AppDbContext>> buildOptions)
{
    Console.WriteLine();
    Console.WriteLine("=== Identity resolution ===");

    await using var trackedContext = new AppDbContext(buildOptions());
    var sampleId = await trackedContext.Quotes.Select(q => q.Id).FirstAsync();

    var trackedRows = await trackedContext.Quotes
        .FromSqlInterpolated(
            $"SELECT * FROM Quotes WHERE Id = {sampleId} UNION ALL SELECT * FROM Quotes WHERE Id = {sampleId}")
        .ToListAsync();

    Console.WriteLine($"Tracked query returned {trackedRows.Count} rows for Id={sampleId}");
    Console.WriteLine($"Tracked: ReferenceEquals(row0, row1) = {ReferenceEquals(trackedRows[0], trackedRows[1])}");
    Console.WriteLine($"Tracked: ChangeTracker entries for Id={sampleId} = {trackedContext.ChangeTracker.Entries<Quote>().Count(e => e.Entity.Id == sampleId)}");

    await using var noTrackingContext = new AppDbContext(buildOptions());
    var noTrackingRows = await noTrackingContext.Quotes
        .FromSqlInterpolated(
            $"SELECT * FROM Quotes WHERE Id = {sampleId} UNION ALL SELECT * FROM Quotes WHERE Id = {sampleId}")
        .AsNoTracking()
        .ToListAsync();

    Console.WriteLine($"AsNoTracking query returned {noTrackingRows.Count} rows for Id={sampleId}");
    Console.WriteLine($"AsNoTracking: ReferenceEquals(row0, row1) = {ReferenceEquals(noTrackingRows[0], noTrackingRows[1])}");
    Console.WriteLine($"AsNoTracking: ChangeTracker entries = {noTrackingContext.ChangeTracker.Entries<Quote>().Count()}");
}

static async Task RunTrackedVsNoTrackingDemo(Func<DbContextOptions<AppDbContext>> buildOptions)
{
    Console.WriteLine();
    Console.WriteLine("=== Tracked vs AsNoTracking on repeated reads ===");

    await using var trackedContext = new AppDbContext(buildOptions());
    var id = await trackedContext.Quotes.Select(q => q.Id).FirstAsync();

    var firstTrackedRead = await trackedContext.Quotes.FirstAsync(q => q.Id == id);
    var secondTrackedRead = await trackedContext.Quotes.FirstAsync(q => q.Id == id);
    Console.WriteLine($"Tracked: ReferenceEquals(firstRead, secondRead) = {ReferenceEquals(firstTrackedRead, secondTrackedRead)}");
    Console.WriteLine($"Tracked: ChangeTracker.Entries<Quote>().Count() = {trackedContext.ChangeTracker.Entries<Quote>().Count()}");

    await using var noTrackingContext = new AppDbContext(buildOptions());
    var firstNoTrackingRead = await noTrackingContext.Quotes.AsNoTracking().FirstAsync(q => q.Id == id);
    var secondNoTrackingRead = await noTrackingContext.Quotes.AsNoTracking().FirstAsync(q => q.Id == id);
    Console.WriteLine($"AsNoTracking: ReferenceEquals(firstRead, secondRead) = {ReferenceEquals(firstNoTrackingRead, secondNoTrackingRead)}");
    Console.WriteLine($"AsNoTracking: ChangeTracker.Entries<Quote>().Count() = {noTrackingContext.ChangeTracker.Entries<Quote>().Count()}");
}

static async Task RunBenchmark(Func<DbContextOptions<AppDbContext>> buildOptions, int iterations)
{
    Console.WriteLine();
    Console.WriteLine("=== 10,000-row benchmark: tracked vs AsNoTracking ===");

    var trackedResults = new List<(long ElapsedMs, long AllocatedBytes, int RowCount)>();
    var noTrackingResults = new List<(long ElapsedMs, long AllocatedBytes, int RowCount)>();

    await MeasureAsync(buildOptions, tracked: true);
    await MeasureAsync(buildOptions, tracked: false);

    for (var i = 0; i < iterations; i++)
    {
        trackedResults.Add(await MeasureAsync(buildOptions, tracked: true));
    }

    for (var i = 0; i < iterations; i++)
    {
        noTrackingResults.Add(await MeasureAsync(buildOptions, tracked: false));
    }

    PrintResults("Tracked", trackedResults);
    PrintResults("AsNoTracking", noTrackingResults);
}

static async Task<(long ElapsedMs, long AllocatedBytes, int RowCount)> MeasureAsync(
    Func<DbContextOptions<AppDbContext>> buildOptions,
    bool tracked)
{
    await using var context = new AppDbContext(buildOptions());

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    var stopwatch = Stopwatch.StartNew();

    var rows = tracked
        ? await context.Quotes.ToListAsync()
        : await context.Quotes.AsNoTracking().ToListAsync();

    stopwatch.Stop();
    var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

    return (stopwatch.ElapsedMilliseconds, allocatedAfter - allocatedBefore, rows.Count);
}

static void PrintResults(string label, List<(long ElapsedMs, long AllocatedBytes, int RowCount)> results)
{
    foreach (var (elapsed, allocated, rowCount) in results)
    {
        Console.WriteLine($"{label}: rows={rowCount} elapsedMs={elapsed} allocatedBytes={allocated}");
    }

    Console.WriteLine($"{label}: avgElapsedMs={results.Average(r => r.ElapsedMs):F2} avgAllocatedBytes={results.Average(r => r.AllocatedBytes):F0}");
}

static async Task RunWhenNotToUseAsNoTrackingDemo(Func<DbContextOptions<AppDbContext>> buildOptions)
{
    Console.WriteLine();
    Console.WriteLine("=== When NOT to use AsNoTracking: update through the same context ===");

    int trackedQuoteId;
    int noTrackingQuoteId;

    await using (var idContext = new AppDbContext(buildOptions()))
    {
        var ids = await idContext.Quotes.OrderBy(q => q.Id).Select(q => q.Id).Take(2).ToListAsync();
        trackedQuoteId = ids[0];
        noTrackingQuoteId = ids[1];
    }

    await using (var trackedContext = new AppDbContext(buildOptions()))
    {
        var quote = await trackedContext.Quotes.FirstAsync(q => q.Id == trackedQuoteId);
        quote.SoftDelete();
        var affected = await trackedContext.SaveChangesAsync();
        Console.WriteLine($"Tracked: SaveChangesAsync() affected {affected} row(s) after quote.SoftDelete()");
    }

    await using (var verifyContext = new AppDbContext(buildOptions()))
    {
        var persisted = await verifyContext.Quotes.AsNoTracking().FirstAsync(q => q.Id == trackedQuoteId);
        Console.WriteLine($"Tracked: reloaded IsDeleted = {persisted.IsDeleted}");
    }

    await using (var noTrackingContext = new AppDbContext(buildOptions()))
    {
        var quote = await noTrackingContext.Quotes.AsNoTracking().FirstAsync(q => q.Id == noTrackingQuoteId);
        quote.SoftDelete();
        var affected = await noTrackingContext.SaveChangesAsync();
        Console.WriteLine($"AsNoTracking: SaveChangesAsync() affected {affected} row(s) after quote.SoftDelete()");
    }

    await using (var verifyContext = new AppDbContext(buildOptions()))
    {
        var stillOriginal = await verifyContext.Quotes.AsNoTracking().FirstAsync(q => q.Id == noTrackingQuoteId);
        Console.WriteLine($"AsNoTracking: reloaded IsDeleted = {stillOriginal.IsDeleted}");
    }
}
