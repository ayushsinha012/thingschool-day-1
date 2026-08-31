namespace QuotesApi.DTOs;

/// <summary>
/// POST /api/jobs body. DurationSeconds and SimulateFailure exist to make
/// the demo's slow work and failure path controllable from the UI - a real
/// job would take its actual parameters here instead.
/// </summary>
public sealed record CreateJobRequest(string? Label, int? DurationSeconds, bool? SimulateFailure);
