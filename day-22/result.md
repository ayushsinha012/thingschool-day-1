# Day 22 — Resilience with Polly — Result

## Task

> Wrap an outbound dependency with Polly: retry-with-backoff (idempotent
> only), a circuit breaker, a timeout, and a bulkhead. Then prove the
> circuit opens under sustained failure and recovers.

## Exercise

> Paste the resilience pipeline. Show logs/metrics of the breaker opening
> then half-opening to recovery.

## Repository inspection (before writing any code)

| Item | Finding | Classification |
|---|---|---|
| Existing backend that could host this | `day-1/QuotesApi/` (net10.0, ASP.NET Core, Serilog, MediatR) is the canonical backend every prior day extends. | Usable, but out of scope — see below |
| Existing outbound HTTP/dependency patterns | None. `day-1/QuotesApi` calls its own database and Azure Service Bus; there is no `HttpClient`-based outbound dependency and no `IHttpClientFactory` registration anywhere in the repo. | MISSING |
| Existing Polly usage | `grep -ri polly` across the whole repo (excluding `bin`/`obj`) returned nothing. No `Polly*` package reference anywhere. | MISSING |
| Existing testing/load-test setup | xUnit + FluentAssertions (`Tests.Domain`), `ab` for load tests (Day 20/21 use it against the real backend). | Reusable pattern, followed here |
| Existing logging/metrics/observability | Serilog (console sink, config-driven via `appsettings.json`), `Azure.Monitor.OpenTelemetry.AspNetCore`. No Polly telemetry package in use. | Reusable pattern, followed here |
| Prior Day-22 work | No `day-22/`, `Day-22/`, or any Polly-related file existed before this pass. | MISSING (built from scratch) |

**Why this isn't wired into `day-1/QuotesApi`**: the Day-22 rules are explicit —
*"Do NOT modify, refactor, or reorganize Day 1-21"* and *"Keep all
Day-22-specific code/evidence/docs under `day-22/`"*. Every prior day (17-21)
extended the same canonical backend project, but that convention would
require editing `day-1/QuotesApi/Program.cs` and adding files inside a
Day 1-21 tree. Given the explicit rule takes priority, Day 22 is a
self-contained ASP.NET Core project under `day-22/src/ResilienceDemo/` that
reuses the same stack and conventions (net10.0, Serilog, minimal APIs,
options pattern, xUnit + FluentAssertions) without touching a single file
outside `day-22/`.

## What was built

```
day-22/
  src/ResilienceDemo/            outbound client + Polly pipeline + local downstream
  tests/ResilienceDemo.Tests/    xunit tests for all 6 required behaviors
  load-test/run-demo.sh          one-command reproduction of scenarios A-E
  load-test/evidence/            real captured output from the last run
```

### Downstream failure/recovery simulator

`day-22/src/ResilienceDemo/Downstream/` — a real HTTP endpoint (not a unit
test mock) hosted in the same process, controllable at runtime:

- `GET /downstream/quote/{id}` — idempotent read.
- `POST /downstream/events` — a non-idempotent write (recording an event;
  retrying it would double-record, so it must never be retried).
- `POST /downstream/control` — `{ "mode": "Healthy" | "Failing" | "Slow", "delayMs": <int> }`.
  `Healthy` returns 200 immediately, `Failing` returns 503 immediately,
  `Slow` delays `delayMs` before returning 200.
- `GET /downstream/control` — current mode/delay/request count.

The demo endpoints (`day-22/src/ResilienceDemo/Demo/DemoEndpoints.cs`) call
this downstream over a real `HttpClient` (`GET /demo/get/{id}`,
`POST /demo/event`, `POST /demo/concurrent?count=N`), so every scenario
below is the actual resilience pipeline executing real HTTP calls, not a
mocked-out unit test.

### The resilience pipeline

