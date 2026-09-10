using Microsoft.Extensions.Caching.Hybrid;
using QuotesApi.Caching;
using StackExchange.Redis;

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

        // StackExchange.Redis's own defaults (5000ms ConnectTimeout,
        // AbortOnConnectFail: true) are tuned for "Redis is a required,
        // always-on dependency". Here it backs an optional L2 read-through
        // cache (HybridCache falls back to the DB on any cache failure - see
        // GetQuoteByIdQueryHandler and TryInvalidateQuoteCacheAsync in
        // QuoteEndpoints), so a Redis outage/misconfiguration should degrade
        // every quote GET/PUT/DELETE to "slightly slower, no cache" - not
        // make each one pay a multi-second connect timeout, or (with the
        // default AbortOnConnectFail) crash the multiplexer outright. This
        // was the actual cause of "delete/edit/GET all take ~5s" when Redis
        // isn't running locally.
        var redisOptions = ConfigurationOptions.Parse(redisConnectionString);
        redisOptions.AbortOnConnectFail = false;
        redisOptions.ConnectTimeout = 300;
        redisOptions.ConnectRetry = 0;
        redisOptions.SyncTimeout = 500;
        redisOptions.AsyncTimeout = 500;

        // Registering IDistributedCache is what makes HybridCache use Redis
        // as its L2 - AddHybridCache below picks up whatever IDistributedCache
        // is already registered automatically, no extra wiring needed.
        services.AddStackExchangeRedisCache(options =>
        {
            options.ConfigurationOptions = redisOptions;
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
