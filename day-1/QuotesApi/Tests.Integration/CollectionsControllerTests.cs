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
/// Integration tests for CollectionsController.cs: creating collections,
/// retrieving them, adding/removing items, and enforcing owner-only
/// mutation via CollectionOwnershipAuthorizationHandler. Each test builds
/// its own factory/database/client so tests never share state.
/// </summary>
public class CollectionsControllerTests : IDisposable
{
    private readonly QuotesApiFactory _factory;
    private readonly HttpClient _client;

    public CollectionsControllerTests()
    {
        _factory = new QuotesApiFactory();
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Post_collection_with_valid_request_returns_created_and_persists_it_in_database()
    {
        // Arrange
        var owner = await _factory.SeedUserAsync("collection-owner@example.com");
        var token = _factory.CreateAccessToken(owner);

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var request = new { Name = "Stoic Favorites", OwnerId = owner.Id };

        // Act
        var response = await _client.PostAsJsonAsync("/api/collections", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content
            .ReadFromJsonAsync<CollectionBody>();

        created.Should().NotBeNull();
        created!.Name.Should().Be("Stoic Favorites");
        created.OwnerId.Should().Be(owner.Id);

        using var scope = _factory.Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var persisted = await db.Collections
            .FirstOrDefaultAsync(collection => collection.Id == created.Id);

        persisted.Should().NotBeNull();
        persisted!.Name.Should().Be("Stoic Favorites");
        persisted.OwnerId.Should().Be(owner.Id);
    }

    [Fact]
    public async Task Post_collection_with_too_short_name_returns_400()
    {
        // Arrange
        var owner = await _factory.SeedUserAsync("collection-owner-2@example.com");
        var token = _factory.CreateAccessToken(owner);

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var request = new { Name = "ab", OwnerId = owner.Id };

        // Act
        var response = await _client.PostAsJsonAsync("/api/collections", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Get_collection_by_id_returns_existing_collection()
    {
        // Arrange
        var owner = await _factory.SeedUserAsync("collection-owner-3@example.com");
        var token = _factory.CreateAccessToken(owner);

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var createResponse = await _client.PostAsJsonAsync(
            "/api/collections",
            new { Name = "Morning Reads", OwnerId = owner.Id });

        var created = await createResponse.Content
            .ReadFromJsonAsync<CollectionBody>();

        // Act
        var response = await _client.GetAsync($"/api/collections/{created!.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var fetched = await response.Content
            .ReadFromJsonAsync<CollectionBody>();

        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(created.Id);
        fetched.Name.Should().Be("Morning Reads");
    }

    [Fact]
    public async Task Get_collection_by_id_that_does_not_exist_returns_404()
    {
        // Arrange
        // (the freshly migrated database has no collection with ID 999)

        // Act
        var response = await _client.GetAsync("/api/collections/999");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Post_add_quote_to_collection_as_owner_succeeds_and_persists_item()
    {
        // Arrange
        var owner = await _factory.SeedUserAsync("collection-owner-4@example.com");
        var token = _factory.CreateAccessToken(owner);

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var createResponse = await _client.PostAsJsonAsync(
            "/api/collections",
            new { Name = "Evening Reads", OwnerId = owner.Id });

        var created = await createResponse.Content
            .ReadFromJsonAsync<CollectionBody>();

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/collections/{created!.Id}/items",
            new { QuoteId = 42 });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content
            .ReadFromJsonAsync<CollectionBody>();

        updated.Should().NotBeNull();
        updated!.Items.Should().ContainSingle(item => item.QuoteId == 42);

        using var scope = _factory.Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var persisted = await db.Collections
            .Include(collection => collection.Items)
            .FirstAsync(collection => collection.Id == created.Id);

        persisted.Items.Should().ContainSingle(item => item.QuoteId == 42);
    }

    [Fact]
    public async Task Delete_remove_quote_from_collection_as_owner_succeeds()
    {
        // Arrange
        var owner = await _factory.SeedUserAsync("collection-owner-5@example.com");
        var token = _factory.CreateAccessToken(owner);

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var createResponse = await _client.PostAsJsonAsync(
            "/api/collections",
            new { Name = "Afternoon Reads", OwnerId = owner.Id });

        var created = await createResponse.Content
            .ReadFromJsonAsync<CollectionBody>();

        await _client.PostAsJsonAsync(
            $"/api/collections/{created!.Id}/items",
            new { QuoteId = 7 });

        // Act
        var response = await _client.DeleteAsync(
            $"/api/collections/{created.Id}/items/7");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content
            .ReadFromJsonAsync<CollectionBody>();

        updated.Should().NotBeNull();
        updated!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Post_add_quote_to_collection_as_non_owner_returns_forbidden()
    {
        // Arrange
        var owner = await _factory.SeedUserAsync("collection-owner-6@example.com");
        var intruder = await _factory.SeedUserAsync("collection-intruder@example.com");

        var ownerToken = _factory.CreateAccessToken(owner);
        var intruderToken = _factory.CreateAccessToken(intruder);

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ownerToken);

        var createResponse = await _client.PostAsJsonAsync(
            "/api/collections",
            new { Name = "Owner Only Reads", OwnerId = owner.Id });

        var created = await createResponse.Content
            .ReadFromJsonAsync<CollectionBody>();

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", intruderToken);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/collections/{created!.Id}/items",
            new { QuoteId = 5 });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private sealed record CollectionBody(
        int Id,
        string Name,
        int OwnerId,
        List<CollectionItemBody> Items);

    private sealed record CollectionItemBody(
        int QuoteId,
        DateTimeOffset AddedAt);
}
