# Day 22 — Resilience with Polly

## Task

> Wrap an outbound dependency with Polly: retry-with-backoff (idempotent
> only), a circuit breaker, a timeout, and a bulkhead. Then prove the
> circuit opens under sustained failure and recovers.

## Exercise

> Paste the resilience pipeline. Show logs/metrics of the breaker opening
> then half-opening to recovery.

## Source layout

Unlike Day 18-21, this day's code is **not** added to the canonical
`day-1/QuotesApi` project. The task rules for Day 22 explicitly forbid
modifying Day 1-21, so the whole exercise — pipeline, the outbound
client, the local test-controlled downstream, tests, and the demo script
— lives self-contained under `day-22/`:

- `day-22/src/ResilienceDemo/` — the ASP.NET Core minimal API host (net10.0).
  Reuses the same conventions already established in `day-1/QuotesApi`:
  Serilog (console sink, `appsettings.json`-driven), minimal API endpoint
  groups, options-pattern configuration.
- `day-22/tests/ResilienceDemo.Tests/` — xUnit + FluentAssertions, same
  stack as `day-1/QuotesApi/Tests.Domain`.
- `day-22/load-test/run-demo.sh` — one command that builds, runs, and
  drives all five demonstration scenarios (A-E) against the real pipeline,
  writing the evidence used in this write-up to `load-test/evidence/`.
- `day-22/load-test/evidence/` — the actual captured logs from the run
  referenced below (`app.log`, `demo-run.log`, `dotnet-test.log`).

Full write-up, pipeline code, downstream simulator, reproduction commands,
and the real captured evidence: [`result.md`](./result.md).
