namespace QuotesApi.Caching;

/// <summary>
/// Stable cache keys for quote reads. Centralized so the read path
/// (<see cref="QuotesApi.Application.Quotes.GetQuoteByIdQueryHandler"/>) and
/// every invalidation call site (the quote delete endpoint) always agree on
/// the exact same key for a given id.
/// </summary>
public static class QuoteCacheKeys
{
    public static string ById(int id) => $"quote:{id}";
}
