using Hangfire;
using Hangfire.InMemory;
using QuotesApi.Jobs;

namespace QuotesApi.Extensions;

/// <summary>
/// Day 18: the queue + BackgroundService consumer (IBackgroundTaskQueue /
/// BackgroundTaskQueue / BackgroundJobWorker) for ad-hoc "do this slow thing
/// now, off the request thread" work, plus a small Hangfire setup
/// demonstrating what that BackgroundService does NOT give you for free -
/// durable scheduling. See README.md / result.md for the full contrast.
/// </summary>
public static class BackgroundJobsExtensions
{
    public static IServiceCollection AddBackgroundJobs(this IServiceCollection services)
    {
        services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
        services.AddSingleton<IJobStore, JobStore>();
        services.AddHostedService<BackgroundJobWorker>();

        // Hangfire: durable/scheduled jobs, contrasted with the queue above.
        // InMemory storage keeps this demo dependency-free (no SQL Server/
        // Redis to stand up) - see README.md "What Would Break" for why
        // that means Hangfire's own jobs don't survive a restart here
        // either, same as the in-memory JobStore above. A real deployment
        // would point UseInMemoryStorage() at UseSqlServerStorage/
        // UseRedisStorage instead; nothing else in this file would change.
        services.AddHangfire(configuration => configuration
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseInMemoryStorage());

        services.AddHangfireServer();

        return services;
    }

    /// <summary>
    /// One recurring job: purge job records the queue's own worker finished
    /// more than 10 minutes ago. This is the scheduling/cleanup work
    /// Hangfire is actually good for - durable (survives this process
    /// restart, given real storage), retried on failure, and visible on the
    /// dashboard - not something you'd hand-roll a Timer/PeriodicTimer for.
    /// Registered once at startup; Hangfire persists the schedule itself.
    /// </summary>
    public static void MapBackgroundJobsRecurringJobs(this WebApplication app)
    {
        RecurringJob.AddOrUpdate<IJobStore>(
            "purge-finished-jobs",
            jobStore => jobStore.PurgeFinishedOlderThan(TimeSpan.FromMinutes(10)),
            Cron.Minutely);
    }

    /// <summary>
    /// Dashboard at /hangfire. Left unauthenticated - the default
    /// LocalRequestsOnlyAuthorizationFilter already restricts it to
    /// same-machine requests, and this app has no operator-role claim to
    /// gate it on properly; a production deployment would add one before
    /// exposing this beyond localhost. See README.md "What Would Break".
    /// </summary>
    public static void MapBackgroundJobsDashboard(this WebApplication app)
    {
        app.MapHangfireDashboard("/hangfire");
    }
}
