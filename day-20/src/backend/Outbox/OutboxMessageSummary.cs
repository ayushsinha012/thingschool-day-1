namespace QuotesApi.Outbox;

public sealed record OutboxMessageSummary(
    int Id,
    string MessageId,
    string MessageType,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt,
    int AttemptCount,
    string? LastError);
