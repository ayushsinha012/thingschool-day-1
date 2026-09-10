# Day 26 — App Insights + KQL

## Exercise

> Make production legible. Wire OpenTelemetry → App Insights, then write KQL for p50/p99 by endpoint, the dependency call breakdown, and an alert on error-rate. Confirm distributed tracing stitches API → worker → DB.
>
> Paste your KQL queries + a screenshot of a distributed trace spanning the API and the worker.

## What this is built on

QuotesApi (`day-1/QuotesApi`) already had a partial OpenTelemetry → Azure Monitor pipeline (`Extensions/InfrastructureExtensions.cs`, `AddObservability`): ASP.NET Core + HttpClient instrumentation, wired to `Azure.Monitor.OpenTelemetry.AspNetCore`, attached only when a connection string resolves. It had no EF Core/SQL instrumentation, no Service Bus trace propagation, and — as this exercise found — its connection-string resolution had never actually worked (see below). `day-1` was not modified; a copy lives in `day-26/src/QuotesApi` with the Day 26 changes.

The real Application Insights resource for this app already exists: **`appi-2i2oapij4zsrc`** in `thinkschool-rg` (created by `azd`, tag `azd-env-name=quotesapi-thinkschool`, workspace-based, linked to `log-2i2oapij4zsrc`). No new Application Insights resource was created. Day 26 telemetry is tagged with a distinct `cloud_RoleName` (`quotes-api-day26`) so it's isolated from any other telemetry in the same resource — every KQL query below filters on it.

Both the API and its Service Bus consumers (`SubscriptionAWorker`/`SubscriptionBWorker`) run **in the same process** — the "worker" is a `BackgroundService`, not a separate deployable. Rather than push a new revision to the production `quotes-api` Container App, the Day 26 instrumented copy was run as a local process configured against real, isolated Azure resources: two new Service Bus subscriptions (`sub-day26-audit`, `sub-day26-notifications`, additive, on the existing `quote-events` topic in the existing `sb-quotesapi-thinkschool` namespace) and one new Azure Monitor alert rule. Nothing pre-existing was modified — see `evidence/deployment.txt` and `evidence/bicep-what-if.txt` (a `what-if` run confirms only these 3 resources change; everything else in `thinkschool-rg` is `Ignore`).

## OpenTelemetry → Application Insights

`Extensions/InfrastructureExtensions.cs` (`AddObservability`):

```csharp
services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName))
    .WithTracing(tracing => tracing
        .AddSource(MessagingTelemetry.SourceName)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation());

if (!string.IsNullOrWhiteSpace(connectionString))
    openTelemetry.UseAzureMonitor(options => options.ConnectionString = connectionString);
```

`OpenTelemetry.Instrumentation.EntityFrameworkCore` was added (the app persists to SQLite; this instrumentation supports it, emitting real `sqlite`-typed dependency spans — the KQL/README describe it as SQLite throughout, never as Azure SQL). `serviceName` comes from `ApplicationInsights:CloudRoleName` = `quotes-api-day26`.

**A real bug was found and fixed here.** `appsettings.json` sets `ApplicationInsights:ConnectionString` to `""` (present, empty) rather than leaving it absent. The original `?? ` chain (`configuration[key] ?? configuration[envKey] ?? Environment.GetEnvironmentVariable(...)`) treats `""` as a valid non-null value, so it never fell through to the environment-variable fallback — `UseAzureMonitor` was silently never called even with a real connection string in the process environment. Confirmed live: the first run logged `Azure Monitor OpenTelemetry export enabled: False` and, after 150+ real requests and ~10 minutes, zero telemetry had reached Application Insights. Fixed by treating empty/whitespace as absent at each resolution step; rebuilt and reran — startup now logs `Azure Monitor OpenTelemetry export enabled: True`, and telemetry is confirmed arriving within minutes. Full detail in `evidence/otel-config.txt`.

Distributed tracing across the async Service Bus boundary needed explicit instrumentation (`Messaging/MessagingTelemetry.cs`, `QuoteEventPublisher.cs`, `SubscriptionWorker.cs`): the publisher starts a `Producer`-kind Activity and injects its W3C `traceparent`/`tracestate` into the Service Bus message's `ApplicationProperties`; the worker reads it back and starts a `Consumer`-kind Activity parented to it before invoking `IQuoteEventProcessor`, so the EF Core DB span underneath is a child of that same trace.

![OpenTelemetry and Application Insights](evidence/screenshots/01-opentelemetry-app-insights.png)

## KQL — p50/p99 by endpoint

`kql/endpoint-latency.kql`:

```kql
requests
| where cloud_RoleName == "quotes-api-day26"
| where timestamp > ago(2h)
| summarize RequestCount = count(), P50Ms = percentile(duration, 50), P99Ms = percentile(duration, 99) by name
| order by RequestCount desc
```

Executed against `appi-2i2oapij4zsrc` via `az monitor app-insights query` — 8 real endpoints returned, e.g. `GET /api/quotes/` (59 requests, p50 16.9ms, p99 54.6ms) and the worker's own `quote-events process` span (82 requests, p50 120.6ms, p99 945.6ms). Full output in `evidence/endpoint-latency.txt`.

