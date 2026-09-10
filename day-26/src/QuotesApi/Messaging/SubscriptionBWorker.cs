using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace QuotesApi.Messaging;

public sealed class SubscriptionBWorker(
    ServiceBusClient client,
    IOptions<ServiceBusOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<SubscriptionBWorker> logger)
    : SubscriptionWorker(client, options, scopeFactory, logger, options.Value.SubscriptionB, "B")
{
}
