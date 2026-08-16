using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using Tests.Integration.Infrastructure;

namespace Tests.Integration;

/// <summary>
/// Integration tests for the minimal-API quote endpoints in
/// QuoteEndpoints.cs (GET list, GET by id, POST, DELETE). Each test builds
/// its own factory/database/client so tests never share state.
/// </summary>
public class QuoteEndpointsTests : IDisposable
{
    private readonly QuotesApiFactory _factory;
    private readonly HttpClient _client;

    public QuoteEndpointsTests()
    {
        _factory = new QuotesApiFactory();
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Get_quotes_with_invalid_page_size_returns_400_with_problem_details()
    {
        // Arrange
        // (page size of 0 is outside the allowed 1-100 range)

        // Act
        var response = await _client.GetAsync("/api/quotes?page=1&size=0");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content
            .ReadFromJsonAsync<ProblemDetailsBody>();

        problem.Should().NotBeNull();
        problem!.Status.Should().Be((int)HttpStatusCode.BadRequest);
        problem.Title.Should().NotBeNullOrWhiteSpace();
        problem.Detail.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Get_quote_by_id_that_does_not_exist_returns_404_with_problem_details()
    {
        // Arrange
        // (the freshly migrated database has no quote with ID 999)

        // Act
        var response = await _client.GetAsync("/api/quotes/999");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var problem = await response.Content
            .ReadFromJsonAsync<ProblemDetailsBody>();

        problem.Should().NotBeNull();
        problem!.Title.Should().NotBeNullOrWhiteSpace();
        problem.Detail.Should().Contain("999");
    }

    [Fact]
    public async Task Post_quote_with_valid_token_creates_quote_and_persists_it_in_database()
    {
        // Arrange
        var user = await _factory.SeedUserAsync("quote-writer@example.com");
        var token = _factory.CreateAccessToken(user);

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var request = new
        {
            Author = "Marcus Aurelius",
            Text = "You have power over your mind, not outside events."
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/quotes", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content
            .ReadFromJsonAsync<QuoteBody>();

        created.Should().NotBeNull();
        created!.Author.Should().Be("Marcus Aurelius");

        using var scope = _factory.Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var persisted = await db.Quotes
            .FirstOrDefaultAsync(quote => quote.Id == created.Id);

        persisted.Should().NotBeNull();
        persisted!.Author.Should().Be("Marcus Aurelius");
        persisted.Text.Should().Be(
            "You have power over your mind, not outside events.");
        persisted.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Delete_quote_with_valid_token_soft_deletes_existing_quote()
    {
        // Arrange
        var user = await _factory.SeedUserAsync("quote-deleter@example.com");
        var token = _factory.CreateAccessToken(user);

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var createResponse = await _client.PostAsJsonAsync(
            "/api/quotes",
            new { Author = "Seneca", Text = "Luck is what happens when preparation meets opportunity." });

        var created = await createResponse.Content
            .ReadFromJsonAsync<QuoteBody>();

        // Act
        var deleteResponse = await _client.DeleteAsync($"/api/quotes/{created!.Id}");

        // Assert
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await _client.GetAsync($"/api/quotes/{created.Id}");

        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private sealed record ProblemDetailsBody(
        int? Status,
        string? Title,
        string? Detail);

    private sealed record QuoteBody(
        int Id,
        string Author,
        string Text,
        bool IsDeleted);
}
