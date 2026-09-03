# Day 21 Part 1 — Raw Local Measurements

Raw data only (not the finalized write-up — see day-21/README.md status).
All runs: `ab -n 2000 -c 20` (baseline/cached) or `-n 50 -c 50` (stampede)
against `GET /api/quotes/{id}`, same local machine, same SQLite DB
(`day-1/QuotesApi/quotes.db`), same local Redis (`localhost:6379`),
Debug build, `ASPNETCORE_ENVIRONMENT=Development`, EF command logging
forced to Warning (to avoid skewing throughput with per-query console I/O).
Full `ab` output for each run is alongside this file
(baseline-ab.txt / cached-ab.txt / stampede-ab.txt).

## Baseline (HybridCache bypassed via Caching:Enabled=false)

- Requests: 2000, Concurrency: 20, Failures: 0
- Time taken: 1.352s → **1478.96 req/sec**
- p50: 10ms, p90: 25ms, p95: 32ms, **p99: 57ms**, max: 81ms
- `/api/quotes/cache/metrics` after run:
  `{"cacheRequests":2000,"cacheHits":0,"cacheMisses":2000,"hitRatePercent":0,"dbCommandCount":2004}`
- **DB queries: 2000 of 2000 requests (100%) → ~1479 DB queries/sec**
  (dbCommandCount 2004 includes ~4 background Outbox relay/Hangfire polls
  during the 1.35s window, unrelated to this endpoint)

## Cached (HybridCache enabled, cold start: fresh process, flushed Redis)

- Requests: 2000, Concurrency: 20, Failures: 0
- Time taken: 0.913s → **2189.63 req/sec**
- p50: 5ms, p90: 12ms, p95: 15ms, **p99: 37ms**, max: 291ms
- `/api/quotes/cache/metrics` after run:
  `{"cacheRequests":2000,"cacheHits":1999,"cacheMisses":1,"hitRatePercent":99.95,"dbCommandCount":11}`
- **DB queries: 1 of 2000 requests (0.05%) → hit rate 99.95%**

## Before/After comparison

| Metric              | Baseline (no cache) | Cached (HybridCache) | Change             |
|----------------------|---------------------|-----------------------|---------------------|
| Requests/sec          | 1478.96             | 2189.63               | +48.1%              |
| p99 latency           | 57ms                | 37ms                  | -35.1%              |
| DB queries (of 2000)  | 2000                | 1                     | -99.95%             |
| Cache hit rate        | n/a                 | 99.95%                | —                   |

## Explicit stampede test (50 concurrent requests, same never-before-cached key)

- Fresh quote id (2), never read before this run — cold in both L1 and L2.
- `ab -n 50 -c 50 http://localhost:5062/api/quotes/2`
- Requests: 50, Concurrency: 50, Failures: 0
- Time taken: 0.019s → 2659.01 req/sec
- p50: 9ms, p95: 10ms, p99: 13ms, max: 13ms
- `/api/quotes/cache/metrics` after run:
  `{"cacheRequests":50,"cacheHits":49,"cacheMisses":1,"hitRatePercent":98,"dbCommandCount":6}`
- **Result: 50 concurrent cold-cache requests for the same key produced
  exactly 1 cache miss (1 DB read), not 50.** HybridCache's built-in
  single-flight collapsed the other 49 onto the one in-flight factory
  execution; all 50 HTTP requests still received a 200 with the correct
  quote body (verified via ab's 0 failed requests + HTML transferred size
  consistent with 50 identical bodies).
- Confirmed in Redis afterward: `quotesapi:quote:2` present (L2 populated
  from the single factory execution).

## Redis (L2) verification

- `quotesapi:quote:1` and `quotesapi:quote:2` both present in Redis after
  the runs above (`redis-cli keys "quotesapi:*"`).
- Separately verified: killed the running process (clears L1), left Redis
  populated, restarted fresh — first request for the already-cached id
  returned `cacheHits:1, cacheMisses:0` with no new DB command, proving the
  read was served from Redis (L2), not just in-process L1.

## Environment

- Local: Debian-based Linux, .NET 10.0.302, redis-server (local, port 6379,
  no persistence, started via `redis-server --daemonize yes --save "" --appendonly no`)
- Load tool: Apache Bench (`ab`) — already the load-test tool used
  elsewhere in this repo (see day-1/QuotesApi/Extensions/InfrastructureExtensions.cs
  ThreadPool.SetMinThreads comment).
- Dataset: 2 quotes seeded directly via sqlite3 (id 1 for baseline/cached
  runs, id 2 reserved for the stampede test so it starts genuinely cold).
