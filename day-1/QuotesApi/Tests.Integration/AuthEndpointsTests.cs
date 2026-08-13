using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Tests.Integration.Infrastructure;

namespace Tests.Integration;

/// <summary>
/// Integration tests for AuthController.cs: login validation, login
/// success, refresh-token rotation, reuse detection killing the whole
/// token chain, logout, and expired-token rejection. Each test builds its
/// own factory/database/client so tests never share state.
/// </summary>
public class AuthEndpointsTests : IDisposable
{
    private readonly QuotesApiFactory _factory;
    private readonly HttpClient _client;

    public AuthEndpointsTests()
    {
        _factory = new QuotesApiFactory();
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Login_with_missing_email_returns_400_with_validation_problem_details()
    {
        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new { Email = "", Password = "whatever" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        document.RootElement.TryGetProperty("errors", out _)
            .Should().BeTrue("a ValidationProblemDetails body carries an 'errors' dictionary");
    }

    [Fact]
    public async Task Login_with_correct_credentials_then_refresh_returns_a_new_token_pair()
    {
        // Arrange
        var user = await _factory.SeedUserAsync("refresh-user@example.com", "correct-password");

        var loginResponse = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new { Email = "refresh-user@example.com", Password = "correct-password" });

        var loginBody = await loginResponse.Content.ReadFromJsonAsync<TokenPairBody>();

        // Act
        var refreshResponse = await _client.PostAsJsonAsync(
            "/api/auth/refresh",
            new { RefreshToken = loginBody!.refresh_token });

        // Assert
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var refreshedBody = await refreshResponse.Content.ReadFromJsonAsync<TokenPairBody>();

        refreshedBody.Should().NotBeNull();
        refreshedBody!.access_token.Should().NotBeNullOrWhiteSpace();
        refreshedBody.refresh_token.Should().NotBeNullOrWhiteSpace();
        refreshedBody.refresh_token.Should().NotBe(loginBody.refresh_token);
    }

    [Fact]
    public async Task Refresh_with_a_reused_token_returns_401_and_revokes_the_entire_chain()
    {
        // Arrange - rotate once (legitimate), then replay the original,
        // now-stale token as an attacker replaying a leaked token would.
        await _factory.SeedUserAsync("reuse-user@example.com", "correct-password");

        var loginResponse = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new { Email = "reuse-user@example.com", Password = "correct-password" });

        var loginBody = await loginResponse.Content.ReadFromJsonAsync<TokenPairBody>();

        var firstRefreshResponse = await _client.PostAsJsonAsync(
            "/api/auth/refresh",
            new { RefreshToken = loginBody!.refresh_token });

        var firstRefreshedBody = await firstRefreshResponse.Content.ReadFromJsonAsync<TokenPairBody>();

        // Act - replay the already-used original refresh token.
        var reuseResponse = await _client.PostAsJsonAsync(
            "/api/auth/refresh",
            new { RefreshToken = loginBody.refresh_token });

        // Assert
        reuseResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // The whole chain is revoked, so even the legitimately-rotated
        // second token (never itself replayed) is now rejected too.
        var secondRefreshResponse = await _client.PostAsJsonAsync(
            "/api/auth/refresh",
            new { RefreshToken = firstRefreshedBody!.refresh_token });

        secondRefreshResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token_so_it_can_no_longer_be_refreshed()
    {
        // Arrange
        await _factory.SeedUserAsync("logout-user@example.com", "correct-password");

        var loginResponse = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new { Email = "logout-user@example.com", Password = "correct-password" });

        var loginBody = await loginResponse.Content.ReadFromJsonAsync<TokenPairBody>();

        // Act
        var logoutResponse = await _client.PostAsJsonAsync(
            "/api/auth/logout",
            new { RefreshToken = loginBody!.refresh_token });

        // Assert
        logoutResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var refreshAfterLogoutResponse = await _client.PostAsJsonAsync(
            "/api/auth/refresh",
            new { RefreshToken = loginBody.refresh_token });

        refreshAfterLogoutResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_quote_with_expired_token_returns_401_with_www_authenticate_header()
    {
        // Arrange
        var user = await _factory.SeedUserAsync("expired-token-user@example.com");
        var expiredToken = _factory.CreateExpiredAccessToken(user);

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", expiredToken);

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/quotes",
            new { Author = "Epictetus", Text = "It's not what happens to you, but how you react to it." });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Should().NotBeEmpty();
        response.Headers.WwwAuthenticate.ToString().Should().Contain("Bearer");
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private sealed record TokenPairBody(
        string access_token,
        string refresh_token,
        int expires_in);
}
