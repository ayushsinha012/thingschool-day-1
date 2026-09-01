namespace QuotesApi.Messaging;

public sealed record DeadLetterMessageSummary(
    string MessageId,
    string EventType,
    string SubscriptionName,
    int DeliveryCount,
    string? DeadLetterReason,
    string? DeadLetterErrorDescription,
    DateTimeOffset EnqueuedTimeUtc,
    string Body);
