namespace QuotesApi.Messaging;

public sealed class PoisonMessageException(string messageId)
    : Exception($"Poison message {messageId} failed processing by design.")
{
    public string MessageId { get; } = messageId;
}
