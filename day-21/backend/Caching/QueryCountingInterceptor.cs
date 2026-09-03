using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace QuotesApi.Caching;

/// <summary>
/// Counts every DB command EF actually sends to the database, via the same
/// <see cref="DbCommandInterceptor"/> extension point EF Core exposes for
/// this exact purpose - not log-scraping, and not a change to query
/// behavior. Used only to measure DB load (queries/sec) for Day 21's
/// before/after HybridCache load tests.
/// </summary>
public sealed class QueryCountingInterceptor : DbCommandInterceptor
{
    private readonly CacheMetrics _metrics;

    public QueryCountingInterceptor(CacheMetrics metrics)
    {
        _metrics = metrics;
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        _metrics.RecordDbCommand();

        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        _metrics.RecordDbCommand();

        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
