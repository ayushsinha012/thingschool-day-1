namespace QuotesApi.Models;

public class Collection
{
    private readonly List<CollectionItem> _items = new();

    private Collection()
    {
    }

    public Collection(string name, int ownerId)
    {
        ValidateName(name);

        OwnerId = ownerId;
        Name = name;
    }

    public int Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public int OwnerId { get; private set; }

    public IReadOnlyCollection<CollectionItem> Items => _items.AsReadOnly();

    public void AddItem(int quoteId)
    {
        if (quoteId <= 0)
        {
            throw new ArgumentException(
                "Quote ID must be greater than zero.");
        }

        if (_items.Count >= 50)
        {
            throw new InvalidOperationException(
                "A collection cannot contain more than 50 items.");
        }

        if (_items.Any(item => item.QuoteId == quoteId))
        {
            throw new InvalidOperationException(
                "The quote already exists in this collection.");
        }

        _items.Add(new CollectionItem(
            quoteId,
            DateTime.UtcNow));
    }

    public void RemoveItem(int quoteId)
    {
        var item = _items.FirstOrDefault(
            item => item.QuoteId == quoteId);

        if (item is null)
        {
            throw new InvalidOperationException(
                "The quote does not exist in this collection.");
        }

        _items.Remove(item);
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Collection name is required.");
        }

        if (name.Length < 3 || name.Length > 80)
        {
            throw new ArgumentException(
                "Collection name must be between 3 and 80 characters.");
        }
    }
}