namespace QuotesApi.Messaging;

public interface IMessagingActivityLog
{
    void Record(ConsumerActivityEntry entry);

    IReadOnlyList<ConsumerActivityEntry> GetRecent(int count);
}
