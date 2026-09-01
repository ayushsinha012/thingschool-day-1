namespace QuotesApi.Messaging;

public sealed record ConsumerActivityEntry(
    DateTimeOffset TimestampUtc,
    string SubscriptionName,
    string WorkerSlot,
    string MessageId,
    string EventType,
    ActivityOutcome Outcome,
    int DeliveryCount,
    string? Detail);