![Endpoint p50 and p99](evidence/screenshots/02-endpoint-p50-p99.png)

## KQL — dependency breakdown

`kql/dependency-breakdown.kql`:

```kql
dependencies
| where cloud_RoleName == "quotes-api-day26"
| where timestamp > ago(2h)
| summarize Count = count(), Failures = countif(success == false), P50Ms = percentile(duration, 50), P95Ms = percentile(duration, 95), P99Ms = percentile(duration, 99) by target, type, name
| order by Count desc
```

Real result: SQLite EF Core spans (`type=sqlite`, targets `Users | main` and `quotes.db | main`, 464 + 33 calls), the custom `quote-events publish` Service Bus producer span (33 calls), and one `DefaultAzureCredential.GetToken` span from Azure Identity's own instrumentation. Full output in `evidence/dependency-breakdown.txt`.

![Dependency breakdown](evidence/screenshots/03-dependency-breakdown.png)

## KQL — error rate

`kql/error-rate.kql`:

```kql
requests
| where cloud_RoleName == "quotes-api-day26"
| where timestamp > ago(2h)
| summarize Total = count(), Failed = countif(success == false) by bin(timestamp, 5m)
| extend ErrorRatePercent = iff(Total == 0, 0.0, round(100.0 * Failed / Total, 2))
| order by timestamp asc
```

Real result: two 5-minute bins at 8.97% and 35.05% error rate — driven by 11 genuinely-triggered 500s (malformed JSON bodies causing an unhandled model-binding exception, caught by `GlobalExceptionHandler`) plus repeated poison-message failures on the `quote-events process` worker span (4 poison publishes × up to 3 delivery attempts × 2 competing subscriptions). Full output in `evidence/error-rate.txt`.

An Azure Monitor scheduled query (log) alert, **`quotes-api-day26-error-rate-alert`**, was created against `appi-2i2oapij4zsrc`:

```kql
requests
| where cloud_RoleName == "quotes-api-day26"
| summarize Total = count(), Failed = countif(success == false)
| extend ErrorRatePercent = iff(Total == 0, 0.0, round(100.0 * Failed / Total, 2))
| where ErrorRatePercent > 5
```

Condition: `count > 0` (i.e. any row returned — error rate over 5%) · evaluation frequency 5m · window 15m · severity 3 · auto-mitigate on · no action group attached (so it cannot spam anyone; it still records/fires in Azure Monitor). Verified live via `az monitor scheduled-query show` — `enabled: true`. Full config in `evidence/alert.txt`.

![Error rate alert](evidence/screenshots/04-error-rate-alert.png)

## Distributed Trace

One real trace (`operation_Id 124af37ff165f59beb0b3c78fee29a24`), queried with `kql/distributed-trace.kql`, proves **API → Service Bus → worker → DB** as a single connected trace, not four operations that merely share a timestamp:

```
POST /api/messaging/publish  (request, root span)
  └─ quote-events publish     (dependency, servicebus — parent = the API request)
       ├─ quote-events process (request — sub-day26-audit worker, parent = the producer span)
       │    └─ main (sqlite, Users|main) (dependency — parent = that worker span)
       └─ quote-events process (request — sub-day26-notifications worker, parent = the producer span)
            └─ main (sqlite, Users|main) (dependency — parent = that worker span)
```

Each row's `operation_ParentId` is literally the `id` of the row above it — the worker's parent is the producer span's id, which is exactly the W3C `traceparent` that `QuoteEventPublisher` wrote into the Service Bus message and `SubscriptionWorker` read back. This is the real mechanism, not an assumption: both competing subscriptions received the same message (topic fan-out) and both produced a correctly-parented worker span and DB write. Cross-checked live: both `sub-day26-audit` and `sub-day26-notifications` show `activeMessageCount=0` after this run. Full trace and the broader multi-trace query output are in `evidence/telemetry-verification.txt` and `evidence/distributed-trace.txt`.

![Distributed trace](evidence/screenshots/05-distributed-trace.png)

## Final Verification

- Application Insights (`appi-2i2oapij4zsrc`) confirmed receiving telemetry: 254 `requests` rows, 706 `dependencies` rows for `cloud_RoleName=quotes-api-day26` (`az monitor app-insights query`).
- All four KQL queries executed successfully against the real resource and returned real rows (no query ran only in theory — see `evidence/*.txt`).
- The error-rate alert rule exists and is enabled (`az monitor scheduled-query show`); it had not yet fired at verification time (evaluation cycles run every 5 minutes) — reported as observed, not assumed.
- Distributed tracing confirmed end-to-end for a real `operation_Id`: API request → Service Bus producer span → two competing worker spans → two SQLite dependency writes.
- `dotnet build` on `day-26/src/QuotesApi`: 0 warnings, 0 errors.
- `git status --short -- day-26` / `git status --short` confirm no file outside `day-26/` was intentionally modified; no commit or push was made.

![Final verification](evidence/screenshots/06-final-verification.png)
