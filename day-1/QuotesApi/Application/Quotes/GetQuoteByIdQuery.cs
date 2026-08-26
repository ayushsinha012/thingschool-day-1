using MediatR;

namespace QuotesApi.Application.Quotes;

public sealed record GetQuoteByIdQuery(
    int Id) : IRequest<QuoteReadModel?>;

public sealed record QuoteReadModel(
    int Id,
    string Author,
    string Text,
    string Display,
    int CharacterCount);
