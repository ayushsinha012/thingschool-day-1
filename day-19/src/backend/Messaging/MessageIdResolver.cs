namespace QuotesApi.Messaging;

public static class MessageIdResolver
{
    public static string Resolve(string? idempotencyKey) =>
        string.IsNullOrWhiteSpace(idempotencyKey)
            ? Guid.NewGuid().ToString("N")
            : idempotencyKey.Trim();
}
