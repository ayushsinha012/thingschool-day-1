using Testcontainers.MsSql;

namespace Tests.Integration.Infrastructure;

/// <summary>
/// Owns a single Testcontainers-managed SQL Server instance: builds and
/// starts a real "mssql/server" Docker container, exposes the connection
/// string EF Core can use to talk to it, and stops/removes the container
/// on disposal.
///
/// This class has no test-specific logic (no xunit fixture interfaces, no
/// WebApplicationFactory wiring) so it can be composed into whatever needs
/// a real SQL Server instance later.
/// </summary>
public sealed class SqlServerContainerFixture : IAsyncDisposable
{
    private readonly MsSqlContainer _container;

    public SqlServerContainerFixture()
    {
        _container = new MsSqlBuilder(
                "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
            .Build();
    }

    /// <summary>
    /// The connection string for the running container. Only valid after
    /// <see cref="StartAsync"/> has completed; the container assigns its
    /// host port dynamically, so this is never a fixed/hardcoded value.
    /// </summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <summary>
    /// Starts the SQL Server container. Must complete before
    /// <see cref="ConnectionString"/> is read.
    /// </summary>
    public Task StartAsync() => _container.StartAsync();

    /// <summary>
    /// Stops and removes the container.
    /// </summary>
    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
