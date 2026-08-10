using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Repositories;

public class CollectionRepository : ICollectionRepository
{
    private readonly AppDbContext _db;

    public CollectionRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Collection?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return await _db.Collections
            .Include(collection => collection.Items)
            .FirstOrDefaultAsync(
                collection => collection.Id == id,
                cancellationToken);
    }

    public async Task AddAsync(
        Collection collection,
        CancellationToken cancellationToken)
    {
        _db.Collections.Add(collection);

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(
        Collection collection,
        CancellationToken cancellationToken)
    {
        _db.Collections.Update(collection);

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(
        Collection collection,
        CancellationToken cancellationToken)
    {
        _db.Collections.Remove(collection);

        await _db.SaveChangesAsync(cancellationToken);
    }
}