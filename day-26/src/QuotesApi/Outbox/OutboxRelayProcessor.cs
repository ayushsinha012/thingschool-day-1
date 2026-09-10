using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Messaging;

namespace QuotesApi.Outbox;

public sealed class OutboxRelayProcessor(
    AppDbContext db,
    IQuoteEventPublisher publisher,
    ILogger<OutboxRelayProcessor> logger)
{
    public async Task<int> ProcessBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        var pendingIds = await db.OutboxMessages
            .Where(message => message.SentAt == null)
            .OrderBy(message => message.Id)
            .Take(batchSize)
            .Select(message => message.Id)
            .ToListAsync(cancellationToken);

        var publishedCount = 0;

        foreach (var id in pendingIds)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (await PublishOneAsync(id, cancellationToken))
            {
                publishedCount++;
            }
        }

        return publishedCount;
    }

    public async Task<bool> PublishOneAsync(int outboxMessageId, CancellationToken cancellationToken)
    {
        var claimed = await db.OutboxMessages
            .Where(message => message.Id == outboxMessageId && message.SentAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    message => message.AttemptCount,
                    message => message.AttemptCount + 1),
                cancellationToken);

        if (claimed == 0)
        {
            return false;
        }

        var message = await db.OutboxMessages
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == outboxMessageId, cancellationToken);

        if (message is null)
        {
            return false;
        }

        try
        {
            await publisher.PublishAsync(
                message.MessageType,
                message.Payload,
                message.MessageId,
                poison: false,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await db.OutboxMessages
                .Where(candidate => candidate.Id == outboxMessageId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(candidate => candidate.LastError, ex.Message),
                    cancellationToken);

            logger.LogWarning(
                ex,
                "Outbox publish failed for {MessageId} (attempt {AttemptCount})",
                message.MessageId,
                message.AttemptCount);

            return false;
        }

        await db.OutboxMessages
            .Where(candidate => candidate.Id == outboxMessageId && candidate.SentAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(candidate => candidate.SentAt, DateTimeOffset.UtcNow)
                    .SetProperty(candidate => candidate.LastError, (string?)null),
                cancellationToken);

        logger.LogInformation(
            "Outbox message {MessageId} ({MessageType}) published",
            message.MessageId,
            message.MessageType);

        return true;
    }
}
