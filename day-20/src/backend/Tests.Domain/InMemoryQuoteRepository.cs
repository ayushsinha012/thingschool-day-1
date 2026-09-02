using QuotesApi.Models;
using QuotesApi.Repositories;

namespace Tests.Domain.TestDoubles;

public sealed class InMemoryQuoteRepository : IQuoteRepository
{
    public List<Quote> AddedQuotes { get; } = new();

    public Task<(IReadOnlyList<Quote> Items, int Total)> GetPagedAsync(
        int page,
        int size,
        string? search,
        CancellationToken cancellationToken)
    {
        var trimmedSearch = search?.Trim();

        IReadOnlyList<Quote> items = string.IsNullOrEmpty(trimmedSearch)
            ? AddedQuotes
            : AddedQuotes
                .Where(quote =>
                    quote.Author.Contains(trimmedSearch, StringComparison.OrdinalIgnoreCase) ||
                    quote.Text.Contains(trimmedSearch, StringComparison.OrdinalIgnoreCase))
                .ToList();

        return Task.FromResult<(IReadOnlyList<Quote> Items, int Total)>(
            (items, items.Count));
    }

    public Task<Quote?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(AddedQuotes.FirstOrDefault(quote => quote.Id == id));
    }

    public Task<Quote> AddAsync(
        Quote quote,
        CancellationToken cancellationToken)
    {
        AddedQuotes.Add(quote);

        return Task.FromResult(quote);
    }

    public List<(string EventType, string Payload)> OutboxMessages { get; } = new();

    public Task<Quote> AddWithOutboxMessageAsync(
        Quote quote,
        string eventType,
        Func<Quote, string> buildPayload,
        CancellationToken cancellationToken)
    {
        AddedQuotes.Add(quote);
        OutboxMessages.Add((eventType, buildPayload(quote)));

        return Task.FromResult(quote);
    }

    public Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var quote = AddedQuotes.FirstOrDefault(quote => quote.Id == id);

        if (quote is null || quote.IsDeleted)
        {
            return Task.FromResult(false);
        }

        quote.SoftDelete();

        return Task.FromResult(true);
    }
}
