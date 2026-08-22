using System.Diagnostics;
using System.Runtime.CompilerServices;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Application.Quotes;
using QuotesApi.Data;

// Benchmarks the API's existing "get one quote by id" read against its real,
// already-seeded SQLite database - once through the existing EF Core
// GetQuoteByIdQueryHandler (Day 12 Task 1's CQRS read side, called directly
// and unmodified) and once through an equivalent hand-written Dapper query
// that returns the same QuoteReadModel shape. Nothing here reimplements the
// EF Core query logic; the Dapper path is the only new query.
//
// Why this query: it is a single-row, primary-key lookup (`WHERE Id = @id`)
// against an indexed column with no joins and no pagination, which is the
// simplest possible read the API exposes. That isolates ORM/mapping
// overhead as the main variable between the two approaches instead of
// letting query-plan differences (joins, scans, sorting) dominate the
// comparison, and it is the query Task 1 already split out onto its own
// CQRS read side.
const int WarmupIterations = 50;
const int MeasuredIterations = 1000;

var quotesDbPath = ResolveQuotesDbPath();

if (!File.Exists(quotesDbPath))
{
    Console.Error.WriteLine($"Quotes database not found at {quotesDbPath}.");
    Console.Error.WriteLine("Run the API once from day-1/QuotesApi (dotnet run) so migrations/seed apply, then re-run this benchmark.");
    return 1;
}

// Mode=ReadOnly: this benchmark only ever selects rows. Opening the shared
// dev database read-only makes it impossible for this tool to accidentally
// write to or lock the file the API itself uses.
var connectionString = $"Data Source={quotesDbPath};Mode=ReadOnly";

Console.WriteLine("EF Core vs Dapper - GetQuoteById read benchmark");
Console.WriteLine("================================================");
Console.WriteLine($"Database: {quotesDbPath} (opened read-only)");

var (minId, maxId, rowCount) = GetIdRange(connectionString);
Console.WriteLine($"Quotes table: {rowCount} row(s), Id range [{minId}, {maxId}]");
Console.WriteLine($"Warmup iterations: {WarmupIterations}, measured iterations: {MeasuredIterations}");
Console.WriteLine();

var efOptions = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlite(connectionString)
    .Options;

// Deterministic id sequence (fixed seed) shared by both approaches, sampled
// across the whole table rather than repeatedly hitting one row, so page-
// cache locality is identical for both sides.
var randomIds = GenerateIds(minId, maxId, WarmupIterations + MeasuredIterations, seed: 42);

await CorrectnessCheckAsync(efOptions, connectionString, randomIds.Take(20).ToArray());

Console.WriteLine("Correctness check passed: EF Core and Dapper returned identical QuoteReadModel values for 20 sample ids.");
Console.WriteLine();

var efSamplesUs = new List<double>(MeasuredIterations);
var dapperSamplesUs = new List<double>(MeasuredIterations);

// Interleaved (EF, Dapper, EF, Dapper, ...) on the same id sequence so any
// drift over the run (GC, OS page cache warming, thermal throttling) affects
// both approaches equally instead of biasing whichever one ran first.
for (var i = 0; i < WarmupIterations + MeasuredIterations; i++)
{
    var id = randomIds[i];
    var isWarmup = i < WarmupIterations;

    var efUs = await TimeEfLookupAsync(efOptions, id);
    var dapperUs = await TimeDapperLookupAsync(connectionString, id);

    if (!isWarmup)
    {
        efSamplesUs.Add(efUs);
        dapperSamplesUs.Add(dapperUs);
    }
}

PrintStats("EF Core (GetQuoteByIdQueryHandler)", efSamplesUs);
PrintStats("Dapper (hand-written SQL)", dapperSamplesUs);

Console.WriteLine();
Console.WriteLine("Raw CSV (iteration,ef_us,dapper_us):");
for (var i = 0; i < MeasuredIterations; i++)
{
    Console.WriteLine($"{i + 1},{efSamplesUs[i]:F1},{dapperSamplesUs[i]:F1}");
}

return 0;

static string ResolveQuotesDbPath([CallerFilePath] string sourceFile = "")
{
    // Anchored to this source file's location (not the process working
    // directory) so `dotnet run` behaves the same regardless of where it
    // is invoked from. Three levels up from
    // Day-12/task-2/EfVsDapperBenchmark/ is the repository root.
    var projectDir = Path.GetDirectoryName(sourceFile)!;
    var repoRoot = Path.GetFullPath(Path.Combine(projectDir, "..", "..", ".."));
    return Path.Combine(repoRoot, "day-1", "QuotesApi", "quotes.db");
}

