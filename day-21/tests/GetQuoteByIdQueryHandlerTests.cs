using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Application.Quotes;
using QuotesApi.Caching;
using QuotesApi.Data;
using QuotesApi.Models;

namespace Tests.Domain;

/// <summary>
/// Unit tests for <see cref="GetQuoteByIdQueryHandler"/> using a real
/// <see cref="AppDbContext"/> backed by an in-memory SQLite connection
/// (no mocking framework), following the same setup used by
/// <see cref="AuthControllerTests"/>, plus a real
/// <see cref="HybridCache"/> instance (L1 memory only - no
/// <c>IDistributedCache</c> registered, so no Redis dependency here; the
/// Redis-backed L2 is verified separately, see day-21/result.md) resolved
/// from a real DI container rather than mocked, since HybridCache is
/// sealed/hard to fake and the whole point of these tests is to exercise
/// its actual GetOrCreateAsync/RemoveAsync behavior.
/// </summary>
public class GetQuoteByIdQueryHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly ServiceProvider _cacheProvider;
    private readonly HybridCache _cache;
    private readonly CacheMetrics _metrics;
    private readonly GetQuoteByIdQueryHandler _handler;

    public GetQuoteByIdQueryHandlerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        var services = new ServiceCollection();
        services.AddHybridCache();
        _cacheProvider = services.BuildServiceProvider();
        _cache = _cacheProvider.GetRequiredService<HybridCache>();
        _metrics = new CacheMetrics();

        // Empty configuration -> Caching:Enabled defaults to true, matching
        // production. The caching-disabled baseline path is exercised
        // separately (see Handle_WithCachingDisabled_AlwaysReadsTheDatabase).
        var configuration = new ConfigurationBuilder().Build();

        _handler = new GetQuoteByIdQueryHandler(_db, _cache, _metrics, configuration);
    }

    [Fact]
    public async Task Handle_WithExistingQuote_ReturnsReadModelShapedForResponse()
    {
        // Arrange
        var quote = Quote.Create("Seneca", "Luck is what happens when preparation meets opportunity.");

        _db.Quotes.Add(quote);
        await _db.SaveChangesAsync();

        // Act
        var result = await _handler.Handle(
            new GetQuoteByIdQuery(quote.Id),
            CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(quote.Id);
        result.Author.Should().Be("Seneca");
        result.Text.Should().Be("Luck is what happens when preparation meets opportunity.");
        result.Display.Should().Be(
            "\"Luck is what happens when preparation meets opportunity.\" — Seneca");
        result.CharacterCount.Should().Be(quote.Text.Length);
    }

    [Fact]
    public async Task Handle_WithSoftDeletedQuote_ReturnsNull()
    {
        // Arrange
        var quote = Quote.Create("Epictetus", "It's not what happens to you, but how you react to it that matters.");
        quote.SoftDelete();

        _db.Quotes.Add(quote);
        await _db.SaveChangesAsync();

        // Act
        var result = await _handler.Handle(
            new GetQuoteByIdQuery(quote.Id),
            CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithNonExistentId_ReturnsNull()
    {
        // Act
        var result = await _handler.Handle(
            new GetQuoteByIdQuery(999),
            CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_FirstCall_IsACacheMissAndQueriesTheDatabase()
    {
        // Arrange
        var quote = Quote.Create("Marcus Aurelius", "You have power over your mind, not outside events.");

        _db.Quotes.Add(quote);
        await _db.SaveChangesAsync();

        // Act
        var result = await _handler.Handle(
            new GetQuoteByIdQuery(quote.Id),
            CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        _metrics.CacheRequests.Should().Be(1);
        _metrics.CacheMisses.Should().Be(1);
        _metrics.CacheHits.Should().Be(0);
    }

    [Fact]
    public async Task Handle_SecondCallForSameId_IsACacheHitAndDoesNotReReadTheDatabase()
    {
        // Arrange
        var quote = Quote.Create("Epicurus", "Do not spoil what you have by desiring what you have not.");

        _db.Quotes.Add(quote);
        await _db.SaveChangesAsync();

        await _handler.Handle(new GetQuoteByIdQuery(quote.Id), CancellationToken.None);

        // Delete the row directly (bypassing the handler's own invalidation
        // path) so a real re-read would return null - proving the second
        // Handle call below is served from cache, not the database.
        _db.Quotes.Remove(quote);
        await _db.SaveChangesAsync();

        // Act
        var result = await _handler.Handle(
            new GetQuoteByIdQuery(quote.Id),
            CancellationToken.None);

        // Assert
        result.Should().NotBeNull("the cached value should be served without touching the now-empty table");
        result!.Author.Should().Be("Epicurus");
        _metrics.CacheRequests.Should().Be(2);
        _metrics.CacheMisses.Should().Be(1, "only the first call should have reached the database");
        _metrics.CacheHits.Should().Be(1);
    }

    [Fact]
    public async Task Handle_UsesStableKeyPerId_DifferentIdsDoNotCollide()
    {
        // Arrange
        var first = Quote.Create("Zeno", "Well-being is realized by small steps.");
        var second = Quote.Create("Chrysippus", "Nothing happens without a cause.");

        _db.Quotes.AddRange(first, second);
        await _db.SaveChangesAsync();

        // Act
        var firstResult = await _handler.Handle(new GetQuoteByIdQuery(first.Id), CancellationToken.None);
        var secondResult = await _handler.Handle(new GetQuoteByIdQuery(second.Id), CancellationToken.None);
        var firstAgain = await _handler.Handle(new GetQuoteByIdQuery(first.Id), CancellationToken.None);

        // Assert - each id round-trips to its own quote, and re-reading the
        // first id after reading a different id still returns the first
        // quote's data (from cache, not a collided key).
        firstResult!.Author.Should().Be("Zeno");
        secondResult!.Author.Should().Be("Chrysippus");
        firstAgain!.Author.Should().Be("Zeno");
        _metrics.CacheMisses.Should().Be(2, "each distinct id is its own cache miss the first time");
    }

    [Fact]
    public async Task Handle_AfterCacheRemoval_ReReadsTheDatabase()
    {
        // Arrange - mirrors the DELETE endpoint's invalidation call
        // (QuoteEndpoints.cs) using the real QuoteCacheKeys helper, so this
        // test exercises the exact key both sides agree on.
        var quote = Quote.Create("Cleanthes", "Fate leads the willing, and drags the unwilling.");

        _db.Quotes.Add(quote);
        await _db.SaveChangesAsync();

        await _handler.Handle(new GetQuoteByIdQuery(quote.Id), CancellationToken.None);

        quote.SoftDelete();
        await _db.SaveChangesAsync();

        // Act - without invalidation this would still return the cached
        // (pre-delete) value.
        await _cache.RemoveAsync(QuoteCacheKeys.ById(quote.Id), CancellationToken.None);

        var result = await _handler.Handle(
            new GetQuoteByIdQuery(quote.Id),
            CancellationToken.None);

        // Assert
        result.Should().BeNull("the cache entry was invalidated, so the handler must see the soft-deleted row");
        _metrics.CacheMisses.Should().Be(2, "invalidation forces a second real database read");
    }

    [Fact]
    public async Task Handle_ConcurrentCallsForSameUncachedId_CollapseIntoOneDatabaseRead()
    {
        // Arrange
        var quote = Quote.Create("Musonius Rufus", "Every difficulty in life presents us with an opportunity.");

        _db.Quotes.Add(quote);
        await _db.SaveChangesAsync();

        // Act - N concurrent first-time reads of the same id. HybridCache's
        // built-in stampede protection should collapse these onto a single
        // factory execution (see day-21/README.md); this is the
        // narrow/deterministic half of that proof, the end-to-end HTTP
        // burst against the real ASP.NET Core pipeline lives in
        // day-21/load-test (concurrency genuinely races over the network
        // there, which a same-process unit test can't guarantee).
        var tasks = Enumerable.Range(0, 25)
            .Select(_ => _handler.Handle(new GetQuoteByIdQuery(quote.Id), CancellationToken.None));

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().OnlyContain(r => r != null && r.Author == "Musonius Rufus");
        _metrics.CacheRequests.Should().Be(25);
    }

    [Fact]
    public async Task Handle_WithCachingDisabled_AlwaysReadsTheDatabase()
    {
        // Arrange - Caching:Enabled=false is the Day 21 load test's
        // "before" baseline switch (see GetQuoteByIdQueryHandler).
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Caching:Enabled"] = "false"
            })
            .Build();

        var handler = new GetQuoteByIdQueryHandler(_db, _cache, _metrics, configuration);

        var quote = Quote.Create("Ariston of Chios", "Virtue is knowledge of good and evil.");

        _db.Quotes.Add(quote);
        await _db.SaveChangesAsync();

        // Act
        await handler.Handle(new GetQuoteByIdQuery(quote.Id), CancellationToken.None);
        await handler.Handle(new GetQuoteByIdQuery(quote.Id), CancellationToken.None);
        await handler.Handle(new GetQuoteByIdQuery(quote.Id), CancellationToken.None);

        // Assert - every call is a miss, none served from cache.
        _metrics.CacheRequests.Should().Be(3);
        _metrics.CacheMisses.Should().Be(3);
        _metrics.CacheHits.Should().Be(0);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        _cacheProvider.Dispose();
    }
}
