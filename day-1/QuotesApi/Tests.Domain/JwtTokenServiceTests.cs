using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using QuotesApi.Authorization;
using QuotesApi.Models;
using QuotesApi.Services;

namespace Tests.Domain;

/// <summary>
/// Unit tests for <see cref="JwtTokenService"/> using a real
/// <see cref="IConfiguration"/> built in-memory (no mocking framework),
/// bound into <see cref="JwtOptions"/> the same way DI would via
/// services.Configure&lt;JwtOptions&gt;(...).
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

    private static JwtTokenService CreateService(
        string? key,
        int? accessTokenMinutes = null)
    {
        var configuration = BuildConfiguration(key, accessTokenMinutes);

        var options = configuration
            .GetSection(JwtOptions.SectionName)
            .Get<JwtOptions>() ?? new JwtOptions();

        return new JwtTokenService(Options.Create(options));
    }

    [Fact]
    public void CreateAccessToken_WithValidConfiguration_ReturnsTokenContainingExpectedClaims()
    {
        // Arrange
        var service = CreateService(ValidSigningKey);
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
        var service = CreateService(key: null);
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
        var service = CreateService(key: "too-short-key");
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
        var service = CreateService(ValidSigningKey, accessTokenMinutes: 45);
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
        var service = CreateService(ValidSigningKey, accessTokenMinutes: 10);

        // Act
        var lifetimeSeconds = service.GetAccessTokenLifetimeSeconds();

        // Assert
        lifetimeSeconds.Should().Be(600);
    }

    [Fact]
    public void GetAccessTokenLifetimeSeconds_WithNoConfiguredMinutes_DefaultsToFifteenMinutes()
    {
        // Arrange
        var service = CreateService(ValidSigningKey);

        // Act
        var lifetimeSeconds = service.GetAccessTokenLifetimeSeconds();

        // Assert
        lifetimeSeconds.Should().Be(15 * 60);
    }
}
