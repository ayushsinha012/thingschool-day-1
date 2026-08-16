using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using QuotesApi.Data;

namespace QuotesApi.Migrations.SqlServer;

/// <summary>
/// Lets "dotnet ef migrations add" scaffold a SQL-Server-flavored
/// migrations history for <see cref="AppDbContext"/>, separate from the
/// SQLite migrations under QuotesApi/Migrations that the app actually
/// ships with. EF Core migrations bake in provider-specific column types
/// at generation time, so the same migration files cannot be replayed
/// against a different provider (see Tests.Integration's
/// QuotesApiFactory, which points the SQL Server Testcontainers-backed
/// DbContext at this assembly instead). The connection string below is
/// only used to determine SQL Server as the target provider when
/// scaffolding migrations - it is never actually connected to.
/// </summary>
public class SqlServerDesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();

        optionsBuilder.UseSqlServer(
            "Server=localhost;Database=QuotesApiDesignTimeOnly;Trusted_Connection=True;TrustServerCertificate=True;",
            sql => sql.MigrationsAssembly(
                typeof(SqlServerDesignTimeDbContextFactory).Assembly.GetName().Name));

        return new AppDbContext(optionsBuilder.Options);
    }
}
