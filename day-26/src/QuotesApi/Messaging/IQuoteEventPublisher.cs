namespace QuotesApi.Messaging;

public interface IQuoteEventPublisher
{
    Task<PublishedEvent> PublishAsync(
        string eventType,
        string payload,
        string? idempotencyKey,
        bool poison,
        CancellationToken cancellationToken);
}
