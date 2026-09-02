using System.Text.Json;
using MediatR;
using QuotesApi.Models;
using QuotesApi.Repositories;

namespace QuotesApi.Application.Quotes;

public sealed class CreateQuoteCommandHandler : IRequestHandler<CreateQuoteCommand, CreateQuoteResult>
{
    private const string QuoteCreatedEventType = "quote.created";

    private readonly IQuoteRepository _repository;

    public CreateQuoteCommandHandler(IQuoteRepository repository)
    {
        _repository = repository;
    }

    public async Task<CreateQuoteResult> Handle(
        CreateQuoteCommand request,
        CancellationToken cancellationToken)
    {
        var quote = Quote.Create(
            request.Author,
            request.Text);

        var created = await _repository.AddWithOutboxMessageAsync(
            quote,
            QuoteCreatedEventType,
            BuildQuoteCreatedPayload,
            cancellationToken);

        return new CreateQuoteResult(
            created.Id,
            created.Author,
            created.Text,
            created.IsDeleted);
    }

    private static string BuildQuoteCreatedPayload(Quote quote) =>
        JsonSerializer.Serialize(new
        {
            quote.Id,
            quote.Author,
            quote.Text
        });
}
