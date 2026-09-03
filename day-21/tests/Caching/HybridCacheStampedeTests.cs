using FluentAssertions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Caching;

namespace Tests.Domain.Caching;

/// <summary>
/// Exercises HybridCache's own built-in single-flight/stampede-protection
/// mechanism directly (a bare HybridCache from DI, no AppDbContext), with a
/// factory that sleeps briefly so concurrent callers are guaranteed to
/// actually overlap. This is what
/// <see cref="Tests.Domain.GetQuoteByIdQueryHandlerTests.Handle_ConcurrentCallsForSameUncachedId_CollapseIntoOneDatabaseRead"/>
/// cannot fully guarantee on its own (a same-process, very fast in-memory
/// SQLite read can finish before a second concurrent call even starts) -
/// this test forces the race deterministically.
/// </summary>
public class HybridCacheStampedeTests
{
    private static HybridCache CreateCache(out ServiceProvider provider)
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        provider = services.BuildServiceProvider();

        return provider.GetRequiredService<HybridCache>();
    }

    [Fact]
    public async Task GetOrCreateAsync_WithConcurrentCallersForSameKey_RunsFactoryOnlyOnce()
    {
        // Arrange
        var cache = CreateCache(out var provider);

        using (provider)
        {
            var executions = 0;

            async ValueTask<string> Factory(CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref executions);

                // Long enough that, without stampede protection, 50
                // concurrent GetOrCreateAsync calls for the same still-empty
                // key would all observe a miss and all run this factory.
                await Task.Delay(100, cancellationToken);

                return "db-value";
            }

            // Act - 50 concurrent callers, same key, cold cache.
            var tasks = Enumerable.Range(0, 50)
                .Select(_ => cache.GetOrCreateAsync("stampede-test-key", Factory).AsTask());

            var results = await Task.WhenAll(tasks);

            // Assert
            executions.Should().Be(1, "HybridCache must collapse concurrent misses for the same key into one factory execution");
            results.Should().OnlyContain(r => r == "db-value");
        }
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenFactoryThrows_DoesNotCacheTheFailureAndRetriesOnNextCall()
    {
        // Arrange
        var cache = CreateCache(out var provider);

        using (provider)
        {
            var attempt = 0;

            async ValueTask<string> Factory(CancellationToken cancellationToken)
            {
                attempt++;

                if (attempt == 1)
                {
                    throw new InvalidOperationException("simulated database failure");
                }

                await Task.Yield();

                return "recovered-value";
            }

            // Act
            var firstCall = async () => await cache.GetOrCreateAsync("error-test-key", Factory);

            // Assert - the failed attempt must propagate, not be swallowed
            // or cached as if it were a valid (e.g. null) result.
            await firstCall.Should().ThrowAsync<InvalidOperationException>();

            var secondResult = await cache.GetOrCreateAsync("error-test-key", Factory);

            secondResult.Should().Be("recovered-value");
            attempt.Should().Be(2, "a failed factory execution must not be cached, so the next call retries it");
        }
    }

    [Fact]
    public void ById_ProducesAStableDistinctKeyPerId()
    {
        // Assert
        QuoteCacheKeys.ById(42).Should().Be(QuoteCacheKeys.ById(42));
        QuoteCacheKeys.ById(42).Should().NotBe(QuoteCacheKeys.ById(43));
    }
}
