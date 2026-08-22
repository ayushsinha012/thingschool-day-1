using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Application.Quotes;
using QuotesApi.Data;
using QuotesApi.Models;

namespace Tests.Domain;

/// <summary>
/// Unit tests for <see cref="GetQuoteByIdQueryHandler"/> using a real
/// <see cref="AppDbContext"/> backed by an in-memory SQLite connection
/// (no mocking framework), following the same setup used by
/// <see cref="AuthControllerTests"/>.
/// </summary>
public class GetQuoteByIdQueryHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly GetQuoteByIdQueryHandler _handler;

    public GetQuoteByIdQueryHandlerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _handler = new GetQuoteByIdQueryHandler(_db);
    }

    [Fact]
    public async Task Handle_WithExistingQuote_ReturnsReadModelShapedForResponse()
    {
        // Arrange
        var quote = Quote.Create("Seneca", "Luck is what happens when preparation meets opportunity.");

        _db.Quotes.Add(quote);
        await _db.SaveChangesAsync();

        // Act
        var result = await _handler.Handle(
            new GetQuoteByIdQuery(quote.Id),
            CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(quote.Id);
        result.Author.Should().Be("Seneca");
        result.Text.Should().Be("Luck is what happens when preparation meets opportunity.");
        result.Display.Should().Be(
            "\"Luck is what happens when preparation meets opportunity.\" — Seneca");
        result.CharacterCount.Should().Be(quote.Text.Length);
    }

    [Fact]
    public async Task Handle_WithSoftDeletedQuote_ReturnsNull()
    {
        // Arrange
        var quote = Quote.Create("Epictetus", "It's not what happens to you, but how you react to it that matters.");
        quote.SoftDelete();

        _db.Quotes.Add(quote);
        await _db.SaveChangesAsync();

        // Act
        var result = await _handler.Handle(
            new GetQuoteByIdQuery(quote.Id),
            CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithNonExistentId_ReturnsNull()
    {
        // Act
        var result = await _handler.Handle(
            new GetQuoteByIdQuery(999),
            CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
