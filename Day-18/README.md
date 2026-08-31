# Day 18 — Background Jobs: BackgroundService, IHostedService, and Hangfire

## Task

> Move slow work off the request thread. Implement a BackgroundService that
> drains a queue, and contrast it with IHostedService and Hangfire for
> scheduled work. Handle graceful shutdown via the cancellation token.

**Exercise:** Paste the BackgroundService + how it shuts down cleanly. One
line: when Hangfire over a hosted service?

The full write-up — verification log, the real bug found and fixed, and what
would still break — is in `result.md`. This file is the map.

## What's here

This day adds code to the two projects the rest of the week already uses
rather than starting a new one. The canonical, live source stays where the
app runs from; this folder additionally holds working copies of every
Day-18-related file under `src/` for anyone reviewing this submission
without checking out the whole repo (see "Files" below).

- **`day-1/QuotesApi/Jobs/`** — the queue (`IBackgroundTaskQueue` /
  `BackgroundTaskQueue`) and the consumer (`BackgroundJobWorker :
  BackgroundService`), plus the in-memory job status board (`IJobStore` /
  `JobStore`, `JobRecord`, `JobStatus`).
- **`day-1/QuotesApi/Endpoints/JobEndpoints.cs`** — `POST /api/jobs`,
  `GET /api/jobs`, `GET /api/jobs/{id}`.
- **`day-1/QuotesApi/Extensions/BackgroundJobsExtensions.cs`** — wires the
  queue/worker into DI, and configures Hangfire (in-memory storage) for the
  one recurring job that contrasts with the queue: cleaning up old finished
  job records.
- **`day-1/QuotesApi/Tests.Domain/Jobs/`** — unit tests for the queue, the
  worker's shutdown/exception behavior, and the job store.
- **`Day-16/task-2/src/app/jobs/`** — the Background Jobs page, routed at
  `/jobs` in the existing Angular app and linked from its nav (see
  `app.routes.ts`, `app.html`). Reuses the app's existing `HttpClient`
  setup, interceptors, and signal-based component style — no new Angular
  app, no duplicated services.
- **`docs/screenshots/`** — five real screenshots of the live app; see
  "Screenshots" in `result.md` for what each shows and how it was taken.

## Files

Everything genuinely specific to Day 18 lives once, at its canonical path,
and is also copied — unmodified — into `Day-18/src/` so this folder is
self-contained for review. **Edit the canonical path, not the copy** — the
running app reads from `day-1/QuotesApi` and `Day-16/task-2`, not from here.

| Canonical path | Copy in this folder | Dedicated to Day 18? |
|---|---|---|
| `day-1/QuotesApi/Jobs/*.cs` (7 files) | `src/backend/Jobs/` | Yes — new |
| `day-1/QuotesApi/Endpoints/JobEndpoints.cs` | `src/backend/Endpoints/` | Yes — new |
| `day-1/QuotesApi/Extensions/BackgroundJobsExtensions.cs` | `src/backend/Extensions/` | Yes — new |
| `day-1/QuotesApi/DTOs/JobRequests.cs` | `src/backend/DTOs/` | Yes — new |
| `day-1/QuotesApi/Tests.Domain/Jobs/*.cs` (3 files) | `src/backend/Tests.Domain/Jobs/` | Yes — new |
| `day-1/QuotesApi/Program.cs` | `src/backend/Program.cs` | No — shared file, only the Hangfire dashboard/recurring-job mapping (~line 86-89) is Day-18's |
| `day-1/QuotesApi/Extensions/InfrastructureExtensions.cs` | `src/backend/Extensions/InfrastructureExtensions.cs` | No — shared file, only the queue/worker DI registration (~line 122) is Day-18's |
| `day-1/QuotesApi/QuotesApi.csproj` | `src/backend/QuotesApi.csproj` | No — shared project file; only the two Hangfire `PackageReference`s were added for Day 18 |
| `Day-16/task-2/src/app/jobs/*` (4 files) | `src/frontend/app/jobs/` | Yes — new |
| `Day-16/task-2/src/app/job.ts` | `src/frontend/app/job.ts` | Yes — new |
| `Day-16/task-2/src/app/jobs.service.ts` | `src/frontend/app/jobs.service.ts` | Yes — new |
| `Day-16/task-2/src/app/app.routes.ts` | `src/frontend/app/app.routes.ts` | No — shared file, only the `/jobs` route entry is Day-18's |
| `Day-16/task-2/src/app/app.html` | `src/frontend/app/app.html` | No — shared file, only the "Background Jobs" nav link is Day-18's |

