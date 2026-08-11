using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

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
            .OrderBy(quote => quote.Id);

        var total = await query.CountAsync(cancellationToken);

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
                quote => quote.Id == id,
                cancellationToken);
    }

    public async Task<Quote> AddAsync(
        Quote quote,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(quote);

        _db.Quotes.Add(quote);

        await _db.SaveChangesAsync(cancellationToken);

        return quote;
    }

    public async Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var quote = await _db.Quotes
            .FirstOrDefaultAsync(
                quote => quote.Id == id,
                cancellationToken);

        if (quote is null)
        {
            return false;
        }

        _db.Quotes.Remove(quote);

        await _db.SaveChangesAsync(cancellationToken);

        return true;
    }
}