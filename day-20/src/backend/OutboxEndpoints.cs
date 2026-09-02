using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Outbox;

namespace QuotesApi.Endpoints;

public static class OutboxEndpoints
{
    private const int DefaultTake = 50;
    private const int MaxTake = 200;

    public static void MapOutboxEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/outbox");

        group.MapGet(
            "/",
            async (
                int? take,
                AppDbContext db,
                CancellationToken cancellationToken) =>
            {
                var limit = Math.Clamp(take.GetValueOrDefault(DefaultTake), 1, MaxTake);

                var messages = await db.OutboxMessages
                    .AsNoTracking()
                    .OrderByDescending(message => message.Id)
                    .Take(limit)
                    .Select(message => new OutboxMessageSummary(
                        message.Id,
                        message.MessageId,
                        message.MessageType,
                        message.CreatedAt,
                        message.SentAt,
                        message.AttemptCount,
                        message.LastError))
                    .ToListAsync(cancellationToken);

                return Results.Ok(messages);
            });

        group.MapGet(
            "/status",
            (IOutboxRelayStatus status) => Results.Ok(status.GetSnapshot()));
    }
}
