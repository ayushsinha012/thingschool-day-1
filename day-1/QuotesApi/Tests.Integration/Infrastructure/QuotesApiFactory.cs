using BCrypt.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Services;
using Tests.Integration.TestDoubles;

namespace Tests.Integration.Infrastructure;

/// <summary>
/// Boots the real QuotesApi application (real middleware pipeline, real
/// routing, real controllers, real minimal API endpoints, real
/// authentication/authorization configuration) against a dedicated,
/// temporary SQLite database file so integration tests never touch the
/// developer's local quotes.db.
///
/// The application's own startup code (see Program.cs) already calls
/// AppDbContext.Database.Migrate() and DbSeeder.SeedAsync() before
/// app.Run(). WebApplicationFactory only intercepts Run(), so that
/// migration/seeding still executes normally against the isolated
/// database every time a factory instance is built.
///
/// Create a new instance per test (do not share via IClassFixture across
/// multiple test methods) so every test gets its own database file, its
/// own clock and its own HttpClient.
/// </summary>
public class QuotesApiFactory : WebApplicationFactory<Program>
{
    private const string TestJwtKey =
        "Tests-Integration-Test-Signing-Key-0123456789-ABCDEFGH";

    private readonly string _databasePath;

    public QuotesApiFactory()
    {
        _databasePath = Path.Combine(
            Path.GetTempPath(),
            $"quotes-integration-tests-{Guid.NewGuid():N}.db");
    }

    /// <summary>
    /// The fake clock installed into the application's DI container in
    /// place of the real <see cref="IClock"/>. Tests can adjust
    /// <see cref="TestDoubles.FakeClock.UtcNow"/> to control what the
    /// running application sees as "now", without touching production
    /// clock behavior.
    /// </summary>
    public FakeClock Clock { get; } = new(DateTimeOffset.UtcNow);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Jwt:Key", TestJwtKey);

        builder.UseSetting(
            "ConnectionStrings:DefaultConnection",
            $"Data Source={_databasePath}");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);
        });
    }

    /// <summary>
    /// Seeds a user directly into this factory's isolated database so
    /// tests can control exactly which identity a token represents,
    /// independent of DbSeeder's fixed default user.
    /// </summary>
    public async Task<User> SeedUserAsync(
        string email,
        string password = "TestPassword123!")
    {
        using var scope = Services.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var user = new User
        {
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password)
        };

        db.Users.Add(user);

        await db.SaveChangesAsync();

        return user;
    }

    /// <summary>
    /// Mints an access token for the given user using the application's
    /// real <see cref="JwtTokenService"/>, so tests exercise the same
    /// token-issuing code path production traffic uses.
    /// </summary>
    public string CreateAccessToken(User user)
    {
        using var scope = Services.CreateScope();

        var jwtTokenService = scope.ServiceProvider
            .GetRequiredService<JwtTokenService>();

        return jwtTokenService.CreateAccessToken(user);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
