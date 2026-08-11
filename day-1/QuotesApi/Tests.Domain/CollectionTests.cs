using FluentAssertions;
using QuotesApi.Models;

namespace Tests.Domain;

public class CollectionTests
{
    [Fact]
    public void Empty_name_should_throw()
    {
        var act = () => new Collection("", 1);

        act.Should()
            .Throw<ArgumentException>();
    }

    [Fact]
    public void Name_longer_than_80_characters_should_throw()
    {
        var name = new string('A', 81);

        var act = () => new Collection(name, 1);

        act.Should()
            .Throw<ArgumentException>();
    }

    [Fact]
    public void Adding_51st_item_should_throw()
    {
        var collection = new Collection("My Quotes", 1);

        for (var quoteId = 1; quoteId <= 50; quoteId++)
        {
            collection.AddItem(
                quoteId,
                DateTimeOffset.UtcNow);
        }

        var act = () => collection.AddItem(
            51,
            DateTimeOffset.UtcNow);

        act.Should()
            .Throw<InvalidOperationException>();
    }

    [Fact]
    public void Duplicate_quote_id_should_throw()
    {
        var collection = new Collection("My Quotes", 1);

        collection.AddItem(
            1,
            DateTimeOffset.UtcNow);

        var act = () => collection.AddItem(
            1,
            DateTimeOffset.UtcNow);

        act.Should()
            .Throw<InvalidOperationException>();
    }

    [Fact]
    public void Removing_nonexistent_item_should_throw()
    {
        var collection = new Collection("My Quotes", 1);

        var act = () => collection.RemoveItem(1);

        act.Should()
            .Throw<InvalidOperationException>();
    }

    [Fact]
    public void Adding_then_removing_item_should_leave_zero_items()
    {
        var collection = new Collection("My Quotes", 1);

        collection.AddItem(
            1,
            DateTimeOffset.UtcNow);

        collection.RemoveItem(1);

        collection.Items.Should().BeEmpty();
    }
}