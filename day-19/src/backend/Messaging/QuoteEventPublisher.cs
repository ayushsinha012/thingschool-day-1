using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace QuotesApi.Messaging;

public sealed class QuoteEventPublisher : IQuoteEventPublisher, IAsyncDisposable
{
    private readonly ServiceBusSender _sender;
    private readonly string _topicName;

    public QuoteEventPublisher(ServiceBusClient client, IOptions<ServiceBusOptions> options)
    {
        _topicName = options.Value.TopicName;
        _sender = client.CreateSender(_topicName);
    }

    public async Task<PublishedEvent> PublishAsync(
        string eventType,
        string payload,
        string? idempotencyKey,
        bool poison,
        CancellationToken cancellationToken)
    {
        var messageId = MessageIdResolver.Resolve(idempotencyKey);
        var publishedAt = DateTimeOffset.UtcNow;

        var message = new ServiceBusMessage(payload)
        {
            MessageId = messageId,
            ContentType = "text/plain"
        };

        message.ApplicationProperties["EventType"] = eventType;
        message.ApplicationProperties["PublishedAtUtc"] = publishedAt.ToString("O");
        message.ApplicationProperties["Poison"] = poison;

        await _sender.SendMessageAsync(message, cancellationToken);

        return new PublishedEvent(messageId, eventType, _topicName, publishedAt, poison);
    }

    public async ValueTask DisposeAsync() => await _sender.DisposeAsync();
}
