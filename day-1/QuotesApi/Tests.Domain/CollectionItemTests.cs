using FluentAssertions;
using QuotesApi.Models;

namespace Tests.Domain;

/// <summary>
/// Unit tests for the <see cref="CollectionItem"/> constructor's validation
/// branches.
/// </summary>
public class CollectionItemTests
{
    [Fact]
    public void Constructor_WithValidArguments_SetsQuoteIdAndAddedAt()
    {
        // Arrange
        var quoteId = 42;
        var addedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Act
        var item = new CollectionItem(quoteId, addedAt);

        // Assert
        item.QuoteId.Should().Be(quoteId);
        item.AddedAt.Should().Be(addedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithNonPositiveQuoteId_ThrowsArgumentException(
        int quoteId)
    {
        // Arrange
        var addedAt = DateTimeOffset.UtcNow;

        // Act
        var act = () => new CollectionItem(quoteId, addedAt);

        // Assert
        act.Should()
            .Throw<ArgumentException>()
            .Where(ex => ex.ParamName == "quoteId");
    }
}
