namespace QuotesApi.Jobs;

/// <summary>
/// One work item queued for the background worker: given the request scope's
/// IServiceProvider (see BackgroundJobWorker - resolved per item via
/// IServiceScopeFactory, never injected straight into this singleton queue)
/// and the worker's shutdown CancellationToken, do the slow work.
/// </summary>
public delegate Task BackgroundWorkItem(IServiceProvider services, CancellationToken cancellationToken);

/// <summary>
/// Thread-safe FIFO handoff from request threads (producers) to
/// <see cref="BackgroundJobWorker"/> (the single consumer). An endpoint calls
/// QueueBackgroundWorkItemAsync and returns immediately - the slow work runs
/// later, off the request thread.
/// </summary>
public interface IBackgroundTaskQueue
{
    ValueTask QueueBackgroundWorkItemAsync(BackgroundWorkItem workItem);

    ValueTask<BackgroundWorkItem> DequeueAsync(CancellationToken cancellationToken);
}
