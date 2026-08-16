using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using Tests.Integration.Infrastructure;

namespace Tests.Integration;

/// <summary>
/// Verifies that QuotesApiFactory boots the real application startup path
/// (Program.cs calls AppDbContext.Database.Migrate() before app.Run()) and
/// that every EF Core migration defined in the project actually gets
/// applied to the isolated SQLite database, rather than the schema being
/// created some other way that would hide a broken migration.
/// </summary>
public class MigrationTests : IDisposable
{
    private readonly QuotesApiFactory _factory;

    public MigrationTests()
    {
        _factory = new QuotesApiFactory();
    }

    [Fact]
    public async Task Factory_applies_all_ef_core_migrations_on_startup()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Act
        var applied = await db.Database.GetAppliedMigrationsAsync();
        var all = db.Database.GetMigrations();

        // Assert
        all.Should().NotBeEmpty();
        applied.Should().BeEquivalentTo(all);

        var pending = await db.Database.GetPendingMigrationsAsync();

        pending.Should().BeEmpty();
    }

    public void Dispose()
    {
        _factory.Dispose();
    }
}
