using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace QuotesApi.Messaging;

public sealed class SubscriptionAWorker(
    ServiceBusClient client,
    IOptions<ServiceBusOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<SubscriptionAWorker> logger)
    : SubscriptionWorker(client, options, scopeFactory, logger, options.Value.SubscriptionA, "A")
{
}
