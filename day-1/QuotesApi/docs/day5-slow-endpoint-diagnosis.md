# Day 5 - Diagnose a slow endpoint using traces

This documents the Day 5 exercise for QuotesApi: introducing a temporary,
artificial slowdown into a real endpoint, diagnosing it from real local
timing and Serilog trace correlation, then removing it.

No Jaeger/Aspire/OTLP graphical trace viewer was available in this
environment, so this diagnosis is based on real observed request timing and
real Serilog `TraceId` correlation - not on a graphical trace screenshot.
The intentional slowdown described below was never committed to Git
history; it existed only as a temporary local working-tree change while the
exercise was performed, and the endpoint has since been restored to its
original behavior.

## Selected endpoint

**`GET /api/quotes`** (`day-1/QuotesApi/Endpoints/QuoteEndpoints.cs`) - the
primary paged listing endpoint. It was chosen because it is a read-only
path that calls into `IQuoteRepository.GetPagedAsync` and could be
re-requested repeatedly, safely, without mutating state.

## Temporary change (working tree only, never committed)

A single `Thread.Sleep(1500)` was inserted into the `GET /api/quotes`
handler, immediately after the `await repository.GetPagedAsync(...)` call
and before the response was constructed. This placed the artificial delay
squarely in application code, after the repository/SQL work had already
completed and before any outbound HTTP dependency was involved.

## Before-fix observation

With the delay in place, repeated real local requests to `GET /api/quotes`
consistently took approximately **1.5 seconds** of additional latency
compared to baseline. Serilog's console output recorded the real
`Activity` `TraceId` for each of these requests, correlating each slow
request in the logs.

## Diagnosis

The GET /api/quotes operation was intentionally slowed with
Thread.Sleep(1500) after the repository query completed. Repeated real
requests showed approximately 1.5 seconds of additional latency, and
Serilog recorded the Activity TraceId for correlation. Because the delay
was placed after GetPagedAsync() and before the response was returned, the
added latency was application-level blocking work rather than SQL or
outbound HTTP work. The artificial delay was removed without changing the
endpoint response contract, repository behavior, authentication, or
authorization. After the fix, repeated steady-state requests completed in
single-digit milliseconds, confirming that the artificial latency was
removed. No graphical Jaeger/Aspire trace viewer was available locally.

## After-fix observation

Once `Thread.Sleep(1500)` was removed, repeated steady-state requests to
`GET /api/quotes` completed in **single-digit milliseconds**. The first
request after the fix was marginally slower than the rest, consistent with
ordinary startup/JIT/database-initialization warm-up rather than any
residual artificial delay.

## Production behavior after this exercise

- `GET /api/quotes` response contract: unchanged.
- Repository behavior (`GetPagedAsync`): unchanged.
- Authentication: unchanged.
- Authorization (`RequireAuthorization` on the mutating endpoints):
  unchanged.
- Serilog configuration and `TraceId` correlation: unchanged, intact.
- OpenTelemetry tracing/metrics pipeline: unchanged, intact.
- Application Insights / Azure Monitor wiring (optional, connection-string
  gated): unchanged, intact.
- No permanent delay, timer, or blocking call was added anywhere.

## App Insights KQL - slow requests in the last hour

```kql
requests
| where timestamp > ago(1h)
| where duration > 500
| order by duration desc
| take 10
| project timestamp, name, url, resultCode, success, duration, operation_Id
```

`duration` in Application Insights `requests` is expressed in
milliseconds, so `duration > 500` filters to requests slower than 500ms.
This query has not been run against a live Application Insights resource;
no connection string is configured in this environment (see
[azure-monitor.md](./azure-monitor.md)).
