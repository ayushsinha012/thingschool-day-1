using System;

namespace QuotesApi.Models;

public class CollectionItem
{
    // EF Core needs a private constructor.
    private CollectionItem()
    {
    }

    public CollectionItem(
        int quoteId,
        DateTimeOffset addedAt)
    {
        if (quoteId <= 0)
        {
            throw new ArgumentException(
                "Quote ID must be greater than zero.",
                nameof(quoteId));
        }

        QuoteId = quoteId;
        AddedAt = addedAt;
    }

    public int QuoteId { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }
}