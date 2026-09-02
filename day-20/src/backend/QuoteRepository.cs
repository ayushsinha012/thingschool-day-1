using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Outbox;

namespace QuotesApi.Repositories;

public class QuoteRepository : IQuoteRepository
{
    private readonly AppDbContext _db;

    public QuoteRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<(IReadOnlyList<Quote> Items, int Total)> GetPagedAsync(
        int page,
        int size,
        string? search,
        CancellationToken cancellationToken)
    {
        if (page < 1)
        {
            throw new ArgumentException(
                "Page must be greater than or equal to 1.",
                nameof(page));
        }

        if (size < 1)
        {
            throw new ArgumentException(
                "Page size must be greater than or equal to 1.",
                nameof(size));
        }

        var query = _db.Quotes
            .AsNoTracking()
            .Where(quote => !quote.IsDeleted);

        var trimmedSearch = search?.Trim();

        if (!string.IsNullOrEmpty(trimmedSearch))
        {
            // EF.Functions.Like translates to a single SQL LIKE, matching
            // against both Author and Text so a search term can hit either
            // one - not just the author, which a naive
            // Where(q => q.Author.Contains(search)) would silently do.
            // SQLite's LIKE is case-insensitive for ASCII by default (no
            // COLLATE/ToLower() needed), and the pattern itself is built
            // from the already-trimmed term so leading/trailing whitespace
            // in the query doesn't prevent an otherwise-matching row.
            var pattern = $"%{trimmedSearch}%";

            query = query.Where(quote =>
                EF.Functions.Like(quote.Author, pattern) ||
                EF.Functions.Like(quote.Text, pattern));
        }

        query = query.OrderBy(quote => quote.Id);

        var total = await query.CountAsync(
            cancellationToken);

        var items = await query
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<Quote?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return await _db.Quotes
            .AsNoTracking()
            .FirstOrDefaultAsync(
                quote =>
                    quote.Id == id &&
                    !quote.IsDeleted,
                cancellationToken);
    }

    public async Task<Quote> AddAsync(
        Quote quote,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(quote);

        _db.Quotes.Add(quote);

        await _db.SaveChangesAsync(
            cancellationToken);

        return quote;
    }

    public async Task<Quote> AddWithOutboxMessageAsync(
        Quote quote,
        string eventType,
        Func<Quote, string> buildPayload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(quote);
        ArgumentNullException.ThrowIfNull(buildPayload);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            _db.Quotes.Add(quote);

            await _db.SaveChangesAsync(cancellationToken);

            _db.OutboxMessages.Add(new OutboxMessage
            {
                MessageId = $"quote-created-{quote.Id}",
                MessageType = eventType,
                Payload = buildPayload(quote),
                CreatedAt = DateTimeOffset.UtcNow
            });

            await _db.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);

            throw;
        }

        return quote;
    }

    public async Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var quote = await _db.Quotes
            .FirstOrDefaultAsync(
                quote =>
                    quote.Id == id &&
                    !quote.IsDeleted,
                cancellationToken);

        if (quote is null)
        {
            return false;
        }

        quote.SoftDelete();

        await _db.SaveChangesAsync(
            cancellationToken);

        return true;
    }
}