`day-22/src/ResilienceDemo/Resilience/OutboundResiliencePipelineFactory.cs`
— one shared `ResiliencePipeline<HttpResponseMessage>` per downstream
dependency, built with Polly.Core v8 + Polly.RateLimiting, in the order
Microsoft's own `Microsoft.Extensions.Http.Resilience` "standard" handler
uses (rate limiter → retry → circuit breaker → timeout, outermost to
innermost):

```csharp
using Polly;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Retry;
using Polly.Timeout;

namespace ResilienceDemo.Resilience;

public static class ResilienceContextKeys
{
    public static readonly ResiliencePropertyKey<bool> IsIdempotent = new("IsIdempotent");
}

public sealed class OutboundResilienceOptions
{
    public int RetryMaxAttempts { get; init; } = 3;
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromMilliseconds(200);

    public double CircuitBreakerFailureRatio { get; init; } = 0.5;
    public int CircuitBreakerMinimumThroughput { get; init; } = 4;
    public TimeSpan CircuitBreakerSamplingDuration { get; init; } = TimeSpan.FromSeconds(4);
    public TimeSpan CircuitBreakerBreakDuration { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromMilliseconds(800);

    public int BulkheadPermitLimit { get; init; } = 3;
    public int BulkheadQueueLimit { get; init; } = 2;
}

public static class OutboundResiliencePipelineFactory
{
    public static ResiliencePipeline<HttpResponseMessage> Create(
        OutboundResilienceOptions options,
        ResilienceMetrics metrics,
        ILogger logger)
    {
        var builder = new ResiliencePipelineBuilder<HttpResponseMessage>();

        var bulkheadLimiter = new System.Threading.RateLimiting.ConcurrencyLimiter(new System.Threading.RateLimiting.ConcurrencyLimiterOptions
        {
            PermitLimit = options.BulkheadPermitLimit,
            QueueLimit = options.BulkheadQueueLimit,
            QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst,
        });

        builder.AddRateLimiter(new RateLimiterStrategyOptions
        {
            RateLimiter = args => bulkheadLimiter.AcquireAsync(1, args.Context.CancellationToken),
            OnRejected = args =>
            {
                metrics.RecordBulkheadRejection();
                logger.LogWarning(
                    "bulkhead rejected permitLimit={PermitLimit} queueLimit={QueueLimit}",
                    options.BulkheadPermitLimit,
                    options.BulkheadQueueLimit);
                return ValueTask.CompletedTask;
            },
        });

        if (options.RetryMaxAttempts > 0)
        {
            builder.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = options.RetryMaxAttempts,
                BackoffType = DelayBackoffType.Exponential,
                Delay = options.RetryBaseDelay,
                UseJitter = true,
                ShouldHandle = args =>
                {
                    if (!args.Context.Properties.TryGetValue(ResilienceContextKeys.IsIdempotent, out var idempotent) || !idempotent)
                    {
                        return ValueTask.FromResult(false);
                    }

                    if (args.Outcome.Exception is BrokenCircuitException)
                    {
                        return ValueTask.FromResult(false);
                    }

                    if (args.Outcome.Exception is TimeoutRejectedException || args.Outcome.Exception is HttpRequestException)
                    {
                        return ValueTask.FromResult(true);
                    }

                    var isTransientStatus = args.Outcome.Result is { } response &&
                        ((int)response.StatusCode >= 500 || response.StatusCode == System.Net.HttpStatusCode.RequestTimeout);
                    return ValueTask.FromResult(isTransientStatus);
                },
                OnRetry = args =>
                {
                    metrics.RecordRetryAttempt();
                    logger.LogWarning(
                        "retry attempt={AttemptNumber} delay={DelayMs}ms reason={Reason}",
                        args.AttemptNumber + 1,
                        args.RetryDelay.TotalMilliseconds,
                        DescribeOutcome(args.Outcome));
                    return ValueTask.CompletedTask;
                },
            });
        }

        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
        {
            FailureRatio = options.CircuitBreakerFailureRatio,
            MinimumThroughput = options.CircuitBreakerMinimumThroughput,
            SamplingDuration = options.CircuitBreakerSamplingDuration,
            BreakDuration = options.CircuitBreakerBreakDuration,
            ShouldHandle = args =>
            {
                if (args.Outcome.Exception is TimeoutRejectedException || args.Outcome.Exception is HttpRequestException)
                {
                    return ValueTask.FromResult(true);
                }

                var isTransientStatus = args.Outcome.Result is { } response && (int)response.StatusCode >= 500;
                return ValueTask.FromResult(isTransientStatus);
            },
            OnOpened = args =>
            {
                metrics.RecordCircuitTransition("Closed", "Open", DescribeOutcome(args.Outcome));
                logger.LogError(
                    "circuit state=Open breakDuration={BreakDurationMs}ms lastReason={Reason}",
                    args.BreakDuration.TotalMilliseconds,
                    DescribeOutcome(args.Outcome));
                return ValueTask.CompletedTask;
            },
            OnHalfOpened = args =>
            {
                metrics.RecordCircuitTransition("Open", "HalfOpen", "break duration elapsed, probing");
                logger.LogWarning("circuit state=HalfOpen reason=probing downstream after break duration");
                return ValueTask.CompletedTask;
            },
            OnClosed = args =>
            {
                metrics.RecordCircuitTransition("HalfOpen", "Closed", "probe succeeded");
                logger.LogInformation("circuit state=Closed reason=probe succeeded, downstream recovered");
                return ValueTask.CompletedTask;
            },
        });

        builder.AddTimeout(new TimeoutStrategyOptions
        {
            Timeout = options.AttemptTimeout,
            OnTimeout = args =>
            {
                metrics.RecordTimeout();
                logger.LogWarning("timeout after={TimeoutMs}ms", args.Timeout.TotalMilliseconds);
                return ValueTask.CompletedTask;
            },
        });

        return builder.Build();
    }

    private static string DescribeOutcome(Outcome<HttpResponseMessage> outcome)
    {
        if (outcome.Exception is not null)
        {
            return outcome.Exception.GetType().Name;
        }

        return outcome.Result is { } response ? $"HTTP {(int)response.StatusCode}" : "unknown";
    }
}
```

