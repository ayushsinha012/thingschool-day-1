namespace QuotesApi.Messaging;

public class ProcessedMessage
{
    public string SubscriptionName { get; set; } = string.Empty;

    public string MessageId { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public DateTimeOffset ProcessedAtUtc { get; set; }
}
