namespace QuotesApi.Messaging;

public sealed record ProcessQuoteEventCommand(
    string SubscriptionName,
    string MessageId,
    string EventType,
    string Payload,
    int DeliveryCount,
    bool Poison);
