using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace QuotesApi.Messaging;

public abstract class SubscriptionWorker(
    ServiceBusClient client,
    IOptions<ServiceBusOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger logger,
    string subscriptionName,
    string workerSlotPrefix) : BackgroundService
{
    private readonly ServiceBusOptions _options = options.Value;

    private readonly ConcurrentQueue<string> _slots = new(
        Enumerable.Range(1, Math.Max(1, options.Value.MaxConcurrentCallsPerSubscription))
            .Select(slot => $"{workerSlotPrefix}{slot}"));

    private ServiceBusProcessor? _processor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor = client.CreateProcessor(
            _options.TopicName,
            subscriptionName,
            new ServiceBusProcessorOptions
            {
                AutoCompleteMessages = false,
                MaxConcurrentCalls = Math.Max(1, _options.MaxConcurrentCallsPerSubscription)
            });

        _processor.ProcessMessageAsync += HandleMessageAsync;
        _processor.ProcessErrorAsync += HandleErrorAsync;

        await _processor.StartProcessingAsync(stoppingToken);

        logger.LogInformation("Subscription worker started for {Subscription}", subscriptionName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(cancellationToken);

            _processor.ProcessMessageAsync -= HandleMessageAsync;
            _processor.ProcessErrorAsync -= HandleErrorAsync;

            await _processor.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);

        logger.LogInformation("Subscription worker stopped for {Subscription}", subscriptionName);
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        var message = args.Message;
        var slot = _slots.TryDequeue(out var acquired) ? acquired : $"{workerSlotPrefix}overflow";

        try
        {
            var poison = message.ApplicationProperties.TryGetValue("Poison", out var poisonValue)
                && poisonValue is bool isPoison
                && isPoison;

            var eventType = message.ApplicationProperties.TryGetValue("EventType", out var eventTypeValue)
                ? eventTypeValue?.ToString() ?? "unknown"
                : "unknown";

            var command = new ProcessQuoteEventCommand(
                subscriptionName,
                message.MessageId,
                eventType,
                message.Body.ToString(),
                message.DeliveryCount,
                poison);

            await using var scope = scopeFactory.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<IQuoteEventProcessor>();

            await processor.ProcessAsync(command, slot, args.CancellationToken);

            await args.CompleteMessageAsync(message, args.CancellationToken);
        }
        catch (PoisonMessageException ex)
        {
            logger.LogWarning(
                "Poison message {MessageId} on {Subscription} failed on delivery {DeliveryCount}: {Reason}",
                message.MessageId,
                subscriptionName,
                message.DeliveryCount,
                ex.Message);

            await args.AbandonMessageAsync(message, cancellationToken: args.CancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Unhandled failure processing {MessageId} on {Subscription}",
                message.MessageId,
                subscriptionName);

            await args.AbandonMessageAsync(message, cancellationToken: args.CancellationToken);
        }
        finally
        {
            _slots.Enqueue(slot);
        }
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        logger.LogError(
            args.Exception,
            "Service Bus processor error on {Subscription} ({ErrorSource})",
            subscriptionName,
            args.ErrorSource);

        return Task.CompletedTask;
    }
}
