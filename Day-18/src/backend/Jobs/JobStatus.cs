using System.Text.Json.Serialization;

namespace QuotesApi.Jobs;

/// <summary>
/// Lifecycle of one background job tracked by <see cref="IJobStore"/>. A job
/// moves Queued -> Running -> (Completed | Failed) and never moves backwards -
/// see <see cref="JobStore.UpdateStatus"/> for the one-way transition.
///
/// Serialized as its name ("Queued", not 0) - scoped to this one enum via
/// the attribute rather than a global JSON convention, so the rest of the
/// API's existing responses are untouched.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<JobStatus>))]
public enum JobStatus
{
    Queued,
    Running,
    Completed,
    Failed
}
