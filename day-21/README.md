# Day 21 (Part 1) — HybridCache + Redis + Stampede Protection

## Task

> Add HybridCache (in-memory + Redis) to a hot read, with stampede protection
> so a cache miss doesn't fan out N identical DB hits. Measure the hit rate
> and the DB load drop under concurrent load.

## Exercise

> Paste the cache wiring + the load-test before/after (DB queries/sec, p99).
> Show stampede protection working under concurrency.

## Canonical source locations

- **Backend**: `day-1/QuotesApi/` (the same Day 18-20 QuotesApi project).
  This folder (`day-21/backend/…`) holds copies of the changed files for
  quick reference only — they are byte-identical to the canonical copies at
  the time of writing.
- **Frontend**: `Day-16/task-2/` (the same Day 16-20 Angular app). The new
  HybridCache tab lives at `Day-16/task-2/src/app/cache/` (+
  `src/app/cache.ts`, `src/app/cache.service.ts`), not copied into
  `day-21/` — an Angular app can't be split across two source trees and
  still compile, so it stays canonical in place.
- **Tests**: `day-1/QuotesApi/Tests.Domain/Caching/` and
  `Tests.Domain/GetQuoteByIdQueryHandlerTests.cs` (backend, copied into
  `day-21/tests/`); `Day-16/task-2/src/app/cache/cache.spec.ts` (frontend,
  stays with the component per the same compile-in-place rule).

## Hot read

`GET /api/quotes/{id}` (`day-1/QuotesApi/Application/Quotes/GetQuoteByIdQueryHandler.cs`).

## Full write-up

See [`result.md`](./result.md) for the wiring, the real load-test
numbers, the stampede-protection result, local/Azure verification status,
the UI, and screenshots.
