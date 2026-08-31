using System.Collections.Concurrent;

namespace QuotesApi.Jobs;

/// <summary>
/// Singleton, process-local <see cref="IJobStore"/>. Safe to call from both
/// the request thread (Create/Get/GetRecent) and the background worker
/// thread (UpdateStatus) concurrently: ConcurrentDictionary handles the
/// thread-safety, and every write replaces one immutable JobRecord with
/// another rather than mutating shared state in place.
/// </summary>
public sealed class JobStore : IJobStore
{
    private readonly ConcurrentDictionary<Guid, JobRecord> _jobs = new();

    public JobRecord Create(string label)
    {
        var job = new JobRecord(
            Id: Guid.NewGuid(),
            Label: label,
            Status: JobStatus.Queued,
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: null,
            CompletedAt: null,
            Error: null);

        _jobs[job.Id] = job;

        return job;
    }

    public JobRecord? Get(Guid id) =>
        _jobs.TryGetValue(id, out var job) ? job : null;

    public IReadOnlyList<JobRecord> GetRecent(int count) =>
        _jobs.Values
            .OrderByDescending(job => job.CreatedAt)
            .Take(count)
            .ToList();

    public JobRecord? UpdateStatus(Guid id, JobStatus status, string? error = null)
    {
        // AddOrUpdate's update factory can, in theory, run more than once
        // under contention - it always reads the current value from the
        // dictionary rather than closing over a stale one, so a
        // Running-then-Completed race on the same job still lands on a
        // consistent final record instead of one update clobbering another.
        return _jobs.TryGetValue(id, out var existing)
            ? _jobs.AddOrUpdate(
                id,
                existing,
                (_, current) => current with
                {
                    Status = status,
                    StartedAt = status == JobStatus.Running ? DateTimeOffset.UtcNow : current.StartedAt,
                    CompletedAt = status is JobStatus.Completed or JobStatus.Failed
                        ? DateTimeOffset.UtcNow
                        : current.CompletedAt,
                    Error = error ?? (status == JobStatus.Running ? null : current.Error)
                })
            : null;
    }

    public int PurgeFinishedOlderThan(TimeSpan olderThan)
    {
        var cutoff = DateTimeOffset.UtcNow - olderThan;
        var removed = 0;

        foreach (var job in _jobs.Values)
        {
            var finishedAt = job.CompletedAt;

            if (finishedAt is not null &&
                finishedAt.Value < cutoff &&
                _jobs.TryRemove(job.Id, out _))
            {
                removed++;
            }
        }

        return removed;
    }
}
