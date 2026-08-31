using FluentAssertions;
using QuotesApi.Jobs;

namespace Tests.Domain.Jobs;

public class JobStoreTests
{
    [Fact]
    public void Create_ReturnsQueuedJob_WithCreatedAtSet_AndNoOtherTimestamps()
    {
        var store = new JobStore();

        var job = store.Create("demo digest");

        job.Label.Should().Be("demo digest");
        job.Status.Should().Be(JobStatus.Queued);
        job.CreatedAt.Should().NotBe(default);
        job.StartedAt.Should().BeNull();
        job.CompletedAt.Should().BeNull();
        job.Error.Should().BeNull();
    }

    [Fact]
    public void Get_ForUnknownId_ReturnsNull()
    {
        var store = new JobStore();

        store.Get(Guid.NewGuid()).Should().BeNull();
    }

    [Fact]
    public void UpdateStatus_ToRunning_SetsStartedAt_AndLeavesCompletedAtNull()
    {
        var store = new JobStore();
        var job = store.Create("demo");

        var updated = store.UpdateStatus(job.Id, JobStatus.Running);

        updated.Should().NotBeNull();
        updated!.Status.Should().Be(JobStatus.Running);
        updated.StartedAt.Should().NotBeNull();
        updated.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void UpdateStatus_ToCompleted_SetsCompletedAt()
    {
        var store = new JobStore();
        var job = store.Create("demo");
        store.UpdateStatus(job.Id, JobStatus.Running);

        var updated = store.UpdateStatus(job.Id, JobStatus.Completed);

        updated.Should().NotBeNull();
        updated!.Status.Should().Be(JobStatus.Completed);
        updated.CompletedAt.Should().NotBeNull();
        updated.Error.Should().BeNull();
    }

    [Fact]
    public void UpdateStatus_ToFailed_SetsCompletedAt_AndError()
    {
        var store = new JobStore();
        var job = store.Create("demo");

        var updated = store.UpdateStatus(job.Id, JobStatus.Failed, "boom");

        updated.Should().NotBeNull();
        updated!.Status.Should().Be(JobStatus.Failed);
        updated.CompletedAt.Should().NotBeNull();
        updated.Error.Should().Be("boom");
    }

    [Fact]
    public void UpdateStatus_ForUnknownId_ReturnsNull_AndDoesNotThrow()
    {
        var store = new JobStore();

        store.UpdateStatus(Guid.NewGuid(), JobStatus.Completed).Should().BeNull();
    }

    [Fact]
    public void GetRecent_ReturnsMostRecentlyCreatedFirst_CappedAtCount()
    {
        var store = new JobStore();

        for (var i = 0; i < 5; i++)
        {
            store.Create($"job-{i}");

            // Ensures each Create gets a strictly later CreatedAt than the
            // previous one - DateTimeOffset.UtcNow's resolution can
            // otherwise tie two calls made back-to-back in a tight loop.
            Thread.Sleep(1);
        }

        var recent = store.GetRecent(3);

        recent.Should().HaveCount(3);
        recent.Select(j => j.Label).Should().Equal("job-4", "job-3", "job-2");
    }

    [Fact]
    public void PurgeFinishedOlderThan_WithZeroThreshold_RemovesAlreadyFinishedJobs()
    {
        var store = new JobStore();
        var job = store.Create("demo");
        store.UpdateStatus(job.Id, JobStatus.Completed);

        var removed = store.PurgeFinishedOlderThan(TimeSpan.Zero);

        removed.Should().Be(1);
        store.Get(job.Id).Should().BeNull();
    }

    [Fact]
    public void PurgeFinishedOlderThan_DoesNotRemoveJobsStillInFlight()
    {
        var store = new JobStore();
        var queued = store.Create("still-queued");
        var running = store.Create("still-running");
        store.UpdateStatus(running.Id, JobStatus.Running);

        var removed = store.PurgeFinishedOlderThan(TimeSpan.Zero);

        removed.Should().Be(0);
        store.Get(queued.Id).Should().NotBeNull();
        store.Get(running.Id).Should().NotBeNull();
    }
}
