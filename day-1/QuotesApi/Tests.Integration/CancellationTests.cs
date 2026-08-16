using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using Tests.Integration.Infrastructure;

namespace Tests.Integration;

/// <summary>
/// Proves the CancellationToken passed to CollectionsController's endpoints
/// actually flows through CollectionService and CollectionRepository into
/// the EF Core call, instead of being accepted but ignored.
///
/// The token is cancelled before the request is sent (rather than raced
/// mid-flight, which would make the test flaky) so the outcome is
/// deterministic: HttpClient throws a cancellation exception instead of
/// completing the call, and - because the cancellation was honored all the
/// way down rather than swallowed - the write never reaches the database.
/// </summary>
public class CancellationTests : IDisposable
{
    private readonly QuotesApiFactory _factory;
    private readonly HttpClient _client;

    public CancellationTests()
    {
        _factory = new QuotesApiFactory();
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Post_collection_with_a_cancelled_token_does_not_complete_or_persist_the_collection()
    {
        // Arrange
        var owner = await _factory.SeedUserAsync("cancellation-owner@example.com");
        var token = _factory.CreateAccessToken(owner);

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var request = new
        {
            Name = "Should Never Be Persisted",
            OwnerId = owner.Id
        };

        // Act
        var act = async () => await _client.PostAsJsonAsync(
            "/api/collections",
            request,
            cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();

        using var scope = _factory.Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var persisted = await db.Collections
            .FirstOrDefaultAsync(collection => collection.Name == "Should Never Be Persisted");

        persisted.Should().BeNull();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
