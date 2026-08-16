using QuotesApi.Models;
using QuotesApi.Repositories;

namespace QuotesApi.Services;

public class CollectionService : ICollectionService
{
    private readonly ICollectionRepository _repository;
    private readonly IClock _clock;

    public CollectionService(
        ICollectionRepository repository,
        IClock clock)
    {
        _repository = repository;
        _clock = clock;
    }

    public async Task<Collection> CreateAsync(
        string name,
        int ownerId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var collection = new Collection(name, ownerId);

        await _repository.AddAsync(
            collection,
            cancellationToken);

        return collection;
    }

    public async Task<Collection?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return await _repository.GetByIdAsync(
            id,
            cancellationToken);
    }

    public async Task<Collection> AddQuoteAsync(
        int collectionId,
        int quoteId,
        CancellationToken cancellationToken)
    {
        var collection = await _repository.GetByIdAsync(
            collectionId,
            cancellationToken);

        if (collection is null)
        {
            throw new KeyNotFoundException(
                "Collection not found.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        collection.AddItem(
            quoteId,
            _clock.UtcNow);

        await _repository.UpdateAsync(
            collection,
            cancellationToken);

        return collection;
    }

    public async Task<Collection> RemoveQuoteAsync(
        int collectionId,
        int quoteId,
        CancellationToken cancellationToken)
    {
        var collection = await _repository.GetByIdAsync(
            collectionId,
            cancellationToken);

        if (collection is null)
        {
            throw new KeyNotFoundException(
                "Collection not found.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        collection.RemoveItem(quoteId);

        await _repository.UpdateAsync(
            collection,
            cancellationToken);

        return collection;
    }
}
