using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using QuotesApi.Jobs;

namespace Tests.Domain.Jobs;

/// <summary>
/// Exercises BackgroundJobWorker through the same IHostedService
/// StartAsync/StopAsync lifecycle the real host drives it with (see
/// BackgroundService), rather than calling its protected ExecuteAsync
/// directly - StopAsync is what actually proves graceful shutdown, since it
/// signals the token BackgroundService created for ExecuteAsync and awaits
/// completion.
/// </summary>
public class BackgroundJobWorkerTests
{
    private static IServiceScopeFactory BuildScopeFactory(IJobStore jobStore)
    {
        var services = new ServiceCollection();
        services.AddSingleton(jobStore);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static async Task<JobStatus> PollUntilAsync(
        IJobStore jobStore,
        Guid jobId,
        Func<JobStatus, bool> predicate,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var status = jobStore.Get(jobId)?.Status;

            if (status is not null && predicate(status.Value))
            {
                return status.Value;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"Job {jobId} did not reach the expected status within {timeout}.");
    }

    [Fact]
    public async Task Worker_DrainsQueuedWorkItem_AndUpdatesJobStore()
    {
        var jobStore = new JobStore();
        var queue = new BackgroundTaskQueue();
        var job = jobStore.Create("demo");

        await queue.QueueBackgroundWorkItemAsync((services, _) =>
        {
            services.GetRequiredService<IJobStore>().UpdateStatus(job.Id, JobStatus.Completed);
            return Task.CompletedTask;
        });

        var worker = new BackgroundJobWorker(queue, BuildScopeFactory(jobStore), NullLogger<BackgroundJobWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            var status = await PollUntilAsync(jobStore, job.Id, s => s == JobStatus.Completed, TimeSpan.FromSeconds(2));
            status.Should().Be(JobStatus.Completed);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Worker_UnhandledExceptionFromOneWorkItem_DoesNotStopLaterItemsFromRunning()
    {
        var jobStore = new JobStore();
        var queue = new BackgroundTaskQueue();
        var failingJob = jobStore.Create("throws-past-its-own-catch");
        var healthyJob = jobStore.Create("runs-after");

        // Unlike JobEndpoints' real work items (which always catch their
        // own exceptions and record JobStatus.Failed), this one throws
        // straight past that - simulating a bug in a future job type - to
        // prove BackgroundJobWorker's safety-net catch keeps the loop alive
        // for the item queued after it.
        await queue.QueueBackgroundWorkItemAsync((_, _) => throw new InvalidOperationException("boom"));
        await queue.QueueBackgroundWorkItemAsync((services, _) =>
        {
            services.GetRequiredService<IJobStore>().UpdateStatus(healthyJob.Id, JobStatus.Completed);
            return Task.CompletedTask;
        });

        var worker = new BackgroundJobWorker(queue, BuildScopeFactory(jobStore), NullLogger<BackgroundJobWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            var status = await PollUntilAsync(jobStore, healthyJob.Id, s => s == JobStatus.Completed, TimeSpan.FromSeconds(2));
            status.Should().Be(JobStatus.Completed);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        // The failing job was never marked Completed/Failed by anything -
        // its delegate threw before touching the store - it stays Queued,
        // which is the honest reflection of "never finished", not evidence
        // of a bug.
        jobStore.Get(failingJob.Id)!.Status.Should().Be(JobStatus.Queued);
    }

    [Fact]
    public async Task Worker_StopAsync_CompletesPromptly_WhileWaitingOnAnEmptyQueue()
    {
        var jobStore = new JobStore();
        var queue = new BackgroundTaskQueue();
        var worker = new BackgroundJobWorker(queue, BuildScopeFactory(jobStore), NullLogger<BackgroundJobWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);

        // The worker is now parked inside DequeueAsync on an empty queue -
        // exactly the state it's in for most of its life. StopAsync must
        // still return promptly: BackgroundService cancels the token
        // ExecuteAsync is awaiting DequeueAsync with, which should unblock
        // it immediately rather than hanging until some future item shows
        // up.
        var stopTask = worker.StopAsync(CancellationToken.None);
        var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(5)));

        completed.Should().Be(stopTask, "StopAsync must not hang waiting for the (empty, cancelled) queue");
        await stopTask;
    }
}
