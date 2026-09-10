namespace QuotesApi.Messaging;

public sealed class ServiceBusOptions
{
    public const string SectionName = "ServiceBus";

    public string FullyQualifiedNamespace { get; set; } = string.Empty;

    public string TopicName { get; set; } = "quote-events";

    public string SubscriptionA { get; set; } = "sub-audit";

    public string SubscriptionB { get; set; } = "sub-notifications";

    public int MaxConcurrentCallsPerSubscription { get; set; } = 2;
}
