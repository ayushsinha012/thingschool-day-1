using ResilienceDemo.Client;
using ResilienceDemo.Downstream;
using ResilienceDemo.Resilience;

namespace ResilienceDemo.Demo;

public static class DemoEndpoints
{
    public static void MapDemoEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/demo");

        group.MapGet("/get/{id:int}", async (int id, OutboundDependencyClient client, CancellationToken ct) =>
        {
            var result = await client.GetQuoteAsync(id, ct);
            return ToHttpResult(result);
        });

        group.MapPost("/event", async (OutboundDependencyClient client, CancellationToken ct) =>
        {
            var result = await client.RecordEventAsync(ct);
            return ToHttpResult(result);
        });

        group.MapPost("/concurrent", async (int count, OutboundDependencyClient client, CancellationToken ct) =>
        {
            var tasks = Enumerable.Range(0, count).Select(id => client.GetQuoteAsync(id, ct));
            var results = await Task.WhenAll(tasks);

            var summary = results
                .GroupBy(r => r.Outcome)
                .ToDictionary(g => g.Key.ToString(), g => g.Count());

            return Results.Ok(new { requested = count, summary });
        });

        group.MapGet("/status", (ResilienceMetrics metrics, DownstreamState downstream) => Results.Ok(new
        {
            circuitState = metrics.CircuitState,
            downstreamMode = downstream.Mode.ToString(),
            counters = new
            {
                metrics.Successes,
                metrics.Failures,
                metrics.RetryAttempts,
                metrics.Timeouts,
                metrics.BulkheadRejections,
                metrics.CircuitShortCircuited,
            },
        }));

        group.MapGet("/metrics", (ResilienceMetrics metrics) => Results.Ok(new
        {
            circuitState = metrics.CircuitState,
            counters = new
            {
                metrics.Successes,
                metrics.Failures,
                metrics.RetryAttempts,
                metrics.Timeouts,
                metrics.BulkheadRejections,
                metrics.CircuitShortCircuited,
            },
            transitions = metrics.Transitions,
        }));

        group.MapPost("/reset", (ResilienceMetrics metrics, DownstreamState downstream) =>
        {
            metrics.Reset();
            downstream.Reset();
            return Results.NoContent();
        });
    }

    private static IResult ToHttpResult(OutboundCallResult result) => result.Outcome switch
    {
        OutboundOutcome.Success => Results.Ok(result),
        OutboundOutcome.CircuitOpen => Results.Json(result, statusCode: StatusCodes.Status503ServiceUnavailable),
        OutboundOutcome.Timeout => Results.Json(result, statusCode: StatusCodes.Status504GatewayTimeout),
        OutboundOutcome.BulkheadRejected => Results.Json(result, statusCode: StatusCodes.Status429TooManyRequests),
        _ => Results.Json(result, statusCode: StatusCodes.Status502BadGateway),
    };
}
