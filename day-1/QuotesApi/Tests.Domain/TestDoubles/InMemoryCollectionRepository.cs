using QuotesApi.Models;
using QuotesApi.Repositories;

namespace Tests.Domain.TestDoubles;

/// <summary>
/// Hand-written in-memory test double for <see cref="ICollectionRepository"/>.
/// Holds only what CollectionService tests need: the ability to seed an
/// existing collection under a given id, and flags/lists that let a test
/// observe which repository methods were actually invoked. No database.
/// </summary>
public sealed class InMemoryCollectionRepository : ICollectionRepository
{
    private readonly Dictionary<int, Collection> _collectionsById = new();

    public List<Collection> AddedCollections { get; } = new();

    public bool UpdateAsyncWasCalled { get; private set; }

    public bool DeleteAsyncWasCalled { get; private set; }

    public void Seed(int id, Collection collection)
    {
        _collectionsById[id] = collection;
    }

    public Task<Collection?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        _collectionsById.TryGetValue(id, out var collection);

        return Task.FromResult(collection);
    }

    public Task AddAsync(
        Collection collection,
        CancellationToken cancellationToken)
    {
        AddedCollections.Add(collection);

        return Task.CompletedTask;
    }

    public Task UpdateAsync(
        Collection collection,
        CancellationToken cancellationToken)
    {
        UpdateAsyncWasCalled = true;

        return Task.CompletedTask;
    }

    public Task DeleteAsync(
        Collection collection,
        CancellationToken cancellationToken)
    {
        DeleteAsyncWasCalled = true;

        return Task.CompletedTask;
    }
}
