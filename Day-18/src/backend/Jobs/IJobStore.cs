namespace QuotesApi.Jobs;

/// <summary>
/// In-memory job status board. Deliberately not durable - see README/result
/// "What Would Break" for why this is fine for a demo queue but is exactly
/// the gap Hangfire's persistent storage closes for real scheduled work.
/// </summary>
public interface IJobStore
{
    JobRecord Create(string label);

    JobRecord? Get(Guid id);

    /// <summary>Most recently created jobs first, capped at <paramref name="count"/>.</summary>
    IReadOnlyList<JobRecord> GetRecent(int count);

    /// <summary>
    /// Moves a job to <paramref name="status"/>. Returns the updated record,
    /// or null if no job with <paramref name="id"/> exists (e.g. it was
    /// already purged - see the Hangfire cleanup job).
    /// </summary>
    JobRecord? UpdateStatus(Guid id, JobStatus status, string? error = null);

    /// <summary>
    /// Removes completed/failed jobs older than <paramref name="olderThan"/>.
    /// Called by the Hangfire recurring job, not the queue worker - see
    /// BackgroundJobsExtensions. Returns how many were removed.
    /// </summary>
    int PurgeFinishedOlderThan(TimeSpan olderThan);
}
