using System.Text.Json.Serialization;

namespace QuotesApi.Messaging;

[JsonConverter(typeof(JsonStringEnumConverter<ActivityOutcome>))]
public enum ActivityOutcome
{
    Received,
    Processed,
    Duplicate,
    PoisonFailed
}
