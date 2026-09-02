namespace QuotesApi.Outbox;

public sealed record OutboxRelaySnapshot(
    DateTimeOffset? LastRunAtUtc,
    int LastPublishedCount,
    string? LastError);

public interface IOutboxRelayStatus
{
    void RecordRun(DateTimeOffset atUtc, int publishedCount, string? error);

    OutboxRelaySnapshot GetSnapshot();
}