One pipeline instance is shared by both operations on the dependency
(`GetQuoteAsync` for the idempotent read, `RecordEventAsync` for the
non-idempotent write) — `day-22/src/ResilienceDemo/Client/OutboundDependencyClient.cs`
tags each call's `ResilienceContext` with `IsIdempotent`, and the retry
strategy's `ShouldHandle` reads that flag before ever treating a failure as
retryable. The circuit breaker and bulkhead apply to **every** call
regardless of idempotency — they protect the dependency itself, not a
specific operation.

### Why retries are limited to idempotent operations

`POST /downstream/events` records an event exactly once per call by
design. If the pipeline retried it after a transient 503 or a timeout, and
the original request had actually been applied downstream before the
response was lost, a blind retry would double-record it — a real
correctness bug, not just wasted work. `GET /downstream/quote/{id}` has no
such risk: reading the same id twice is always safe. The `IsIdempotent`
context flag is the single place this distinction is enforced, so a new
call site can't accidentally get retry behavior it didn't opt into.

## Reproducing the demonstration

Requires the .NET 10 SDK on `PATH` (the same `net10.0` target every other
project in this repo already uses).

```bash
cd day-22/load-test
./run-demo.sh
```

The script builds the project, starts it on `http://localhost:5080`,
drives scenarios A-E against the real pipeline with `curl`, and writes:

- `evidence/app.log` — the app's own Serilog console output (retry
  attempts, circuit transitions, timeouts, bulkhead rejections).
- `evidence/demo-run.log` — every request the script made, with status
  code, elapsed time, and response body.
- `evidence/dotnet-test.log` — from `dotnet test` (run separately, see
  below).

Tests:

```bash
cd day-22/tests/ResilienceDemo.Tests
dotnet test
```

## Actual results

### Build

