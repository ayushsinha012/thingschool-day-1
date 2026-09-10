using Microsoft.AspNetCore.Mvc;
using QuotesApi.DTOs;
using QuotesApi.Jobs;

namespace QuotesApi.Endpoints;

public sealed class JobEndpointsLogCategory;

/// <summary>
/// The Day 18 demo endpoint: enqueues slow work onto IBackgroundTaskQueue
/// and returns immediately, instead of awaiting the slow work inline. See
/// BackgroundJobWorker for the consumer side.
/// </summary>
public static class JobEndpoints
{
    private const int MinDurationSeconds = 1;
    private const int MaxDurationSeconds = 20;
    private const int MaxLabelLength = 200;

    public static void MapJobEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/jobs");

        group.MapPost(
            "/",
            async (
                CreateJobRequest request,
                IBackgroundTaskQueue queue,
                IJobStore jobStore,
                ILogger<JobEndpointsLogCategory> logger,
                CancellationToken cancellationToken) =>
            {
                var durationSeconds = request.DurationSeconds.GetValueOrDefault(3);
                var label = string.IsNullOrWhiteSpace(request.Label)
                    ? "Background job"
                    : request.Label.Trim();

                if (durationSeconds < MinDurationSeconds || durationSeconds > MaxDurationSeconds)
                {
                    return Results.BadRequest(
                        new ProblemDetails
                        {
                            Title = "Invalid job request",
                            Detail = $"DurationSeconds must be between {MinDurationSeconds} and {MaxDurationSeconds}."
                        });
                }

                if (label.Length > MaxLabelLength)
                {
                    return Results.BadRequest(
                        new ProblemDetails
                        {
                            Title = "Invalid job request",
                            Detail = $"Label must be {MaxLabelLength} characters or fewer."
                        });
                }

                var simulateFailure = request.SimulateFailure.GetValueOrDefault(false);
                var job = jobStore.Create(label);

                // The work item captures only the job id and the demo
                // parameters - it resolves IJobStore itself from the scope
                // BackgroundJobWorker hands it, rather than closing over the
                // jobStore instance from this request's DI container.
                await queue.QueueBackgroundWorkItemAsync(async (services, token) =>
                {
                    var scopedJobStore = services.GetRequiredService<IJobStore>();

                    scopedJobStore.UpdateStatus(job.Id, JobStatus.Running);

                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(durationSeconds), token);

                        if (simulateFailure)
                        {
                            throw new InvalidOperationException(
                                "Simulated failure for demo purposes (SimulateFailure=true).");
                        }

                        scopedJobStore.UpdateStatus(job.Id, JobStatus.Completed);
                    }
                    catch (OperationCanceledException)
                    {
                        // Rethrow so BackgroundJobWorker's shutdown branch
                        // sees this as cancellation, not a job failure - the
                        // job is left Queued/Running rather than marked
                        // Failed, since shutdown aborted it rather than the
                        // work itself failing.
                        throw;
                    }
                    catch (Exception ex)
                    {
                        scopedJobStore.UpdateStatus(job.Id, JobStatus.Failed, ex.Message);
                    }
                });

                logger.LogInformation(
                    "Enqueued job {JobId} ({Label}, {DurationSeconds}s, simulateFailure={SimulateFailure})",
                    job.Id,
                    label,
                    durationSeconds,
                    simulateFailure);

                // 202 Accepted, not 200/201: the work has been accepted for
                // processing but has not happened yet - the caller must poll
                // GET /api/jobs/{id} (the Location header) for the outcome.
                return Results.Accepted($"/api/jobs/{job.Id}", job);
            });

        group.MapGet(
            "/",
            (IJobStore jobStore) => Results.Ok(jobStore.GetRecent(20)));

        group.MapGet(
            "/{id:guid}",
            (Guid id, IJobStore jobStore) =>
            {
                var job = jobStore.Get(id);

                return job is null
                    ? Results.NotFound(
                        new ProblemDetails
                        {
                            Title = "Job not found",
                            Detail = $"No job exists with ID {id}."
                        })
                    : Results.Ok(job);
            });
    }
}
