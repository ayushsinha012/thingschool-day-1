using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;

namespace QuotesApi.Messaging;

public sealed class QuoteEventProcessor(
    AppDbContext db,
    IMessagingActivityLog activityLog,
    ILogger<QuoteEventProcessor> logger) : IQuoteEventProcessor
{
    public async Task<MessageProcessingOutcome> ProcessAsync(
        ProcessQuoteEventCommand command,
        string workerSlot,
        CancellationToken cancellationToken)
    {
        activityLog.Record(new ConsumerActivityEntry(
            DateTimeOffset.UtcNow,
            command.SubscriptionName,
            workerSlot,
            command.MessageId,
            command.EventType,
            ActivityOutcome.Received,
            command.DeliveryCount,
            null));

        if (command.Poison)
        {
            activityLog.Record(new ConsumerActivityEntry(
                DateTimeOffset.UtcNow,
                command.SubscriptionName,
                workerSlot,
                command.MessageId,
                command.EventType,
                ActivityOutcome.PoisonFailed,
                command.DeliveryCount,
                "Simulated poison payload"));

            throw new PoisonMessageException(command.MessageId);
        }

        var alreadyProcessed = await db.ProcessedMessages
            .AsNoTracking()
            .AnyAsync(
                processed =>
                    processed.SubscriptionName == command.SubscriptionName &&
                    processed.MessageId == command.MessageId,
                cancellationToken);

        if (alreadyProcessed)
        {
            activityLog.Record(new ConsumerActivityEntry(
                DateTimeOffset.UtcNow,
                command.SubscriptionName,
                workerSlot,
                command.MessageId,
                command.EventType,
                ActivityOutcome.Duplicate,
                command.DeliveryCount,
                "MessageId already processed on this subscription"));

            return MessageProcessingOutcome.Duplicate;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        db.ProcessedMessages.Add(new ProcessedMessage
        {
            SubscriptionName = command.SubscriptionName,
            MessageId = command.MessageId,
            EventType = command.EventType,
            ProcessedAtUtc = DateTimeOffset.UtcNow
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);

            activityLog.Record(new ConsumerActivityEntry(
                DateTimeOffset.UtcNow,
                command.SubscriptionName,
                workerSlot,
                command.MessageId,
                command.EventType,
                ActivityOutcome.Duplicate,
                command.DeliveryCount,
                "MessageId already processed on this subscription (concurrent insert)"));

            return MessageProcessingOutcome.Duplicate;
        }

        await transaction.CommitAsync(cancellationToken);

        activityLog.Record(new ConsumerActivityEntry(
            DateTimeOffset.UtcNow,
            command.SubscriptionName,
            workerSlot,
            command.MessageId,
            command.EventType,
            ActivityOutcome.Processed,
            command.DeliveryCount,
            null));

        logger.LogInformation(
            "Processed {MessageId} on {Subscription} via worker {WorkerSlot}",
            command.MessageId,
            command.SubscriptionName,
            workerSlot);

        return MessageProcessingOutcome.Processed;
    }
}
