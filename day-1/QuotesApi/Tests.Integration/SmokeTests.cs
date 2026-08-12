using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Tests.Integration.Infrastructure;

namespace Tests.Integration;

/// <summary>
/// Part 1 smoke tests for the WebApplicationFactory-based integration test
/// infrastructure: one public endpoint and one protected endpoint hit
/// through the real middleware pipeline. xUnit creates a fresh instance of
/// this class per test method, and the constructor creates a brand new
/// factory/database/client each time, so no state leaks between tests.
/// </summary>
public class SmokeTests : IDisposable
{
    private readonly QuotesApiFactory _factory;
    private readonly HttpClient _client;

    public SmokeTests()
    {
        _factory = new QuotesApiFactory();
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Get_quotes_public_endpoint_succeeds_with_valid_body()
    {
        // Arrange
        // (no arrangement needed: GET /api/quotes is public and this
        // factory's database is freshly migrated with no quotes seeded)

        // Act
        var response = await _client.GetAsync("/api/quotes");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content
            .ReadFromJsonAsync<QuotesPageResponse>();

        body.Should().NotBeNull();
        body!.Page.Should().Be(1);
        body.Size.Should().Be(10);
        body.Total.Should().Be(0);
        body.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Post_quote_without_authentication_returns_unauthorized()
    {
        // Arrange
        var request = new
        {
            Author = "Marcus Aurelius",
            Text = "You have power over your mind, not outside events."
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/quotes",
            request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private sealed class QuotesPageResponse
    {
        public int Page { get; set; }

        public int Size { get; set; }

        public int Total { get; set; }

        public List<object> Items { get; set; } = new();
    }
}
