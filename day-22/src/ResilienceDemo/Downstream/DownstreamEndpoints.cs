namespace ResilienceDemo.Downstream;

public static class DownstreamEndpoints
{
    public static void MapDownstreamEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/downstream");

        group.MapGet("/quote/{id:int}", async (int id, DownstreamState state, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("Downstream");
            state.CountRequest();
            logger.LogInformation("downstream request id={QuoteId} mode={Mode}", id, state.Mode);

            return await RespondAsync(state, () => Results.Ok(new { id, text = "an ordinary quote", source = "downstream" }));
        });

        group.MapPost("/events", async (DownstreamState state, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("Downstream");
            state.CountRequest();
            logger.LogInformation("downstream event write mode={Mode}", state.Mode);

            return await RespondAsync(state, () => Results.Created("/downstream/events/1", new { recorded = true }));
        });

        group.MapPost("/control", (DownstreamControlRequest request, DownstreamState state) =>
        {
            state.Configure(request.Mode, request.DelayMs);
            return Results.Ok(new { state.Mode, state.DelayMs });
        });

        group.MapGet("/control", (DownstreamState state) =>
            Results.Ok(new { state.Mode, state.DelayMs, state.RequestCount }));
    }

    private static async Task<IResult> RespondAsync(DownstreamState state, Func<IResult> onSuccess)
    {
        switch (state.Mode)
        {
            case DownstreamMode.Failing:
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, detail: "downstream unavailable");

            case DownstreamMode.Slow:
                await Task.Delay(state.DelayMs);
                return onSuccess();

            case DownstreamMode.Healthy:
            default:
                return onSuccess();
        }
    }
}

public sealed record DownstreamControlRequest(DownstreamMode Mode, int DelayMs);
