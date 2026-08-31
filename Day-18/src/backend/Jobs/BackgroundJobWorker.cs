namespace QuotesApi.Jobs;

/// <summary>
/// Drains <see cref="IBackgroundTaskQueue"/> off the request thread. This is
/// the BackgroundService referenced throughout Day 18 - see README.md /
/// result.md for the IHostedService/Hangfire contrast.
///
/// Registered via AddHostedService (BackgroundJobsExtensions), so the host
/// calls StartAsync (which schedules ExecuteAsync on the thread pool and
/// returns immediately) at startup and StopAsync (which signals the
/// CancellationToken passed to ExecuteAsync, then awaits the running task up
/// to HostOptions.ShutdownTimeout) during shutdown - both inherited from
/// BackgroundService, not overridden here.
/// </summary>
public sealed class BackgroundJobWorker(
    IBackgroundTaskQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<BackgroundJobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Background job worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            BackgroundWorkItem workItem;

            try
            {
                // Suspends asynchronously (no polling, no Thread.Sleep) until
                // either a job is enqueued or stoppingToken is cancelled.
                workItem = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown: the host cancelled stoppingToken while
                // this await was waiting on an empty queue. Not an error -
                // exit the loop so ExecuteAsync completes and StopAsync can
                // return.
                break;
            }

            // A scoped dependency (e.g. AppDbContext, were a job to need one)
            // must not be injected into this class - BackgroundJobWorker is
            // a singleton for the app's lifetime, but a DbContext is scoped
            // per-request-equivalent. Each work item gets its own scope here
            // instead, exactly the pattern .NET docs prescribe for
            // BackgroundService + scoped services.
            await using var scope = scopeFactory.CreateAsyncScope();

            try
            {
                await workItem(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // The job itself was cancelled mid-flight by shutdown (e.g.
                // its Task.Delay observed stoppingToken). The job delegate
                // already leaves the job's stored status as Running/Queued
                // rather than Completed in this case - correct, since it
                // didn't finish - so there is nothing further to record.
                break;
            }
            catch (Exception ex)
            {
                // Safety net only: every job delegate this app enqueues
                // (see JobEndpoints) already catches its own exceptions and
                // records JobStatus.Failed in IJobStore. If a job delegate
                // somehow throws past that, log it loudly but keep the loop
                // alive - one bad job must never kill the worker for every
                // job queued after it.
                logger.LogError(ex, "Unhandled exception from a background work item");
            }
        }

        logger.LogInformation("Background job worker stopping");
    }
}
