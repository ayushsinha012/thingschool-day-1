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
        var query = _db.Quotes
            .AsNoTracking()
            .OrderBy(q => q.Id);

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
            .FirstOrDefaultAsync(q => q.Id == id, cancellationToken);
    }

    public async Task<Quote> AddAsync(
        Quote quote,
        CancellationToken cancellationToken)
    {
        _db.Quotes.Add(quote);
        await _db.SaveChangesAsync(cancellationToken);

        return quote;
    }

    public async Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var quote = await _db.Quotes
            .FirstOrDefaultAsync(q => q.Id == id, cancellationToken);

        if (quote is null)
            return false;

        _db.Quotes.Remove(quote);
        await _db.SaveChangesAsync(cancellationToken);

        return true;
    }
}