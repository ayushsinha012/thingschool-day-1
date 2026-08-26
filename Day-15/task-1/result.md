# Day 15 — Task 1: HttpClient + interceptors — Result

## 1. Brief given to the agent

> Write a characterization test that pins the real Week-1 API contract — the real
> endpoint `GET /api/quotes?page=N&size=N`, its actual response shape
> `{page, size, total, items: [{id, author, text, isDeleted}]}`, and a real 4xx coming
> back as `ProblemDetails`/`ValidationProblemDetails` — green **before** any UI. Then
> wire `HttpClient` + functional interceptors against that contract: an auth-header
> interceptor, a retry-with-backoff interceptor for idempotent GETs, and an
> interceptor that maps `ProblemDetails`/`ValidationProblemDetails` on a 4xx to a
> typed app error that surfaces a friendly message. Base it on the existing
> Day-14/task-2 Explore/Create UI (copied in, not modified in place) and land the new
> work as a fourth tab, "HTTP Lab", so Explore/Create/Create-Signal stay intact.

Before writing anything, I read the actual backend source
(`day-1/QuotesApi/Endpoints/QuoteEndpoints.cs`, `Validation/ValidationExtensions.cs`) and
then started `day-1/QuotesApi` and hit it live with `curl` rather than guessing the
`ProblemDetails` shape from memory — see §2.

## 2. What was actually verified about the API before writing any code

`day-1/QuotesApi` was already running on `http://localhost:5062` (SQLite dev DB,
`day-1/QuotesApi/appsettings.Development.json`). Raw `curl` output, used verbatim as the
fixtures in `src/app/http/quotes-contract.spec.ts`:

```
GET /api/quotes?page=1&size=2
200 {"page":1,"size":2,"total":14,"items":[{"id":1,"author":"Albert Einstein","text":"Imagination is more important than knowledge.","isDeleted":false}, ...]}

GET /api/quotes?page=0
400 {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"Invalid pagination","status":400,"detail":"Page must be at least 1 and size must be between 1 and 100."}

GET /api/quotes/999999
404 {"type":"...#section-15.5.5","title":"Quote not found","status":404,"detail":"No quote exists with ID 999999."}

POST /api/quotes (no Authorization header)
401 (empty body)

POST /api/quotes (Bearer token, body {"author":"","text":""})
400 {"type":"...#section-15.5.1","title":"One or more validation errors occurred.","status":400,
     "errors":{"Author":["The Author field is required.","The field Author must be a string with a minimum length of 1 and a maximum length of 200."],
               "Text":["The Text field is required.","The field Text must be a string with a minimum length of 1 and a maximum length of 1000."]},
     "traceId":"00-e0cb..."}
```

This is the real contract: the list endpoint returns a *plain* `ProblemDetails`
(`title`/`detail`, no `errors` dictionary) for a pagination error, but the create
endpoint returns a `ValidationProblemDetails` (`errors: {Field: [messages]}`) for a bad
body — two different 4xx shapes off two different code paths
(`Results.BadRequest(new ProblemDetails{...})` vs. `Results.ValidationProblem(...)` in
`ValidationExtensions.Validate`). The error-mapping interceptor has to branch on which
shape it got, not just on status code.

## 3. What was built

- **`src/app/http/app-error.ts`** — `AppError` (extends `Error`): `kind`
  (`'validation' | 'not-found' | 'unauthorized' | 'server' | 'network' | 'unknown'`),
  `friendlyMessage`, `status`, optional `fieldErrors`/`detail`.
- **`src/app/http/problem-details.ts`** — `toAppError(HttpErrorResponse): AppError`,
  branching on the exact shapes in §2 (`errors` dict present → `'validation'` with
  `fieldErrors`; plain `ProblemDetails` 400 → `'validation'` using `detail`; 404 →
  `'not-found'`; 401/403 → `'unauthorized'`; 5xx → `'server'`; status 0 → `'network'`).
