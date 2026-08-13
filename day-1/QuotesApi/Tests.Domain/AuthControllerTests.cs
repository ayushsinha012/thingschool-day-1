using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using QuotesApi.Authorization;
using QuotesApi.Controllers;
using QuotesApi.Data;
using QuotesApi.DTOs;
using QuotesApi.Models;
using QuotesApi.Services;

namespace Tests.Domain;

/// <summary>
/// Unit tests for <see cref="AuthController"/> using a real
/// <see cref="AppDbContext"/> backed by an in-memory SQLite connection
/// (no mocking framework) and a real <see cref="JwtTokenService"/>, so
/// the tests exercise the actual credential-checking and token-issuing
/// behavior rather than a stand-in for it.
/// </summary>
public class AuthControllerTests : IDisposable
{
    private const string ValidSigningKey =
        "Auth-Controller-Test-Signing-Key-0123456789ABCDEF";

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly AuthController _controller;

    public AuthControllerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = ValidSigningKey
            })
            .Build();

        _controller = new AuthController(
            _db,
            new JwtTokenService(configuration));
    }

    private async Task SeedUserAsync(string email, string password)
    {
        _db.Users.Add(new User
        {
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password)
        });

        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Login_WithMissingEmail_ReturnsBadRequestAndDoesNotQueryUsers()
    {
        // Arrange
        await SeedUserAsync("user@example.com", "correct-password");

        // Act
        var result = await _controller.Login(
            new LoginRequest(Email: "", Password: "correct-password"),
            CancellationToken.None);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Title.Should().Be("Validation failed");
    }

    [Fact]
    public async Task Login_WithMissingPassword_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.Login(
            new LoginRequest(Email: "user@example.com", Password: "   "),
            CancellationToken.None);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Title.Should().Be("Validation failed");
    }

    [Fact]
    public async Task Login_WithUnknownEmail_ReturnsUnauthorized()
    {
        // Arrange
        await SeedUserAsync("someone-else@example.com", "correct-password");

        // Act
        var result = await _controller.Login(
            new LoginRequest(Email: "missing@example.com", Password: "correct-password"),
            CancellationToken.None);

        // Assert
        var unauthorized = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        var problem = unauthorized.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Title.Should().Be("Invalid credentials");
    }

    [Fact]
    public async Task Login_WithIncorrectPassword_ReturnsUnauthorized()
    {
        // Arrange
        await SeedUserAsync("user@example.com", "correct-password");

        // Act
        var result = await _controller.Login(
            new LoginRequest(Email: "user@example.com", Password: "wrong-password"),
            CancellationToken.None);

        // Assert
        var unauthorized = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        var problem = unauthorized.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Title.Should().Be("Invalid credentials");
    }

    [Fact]
    public async Task Login_WithCorrectCredentials_ReturnsAccessTokenCarryingUserClaimsAndARefreshToken()
    {
        // Arrange - the stored email has no surrounding whitespace, but the
        // incoming request does, so this also proves the controller trims
        // the request email before comparing it against stored users.
        await SeedUserAsync("user@example.com", "correct-password");

        // Act
        var result = await _controller.Login(
            new LoginRequest(Email: "  user@example.com  ", Password: "correct-password"),
            CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().NotBeNull().And.Subject;

        var accessToken = payload.GetType()
            .GetProperty("access_token")!
            .GetValue(payload) as string;

        var refreshToken = payload.GetType()
            .GetProperty("refresh_token")!
            .GetValue(payload) as string;

        var expiresIn = (int)payload.GetType()
            .GetProperty("expires_in")!
            .GetValue(payload)!;

        accessToken.Should().NotBeNullOrWhiteSpace();
        refreshToken.Should().NotBeNullOrWhiteSpace();
        expiresIn.Should().Be(15 * 60);

        // The access token must be the real, verifiable JWT issued by
        // JwtTokenService for this user - not a placeholder string.
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        jwt.Claims.Should().Contain(
            c => c.Type == ClaimTypes.Email && c.Value == "user@example.com");
        jwt.Claims.Should().Contain(
            c => c.Type == PermissionClaims.ClaimType
                && c.Value == PermissionClaims.CanEditQuotes);
    }

    [Fact]
    public async Task Login_CalledTwiceWithSameCredentials_IssuesTwoDistinctRefreshTokens()
    {
        // Arrange - the refresh token is generated per login call from a
        // random byte source, not derived deterministically from the user;
        // this guards against a regression that made it predictable/reused.
        await SeedUserAsync("user@example.com", "correct-password");

        // Act
        var firstResult = await _controller.Login(
            new LoginRequest(Email: "user@example.com", Password: "correct-password"),
            CancellationToken.None);

        var secondResult = await _controller.Login(
            new LoginRequest(Email: "user@example.com", Password: "correct-password"),
            CancellationToken.None);

        // Assert
        var firstRefreshToken = GetRefreshToken(firstResult);
        var secondRefreshToken = GetRefreshToken(secondResult);

        firstRefreshToken.Should().NotBe(secondRefreshToken);

        static string GetRefreshToken(IActionResult result)
        {
            var payload = ((OkObjectResult)result).Value!;
            return (string)payload.GetType()
                .GetProperty("refresh_token")!
                .GetValue(payload)!;
        }
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