The three "shared file" backend copies and two "shared file" frontend
copies are included in full (they're small — under 200 lines each) rather
than as diffs, since a partial file wouldn't compile or run on its own; the
table above says exactly which part of each is actually Day-18's.

## API

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/api/jobs` | Enqueue a job (`label`, `durationSeconds` 1–20, `simulateFailure`). Returns `202 Accepted` with the `Queued` job record immediately — the slow work has not run yet. |
| `GET` | `/api/jobs` | Most recent 20 jobs, newest first. |
| `GET` | `/api/jobs/{id}` | One job's current status. |
| `GET` | `/hangfire` | Hangfire dashboard (recurring-job visibility). |

## IHostedService vs BackgroundService vs Hangfire

- **IHostedService** — the lower-level ASP.NET Core lifecycle interface:
  `StartAsync`/`StopAsync`, called once each by the host at startup/shutdown.
  Used directly when a background component needs explicit control over
  those two moments beyond "run one loop until told to stop."
- **BackgroundService** — the abstract base class this app uses
  (`BackgroundJobWorker`). It implements `IHostedService` for you and gives
  you one `ExecuteAsync(CancellationToken)` to override with a long-running
  loop — the right fit for a queue consumer, which is exactly what this is.
- **Hangfire** — a durable job/scheduling system layered on top of
  persistent storage (SQL Server/Redis/etc. in production; in-memory here).
  It adds what neither of the above give you for free: jobs and schedules
  that survive a process restart, automatic retries on failure, recurring
  jobs (`Cron.Minutely`, etc.), and an operational dashboard. This app uses
  it for exactly one thing — a recurring cleanup job — not for the ad-hoc
  "do this slow thing now" queue, which stays on the `BackgroundService`.

**One line:** reach for Hangfire over a plain hosted service when the job
has to survive a restart, retry itself, run on a schedule, or be visible to
an operator — not just run once, off the request thread, for as long as the
process happens to stay up.

## Deployment

Live on the same Azure resources Day 17 deployed to, no new infra:
backend at https://quotes-api.politeocean-3efec37e.centralindia.azurecontainerapps.io
(Container App `quotes-api`), frontend at
https://polite-mushroom-04dd5ce00.7.azurestaticapps.net (Static Web App
`thinkschool-ayush-swa`). Deployed the same way Day 17 was — manual
`dotnet publish`/`az containerapp update` and `swa deploy`, no CI/CD. See
`result.md` "Deployment (Azure)" for the exact commands, live verification,
and a real mid-deployment mistake (a stale dev-mode build briefly went out
to production) that was caught and fixed in the same pass.

## Verification

Full log with real timings, request/response bodies, and the shutdown trace
is in `result.md`. Summary: a `POST /api/jobs` for a 5-second job returned
`202` in 5ms; five jobs enqueued back-to-back were processed one at a time,
in order, each ~2s apart; a job with `simulateFailure: true` landed in
`Failed` with its error message; `SIGTERM` against the running process
logged `Background job worker stopping` and the process exited in about a
second, with no orphaned process left behind.

## Screenshots

Real captures against the live production app (see "Deployment" above) —
full detail on what each shows and how it was produced is in `result.md`
"Screenshots".

![Background Jobs navigation](docs/screenshots/01-background-jobs-nav.png)
![Background Jobs page, initial state](docs/screenshots/02-background-jobs-page.png)
![A job in the Running state](docs/screenshots/03-job-running.png)
![A job in the Completed state](docs/screenshots/04-job-completed.png)
![A job in the Failed state, with its error message](docs/screenshots/05-job-error.png)

## What Would Break This

- **Process restart** loses every job and every Hangfire-scheduled run —
  both `JobStore` and Hangfire here use in-memory storage. Fine for this
  demo; the fix for either is real persistent storage, not a code change to
  the worker.
- **A burst far larger than one worker can keep up with** queues up (the
  channel is unbounded, so requests still return immediately) but jobs run
  strictly one at a time — there is exactly one `BackgroundJobWorker`
  instance. A real backlog would need multiple consumers or a
  higher-throughput queue, not just a bigger buffer.
- **The Hangfire dashboard at `/hangfire`** relies only on its default
  localhost-only filter — it has no operator-role check, so it isn't safe to
  expose beyond localhost as-is.