- **`src/app/http/error-mapping.interceptor.ts`** — `catchError`s any
  `HttpErrorResponse` and rethrows `toAppError(err)` instead, so components never see
  the raw response.
- **`src/app/http/retry.interceptor.ts`** + **`retry-status.service.ts`** — GET-only,
  retries on status `0` or `>=500` (network drop / server error) with exponential
  backoff (300ms, 600ms, 1200ms; 3 retries max), passes 4xx straight through unretried,
  and reports live attempt counts to a signal the UI reads.
- **`src/app/auth.interceptor.ts`** — copied as-is from Day-14/task-2 (already correct:
  attaches `Authorization: Bearer <token>` only to `QUOTES_API_BASE_URL` requests).
- **`src/app/app.config.ts`** —
  `withInterceptors([authInterceptor, errorMappingInterceptor, retryInterceptor])`.
  Order matters and is load-bearing — see §5.
- **`src/app/http/quotes-contract.spec.ts`** — the characterization test, green before
  the UI existed (7 tests, run standalone before `http-lab` was written; 11 total once
  `http-lab.spec.ts` was added).
- **`src/app/http-lab/`** — new fourth tab (`http-lab.ts/html/css`), wired into
  `app.routes.ts` and `app.html`'s nav. Calls the same `QuotesService.getQuotes` /
  `getQuoteById` Explore already uses; Explore/Create/Create-Signal are untouched.

## 4. Verification log

**Characterization test, green before any UI** — `quotes-contract.spec.ts` was written
and passing (`npx ng test`, 7/7) before `http-lab.ts/html/css` existed.

**Real states exercised, end to end, in a real headless-Chromium browser
(`playwright`) against the live backend** (`day-1/QuotesApi` on `:5062`, this app served
on `:4301`):

- **Idle** — "Press 'Load quotes' to call GET /api/quotes." on first paint, no request
  fired.
- **Success** — clicked "Load quotes": real `GET /api/quotes?page=1&size=5` returned
  the 5 real seeded quotes ("Imagination is more important than knowledge." — Albert
  Einstein, etc.), rendered with `page 1 · 14 total`.
