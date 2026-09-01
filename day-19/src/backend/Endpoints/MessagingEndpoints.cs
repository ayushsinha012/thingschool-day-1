using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using QuotesApi.DTOs;
using QuotesApi.Messaging;

namespace QuotesApi.Endpoints;

public sealed class MessagingEndpointsLogCategory;

public static class MessagingEndpoints
{
    private const int MaxPayloadLength = 2000;
    private const int MaxEventTypeLength = 100;
    private const int MaxDeadLetterPeek = 20;

    public static void MapMessagingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/messaging");

        group.MapPost(
            "/publish",
            async (
                PublishEventRequest request,
                IQuoteEventPublisher publisher,
                ILogger<MessagingEndpointsLogCategory> logger,
                CancellationToken cancellationToken) =>
            {
                var eventType = string.IsNullOrWhiteSpace(request.EventType)
                    ? "quote.demo"
                    : request.EventType.Trim();

                var payload = request.Payload ?? string.Empty;

                if (eventType.Length > MaxEventTypeLength)
                {
                    return Results.BadRequest(new ProblemDetails
                    {
                        Title = "Invalid publish request",
                        Detail = $"EventType must be {MaxEventTypeLength} characters or fewer."
                    });
                }

                if (payload.Length > MaxPayloadLength)
                {
                    return Results.BadRequest(new ProblemDetails
                    {
                        Title = "Invalid publish request",
                        Detail = $"Payload must be {MaxPayloadLength} characters or fewer."
                    });
                }

                var published = await publisher.PublishAsync(
                    eventType,
                    payload,
                    request.IdempotencyKey,
                    request.Poison,
                    cancellationToken);

                logger.LogInformation(
                    "Published {MessageId} ({EventType}, poison={Poison}) to {Topic}",
                    published.MessageId,
                    published.EventType,
                    published.Poison,
                    published.TopicName);

                return Results.Ok(published);
            });

        group.MapGet(
            "/topology",
            async (
                ServiceBusAdministrationClient adminClient,
                IOptions<ServiceBusOptions> options,
                CancellationToken cancellationToken) =>
                Results.Ok(await GetTopologyAsync(adminClient, options.Value, cancellationToken)));

        group.MapGet(
            "/activity",
            (IMessagingActivityLog activityLog, [FromQuery] int? take) =>
                Results.Ok(activityLog.GetRecent(take.GetValueOrDefault(50))));

        group.MapGet(
            "/dead-letters/{subscription}",
            async (
                string subscription,
                ServiceBusClient client,
                IOptions<ServiceBusOptions> options,
                CancellationToken cancellationToken) =>
            {
                var known = new[] { options.Value.SubscriptionA, options.Value.SubscriptionB };

                if (!known.Contains(subscription))
                {
                    return Results.NotFound(new ProblemDetails
                    {
                        Title = "Unknown subscription",
                        Detail = $"No subscription named '{subscription}' exists for this topic."
                    });
                }

                await using var receiver = client.CreateReceiver(
                    options.Value.TopicName,
                    subscription,
                    new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });

                var messages = await receiver.PeekMessagesAsync(MaxDeadLetterPeek, cancellationToken: cancellationToken);

                var summaries = messages.Select(message => new DeadLetterMessageSummary(
                    message.MessageId,
                    message.ApplicationProperties.TryGetValue("EventType", out var eventType)
                        ? eventType?.ToString() ?? "unknown"
                        : "unknown",
                    subscription,
                    message.DeliveryCount,
                    message.DeadLetterReason,
                    message.DeadLetterErrorDescription,
                    message.EnqueuedTime,
                    message.Body.ToString()));

                return Results.Ok(summaries);
            });
    }

    private static async Task<IReadOnlyList<SubscriptionTopology>> GetTopologyAsync(
        ServiceBusAdministrationClient adminClient,
        ServiceBusOptions options,
        CancellationToken cancellationToken)
    {
        var subscriptions = new[] { options.SubscriptionA, options.SubscriptionB };
        var results = new List<SubscriptionTopology>(subscriptions.Length);

        foreach (var subscription in subscriptions)
        {
            var runtimeProperties = await adminClient.GetSubscriptionRuntimePropertiesAsync(
                options.TopicName,
                subscription,
                cancellationToken);

            results.Add(new SubscriptionTopology(
                subscription,
                runtimeProperties.Value.ActiveMessageCount,
                runtimeProperties.Value.DeadLetterMessageCount,
                runtimeProperties.Value.TotalMessageCount));
        }

        return results;
    }
}
