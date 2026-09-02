using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QuotesApi.Data;
using QuotesApi.Messaging;
using QuotesApi.Outbox;
using Tests.Domain.TestDoubles;

namespace Tests.Domain.Outbox;

public class OutboxRelayWorkerTests
{
    private static IServiceScopeFactory BuildScopeFactory(string sqliteConnectionString, FakeQuoteEventPublisher publisher)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(sqliteConnectionString));
        services.AddSingleton<IQuoteEventPublisher>(publisher);
        services.AddScoped<OutboxRelayProcessor>();

        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static IOptions<OutboxRelayOptions> FastPolling() =>
        Options.Create(new OutboxRelayOptions { PollInterval = TimeSpan.FromMilliseconds(20), BatchSize = 10 });

    [Fact]
    public async Task Worker_StopAsync_CompletesPromptly_WhileWaitingOnTheNextPollTick()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"outbox-worker-{Guid.NewGuid():N}.db");

        try
        {
            var connectionString = $"Data Source={tempFile}";

            await using (var db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options))
            {
                await db.Database.EnsureCreatedAsync();
            }

            var scopeFactory = BuildScopeFactory(connectionString, new FakeQuoteEventPublisher());
            var status = new OutboxRelayStatus();
            var worker = new OutboxRelayWorker(scopeFactory, FastPolling(), status, NullLogger<OutboxRelayWorker>.Instance);

            await worker.StartAsync(CancellationToken.None);

            var stopTask = worker.StopAsync(CancellationToken.None);
            var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(5)));

            completed.Should().Be(stopTask, "StopAsync must not hang waiting for the next poll tick");
            await stopTask;
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Worker_WhenTheDatabaseIsUnreachable_RecordsTheFailure_AndKeepsPolling_WithoutCrashingTheHost()
    {
        var scopeFactory = BuildScopeFactory(
            "Data Source=/nonexistent-directory-day20/outbox.db",
            new FakeQuoteEventPublisher());

        var status = new OutboxRelayStatus();
        var worker = new OutboxRelayWorker(scopeFactory, FastPolling(), status, NullLogger<OutboxRelayWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);

        while (status.GetSnapshot().LastRunAtUtc is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        var stopTask = worker.StopAsync(CancellationToken.None);
        var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(5)));

        completed.Should().Be(stopTask, "one failing batch must not stop the worker from being stoppable");
        await stopTask;

        status.GetSnapshot().LastError.Should().NotBeNullOrEmpty();
    }
}