- **A real 4xx surfacing as a friendly message** (`docs/http-lab-01-400-error.png`) —
  clicked "Trigger 400 (page=0)": the UI showed exactly **"Page must be at least 1 and
  size must be between 1 and 100."** (the real `ProblemDetails.detail`, not a generic
  "Bad Request") plus `kind: validation · status: 400`. Clicked "Trigger 404 (missing
  quote)": UI showed **"No quote exists with ID 999999."** plus `kind: not-found ·
  status: 404`. Both are the server's real response text, confirming `toAppError` reads
  `detail`, not just `status`.
- **Empty** — verified via `http-lab.spec.ts` (jsdom + `HttpTestingController`): a
  `total: 0, items: []` response renders "No quotes on this page." (not silently blank).
- **Recovery** (`docs/http-lab-02-success.png`) — clicked "Reset to page 1 & retry"
  after the 400: back to the same 5-item success list, `page()` signal correctly reset
  before reloading.
- **No uncaught page errors** in the browser console across all of the above (Chromium
  logs a `console.error` for the 400/404 network responses themselves — that's Chromium
  logging the failed fetch, not an app error).
- **Auth header** — `quotes-contract.spec.ts`'s POST test asserts
  `req.request.headers.get('Authorization') === 'Bearer fake-token'` after a real
  `login()` round-trip.
- **Retry with backoff** — `retryInterceptor` describe block: a GET that fails 503 twice
  then succeeds resolves successfully after exactly 2 retries at 300ms/600ms; a GET that
  fails 503 four times in a row (1 initial + 3 retries) rejects as `AppError{kind:
  'server', status: 503}`; a GET that fails with 400 is **not** retried at all (only one
  request ever hits `httpMock`, enforced by `httpMock.verify()` in `afterEach`).
- `npx ng build --configuration development` — clean build, no template/type errors.
- `npx ng test --watch=false` — **11/11 passing**.

## 5. One concrete bug caught (interceptor ordering), and how it was actually verified

The obvious wrong assumption to make here is that `withInterceptors([...])` array order
doesn't matter much, or that it's "first interceptor runs first" for *both* the outgoing
request and the incoming error — it isn't. Angular nests interceptors like function
calls: the **first** entry in the array is outermost, so it's the *last* to see a
response or error coming back; the **last** entry is closest to the backend and sees a
raw failure *first*.

`retryInterceptor.isTransient()` decides whether to retry by checking
`err instanceof HttpErrorResponse && (err.status === 0 || err.status >= 500)`. If
`errorMappingInterceptor` ran *closer to the backend* than `retryInterceptor` (i.e. the
array were `[auth, retry, errorMapping]` instead of `[auth, errorMapping, retry]`), every
raw `HttpErrorResponse` would already be converted to an `AppError` before
`retryInterceptor` ever saw it — `instanceof HttpErrorResponse` would always be `false`,
and **no GET would ever retry**, silently, with no compiler or type error to catch it.

Rather than trust the reasoning, I proved it: I deliberately swapped the order in
`quotes-contract.spec.ts` to `[authInterceptor, retryInterceptor, errorMappingInterceptor]`
and reran the suite. Both retry tests failed immediately — `expectOne` found no second
request, because the very first 503 was mapped straight to an `AppError` and the retry
cascade never engaged (`Expected one matching request ... found none`). Reverted to
`[authInterceptor, errorMappingInterceptor, retryInterceptor]`, reran: 11/11 green again.
This is why `app.config.ts` has a comment spelling out the direction, and why the "does
not retry a 400" and "gives up after 3 retries" tests exist alongside the happy path —
without them, a future edit could silently reorder the array and nothing would fail
except real users seeing zero retries in production.

## 6. What breaks if the Week-1 API contract changes

- **Field rename** (`author`/`text`/`isDeleted` → anything else): `Quote`/`QuotesPage`
  in `quote.ts` are plain TypeScript interfaces, not runtime-validated against the
  server. A rename wouldn't fail at compile time or throw at runtime — the renamed
  field would just come through as `undefined` in the UI, exactly like the existing
  Explore tab (same underlying types).
- **`ProblemDetails` losing `detail`** (e.g. the pagination error stopped setting
  `Detail`): `toAppError`'s plain-400 branch falls back to `body?.title`, then to a
  generic `"The request was invalid."` — degrades to a less specific but still
  non-broken message, doesn't throw.
- **`ValidationProblemDetails` losing its `errors` dictionary**, or `errors` becoming a
  flat array instead of `Record<string, string[]>`: `isValidationProblem()`'s type guard
  (`'errors' in body && typeof body.errors === 'object'`) would need updating — a flat
  array is still `typeof === 'object'` in JS, so `Object.values(...).flat()` would
  silently produce garbage-but-non-throwing field messages rather than a clean failure.
  This is the one contract change that would degrade *silently* rather than obviously,
  and is the biggest argument for keeping `quotes-contract.spec.ts` pinned to the real
  shape rather than a schema the team assumes.
- **A 4xx status code being reused for a different meaning** (e.g. pagination errors
  moved from 400 to 422): `toAppError` has no `422` branch, so it would fall through to
  the generic `'unknown'` kind and a `Request failed (422).` message — not wrong, just
  less friendly, and `retryInterceptor` would correctly still not retry it (its
  transient check is `>= 500`, unaffected).
- **A transient failure that isn't `0` or `>=500`** (e.g. a `429 Too Many Requests` from
  future rate limiting): `retryInterceptor.isTransient()` doesn't treat 429 as
  transient today, so it would surface immediately as an `AppError{kind: 'unknown'}`
  instead of backing off and retrying — a real gap if the API ever adds rate limiting,
  flagged here rather than silently assumed away.
