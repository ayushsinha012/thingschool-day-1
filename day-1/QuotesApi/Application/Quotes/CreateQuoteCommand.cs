using MediatR;

namespace QuotesApi.Application.Quotes;

public sealed record CreateQuoteCommand(
    string Author,
    string Text) : IRequest<CreateQuoteResult>;

public sealed record CreateQuoteResult(
    int Id,
    string Author,
    string Text,
    bool IsDeleted);
