using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;

namespace Tests.Domain;

/// <summary>
/// End-to-end coverage of the "can-edit-quotes" claim policy and the
/// collection-ownership requirement, asserting the actual HTTP status
/// codes returned by the running QuotesApi pipeline.
/// </summary>
public class CollectionAuthorizationTests : IClassFixture<AuthorizationTestFactory>
{
    private readonly AuthorizationTestFactory _factory;
    private readonly HttpClient _client;

    public CollectionAuthorizationTests(AuthorizationTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Authenticated_user_with_required_claim_is_authorized()
    {
        var token = _factory.CreateAccessToken(
            userId: 101,
            includeEditQuotesClaim: true);

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync(
            "/api/collections",
            new { Name = "Stoic Quotes", OwnerId = 101 });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Authenticated_user_without_claim_receives_403()
    {
        var token = _factory.CreateAccessToken(
            userId: 102,
            includeEditQuotesClaim: false);

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync(
            "/api/collections",
            new { Name = "Stoic Quotes", OwnerId = 102 });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Authenticated_user_who_does_not_own_resource_receives_403()
    {
        var collection = await _factory.SeedCollectionAsync(ownerId: 201);

        var token = _factory.CreateAccessToken(
            userId: 202,
            includeEditQuotesClaim: true);

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync(
            $"/api/collections/{collection.Id}/items",
            new { QuoteId = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Owner_is_authorized()
    {
        var collection = await _factory.SeedCollectionAsync(ownerId: 301);

        var token = _factory.CreateAccessToken(
            userId: 301,
            includeEditQuotesClaim: true);

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync(
            $"/api/collections/{collection.Id}/items",
            new { QuoteId = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
