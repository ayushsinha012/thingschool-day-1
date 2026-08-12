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
/// disposable SQL Server database running in a Testcontainers container,
/// so integration tests never touch a developer's local database.
///
/// The application's own startup code (see Program.cs) already calls
/// AppDbContext.Database.Migrate() and DbSeeder.SeedAsync() before
/// app.Run(). WebApplicationFactory only intercepts Run(), so that
/// migration/seeding still executes normally against the container-backed
/// database every time a factory instance is built. Program.cs itself is
/// untouched - it still registers AppDbContext with the SQLite provider
/// for production use; this factory replaces that registration with the
/// SQL Server provider pointed at the running container, in
/// ConfigureWebHost below.
///
/// Create a new instance per test (do not share via IClassFixture across
/// multiple test methods) so every test gets its own container, its own
/// clock and its own HttpClient.
/// </summary>
public class QuotesApiFactory : WebApplicationFactory<Program>
{
    private const string TestJwtKey =
        "Tests-Integration-Test-Signing-Key-0123456789-ABCDEFGH";

    private readonly SqlServerContainerFixture _sqlServerContainer;

    public QuotesApiFactory()
    {
        _sqlServerContainer = new SqlServerContainerFixture();

        // The container must be up, with its connection string known,
        // before ConfigureWebHost runs (that happens the first time
        // Services/CreateClient() is accessed, not in this constructor).
        // WebApplicationFactory offers no async construction hook, so we
        // block here - this is the one place guaranteed to run before the
        // host is built.
        _sqlServerContainer.StartAsync().GetAwaiter().GetResult();
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

        var connectionString = _sqlServerContainer.ConnectionString;

        builder.UseSetting(
            "ConnectionStrings:DefaultConnection",
            connectionString);

        builder.ConfigureTestServices(services =>
        {
            // Program.cs registers AppDbContext against the SQLite
            // provider for production use. Replace that registration
            // here so tests run against the real SQL Server container
            // instead - Program.cs itself is not modified.
            services.RemoveAll<DbContextOptions<AppDbContext>>();

            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlServer(connectionString));

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

        if (disposing)
        {
            _sqlServerContainer.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
