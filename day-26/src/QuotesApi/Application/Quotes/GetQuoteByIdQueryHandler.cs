using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using QuotesApi.Caching;
using QuotesApi.Data;

namespace QuotesApi.Application.Quotes;

public sealed class GetQuoteByIdQueryHandler : IRequestHandler<GetQuoteByIdQuery, QuoteReadModel?>
{
    private readonly AppDbContext _db;
    private readonly HybridCache _cache;
    private readonly CacheMetrics _metrics;
    private readonly bool _cachingEnabled;

    public GetQuoteByIdQueryHandler(
        AppDbContext db,
        HybridCache cache,
        CacheMetrics metrics,
        IConfiguration configuration)
    {
        _db = db;
        _cache = cache;
        _metrics = metrics;

        // Caching:Enabled (default true) exists solely so the Day 21
        // before/after load test can run the exact same endpoint/code path
        // with HybridCache switched off, for a genuine "before" baseline -
        // not a general feature flag, and not read anywhere else.
        _cachingEnabled = configuration.GetValue("Caching:Enabled", true);
    }

    public async Task<QuoteReadModel?> Handle(
        GetQuoteByIdQuery request,
        CancellationToken cancellationToken)
    {
        _metrics.RecordCacheRequest();

        if (!_cachingEnabled)
        {
            return await LoadFromDatabaseAsync(request.Id, cancellationToken);
        }

        // HybridCache.GetOrCreateAsync gives this hot read L1 (in-process)
        // + L2 (Redis) read-through, plus built-in single-flight stampede
        // protection: N concurrent callers that all miss the same key
        // collapse onto one execution of this factory instead of N
        // identical DB reads (see day-21/README.md - "Stampede
        // Protection"). The factory only runs on an actual miss (L1 and L2
        // both empty or expired), so LoadFromDatabaseAsync's
        // RecordCacheMiss call doubles as this endpoint's DB-load counter
        // for the Day 21 load tests.
        return await _cache.GetOrCreateAsync(
            QuoteCacheKeys.ById(request.Id),
            cancellationTokenFromCache => LoadFromDatabaseAsync(request.Id, cancellationTokenFromCache),
            cancellationToken: cancellationToken);
    }

    private async ValueTask<QuoteReadModel?> LoadFromDatabaseAsync(
        int id,
        CancellationToken cancellationToken)
    {
        _metrics.RecordCacheMiss();

        return await _db.Quotes
            .AsNoTracking()
            .Where(quote =>
                quote.Id == id &&
                !quote.IsDeleted)
            .Select(quote => new QuoteReadModel(
                quote.Id,
                quote.Author,
                quote.Text,
                "\"" + quote.Text + "\" — " + quote.Author,
                quote.Text.Length))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
