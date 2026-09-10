using QuotesApi.Models;

namespace QuotesApi.Services;

public interface ICollectionService
{
    Task<Collection> CreateAsync(
        string name,
        int ownerId,
        CancellationToken cancellationToken);

    Task<Collection?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken);

    Task<Collection> AddQuoteAsync(
        int collectionId,
        int quoteId,
        CancellationToken cancellationToken);

    Task<Collection> RemoveQuoteAsync(
        int collectionId,
        int quoteId,
        CancellationToken cancellationToken);
}