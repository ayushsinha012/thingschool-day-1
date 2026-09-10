namespace QuotesApi.Outbox;

public class OutboxMessage
{
    public int Id { get; set; }

    public string MessageId { get; set; } = string.Empty;

    public string MessageType { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public int AttemptCount { get; set; }

    public string? LastError { get; set; }
}
