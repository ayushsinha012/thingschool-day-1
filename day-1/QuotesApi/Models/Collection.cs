using System;
using System.Collections.Generic;
using System.Linq;

namespace QuotesApi.Models;

public class Collection
{
    // Private backing field.
    // Only the aggregate itself can change the collection items.
    private readonly List<CollectionItem> _items = new();

    // EF Core needs a private constructor.
    private Collection()
    {
    }

    public Collection(string name, int ownerId)
    {
        ValidateName(name);

        if (ownerId <= 0)
        {
            throw new ArgumentException(
                "Owner ID must be greater than zero.",
                nameof(ownerId));
        }

        Name = name.Trim();
        OwnerId = ownerId;
    }

    public int Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public int OwnerId { get; private set; }

    // Read-only to callers.
    // Mutations must go through AddItem/RemoveItem.
    public IReadOnlyCollection<CollectionItem> Items =>
        _items.AsReadOnly();

    /// <summary>
    /// Adds a quote to this collection.
    /// All collection invariants are checked here.
    /// </summary>
    public void AddItem(
        int quoteId,
        DateTimeOffset addedAt)
    {
        if (quoteId <= 0)
        {
            throw new ArgumentException(
                "Quote ID must be greater than zero.",
                nameof(quoteId));
        }

        // Maximum 50 items.
        if (_items.Count >= 50)
        {
            throw new InvalidOperationException(
                "A collection cannot contain more than 50 items.");
        }

        // No duplicate quotes.
        if (_items.Any(item => item.QuoteId == quoteId))
        {
            throw new InvalidOperationException(
                "The quote already exists in this collection.");
        }

        _items.Add(
            new CollectionItem(
                quoteId,
                addedAt));
    }

    /// <summary>
    /// Removes a quote from this collection.
    /// </summary>
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
                "Collection name is required.",
                nameof(name));
        }

        var trimmedName = name.Trim();

        if (trimmedName.Length < 3 ||
            trimmedName.Length > 80)
        {
            throw new ArgumentException(
                "Collection name must be between 3 and 80 characters.",
                nameof(name));
        }
    }
}