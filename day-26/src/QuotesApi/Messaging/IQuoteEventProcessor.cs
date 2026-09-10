namespace QuotesApi.Messaging;

public interface IQuoteEventProcessor
{
    Task<MessageProcessingOutcome> ProcessAsync(
        ProcessQuoteEventCommand command,
        string workerSlot,
        CancellationToken cancellationToken);
}
