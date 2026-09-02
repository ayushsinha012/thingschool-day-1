namespace QuotesApi.Outbox;

public sealed class OutboxRelayStatus : IOutboxRelayStatus
{
    private readonly object _lock = new();
    private OutboxRelaySnapshot _snapshot = new(null, 0, null);

    public void RecordRun(DateTimeOffset atUtc, int publishedCount, string? error)
    {
        lock (_lock)
        {
            _snapshot = new OutboxRelaySnapshot(atUtc, publishedCount, error);
        }
    }

    public OutboxRelaySnapshot GetSnapshot()
    {
        lock (_lock)
        {
            return _snapshot;
        }
    }
}
