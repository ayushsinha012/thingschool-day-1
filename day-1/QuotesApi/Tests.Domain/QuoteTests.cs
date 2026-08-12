using FluentAssertions;
using QuotesApi.Models;

namespace Tests.Domain;

/// <summary>
/// Unit tests for the <see cref="Quote.Create"/> factory and
/// <see cref="Quote.SoftDelete"/>, covering every validation branch present
/// in the production implementation.
/// </summary>
public class QuoteTests
{
    [Fact]
    public void Create_WithValidAuthorAndText_ReturnsQuoteWithExpectedValues()
    {
        // Arrange
        var author = "Marcus Aurelius";
        var text = "You have power over your mind, not outside events.";

        // Act
        var quote = Quote.Create(author, text);

        // Assert
        quote.Author.Should().Be(author);
        quote.Text.Should().Be(text);
        quote.IsDeleted.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithNullEmptyOrWhitespaceAuthor_ThrowsArgumentException(
        string? author)
    {
        // Arrange
        var text = "Some quote text.";

        // Act
        var act = () => Quote.Create(author!, text);

        // Assert
        act.Should()
            .Throw<ArgumentException>()
            .Where(ex => ex.ParamName == "author");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithNullEmptyOrWhitespaceText_ThrowsArgumentException(
        string? text)
    {
        // Arrange
        var author = "Some Author";

        // Act
        var act = () => Quote.Create(author, text!);

        // Assert
        act.Should()
            .Throw<ArgumentException>()
            .Where(ex => ex.ParamName == "text");
    }

    [Fact]
    public void Create_TrimsLeadingAndTrailingWhitespaceFromAuthorAndText()
    {
        // Arrange
        var author = "   Seneca   ";
        var text = "   It is not that we have a short time to live, but that we waste a lot of it.   ";

        // Act
        var quote = Quote.Create(author, text);

        // Assert
        quote.Author.Should().Be("Seneca");
        quote.Text.Should().Be("It is not that we have a short time to live, but that we waste a lot of it.");
    }

    [Fact]
    public void Create_WithAuthorAtMaximumLength_Succeeds()
    {
        // Arrange
        var author = new string('A', 200);
        var text = "Valid text.";

        // Act
        var quote = Quote.Create(author, text);

        // Assert
        quote.Author.Should().HaveLength(200);
    }

    [Fact]
    public void Create_WithAuthorExceedingMaximumLength_ThrowsArgumentException()
    {
        // Arrange
        var author = new string('A', 201);
        var text = "Valid text.";

        // Act
        var act = () => Quote.Create(author, text);

        // Assert
        act.Should()
            .Throw<ArgumentException>()
            .Where(ex => ex.ParamName == "author");
    }

    [Fact]
    public void Create_WithTextAtMaximumLength_Succeeds()
    {
        // Arrange
        var author = "Valid Author";
        var text = new string('B', 1000);

        // Act
        var quote = Quote.Create(author, text);

        // Assert
        quote.Text.Should().HaveLength(1000);
    }

    [Fact]
    public void Create_WithTextExceedingMaximumLength_ThrowsArgumentException()
    {
        // Arrange
        var author = "Valid Author";
        var text = new string('B', 1001);

        // Act
        var act = () => Quote.Create(author, text);

        // Assert
        act.Should()
            .Throw<ArgumentException>()
            .Where(ex => ex.ParamName == "text");
    }

    [Fact]
    public void SoftDelete_SetsIsDeletedToTrue()
    {
        // Arrange
        var quote = Quote.Create("Author", "Text");

        // Act
        quote.SoftDelete();

        // Assert
        quote.IsDeleted.Should().BeTrue();
    }
}
