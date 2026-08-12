using FluentAssertions;
using QuotesApi.Models;
using QuotesApi.Services;

namespace Tests.Domain;

/// <summary>
/// Unit tests for <see cref="QuoteFormatter"/>.
/// </summary>
public class QuoteFormatterTests
{
    [Fact]
    public void Format_WithValidQuote_ReturnsTextAndAuthorInExpectedStyle()
    {
        // Arrange
        var formatter = new QuoteFormatter();
        var quote = Quote.Create("Marcus Aurelius", "You have power over your mind.");

        // Act
        var formatted = formatter.Format(quote);

        // Assert
        formatted.Should().Be("\"You have power over your mind.\" — Marcus Aurelius");
    }

    [Fact]
    public void Format_WithNullQuote_ThrowsArgumentNullException()
    {
        // Arrange
        var formatter = new QuoteFormatter();

        // Act
        var act = () => formatter.Format(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Format_WithTextContainingEmbeddedQuoteCharacters_FormatsWithoutAlteringContent()
    {
        // Arrange
        var formatter = new QuoteFormatter();
        var quote = Quote.Create("Author", "He said \"hello\" to everyone.");

        // Act
        var formatted = formatter.Format(quote);

        // Assert
        formatted.Should().Be("\"He said \"hello\" to everyone.\" — Author");
    }
}
