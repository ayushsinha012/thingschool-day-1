using QuotesApi.Models;

namespace QuotesApi.Repositories;

public interface IQuoteRepository
{
    Task<(IReadOnlyList<Quote> Items, int Total)> GetPagedAsync(
        int page,
        int size,
        CancellationToken cancellationToken);

    Task<Quote?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken);

    Task<Quote> AddAsync(
        Quote quote,
        CancellationToken cancellationToken);

    Task<Quote> AddWithOutboxMessageAsync(
        Quote quote,
        string eventType,
        Func<Quote, string> buildPayload,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken);

    // Returns the updated Quote, or null if no non-deleted quote exists with
    // that id (mirrors DeleteAsync's bool-for-"not found" convention, but
    // needs to hand the caller the updated Author/Text back).
    Task<Quote?> UpdateAsync(
        int id,
        string author,
        string text,
        CancellationToken cancellationToken);
}