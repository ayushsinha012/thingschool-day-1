namespace ResilienceDemo.Client;

public enum OutboundOutcome
{
    Success,
    CircuitOpen,
    Timeout,
    BulkheadRejected,
    Failed,
}

public sealed record OutboundCallResult(OutboundOutcome Outcome, int? StatusCode, string Detail);
