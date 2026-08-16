using FluentAssertions;
using QuotesApi.Models;
using QuotesApi.Services;
using Tests.Domain.TestDoubles;

namespace Tests.Domain;

/// <summary>
/// Unit tests for <see cref="CollectionService"/> using hand-written test
/// doubles for <see cref="ICollectionRepository"/> and <see cref="IClock"/>
/// (no database, no mocking framework).
/// </summary>
public class CollectionServiceTests
{
    [Fact]
    public async Task CreateAsync_WithValidNameAndOwner_ReturnsAndPersistsCollection()
    {
        // Arrange
        var repository = new InMemoryCollectionRepository();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var service = new CollectionService(repository, clock);

        // Act
        var collection = await service.CreateAsync(
            "Stoic Quotes",
            ownerId: 1,
            CancellationToken.None);

        // Assert
        collection.Name.Should().Be("Stoic Quotes");
        collection.OwnerId.Should().Be(1);
        repository.AddedCollections.Should().ContainSingle()
            .Which.Should().BeSameAs(collection);
    }

    [Fact]
    public async Task CreateAsync_WithInvalidName_ThrowsArgumentException_AndDoesNotPersist()
    {
        // Arrange
        var repository = new InMemoryCollectionRepository();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var service = new CollectionService(repository, clock);

        // Act
        var act = async () => await service.CreateAsync(
            "",
            ownerId: 1,
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
        repository.AddedCollections.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_WithAlreadyCancelledToken_ThrowsOperationCanceledException_AndDoesNotPersist()
    {
        // Arrange
        var repository = new InMemoryCollectionRepository();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var service = new CollectionService(repository, clock);
        var cancellationToken = new CancellationToken(canceled: true);

        // Act
        var act = async () => await service.CreateAsync(
            "Stoic Quotes",
            ownerId: 1,
            cancellationToken);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        repository.AddedCollections.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByIdAsync_WhenCollectionDoesNotExist_ReturnsNull()
    {
        // Arrange
        var repository = new InMemoryCollectionRepository();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var service = new CollectionService(repository, clock);

        // Act
        var result = await service.GetByIdAsync(999, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_WhenCollectionExists_ReturnsMatchingCollection()
    {
        // Arrange
        var repository = new InMemoryCollectionRepository();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var service = new CollectionService(repository, clock);
        var existingCollection = new Collection("My Quotes", 1);
        repository.Seed(id: 5, existingCollection);

        // Act
        var result = await service.GetByIdAsync(5, CancellationToken.None);

        // Assert
        result.Should().BeSameAs(existingCollection);
    }

    [Fact]
    public async Task AddQuoteAsync_UsesInjectedClockUtcNow_ToStampAddedItem()
    {
        // Arrange
        var repository = new InMemoryCollectionRepository();
        var fixedTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(fixedTime);
        var service = new CollectionService(repository, clock);
        var existingCollection = new Collection("My Quotes", 1);
        repository.Seed(id: 5, existingCollection);

        // Act
        var updatedCollection = await service.AddQuoteAsync(
            collectionId: 5,
            quoteId: 7,
            CancellationToken.None);

        // Assert
        var addedItem = updatedCollection.Items.Should().ContainSingle().Subject;
        addedItem.QuoteId.Should().Be(7);
        addedItem.AddedAt.Should().Be(fixedTime);
    }

    [Fact]
    public async Task AddQuoteAsync_WithExistingCollection_CallsRepositoryUpdateAsync()
    {
        // Arrange
        var repository = new InMemoryCollectionRepository();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var service = new CollectionService(repository, clock);
        var existingCollection = new Collection("My Quotes", 1);
        repository.Seed(id: 5, existingCollection);

        // Act
        await service.AddQuoteAsync(
            collectionId: 5,
            quoteId: 7,
            CancellationToken.None);

        // Assert
        repository.UpdateAsyncWasCalled.Should().BeTrue();
    }

    [Fact]
    public async Task AddQuoteAsync_WhenCollectionDoesNotExist_ThrowsKeyNotFoundException()
    {
        // Arrange
        var repository = new InMemoryCollectionRepository();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var service = new CollectionService(repository, clock);

        // Act
        var act = async () => await service.AddQuoteAsync(
            collectionId: 999,
            quoteId: 7,
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task RemoveQuoteAsync_WithExistingItem_RemovesItAndCallsRepositoryUpdateAsync()
    {
        // Arrange
        var repository = new InMemoryCollectionRepository();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var service = new CollectionService(repository, clock);
        var existingCollection = new Collection("My Quotes", 1);
        existingCollection.AddItem(7, DateTimeOffset.UtcNow);
        repository.Seed(id: 5, existingCollection);

        // Act
        var updatedCollection = await service.RemoveQuoteAsync(
            collectionId: 5,
            quoteId: 7,
            CancellationToken.None);

        // Assert
        updatedCollection.Items.Should().BeEmpty();
        repository.UpdateAsyncWasCalled.Should().BeTrue();
    }

    [Fact]
    public async Task RemoveQuoteAsync_WhenCollectionDoesNotExist_ThrowsKeyNotFoundException()
    {
        // Arrange
        var repository = new InMemoryCollectionRepository();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var service = new CollectionService(repository, clock);

        // Act
        var act = async () => await service.RemoveQuoteAsync(
            collectionId: 999,
            quoteId: 7,
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
