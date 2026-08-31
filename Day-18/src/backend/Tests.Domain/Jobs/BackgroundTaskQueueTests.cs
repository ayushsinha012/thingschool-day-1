using FluentAssertions;
using QuotesApi.Jobs;

namespace Tests.Domain.Jobs;

public class BackgroundTaskQueueTests
{
    [Fact]
    public async Task QueueThenDequeue_ReturnsItemsInFifoOrder()
    {
        var queue = new BackgroundTaskQueue();
        var executionOrder = new List<int>();

        BackgroundWorkItem First = (_, _) => { executionOrder.Add(1); return Task.CompletedTask; };
        BackgroundWorkItem Second = (_, _) => { executionOrder.Add(2); return Task.CompletedTask; };

        await queue.QueueBackgroundWorkItemAsync(First);
        await queue.QueueBackgroundWorkItemAsync(Second);

        var dequeuedFirst = await queue.DequeueAsync(CancellationToken.None);
        var dequeuedSecond = await queue.DequeueAsync(CancellationToken.None);

        await dequeuedFirst(null!, CancellationToken.None);
        await dequeuedSecond(null!, CancellationToken.None);

        executionOrder.Should().Equal(1, 2);
    }

    [Fact]
    public async Task DequeueAsync_DoesNotBlockEnqueue_EvenUnderABurstFarBeyondAnyBoundedCapacity()
    {
        // Regression test for the bug fixed in result.md "Bug Found and
        // Fixed": BackgroundTaskQueue originally wrapped a bounded Channel,
        // so QueueBackgroundWorkItemAsync (awaited on the request thread by
        // JobEndpoints) blocked once the channel filled, until the worker
        // drained a slot - the opposite of "the request returns quickly
        // after enqueueing". A bounded channel of capacity 32 with nothing
        // draining it would hang this test past any reasonable timeout;
        // completing well within it proves the channel is unbounded.
        var queue = new BackgroundTaskQueue();

        var enqueueAll = Task.Run(async () =>
        {
            for (var i = 0; i < 500; i++)
            {
                await queue.QueueBackgroundWorkItemAsync((_, _) => Task.CompletedTask);
            }
        });

        var completed = await Task.WhenAny(enqueueAll, Task.Delay(TimeSpan.FromSeconds(5)));

        completed.Should().Be(enqueueAll, "enqueueing must never block the caller, regardless of queue depth");
    }

    [Fact]
    public async Task DequeueAsync_WithAlreadyCancelledToken_ThrowsOperationCanceledException_OnEmptyQueue()
    {
        var queue = new BackgroundTaskQueue();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await queue.DequeueAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
