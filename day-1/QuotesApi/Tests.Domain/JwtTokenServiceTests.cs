using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using QuotesApi.Authorization;
using QuotesApi.Models;
using QuotesApi.Services;

namespace Tests.Domain;

/// <summary>
/// Unit tests for <see cref="JwtTokenService"/> using a real
/// <see cref="IConfiguration"/> built in-memory (no mocking framework).
/// </summary>
public class JwtTokenServiceTests
{
    private const string ValidSigningKey =
        "Jwt-Token-Service-Test-Signing-Key-0123456789AB";

    private static IConfiguration BuildConfiguration(
        string? key,
        int? accessTokenMinutes = null)
    {
        var settings = new Dictionary<string, string?>();

        if (key is not null)
        {
            settings["Jwt:Key"] = key;
        }

        if (accessTokenMinutes is not null)
        {
            settings["Jwt:AccessTokenMinutes"] = accessTokenMinutes.ToString();
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
    }

    [Fact]
    public void CreateAccessToken_WithValidConfiguration_ReturnsTokenContainingExpectedClaims()
    {
        // Arrange
        var configuration = BuildConfiguration(ValidSigningKey);
        var service = new JwtTokenService(configuration);
        var user = new User { Id = 7, Email = "user@example.com" };

        // Act
        var token = service.CreateAccessToken(user);

        // Assert
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should().Contain(
            c => c.Type == ClaimTypes.NameIdentifier && c.Value == "7");
        jwt.Claims.Should().Contain(
            c => c.Type == ClaimTypes.Email && c.Value == "user@example.com");
        jwt.Claims.Should().Contain(
            c => c.Type == PermissionClaims.ClaimType
                && c.Value == PermissionClaims.CanEditQuotes);
    }

    [Fact]
    public void CreateAccessToken_WithMissingSigningKey_ThrowsInvalidOperationException()
    {
        // Arrange
        var configuration = BuildConfiguration(key: null);
        var service = new JwtTokenService(configuration);
        var user = new User { Id = 1, Email = "user@example.com" };

        // Act
        var act = () => service.CreateAccessToken(user);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CreateAccessToken_WithSigningKeyShorterThan256Bits_ThrowsInvalidOperationException()
    {
        // Arrange
        var configuration = BuildConfiguration(key: "too-short-key");
        var service = new JwtTokenService(configuration);
        var user = new User { Id = 1, Email = "user@example.com" };

        // Act
        var act = () => service.CreateAccessToken(user);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CreateAccessToken_UsesConfiguredAccessTokenMinutes_ForExpiry()
    {
        // Arrange
        var configuration = BuildConfiguration(ValidSigningKey, accessTokenMinutes: 45);
        var service = new JwtTokenService(configuration);
        var user = new User { Id = 1, Email = "user@example.com" };
        var expectedExpiry = DateTime.UtcNow.AddMinutes(45);

        // Act
        var token = service.CreateAccessToken(user);

        // Assert
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.ValidTo.Should().BeCloseTo(expectedExpiry, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void GetAccessTokenLifetimeSeconds_WithConfiguredMinutes_ReturnsMinutesConvertedToSeconds()
    {
        // Arrange
        var configuration = BuildConfiguration(ValidSigningKey, accessTokenMinutes: 10);
        var service = new JwtTokenService(configuration);

        // Act
        var lifetimeSeconds = service.GetAccessTokenLifetimeSeconds();

        // Assert
        lifetimeSeconds.Should().Be(600);
    }

    [Fact]
    public void GetAccessTokenLifetimeSeconds_WithNoConfiguredMinutes_DefaultsToFifteenMinutes()
    {
        // Arrange
        var configuration = BuildConfiguration(ValidSigningKey);
        var service = new JwtTokenService(configuration);

        // Act
        var lifetimeSeconds = service.GetAccessTokenLifetimeSeconds();

        // Assert
        lifetimeSeconds.Should().Be(15 * 60);
    }
}
