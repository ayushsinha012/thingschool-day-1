using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QueryTranslationDemo.Dtos;
using QuotesApi.Data;
using QuotesApi.Models;

var dbPath = Path.Combine(AppContext.BaseDirectory, "day10-task2.db");
var connectionString = $"Data Source={dbPath}";

const string TargetAuthor = "Seneca";

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

await using (var setupContext = new AppDbContext(BuildOptions()))
{
    await setupContext.Database.MigrateAsync();
    await SeedQuotesAsync(setupContext, 200);
}

await RunWholeEntityVsProjectionDemo(BuildOptions);
await RunClientSideEvaluationDemo(BuildOptions);

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
            $"Query translation demo quote number {existing + i + 1} attributed to {author}.");
        context.Quotes.Add(quote);
    }

    await context.SaveChangesAsync();
    context.ChangeTracker.Clear();
    Console.WriteLine($"Seeded {toInsert} quotes, table now has {targetCount} rows.");
}

static async Task RunWholeEntityVsProjectionDemo(Func<List<string>?, DbContextOptions<AppDbContext>> buildOptions)
{
    Console.WriteLine();
    Console.WriteLine("=== Whole entity query vs projection ===");

    var wholeEntitySql = new List<string>();
    await using var wholeEntityContext = new AppDbContext(buildOptions(wholeEntitySql));
    var wholeEntityRows = await wholeEntityContext.Quotes
        .Where(q => q.Author == TargetAuthor)
        .ToListAsync();

    Console.WriteLine($"Whole-entity query returned {wholeEntityRows.Count} row(s) for Author={TargetAuthor}");

    var projectionSql = new List<string>();
    await using var projectionContext = new AppDbContext(buildOptions(projectionSql));
    var projectedRows = await projectionContext.Quotes
        .Where(q => q.Author == TargetAuthor)
        .Select(q => new QuoteSummaryDto { Id = q.Id, Author = q.Author })
        .ToListAsync();

    Console.WriteLine($"Projected query returned {projectedRows.Count} row(s) for Author={TargetAuthor}");

    Console.WriteLine();
    Console.WriteLine("--- Whole-entity SQL ---");
    foreach (var line in wholeEntitySql.Where(l => l.Contains("SELECT")))
    {
        Console.WriteLine(line);
    }

    Console.WriteLine();
    Console.WriteLine("--- Projection SQL ---");
    foreach (var line in projectionSql.Where(l => l.Contains("SELECT")))
    {
        Console.WriteLine(line);
    }
}

static async Task RunClientSideEvaluationDemo(Func<List<string>?, DbContextOptions<AppDbContext>> buildOptions)
{
    Console.WriteLine();
    Console.WriteLine("=== Accidental client-side evaluation vs translated query ===");

    var accidentalSql = new List<string>();
    await using var accidentalContext = new AppDbContext(buildOptions(accidentalSql));

    var allRowsFetched = await accidentalContext.Quotes.ToListAsync();
    var filteredInMemory = allRowsFetched.Where(q => q.Author == TargetAuthor).ToList();

    Console.WriteLine($"Accidental: rows fetched from database = {allRowsFetched.Count}");
    Console.WriteLine($"Accidental: rows remaining after in-memory filter = {filteredInMemory.Count}");

    var fixedSql = new List<string>();
    await using var fixedContext = new AppDbContext(buildOptions(fixedSql));

    var translatedRows = await fixedContext.Quotes
        .Where(q => q.Author == TargetAuthor)
        .ToListAsync();

    Console.WriteLine($"Fixed: rows fetched from database = {translatedRows.Count}");

    Console.WriteLine();
    Console.WriteLine("--- Accidental (filter applied in memory, after ToListAsync) SQL ---");
    foreach (var line in accidentalSql.Where(l => l.Contains("SELECT")))
    {
        Console.WriteLine(line);
    }

    Console.WriteLine();
    Console.WriteLine("--- Fixed (filter translated into SQL) SQL ---");
    foreach (var line in fixedSql.Where(l => l.Contains("SELECT")))
    {
        Console.WriteLine(line);
    }
}
