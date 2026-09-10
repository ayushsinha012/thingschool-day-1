using MediatR;

namespace QuotesApi.Application.Quotes;

public sealed record UpdateQuoteCommand(
    int Id,
    string Author,
    string Text) : IRequest<UpdateQuoteResult?>;

// Null Author/Text on UpdateQuoteResult would be indistinguishable from "not
// found" for callers, so the whole result is nullable instead - mirrors
// GetQuoteByIdQuery's QuoteReadModel? convention.
public sealed record UpdateQuoteResult(
    int Id,
    string Author,
    string Text,
    bool IsDeleted);
