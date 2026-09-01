using System.Collections.Concurrent;

namespace QuotesApi.Messaging;

public sealed class MessagingActivityLog : IMessagingActivityLog
{
    private const int Capacity = 200;

    private readonly ConcurrentQueue<ConsumerActivityEntry> _entries = new();

    public void Record(ConsumerActivityEntry entry)
    {
        _entries.Enqueue(entry);

        while (_entries.Count > Capacity && _entries.TryDequeue(out _))
        {
        }
    }

    public IReadOnlyList<ConsumerActivityEntry> GetRecent(int count) =>
        _entries.Reverse().Take(count).ToList();
}
