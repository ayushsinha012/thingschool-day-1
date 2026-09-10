using System.Diagnostics;

namespace QuotesApi.Messaging;

public static class MessagingTelemetry
{
    public const string SourceName = "QuotesApi.Messaging";

    public static readonly ActivitySource ActivitySource = new(SourceName);

    public const string TraceParentProperty = "traceparent";

    public const string TraceStateProperty = "tracestate";
}
