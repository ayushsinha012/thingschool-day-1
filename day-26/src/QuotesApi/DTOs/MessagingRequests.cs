namespace QuotesApi.DTOs;

public sealed class PublishEventRequest
{
    public string? EventType { get; set; }

    public string? Payload { get; set; }

    public string? IdempotencyKey { get; set; }

    public bool Poison { get; set; }
}
