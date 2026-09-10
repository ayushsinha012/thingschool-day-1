namespace QuotesApi.Messaging;

public sealed record PublishedEvent(
    string MessageId,
    string EventType,
    string TopicName,
    DateTimeOffset PublishedAtUtc,
    bool Poison);
