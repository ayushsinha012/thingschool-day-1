# Day 13 — Task 2: Result

## 1. Exercise (as given)

Build a small Angular feature against a real endpoint from the Week-1 `QuotesApi`, list/detail style:

- List quotes from `GET /api/quotes?page={page}&size={size}` (envelope `{ page, size, total, items }`, each item `{ id, author, text, isDeleted }`).
- Selecting an item loads its detail from a second endpoint, `GET /api/quotes/{id}`.
- Cover loading, error, and empty states for both the list and the detail.
- List/detail interaction: selecting a quote shows its detail alongside the list; selecting the same quote again (or closing) clears the detail.
- Find and fix a real bug: stale requests overwriting newer state when the user paginates or selects quickly.

## 2. What was built

- **Models** (`src/app/quote.ts`): `Quote { id, author, text, isDeleted }`, `QuotesPage { page, size, total, items }`, `QuoteDetail { id, author, text, display, characterCount }`.
- **Endpoints used** — real Week-1 `QuotesApi` (`day-1/QuotesApi`), confirmed against `Endpoints/QuoteEndpoints.cs` and `Application/Quotes/GetQuoteByIdQuery.cs`:
  - `GET http://localhost:5062/api/quotes?page={page}&size={size}` → `{ page, size, total, items: Quote[] }`.
  - `GET http://localhost:5062/api/quotes/{id}` → `{ id, author, text, display, characterCount }` (404 if not found).
- **`QuotesService`** (`src/app/quotes.service.ts`): thin injectable, `getQuotes(page, size)` and `getQuoteById(id)`, both plain `HttpClient` calls against the base URL above.
- **`App`** (`src/app/app.ts`), standalone component, `inject()`-based DI, signals throughout:
  - List side: `page`, `size`, `quotes`, `total`, `loading`, `error` signals; `totalPages`, `hasPrevious`, `hasNext` computed from `total`/`size`/`page`; `filter` signal with a `filteredQuotes` computed (case-insensitive match on author or text); `viewState` computed to `loading | error | empty | success`.
  - Detail side: `selectedId`, `quoteDetail`, `detailLoading`, `detailError` signals, plus a `detailRequest` signal that represents "the detail currently asked for" and a `detailViewState` computed to `idle | loading | error | success`.
  - `selectQuote(id)` toggles selection (clicking the selected quote again clears it) and updates `detailRequest`; `closeDetail()` clears both.
- **Template** (`app.html`): `@if`/`@else if` on `viewState()` for the list (skeleton list while loading, error panel with Retry, empty panel, or the `@for (quote of filteredQuotes(); track quote.id)` list), and `@switch (detailViewState())` for the detail pane (idle placeholder, skeleton, error panel with Retry, or the detail card with `display`/`author`/`characterCount`).

## 3. Loading / error / empty states

- **Loading** — `loading` signal drives a skeleton-card list (`@if (loading())`); the detail pane has its own skeleton for `detailViewState() === 'loading'`. Both are marked `aria-busy`/`aria-live="polite"` so it's not just visual.
- **Error** — `describeError()` maps a `404` to "No quotes found."/"Quote not found.", a network failure (`status === 0`) to "Unable to reach the server...", and anything else to a generic fallback. Each error panel has its own Retry button (`retry()` for the list, `retryDetail()` for the detail) that just replays the last request.
- **Empty** — when `filteredQuotes().length === 0` (either the API returned nothing for the page, or the filter matched nothing), the list shows an empty-state panel ("No quotes found. / Try a different author or search term.") instead of a blank list.

## 4. List/detail interaction

Clicking a quote card calls `selectQuote(id)`: if it's already selected, selection is cleared (toggle-off); otherwise `selectedId` is set and `detailRequest` is set to `{ id }`, which drives the detail fetch. The detail pane starts at `idle` ("Select a quote") until something is selected, then walks `loading → success`/`error`. Closing the detail (`closeDetail()`, or the `×` button) resets both `selectedId` and `detailRequest` back to `idle`.

## 5. Bug found and fixed: stale requests during rapid pagination/selection

**Bug:** both `loadQuotes` (list) and the detail fetch subscribe to an HTTP `Observable` per request. With no cancellation, clicking Next/Previous quickly, or clicking through several quotes fast, fires overlapping requests. Responses don't necessarily come back in the order they were sent — a slower response for a page/quote the user has already navigated away from can arrive *after* a faster, more recent one and silently overwrite the correct state with stale data. This is the classic race that plain `subscribe()` calls don't guard against.

**Fix, list side** — `loadQuotes()` keeps a `private quotesSubscription?: Subscription` and calls `this.quotesSubscription?.unsubscribe()` before issuing the next request:

```ts
private loadQuotes(page: number, size: number): void {
  this.quotesSubscription?.unsubscribe();
  this.loading.set(true);
  this.error.set(null);
  this.quotesSubscription = this.quotesService.getQuotes(page, size).subscribe({ ... });
}
```

So the in-flight request for a page the user has since paged away from is cancelled and can never resolve into `quotes`/`total`.

**Fix, detail side** — the detail fetch is driven through `toObservable(this.detailRequest).pipe(switchMap(...))` instead of a manual `subscribe()` per click. `switchMap` unsubscribes the previous inner request the moment a new `detailRequest` value arrives, so a slow response for a quote the user has already clicked past never lands on top of the currently-displayed detail. `detailRequest` is always set to a fresh object (not reused), including on retry, so a retry for the same id still triggers a new emission.

## 6. What would break if the API contract changed

- Renaming/restructuring the list envelope (`page`/`size`/`total`/`items`) or the item shape (`id`/`author`/`text`/`isDeleted`), or the detail shape (`id`/`author`/`text`/`display`/`characterCount`), breaks the `Quote`/`QuotesPage`/`QuoteDetail` interfaces silently — there's no runtime schema validation, so a mismatch surfaces as `undefined` in the template rather than a compile-time or request-time error.
- `QuotesService.baseUrl` is hardcoded to `http://localhost:5062/api/quotes`; moving the API to a different host/port breaks every request.
- `totalPages`/`hasNext`/`hasPrevious` assume `page` is 1-indexed and `total` is the grand total across all pages, not the count on the current page — switching either convention breaks pagination without any error being thrown.
- If the detail endpoint stopped returning `display` (e.g. only returned raw `text`), the detail card would render `undefined` instead of falling back to composing it client-side, since `display` is used as-is rather than derived.
- The 404-vs-network-failure distinction in `describeError()` depends on `HttpErrorResponse.status`; if the API started returning a different status for "not found" (e.g. 200 with an empty body), the error panel would show the generic fallback message instead of "Quote not found."

## 7. Screenshot

Could not capture a live screenshot: `ng serve` in this environment fails immediately because the installed Node.js is v18.20.8, and this Angular CLI version requires Node 20.19+/22.12+ (see `ng-serve` output). Per the task constraints, dependency/toolchain repair was out of scope for this submission, so no `task-2-ui.png` is included — the evidence above is drawn directly from the actual source files (`app.ts`, `app.html`, `quotes.service.ts`, `quote.ts`) and the real API source (`day-1/QuotesApi/Endpoints/QuoteEndpoints.cs`, `Application/Quotes/GetQuoteByIdQuery.cs`).
