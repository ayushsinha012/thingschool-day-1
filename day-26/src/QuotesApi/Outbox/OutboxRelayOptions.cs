namespace QuotesApi.Outbox;

public sealed class OutboxRelayOptions
{
    public const string SectionName = "OutboxRelay";

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    public int BatchSize { get; set; } = 20;
}
