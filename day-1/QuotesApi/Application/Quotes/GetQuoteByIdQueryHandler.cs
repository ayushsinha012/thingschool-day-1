using MediatR;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;

namespace QuotesApi.Application.Quotes;

public sealed class GetQuoteByIdQueryHandler : IRequestHandler<GetQuoteByIdQuery, QuoteReadModel?>
{
    private readonly AppDbContext _db;

    public GetQuoteByIdQueryHandler(AppDbContext db)
    {
        _db = db;
    }

    public async Task<QuoteReadModel?> Handle(
        GetQuoteByIdQuery request,
        CancellationToken cancellationToken)
    {
        return await _db.Quotes
            .AsNoTracking()
            .Where(quote =>
                quote.Id == request.Id &&
                !quote.IsDeleted)
            .Select(quote => new QuoteReadModel(
                quote.Id,
                quote.Author,
                quote.Text,
                "\"" + quote.Text + "\" — " + quote.Author,
                quote.Text.Length))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