```
$ dotnet build day-22/src/ResilienceDemo
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### Tests — 6/6 passed

```
$ dotnet test day-22/tests/ResilienceDemo.Tests
Passed!  - Failed:     0, Passed:     6, Skipped:     0, Total:     6, Duration: 846 ms - ResilienceDemo.Tests.dll (net10.0)
```

Covering exactly the six required behaviors (`day-22/tests/ResilienceDemo.Tests/`):

- `RetryTests.IdempotentGet_RetriesTransientFailuresAndEventuallySucceeds` —
  a `GET` that fails twice then succeeds is retried and returns success
  (3 handler calls, 2 recorded retry attempts).
- `RetryTests.NonIdempotentPost_DoesNotRetryOnTransientFailure` — a `POST`
  that fails is called exactly once; 0 retry attempts recorded.
- `CircuitBreakerTests.SustainedFailures_OpenTheCircuitAndFailFast` — 4
  sustained failures open the circuit; the next call short-circuits
  without reaching the handler.
- `CircuitBreakerTests.AfterBreakDuration_HalfOpenProbeSucceedsAndClosesCircuit` —
  after `BreakDuration` elapses and the downstream recovers, the next call
  is the half-open probe, succeeds, and the transition log shows
  `Open → HalfOpen → Closed`.
- `TimeoutTests.SlowDownstream_ExceedsAttemptTimeoutAndIsReportedAsTimeout` —
  a 500ms downstream delay against a 100ms attempt timeout is reported as
  a timeout, not a hang.
- `BulkheadTests.ExcessConcurrentCalls_AreRejectedInsteadOfUnboundedFanOut` —
  6 concurrent calls against a permit limit of 2 + queue limit of 1 (3
  admitted) results in exactly 3 successes and 3 rejections, deterministically.

### A. Healthy request

```
POST /demo/reset -> http=204
POST /downstream/control {"mode":"healthy"} -> http=200
GET /demo/get/1 -> http=200 elapsed=193ms body={"outcome":"Success","statusCode":200,"detail":"ok"}
GET /demo/status -> {"circuitState":"Closed","downstreamMode":"Healthy","counters":{"successes":1,"failures":0,"retryAttempts":0,"timeouts":0,"bulkheadRejections":0,"circuitShortCircuited":0}}
```

### B. Sustained failure — retries, then the circuit opens and calls fail fast

Downstream switched to `Failing` (immediate 503). Five sequential
idempotent `GET` calls:

```
GET /demo/get/1 -> http=503 elapsed=1097ms body={"outcome":"CircuitOpen", ...}
GET /demo/get/2 -> http=503 elapsed=12ms  body={"outcome":"CircuitOpen", ...}
GET /demo/get/3 -> http=503 elapsed=11ms  body={"outcome":"CircuitOpen", ...}
GET /demo/get/4 -> http=503 elapsed=12ms  body={"outcome":"CircuitOpen", ...}
GET /demo/get/5 -> http=503 elapsed=12ms  body={"outcome":"CircuitOpen", ...}
GET /demo/status -> {"circuitState":"Open", "counters":{"successes":0,"failures":5,"retryAttempts":3,"timeouts":0,"bulkheadRejections":0,"circuitShortCircuited":5}}
GET /demo/get/99 -> http=503 elapsed=11ms body={"outcome":"CircuitOpen", ...}   # extra probe: still fails in ~11ms, no downstream hit
```

App log for the same window (`evidence/app.log`) — retries with real
exponential backoff, then the breaker opening mid-retry:

```
[09:42:57.743 INF] Downstream: downstream request id=1 mode=Failing
[09:42:57.760 WRN] ResiliencePipeline: retry attempt=1 delay=233.5037ms reason=HTTP 503
[09:42:57.996 INF] Downstream: downstream request id=1 mode=Failing
[09:42:57.996 WRN] ResiliencePipeline: retry attempt=2 delay=179.8417ms reason=HTTP 503
[09:42:58.177 INF] Downstream: downstream request id=1 mode=Failing
[09:42:58.185 ERR] ResiliencePipeline: circuit state=Open breakDuration=5000ms lastReason=HTTP 503
[09:42:58.186 WRN] ResiliencePipeline: retry attempt=3 delay=635.0855ms reason=HTTP 503
[09:42:58.826 WRN] ResilienceDemo.Client.OutboundDependencyClient: call rejected reason=circuit-open
[09:42:58.853 WRN] ResilienceDemo.Client.OutboundDependencyClient: call rejected reason=circuit-open
[09:42:58.872 WRN] ResilienceDemo.Client.OutboundDependencyClient: call rejected reason=circuit-open
[09:42:58.892 WRN] ResilienceDemo.Client.OutboundDependencyClient: call rejected reason=circuit-open
[09:42:58.915 WRN] ResilienceDemo.Client.OutboundDependencyClient: call rejected reason=circuit-open
[09:42:58.959 WRN] ResilienceDemo.Client.OutboundDependencyClient: call rejected reason=circuit-open
```

Why the first call itself ends up `CircuitOpen`: the circuit breaker's
`SamplingDuration` window is 4s and its state is shared across every call
through the one pipeline instance — the single healthy call from Scenario
A (0.9s earlier) plus the first 3 failed attempts of call #1 already add
up to `MinimumThroughput=4` samples with a 75% failure ratio, so the
breaker opens **during** call #1's own retry loop. Its scheduled 4th
attempt (the `retry attempt=3` line) hits the now-open breaker immediately,
the retry strategy correctly declines to retry a `BrokenCircuitException`
(see `ShouldHandle` above), and the call surfaces as `CircuitOpen`. Calls
#2-#5 and the extra probe never reach the retry/backoff logic at all —
each one fails in ~11-12ms, i.e. genuinely fast, not the ~1s a real
downstream round trip plus retries would take. This is exactly the
circuit breaker's job: once the dependency is known bad, stop hammering it.

### C. Recovery — OPEN → HALF-OPEN → CLOSED

Downstream switched back to `Healthy`, then waited for `BreakDuration`
(5s) before probing:

```
GET /demo/get/1 -> http=200 elapsed=16ms body={"outcome":"Success", ...}
GET /demo/status -> {"circuitState":"Closed", "counters":{...,"circuitShortCircuited":6}}
GET /demo/metrics -> {
  "circuitState": "Closed",
  "transitions": [
    { "timestamp": "2026-09-04T04:12:58.1852921+00:00", "from": "Closed",   "to": "Open",     "reason": "HTTP 503" },
    { "timestamp": "2026-09-04T04:13:04.5391345+00:00", "from": "Open",     "to": "HalfOpen", "reason": "break duration elapsed, probing" },
    { "timestamp": "2026-09-04T04:13:04.5431663+00:00", "from": "HalfOpen", "to": "Closed",   "reason": "probe succeeded" }
  ]
}
```

App log:

```
[09:43:04.539 WRN] ResiliencePipeline: circuit state=HalfOpen reason=probing downstream after break duration
[09:43:04.541 INF] Downstream: downstream request id=1 mode=Healthy
[09:43:04.543 INF] ResiliencePipeline: circuit state=Closed reason=probe succeeded, downstream recovered
[09:43:04.543 INF] ResilienceDemo.Client.OutboundDependencyClient: call succeeded status=200
```

This is the full `OPEN → HALF-OPEN → CLOSED` sequence the exercise asks
for: opened at `04:12:58.185`, half-opened at `04:13:04.539` (5.004s later
— `BreakDuration` elapsing on schedule), closed 4ms after that once the
one probe request succeeded against the now-healthy downstream.

### D. Timeout

Downstream switched to `Slow` with a 1500ms delay — well above the 800ms
attempt timeout. Used the non-idempotent `POST /demo/event` so the timeout
shows in isolation, with no retry noise:

```
POST /demo/event -> http=504 elapsed=835ms body={"outcome":"Timeout","statusCode":null,"detail":"attempt timed out"}
GET /demo/status -> {"circuitState":"Closed","counters":{"successes":0,"failures":1,"retryAttempts":0,"timeouts":1,"bulkheadRejections":0,"circuitShortCircuited":0}}
```

App log:

```
[09:43:04.660 INF] Downstream: downstream event write mode=Slow
[09:43:05.472 WRN] ResiliencePipeline: timeout after=800ms
[09:43:05.481 WRN] ResilienceDemo.Client.OutboundDependencyClient: call rejected reason=timeout
```

The call returned in 835ms — bounded by the 800ms attempt timeout, not the
1500ms the downstream would have actually taken — and the failure is
recorded both as `metrics.Timeouts=1` and as a circuit-breaker-visible
failure outcome (`ShouldHandle` on the breaker treats `TimeoutRejectedException`
as a failure too), so a downstream that's merely slow — not erroring —
still counts toward tripping the breaker.

### E. Bulkhead

Downstream switched to `Slow` with a 500ms delay (below the 800ms attempt
timeout, so nothing here times out — this isolates the bulkhead). Fired 10
concurrent idempotent `GET` calls (`POST /demo/concurrent?count=10`)
against `BulkheadPermitLimit=3`, `BulkheadQueueLimit=2` (5 calls can be
admitted, the rest must be rejected):

```
POST /demo/concurrent?count=10 -> http=200 elapsed=1039ms body={"requested":10,"summary":{"Success":5,"BulkheadRejected":5}}
GET /demo/status -> {"circuitState":"Closed","counters":{"successes":5,"failures":5,"retryAttempts":0,"timeouts":0,"bulkheadRejections":5,"circuitShortCircuited":0}}
```

App log (5 rejections logged immediately, before the 3 admitted + 2 queued
calls even finish):

```
[09:43:05.580 WRN] ResiliencePipeline: bulkhead rejected permitLimit=3 queueLimit=2
[09:43:05.584 WRN] ResilienceDemo.Client.OutboundDependencyClient: call rejected reason=bulkhead-full
[09:43:05.584 WRN] ResiliencePipeline: bulkhead rejected permitLimit=3 queueLimit=2
[09:43:05.585 WRN] ResilienceDemo.Client.OutboundDependencyClient: call rejected reason=bulkhead-full
[09:43:05.585 WRN] ResiliencePipeline: bulkhead rejected permitLimit=3 queueLimit=2
[09:43:05.586 WRN] ResilienceDemo.Client.OutboundDependencyClient: call rejected reason=bulkhead-full
[09:43:05.586 WRN] ResiliencePipeline: bulkhead rejected permitLimit=3 queueLimit=2
[09:43:05.588 WRN] ResilienceDemo.Client.OutboundDependencyClient: call rejected reason=bulkhead-full
[09:43:05.588 WRN] ResiliencePipeline: bulkhead rejected permitLimit=3 queueLimit=2
[09:43:05.589 WRN] ResilienceDemo.Client.OutboundDependencyClient: call rejected reason=bulkhead-full
[09:43:06.082 INF] ResilienceDemo.Client.OutboundDependencyClient: call succeeded status=200   (x3, the 3 permits)
[09:43:06.584 INF] ResilienceDemo.Client.OutboundDependencyClient: call succeeded status=200   (x2, the queued 2)
```

Exactly 5 of 10 concurrent requests were admitted (3 running immediately +
2 queued, released as permits freed up at ~500ms and ~1000ms) and exactly
5 were rejected outright — proving the bulkhead bounds concurrent fan-out
to the dependency instead of letting all 10 through.

Full raw logs for all five scenarios: `load-test/evidence/app.log` and
`load-test/evidence/demo-run.log`.

## Final check

- [x] Builds cleanly: `dotnet build day-22/src/ResilienceDemo` — 0 errors, 0 warnings.
- [x] Tests pass: `dotnet test day-22/tests/ResilienceDemo.Tests` — 6/6.
- [x] Retry-with-backoff present and idempotent-only — verified by
      `RetryTests` and by Scenario B (retries only happen on the `GET`
      path; `POST /demo/event` in Scenario D never retries).
- [x] Circuit breaker opens under sustained failure — Scenario B, `Closed → Open` at `04:12:58.185`.
- [x] Transitions to half-open during recovery — Scenario C, `Open → HalfOpen` at `04:13:04.539`.
- [x] Successful recovery closes the circuit — Scenario C, `HalfOpen → Closed` at `04:13:04.543`.
- [x] Timeout works — Scenario D, bounded at 835ms against a 1500ms-slow downstream.
- [x] Bulkhead works under concurrency — Scenario E, exactly 5/10 admitted, 5/10 rejected.
- [x] Logs/metrics contain real evidence for all of the above — `load-test/evidence/`.
- [x] Only Day-22 files were changed for this task — everything lives under `day-22/`; no file outside it was touched.

## Report

1. **Already complete, not rerun**: nothing — no Day-22 work, no Polly
   usage, and no outbound-dependency pattern existed anywhere in the repo
   before this pass (confirmed by inspection above).
2. **Implemented**: a self-contained `day-22/src/ResilienceDemo/` ASP.NET
   Core app with a shared Polly v8 pipeline (bulkhead via
   `Polly.RateLimiting` concurrency limiter → retry, idempotent-only →
   circuit breaker → per-attempt timeout), a real local downstream test
   double with `Healthy`/`Failing`/`Slow` modes, an `OutboundDependencyClient`
   wrapping it, in-memory metrics + Serilog logging for every strategy's
   state, 6 focused xUnit tests, and `load-test/run-demo.sh` driving all 5
   demonstration scenarios end to end.
3. **Exact commands**:
   `cd day-22/load-test && ./run-demo.sh` (build + run + demonstrate A-E),
   `cd day-22/tests/ResilienceDemo.Tests && dotnet test`.
4. **Actual results**: 6/6 tests passed; circuit opened under sustained
   failure (`Closed → Open`), recovered through a real half-open probe
   (`Open → HalfOpen → Closed`, 5.004s break duration honored); timeout
   bounded a 1500ms-slow call to 835ms; bulkhead admitted exactly 5 of 10
   concurrent calls and rejected the other 5 — all captured live in
   `load-test/evidence/`.
5. **UI**: intentionally not created. The task rules call this out
   explicitly ("prefer API/test/load-test output over creating a new UI"),
   and every required behavior (retries, circuit transitions, timeouts,
   bulkhead rejections, recovery) is fully demonstrable — and was
   demonstrated — via `curl`, structured JSON from `/demo/metrics`, and
   Serilog console output.
6. **Azure**: not needed and not attempted. This exercise's downstream
   dependency is a deliberately local, test-controlled double (the task
   explicitly asks for this, to keep the failure/recovery demonstration
   deterministic) — there is no new Azure-hosted service for it to call,
   and the existing Azure Container App (`day-1/QuotesApi`) was
   intentionally not modified per the Day-22 rules.
7. **Remaining issues**: none for the exercise as scoped. Two things worth
   flagging for anyone extending this: (a) this sandbox's system-wide
   `dotnet` is v6.0.400, which cannot build any `net10.0` project in this
   repo (not just Day 22) — a local .NET 10 SDK was used only to build,
   test, and run this demo, exactly matching every other project's
   `net10.0` target, and `run-demo.sh` assumes a `net10.0`-capable `dotnet`
   on `PATH` like the rest of the repo already does; (b) `POST /demo/reset`
   resets the demo's own counters and the downstream stub, but not the
   Polly circuit breaker's internal sampling window — that's intentional
   (a real circuit breaker's state can't be wiped mid-flight either), and
   it's the reason Scenario B's first call opens the circuit using one
   leftover sample from Scenario A, as explained above.
