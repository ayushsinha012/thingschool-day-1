namespace QuotesApi.Messaging;

public sealed record SubscriptionTopology(
    string Name,
    long ActiveMessageCount,
    long DeadLetterMessageCount,
    long TotalMessageCount);
