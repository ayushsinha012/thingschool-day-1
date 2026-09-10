using System.Threading.Channels;

namespace QuotesApi.Jobs;

public sealed class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly Channel<BackgroundWorkItem> _channel;

    public BackgroundTaskQueue()
    {
        _channel = Channel.CreateUnbounded<BackgroundWorkItem>();
    }

    public async ValueTask QueueBackgroundWorkItemAsync(BackgroundWorkItem workItem) =>
        await _channel.Writer.WriteAsync(workItem);

    public async ValueTask<BackgroundWorkItem> DequeueAsync(CancellationToken cancellationToken) =>
        await _channel.Reader.ReadAsync(cancellationToken);
}
