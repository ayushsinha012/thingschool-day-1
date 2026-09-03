namespace QuotesApi.Caching;

/// <summary>
/// Lightweight in-process counters used to measure the HybridCache hit rate
/// and DB load for the quote-by-id hot read during Day 21's load tests. Not
/// a general-purpose metrics system - three <see cref="Interlocked"/>
/// counters and a reset, singleton-scoped so they survive across requests
/// within one process. Exposed read-only via GET /api/quotes/cache/metrics
/// and reset via POST /api/quotes/cache/metrics/reset (see
/// QuoteEndpoints.cs) so a load-test script can zero them between runs.
/// </summary>
public sealed class CacheMetrics
{
    private long _cacheRequests;
    private long _cacheMisses;
    private long _dbCommandCount;

    public long CacheRequests => Interlocked.Read(ref _cacheRequests);

    public long CacheMisses => Interlocked.Read(ref _cacheMisses);

    public long CacheHits => CacheRequests - CacheMisses;

    public long DbCommandCount => Interlocked.Read(ref _dbCommandCount);

    /// <summary>Called once per call into the cached read path.</summary>
    public void RecordCacheRequest() => Interlocked.Increment(ref _cacheRequests);

    /// <summary>
    /// Called only when HybridCache's factory delegate actually runs (i.e.
    /// both L1 and L2 missed and the DB was consulted) - see
    /// GetQuoteByIdQueryHandler.
    /// </summary>
    public void RecordCacheMiss() => Interlocked.Increment(ref _cacheMisses);

    /// <summary>
    /// Called by <see cref="QueryCountingInterceptor"/> for every DB command
    /// EF actually sends - the real "DB queries/sec" signal, independent of
    /// how many logical cache misses caused it.
    /// </summary>
    public void RecordDbCommand() => Interlocked.Increment(ref _dbCommandCount);

    public void Reset()
    {
        Interlocked.Exchange(ref _cacheRequests, 0);
        Interlocked.Exchange(ref _cacheMisses, 0);
        Interlocked.Exchange(ref _dbCommandCount, 0);
    }
}
