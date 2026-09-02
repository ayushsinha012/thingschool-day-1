using QuotesApi.Messaging;

namespace Tests.Domain.TestDoubles;

public sealed class FakeQuoteEventPublisher : IQuoteEventPublisher
{
    private int _failuresRemaining;

    public FakeQuoteEventPublisher(int failuresBeforeSuccess = 0)
    {
        _failuresRemaining = failuresBeforeSuccess;
    }

    public List<(string MessageId, string EventType, string Payload)> PublishedMessages { get; } = new();

    public Task<PublishedEvent> PublishAsync(
        string eventType,
        string payload,
        string? idempotencyKey,
        bool poison,
        CancellationToken cancellationToken)
    {
        if (_failuresRemaining > 0)
        {
            _failuresRemaining--;

            throw new InvalidOperationException("simulated Service Bus publish failure");
        }

        var messageId = MessageIdResolver.Resolve(idempotencyKey);

        PublishedMessages.Add((messageId, eventType, payload));

        return Task.FromResult(new PublishedEvent(messageId, eventType, "quote-events", DateTimeOffset.UtcNow, poison));
    }
}
