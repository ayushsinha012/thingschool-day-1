namespace QuotesApi.Models;

public class CollectionItem
{
    private CollectionItem()
    {
    }

    public CollectionItem(int quoteId, DateTime addedAt)
    {
        QuoteId = quoteId;
        AddedAt = addedAt;
    }

    public int QuoteId { get; private set; }

    public DateTime AddedAt { get; private set; }
}