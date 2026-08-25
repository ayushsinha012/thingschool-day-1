# Day 13 — Task 2: Quotes List/Detail (Angular, signals-based)

Standalone Angular component that lists quotes from the Week-1 `QuotesApi` and shows a detail view for the selected quote, with loading/error/empty states on both the list and the detail — built with signals and `computed`, no component-level RxJS state except the detail request's `switchMap` pipeline.

## Development server

Requires the Week-1 `QuotesApi` running locally on `http://localhost:5062` (see `day-1/QuotesApi`).

```bash
npm install
ng serve
```

## API

- `QuotesService` (`src/app/quotes.service.ts`):
  - `getQuotes(page, size)` → `GET http://localhost:5062/api/quotes?page={page}&size={size}` → `{ page, size, total, items }`, each item `{ id, author, text, isDeleted }`.
  - `getQuoteById(id)` → `GET http://localhost:5062/api/quotes/{id}` → real response is `{ id, author, text, isDeleted }` (the same shape as a list item — confirmed against `QuoteEndpoints.cs`, which does `Results.Ok(quote)` on the raw entity, and by calling the live endpoint directly). `display` and `characterCount` are **not** returned by the API; `QuotesService.getQuoteById` composes them client-side from `text`/`author`. See `result.md` §6 for the bug this corrects.
- Models in `src/app/quote.ts`: `Quote`, `QuotesPage`, `QuoteDetail` (the last one a client-side view model, not a distinct API shape).

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

![Detail panel showing the composed quote text and character count](docs/detail-after-fix.png)

Captured against a live run: `ng serve --port 4202` (4200/4201 were already in use by other exercises' apps running alongside it) against the Week-1 `QuotesApi` on `http://localhost:5062`, seeded with 10 quotes. `docs/detail-before-fix.png` shows the same detail panel before the `display`/`characterCount` fix — see `result.md` §6.
