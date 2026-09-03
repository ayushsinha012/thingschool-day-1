using Microsoft.Extensions.Caching.Hybrid;
using QuotesApi.Caching;

namespace QuotesApi.Extensions;

public static class CacheExtensions
{
    /// <summary>
    /// Configuration key (ConnectionStrings:Redis) for the Redis instance
    /// backing HybridCache's L2. Defaults to a local Redis on the standard
    /// port when unset, so local development/tests don't need any config -
    /// production overrides it via ConnectionStrings__Redis (a Container
    /// App secret, never committed - see day-21/infra notes).
    /// </summary>
    private const string RedisConnectionStringKey = "Redis";

    public static IServiceCollection AddQuoteCaching(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var redisConnectionString =
            configuration.GetConnectionString(RedisConnectionStringKey)
            ?? "localhost:6379";

        // Registering IDistributedCache is what makes HybridCache use Redis
        // as its L2 - AddHybridCache below picks up whatever IDistributedCache
        // is already registered automatically, no extra wiring needed.
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConnectionString;
            options.InstanceName = "quotesapi:";
        });

        services.AddHybridCache(options =>
        {
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                // L2 (Redis) lifetime - how long a value survives across
                // process restarts/instances before it's considered stale.
                Expiration = TimeSpan.FromMinutes(5),
                // L1 (in-memory) lifetime - short, since L1 isn't invalidated
                // across instances by the explicit RemoveAsync call on
                // delete (see QuoteEndpoints) in a multi-instance deployment;
                // keeping it short bounds how stale a *different* instance's
                // L1 copy can get after a write elsewhere.
                LocalCacheExpiration = TimeSpan.FromSeconds(30)
            };
        });

        services.AddSingleton<CacheMetrics>();
        services.AddSingleton<QueryCountingInterceptor>();

        return services;
    }
}