static (long MinId, long MaxId, long RowCount) GetIdRange(string connectionString)
{
    using var connection = new SqliteConnection(connectionString);
    connection.Open();

    var row = connection.QuerySingle<IdRangeRow>(
        "SELECT MIN(Id) AS MinId, MAX(Id) AS MaxId, COUNT(*) AS RowCount " +
        "FROM Quotes WHERE IsDeleted = 0;");

    return (row.MinId, row.MaxId, row.RowCount);
}

static int[] GenerateIds(long minId, long maxId, int count, int seed)
{
    var random = new Random(seed);
    var ids = new int[count];

    for (var i = 0; i < count; i++)
    {
        ids[i] = (int)random.NextInt64(minId, maxId + 1);
    }

    return ids;
}

static async Task CorrectnessCheckAsync(
    DbContextOptions<AppDbContext> efOptions,
    string connectionString,
    int[] sampleIds)
{
    foreach (var id in sampleIds)
    {
        await using var db = new AppDbContext(efOptions);
        var efHandler = new GetQuoteByIdQueryHandler(db);
        var efResult = await efHandler.Handle(new GetQuoteByIdQuery(id), CancellationToken.None);

        var dapperResult = await GetQuoteByIdDapperAsync(connectionString, id);

        if (efResult != dapperResult)
        {
            throw new InvalidOperationException(
                $"Mismatch for id {id}: EF returned {efResult}, Dapper returned {dapperResult}.");
        }
    }
}

static async Task<double> TimeEfLookupAsync(DbContextOptions<AppDbContext> efOptions, int id)
{
    // A fresh AppDbContext per call, matching the API's real per-request
    // scoped DbContext lifetime rather than reusing one context for the
    // whole benchmark run.
    var stopwatch = Stopwatch.StartNew();

    await using var db = new AppDbContext(efOptions);
    var handler = new GetQuoteByIdQueryHandler(db);
    _ = await handler.Handle(new GetQuoteByIdQuery(id), CancellationToken.None);

    stopwatch.Stop();

    return stopwatch.Elapsed.TotalMicroseconds;
}

static async Task<double> TimeDapperLookupAsync(string connectionString, int id)
{
    var stopwatch = Stopwatch.StartNew();

    _ = await GetQuoteByIdDapperAsync(connectionString, id);

    stopwatch.Stop();

    return stopwatch.Elapsed.TotalMicroseconds;
}

/// <summary>
/// Dapper equivalent of <see cref="GetQuoteByIdQueryHandler"/>: same filter
/// (id match, not soft-deleted), same three source columns, same
/// <see cref="QuoteReadModel"/> shape (Display/CharacterCount computed the
/// same way), so the two approaches are compared doing identical work - the
/// only difference is EF Core's LINQ-to-SQL translation and change-tracker
/// setup versus a hand-written parameterized query mapped directly with
/// Dapper.
/// </summary>
static async Task<QuoteReadModel?> GetQuoteByIdDapperAsync(string connectionString, int id)
{
    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();

    var row = await connection.QueryFirstOrDefaultAsync<QuoteRow>(
        "SELECT Id, Author, Text FROM Quotes WHERE Id = @Id AND IsDeleted = 0;",
        new { Id = id });

    if (row is null)
    {
        return null;
    }

    return new QuoteReadModel(
        row.Id,
        row.Author,
        row.Text,
        "\"" + row.Text + "\" — " + row.Author,
        row.Text.Length);
}

static void PrintStats(string label, List<double> samplesUs)
{
    var sorted = samplesUs.OrderBy(x => x).ToList();
    var count = sorted.Count;

    double Percentile(double p)
    {
        var rank = p * (count - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        if (lower == upper)
        {
            return sorted[lower];
        }

        var fraction = rank - lower;
        return sorted[lower] + (sorted[upper] - sorted[lower]) * fraction;
    }

    Console.WriteLine($"{label}:");
    Console.WriteLine($"  min    = {sorted[0]:F1} us");
    Console.WriteLine($"  mean   = {sorted.Average():F1} us");
    Console.WriteLine($"  p50    = {Percentile(0.50):F1} us");
    Console.WriteLine($"  p95    = {Percentile(0.95):F1} us");
    Console.WriteLine($"  p99    = {Percentile(0.99):F1} us");
    Console.WriteLine($"  max    = {sorted[^1]:F1} us");
    Console.WriteLine();
}

file sealed record QuoteRow(int Id, string Author, string Text);

file sealed record IdRangeRow(long MinId, long MaxId, long RowCount);
