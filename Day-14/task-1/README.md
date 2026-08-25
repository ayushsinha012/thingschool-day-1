# Day 14 — Task 1: Add a Quote (Angular reactive form)

Standalone Angular component that submits a new quote to the Week-1 `QuotesApi`, built with Angular reactive forms — validation, error messages, and focus management driven from a typed `FormGroup`, state (submitting/success/error) held in signals.

## API contract

`POST http://localhost:5062/api/quotes` (Week-1 `QuotesApi`, see `day-1/QuotesApi`).

Request body:

```json
{ "author": "Marcus Aurelius", "text": "You have power over your mind, not outside events." }
```

- `author` — required, 1–200 characters (`[Required, StringLength(200, MinimumLength = 1)]` in `DTOs/QuoteRequests.cs`).
- `text` — required, 1–1000 characters (`[Required, StringLength(1000, MinimumLength = 1)]`).
- Whitespace-only values are also rejected: .NET's `[Required]` attribute trims before checking length, and `Models/Quote.cs` re-checks `IsNullOrWhiteSpace` in the domain layer.

Success response — `201 Created`:

```json
{ "id": 1, "author": "Marcus Aurelius", "text": "You have power over your mind, not outside events.", "isDeleted": false }
```

Failure responses actually returned by the endpoint:
- `400` — either a validation problem (`{ errors: { Field: [message, ...] } }`) from `ValidationExtensions.Validate`, or a `ProblemDetails` (`{ title, detail }`) from the domain-level `ArgumentException` catch.
- `401`/`403` — the endpoint is `.RequireAuthorization(PermissionClaims.CanEditQuotes)`; no anonymous POST.

## Implementation

- `src/app/quote.ts` — `Quote` (response shape: `id`, `author`, `text`, `isDeleted`) and `CreateQuoteRequest` (`author`, `text`), matching the DTOs above exactly.
- `src/app/quotes.service.ts` — `QuotesService.createQuote()`, `HttpClient.post<Quote>` against the same `http://localhost:5062/api/quotes` base URL Day-13's `QuotesService` already uses.
- `src/app/app.ts` — the standalone root component:
  - `form: FormGroup<{ author: FormControl<string>; text: FormControl<string> }>`, built with `formBuilder.nonNullable.group(...)`.
  - Validators: `Validators.required`, a custom `notBlank` (rejects whitespace-only, since Angular's own `required` doesn't trim), `Validators.maxLength(200)` / `maxLength(1000)`.
  - Signals: `submitting`, `submitError`, `created`; a `status` computed (`idle | submitting | success | error`) drives which panel shows.
  - `submit()` — guards against duplicate submission, focuses the first invalid field on a failed validation attempt, posts the trimmed values, and on success resets the form and refocuses the author field.
  - `extractServerError()` — reads the real error body back from the API (validation `errors` map, `ProblemDetails.detail`/`.title`, or a network/auth fallback) instead of showing a made-up message.
- `src/app/app.html` / `app.css` — the form markup and styling, following Day-13's color tokens/layout conventions.

## Validation and accessibility

- Every field has a `<label for>` matching the input's `id`.
- `[attr.aria-invalid]` is set explicitly to `"true"`/`"false"` on both fields.
- `[attr.aria-describedby]` points at the field's error `<p role="alert">` id when invalid, and is removed otherwise.
- Native `maxlength` attributes mirror the same 200/1000 limits as the validators.
- Failing to submit an invalid form calls `markAllAsTouched()` and moves focus to the first invalid field (`viewChild` + `ElementRef`, author before text).
- The submit button disables and its label changes to "Creating…" while a request is in flight; the section is wrapped in `aria-live="polite"` with a visually-hidden status line so screen readers hear it too.
- Duplicate submission is prevented purely by the `submitting()` signal guard at the top of `submit()` plus the button's `disabled` binding — nothing else is disabled during the request, so keyboard focus stays put.

## Verification

Done without installing dependencies or running the dev server (no `npm install` was run in this task):

- Manual review of every field/route/limit against the live `day-1/QuotesApi` source, and against its own integration test (`Tests.Integration/QuoteEndpointsTests.cs`), which confirms the route, field casing, and response shape.
- A one-off `tsc --noEmit` type-check (temporarily borrowing Day-13/task-1's already-installed `node_modules`, removed again afterward) — passes clean.
- No live POST against a running API — it wasn't up on `localhost:5062` when this was checked, so that's not claimed as verified.

See `result.md` for the full verification log and the one real bug this review caught and fixed.

## How to run it

Needs the Week-1 `QuotesApi` running locally on `http://localhost:5062` first (see `day-1/QuotesApi`), and a user with the `CanEditQuotes` permission to actually get past the 401.

```bash
npm install
ng serve
```

Then open `http://localhost:4200/`.
