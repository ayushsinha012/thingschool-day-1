using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderRefactor.Data;

namespace OrderRefactor.Tests.Integration;

/// <summary>
/// Boots the real OrderRefactor application (real middleware pipeline, real
/// routing, real controllers) against a fresh SQLite connection kept open
/// in memory for the lifetime of the factory, so integration tests never
/// touch the real "orders.db" file Program.cs points at.
///
/// Program.cs itself is untouched - it still registers AppDbContext with
/// the "Data Source=orders.db" SQLite provider for production use; this
/// factory replaces that registration with an in-memory SQLite connection
/// in ConfigureWebHost below.
///
/// Create a new instance per test (do not share via IClassFixture across
/// multiple test methods) so every test gets its own isolated database.
/// </summary>
public class OrderApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;

    public OrderApiFactory()
    {
        // "DataSource=:memory:" only persists for as long as a connection
        // to it stays open, so we open and hold this connection for the
        // lifetime of the factory rather than letting EF Core open/close
        // its own short-lived connections against it.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Program.cs registers AppDbContext against
            // "Data Source=orders.db" for production use. Replace that
            // registration here so tests run against the in-memory SQLite
            // connection instead - Program.cs itself is not modified.
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();

            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlite(_connection));
        });
    }

    /// <summary>
    /// Creates the schema on the in-memory database. Must be awaited
    /// before any request is sent, since Program.cs does not run
    /// migrations/EnsureCreated on startup.
    /// </summary>
    public async Task InitializeDatabaseAsync()
    {
        using var scope = Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.EnsureCreatedAsync();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
