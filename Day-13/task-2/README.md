# Day 13 — Task 2: Quotes List/Detail (Angular, signals-based)

Standalone Angular component that lists quotes from the Week-1 `QuotesApi` and shows a detail view for the selected quote, with loading/error/empty states on both the list and the detail — built with signals and `computed`, no component-level RxJS state except the detail request's `switchMap` pipeline.

## Development server

Requires the Week-1 `QuotesApi` running locally on `http://localhost:5062` (see `day-1/QuotesApi`).

```bash
npm install
ng serve
```

Note: `ng serve` requires Node 20.19+/22.12+. This environment has Node 18.20.8, so the dev server could not be started here — no live screenshot is included in this submission for that reason.

## API

- `QuotesService` (`src/app/quotes.service.ts`):
  - `getQuotes(page, size)` → `GET http://localhost:5062/api/quotes?page={page}&size={size}` → `{ page, size, total, items }`, each item `{ id, author, text, isDeleted }`.
  - `getQuoteById(id)` → `GET http://localhost:5062/api/quotes/{id}` → `{ id, author, text, display, characterCount }`.
- Models in `src/app/quote.ts`: `Quote`, `QuotesPage`, `QuoteDetail`.

## Functionality

- Paginated quote list (`page`/`size` signals, `totalPages`/`hasPrevious`/`hasNext` computed from the API's `total`).
- Client-side filter by author or text (`filter` signal, `filteredQuotes` computed).
- List states via `viewState` computed (`loading | error | empty | success`): skeleton list, error panel with Retry, empty panel, or the quote list.
- Clicking a quote loads its detail (`selectQuote`); clicking the same quote again or the close button clears it. Detail states via `detailViewState` computed (`idle | loading | error | success`).

## Bug fixed: stale requests during rapid pagination/selection

Paging or selecting quickly could let an older, slower response land after a newer one and overwrite the current state with stale data.

- List: `loadQuotes()` unsubscribes the previous in-flight request (`this.quotesSubscription?.unsubscribe()`) before starting the next one.
- Detail: the fetch runs through `toObservable(detailRequest).pipe(switchMap(...))`, so `switchMap` cancels the previous detail request the instant a new one is selected.

See `result.md` for the full breakdown, including the real endpoint fields (verified against `day-1/QuotesApi/Endpoints/QuoteEndpoints.cs`), the states covered, and what would break under an API contract change.

## Screenshot

Not included — the dev server couldn't start in this environment (Node version too old for this Angular CLI). See `result.md` §7.
