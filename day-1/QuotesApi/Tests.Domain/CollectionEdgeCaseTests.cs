using FluentAssertions;
using QuotesApi.Models;

namespace Tests.Domain;

/// <summary>
/// Additional <see cref="Collection"/> validation and behavior coverage
/// that is not already exercised by <see cref="CollectionTests"/> — the
/// name boundary length, whitespace-only names, owner id validation, name
/// trimming, and item ordering.
/// </summary>
public class CollectionEdgeCaseTests
{
    [Fact]
    public void Constructor_WithWhitespaceOnlyName_ThrowsArgumentException()
    {
        // Arrange
        var name = "   ";

        // Act
        var act = () => new Collection(name, 1);

        // Assert
        act.Should()
            .Throw<ArgumentException>()
            .Where(ex => ex.ParamName == "name");
    }

    [Fact]
    public void Constructor_WithNameAtMinimumLength_Succeeds()
    {
        // Arrange
        var name = "Abc";

        // Act
        var collection = new Collection(name, 1);

        // Assert
        collection.Name.Should().Be(name);
    }

    [Fact]
    public void Constructor_WithNameBelowMinimumLength_ThrowsArgumentException()
    {
        // Arrange
        var name = "Ab";

        // Act
        var act = () => new Collection(name, 1);

        // Assert
        act.Should()
            .Throw<ArgumentException>()
            .Where(ex => ex.ParamName == "name");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithNonPositiveOwnerId_ThrowsArgumentException(
        int ownerId)
    {
        // Arrange
        var name = "Valid Name";

        // Act
        var act = () => new Collection(name, ownerId);

        // Assert
        act.Should()
            .Throw<ArgumentException>()
            .Where(ex => ex.ParamName == "ownerId");
    }

    [Fact]
    public void Constructor_TrimsLeadingAndTrailingWhitespaceFromName()
    {
        // Arrange
        var name = "  My Quotes  ";

        // Act
        var collection = new Collection(name, 1);

        // Assert
        collection.Name.Should().Be("My Quotes");
    }

    [Fact]
    public void Items_ReflectsAddedItemsInInsertionOrder()
    {
        // Arrange
        var collection = new Collection("My Quotes", 1);
        var firstAddedAt = DateTimeOffset.UtcNow;
        var secondAddedAt = firstAddedAt.AddMinutes(1);

        // Act
        collection.AddItem(1, firstAddedAt);
        collection.AddItem(2, secondAddedAt);

        // Assert
        collection.Items
            .Select(item => item.QuoteId)
            .Should()
            .Equal(1, 2);
    }
}
