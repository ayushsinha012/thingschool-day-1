# Day 13 — Task 1: Quotes Browser (Angular, signals-based)

Standalone Angular component that lists quotes from the Week-1 QuotesApi, with client-side filtering, pagination, and loading/error/empty states — built entirely with signals and `computed`, no RxJS state in the component itself.

## Development server

Requires the Week-1 `QuotesApi` running locally on `http://localhost:5062` (see `day-1/QuotesApi`) — the component fetches from it on load, so start the API first.

```bash
npm install
ng serve
```

Once the dev server is running, open `http://localhost:4200/`. The app reloads automatically on source changes.

## Functionality

- Fetches a page of quotes from `GET http://localhost:5062/api/quotes?page={page}&size={size}` (the Week-1 `QuotesApi` endpoint).
- Renders each quote's `author` and `text`, plus a derived word count.
- Filters the currently loaded page by author or text via a search box, entirely client-side using a `computed` signal.
- Shows a loading skeleton, an error panel with retry, and an empty state, driven by a single `viewState` computed signal.
- Paginates with Previous/Next buttons backed by `page`/`size` signals and a `totalPages` computed from the API's `total`.

## API

- `QuotesService` (`src/app/quotes.service.ts`) calls `GET http://localhost:5062/api/quotes` with `page` and `size` as query params.
- Response envelope: `{ page, size, total, items }`.
- Each item in `items`: `{ id, author, text, isDeleted }` — mirrored by the `Quote` / `QuotesPage` models in `src/app/quote.ts`.

## Signals

`App` (`src/app/app.ts`) is a standalone component built with `inject()`, `signal()`, `computed()`, and `effect()` — no `NgModule`, no component-level RxJS state:

- Signals: `page`, `size`, `quotes`, `total`, `loading`, `error`, `filter`.
- Computed: `totalPages`, `hasPrevious`, `hasNext`, `filteredQuotes`, `viewState`.
- A constructor `effect()` reads `page()` and `size()` and re-fetches whenever either changes.
- The template (`app.html`) uses `@if`/`@else if` for the loading/error/empty/success branches and `@for (quote of filteredQuotes(); track quote.id)` for the quote list.

## Testing

```bash
ng test
```

Unit tests in `src/app/app.spec.ts` mock the HTTP call with `HttpTestingController` and cover: initial render, filter recomputation when the `filter` signal changes, and the empty-state panel when the filter matches nothing.

## Verification

- `ng build` has completed successfully — `dist/task-1/browser` contains the built `index.html`, bundled JS, and CSS.
- `ng test` has run and passed — see `result.md` for the recorded result and what it covers.

See `result.md` for the full implementation notes and verification log.
