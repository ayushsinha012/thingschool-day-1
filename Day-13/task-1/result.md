# Day 13 — Task 1: Result

## 1. Brief

Build a small Angular feature against a real endpoint from the Week-1 `QuotesApi`: `GET /api/quotes?page={page}&size={size}`, which returns `{ page, size, total, items }` where each item is `{ id, author, text, isDeleted }`.

Goal: a standalone component that fetches a page of quotes and lets the user filter what's on screen by author or text, using signals end to end — no component-level RxJS state, no `NgModule`. Concretely that means:

- `inject()` for `HttpClient`/`QuotesService`, not constructor-parameter DI.
- `page` and `size` as writable `signal()`s driving the API call, with a `computed()` `totalPages` derived from the API's `total`.
- A `filter` `signal()` for the search box, with a `computed()` `filteredQuotes` derived from it and the loaded `quotes` signal.
- An `effect()` that re-fetches whenever `page`/`size` change, instead of manual "call on init, call again on page change" wiring.
- A single `computed()` `viewState` (`loading | error | empty | success`), read in the template with `@if`/`@else if` instead of scattering `*ngIf`-style checks.
- The quote list rendered with `@for` and tracked by `quote.id`, not array index, since the list is re-derived from `filteredQuotes()` on every keystroke.

## 2. What I built

- `Quote` / `QuotesPage` models (`src/app/quote.ts`) that mirror the API response shape exactly: `id`, `author`, `text`, `isDeleted` on each item, and `page`/`size`/`total`/`items` on the envelope.
- `QuotesService` (`src/app/quotes.service.ts`), a thin injectable wrapping `HttpClient.get<QuotesPage>` against `http://localhost:5062/api/quotes` with `page`/`size` as query params.
- `App` (`src/app/app.ts`), the standalone root component:
  - Signals: `page`, `size`, `quotes`, `total`, `loading`, `error`, `filter`.
  - Computed: `totalPages`, `hasPrevious`, `hasNext`, `filteredQuotes` (case-insensitive match on author or text), and `viewState`.
  - A constructor `effect()` that reads `page()` and `size()` and re-fetches whenever either changes — this replaces manual "fetch on init + fetch on page change" wiring.
  - `loadQuotes` sets `loading`/`error`/`quotes`/`total` from the `QuotesService` response; `retry()` re-runs it after a failure.
  - `wordCount()` as a plain helper for the per-card word count shown in the template.
- `app.html` drives everything off `viewState()`: a skeleton grid while loading, an error panel with a retry button, an empty-state panel, or the `@for (quote of filteredQuotes(); track quote.id)` grid — plus a Previous/Next footer gated on `hasPrevious()`/`hasNext()`.

## 3. Verification

- `ng build` — production build completed; `dist/task-1/browser` contains the built `index.html`, bundled JS, and CSS.
- `ng test` (Vitest) — `src/app/app.spec.ts` ran and passed (recorded run: `app.spec.ts`, not failed, ~200ms). It exercises:
  - component creation and initial render against a mocked `GET http://localhost:5062/api/quotes` request,
  - the `h1` title rendering,
  - the rendered list shrinking from 2 items to 1 when the `filter` signal is set to `'twain'`,
  - the empty-state panel and "No quotes found." text appearing when the filter matches nothing.
- All HTTP calls in the spec go through `HttpTestingController`, and `httpMock.verify()` runs in `afterEach`, so there's no unflushed/unexpected request left over from any test.

## 4. States and edges actually exercised

- **Empty list** — both via an API response with `total: 0, items: []` (tests 1 and 2) and via a filter that matches nothing on a non-empty loaded page (test 4). Both correctly resolve `viewState` to `'empty'` and show "No quotes found."
- **Computed value changing when a signal changes** — `filteredQuotes()` recomputes and the rendered `<li>` count drops from 2 to 1 the moment `filter` is set to `'twain'`, with no manual re-fetch.
- **API success** — a 2-item response renders as 2 quote cards with correct author text.

Not covered by the current tests: the `error` branch of `viewState` (a failing HTTP request) and the pagination buttons (`previousPage`/`nextPage`). Both are implemented but only exercised by reading the code, not by a passing test, so I'm not listing them as verified.

## 5. A wrong assumption I ran into

I initially expected one `fixture.detectChanges()` to be enough to get from "component created" to "data on screen" in each test. It isn't: the HTTP call lives inside the constructor's `effect()`, which only fires on the first change-detection pass, so the request isn't sent until after that first `detectChanges()`. The fix (visible in the spec file) is the two-step pattern used in every test: `detectChanges()` to trigger the effect and send the request, then `httpMock.expectOne(...).flush(...)` to resolve it, then (where the test needs to interact further, like typing into the filter box) a second `detectChanges()` to re-render with the resolved data.

## 6. What would break if the API contract changes

- Renaming or restructuring the envelope (`page`/`size`/`total`/`items`) or item fields (`id`/`author`/`text`/`isDeleted`) breaks `QuotesPage`/`Quote` silently — there's no runtime validation, so a mismatch would surface as `undefined` values in the UI (e.g. blank author/text) rather than a compile or request error.
- Moving the base URL or port away from `http://localhost:5062/api/quotes` breaks every request, since `QuotesService.baseUrl` is hardcoded rather than injected from config.
- Switching pagination to zero-indexed pages, or changing `total` to mean "items on this page" instead of the grand total, would break `totalPages`, `hasNext`, and `hasPrevious`, since they're all derived directly from the current `total`/`size`/`page` values under the current (1-indexed, grand-total) assumption.
- Dropping `isDeleted` from the item shape wouldn't break anything today since the component doesn't read it, but if the API ever stopped filtering deleted quotes server-side, they'd start showing up in the list with no client-side guard against it.
