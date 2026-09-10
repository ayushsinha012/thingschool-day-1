using Microsoft.Extensions.Options;

namespace QuotesApi.Outbox;

public sealed class OutboxRelayWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxRelayOptions> options,
    IOutboxRelayStatus status,
    ILogger<OutboxRelayWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Outbox relay worker started");

        using var timer = new PeriodicTimer(options.Value.PollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken);

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        logger.LogInformation("Outbox relay worker stopping");
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<OutboxRelayProcessor>();

            var published = await processor.ProcessBatchAsync(options.Value.BatchSize, stoppingToken);
            status.RecordRun(DateTimeOffset.UtcNow, published, null);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Outbox relay batch failed");
            status.RecordRun(DateTimeOffset.UtcNow, 0, ex.Message);
        }
    }
}
