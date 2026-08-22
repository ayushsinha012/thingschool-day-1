using FluentAssertions;
using QuotesApi.Application.Quotes;
using Tests.Domain.TestDoubles;

namespace Tests.Domain;

/// <summary>
/// Unit tests for <see cref="CreateQuoteCommandHandler"/> using a
/// hand-written in-memory test double for <see cref="QuotesApi.Repositories.IQuoteRepository"/>
/// (no database, no mocking framework).
/// </summary>
public class CreateQuoteCommandHandlerTests
{
    [Fact]
    public async Task Handle_WithValidAuthorAndText_PersistsQuoteAndReturnsResult()
    {
        // Arrange
        var repository = new InMemoryQuoteRepository();
        var handler = new CreateQuoteCommandHandler(repository);

        var command = new CreateQuoteCommand(
            "Marcus Aurelius",
            "You have power over your mind, not outside events.");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Author.Should().Be("Marcus Aurelius");
        result.Text.Should().Be("You have power over your mind, not outside events.");
        result.IsDeleted.Should().BeFalse();

        repository.AddedQuotes.Should().ContainSingle();
        repository.AddedQuotes[0].Author.Should().Be("Marcus Aurelius");
    }

    [Fact]
    public async Task Handle_WithBlankAuthor_ThrowsArgumentException_AndDoesNotPersist()
    {
        // Arrange
        var repository = new InMemoryQuoteRepository();
        var handler = new CreateQuoteCommandHandler(repository);

        var command = new CreateQuoteCommand(
            "   ",
            "Some quote text.");

        // Act
        var act = () => handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should()
            .ThrowAsync<ArgumentException>()
            .Where(ex => ex.ParamName == "author");

        repository.AddedQuotes.Should().BeEmpty();
    }
}
