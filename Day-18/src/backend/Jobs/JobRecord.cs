namespace QuotesApi.Jobs;

/// <summary>
/// Immutable snapshot of one background job's state, as returned by the
/// job endpoints and stored in <see cref="IJobStore"/>. Immutable so that
/// concurrent readers (GET /api/jobs/{id} polling from the UI) never observe
/// a record half-updated by the worker thread - see JobStore.UpdateStatus,
/// which replaces one snapshot with the next via ConcurrentDictionary,
/// rather than mutating fields in place.
/// </summary>
public sealed record JobRecord(
    Guid Id,
    string Label,
    JobStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? Error);
