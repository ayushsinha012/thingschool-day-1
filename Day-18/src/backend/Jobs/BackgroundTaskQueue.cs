using System.Threading.Channels;

namespace QuotesApi.Jobs;

/// <summary>
/// Channel&lt;T&gt;-backed implementation of <see cref="IBackgroundTaskQueue"/>.
/// Registered as a singleton (see BackgroundJobsExtensions) - one queue
/// shared by every request thread that enqueues and the one
/// BackgroundJobWorker that drains it.
/// </summary>
public sealed class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly Channel<BackgroundWorkItem> _channel;

    public BackgroundTaskQueue()
    {
        // Unbounded, deliberately - see result.md "Bug Found and Fixed".
        // This started out as Channel.CreateBounded(32) with the default
        // FullMode (Wait): once the channel filled up, WriteAsync on the
        // request thread would not complete until the worker drained a
        // slot, so a burst of requests past the capacity blocked on
        // QueueBackgroundWorkItemAsync for as long as it took the single
        // worker to drain down to it - the exact thing "the request
        // returns quickly after enqueueing" (requirement 2) rules out.
        // Reproduced directly: 40 rapid POSTs against capacity 32 serialized
        // to one response every ~5s (the job duration) once the channel
        // filled, confirmed in the request logs. An unbounded channel never
        // blocks the writer - the tradeoff (a runaway producer could grow
        // memory without limit) is intentionally out of scope for this
        // demo, and is one of the things a durable queue (or Hangfire's own
        // enqueue, which persists to storage instead of memory) is for.
        _channel = Channel.CreateUnbounded<BackgroundWorkItem>();
    }

    public async ValueTask QueueBackgroundWorkItemAsync(BackgroundWorkItem workItem) =>
        await _channel.Writer.WriteAsync(workItem);

    public async ValueTask<BackgroundWorkItem> DequeueAsync(CancellationToken cancellationToken) =>
        await _channel.Reader.ReadAsync(cancellationToken);
}
