# Day 14 — Task 1: Result

## 1. Brief

Build the create-a-quote screen as an Angular reactive form, against the real Week-1 endpoint — not a mock. Specifically:

- `POST http://localhost:5062/api/quotes`, request body `{ author, text }` — no other fields, and nothing invented beyond what the API actually takes.
- `author` max length 200, `text` max length 1000 — pulled from the real `[StringLength]` attributes on `CreateQuoteRequest` in `day-1/QuotesApi/DTOs/QuoteRequests.cs`, not guessed.
- Angular reactive forms (`FormGroup`/`FormControl`), strongly typed, validators matching those real limits, `inject()` for DI (matching how Day-13 already does it).
- Inline validation errors, accessible labels, `aria-invalid`, `aria-describedby`, keyboard-friendly controls, focus moving to the first invalid field on a failed submit.
- Clear empty/invalid/submitting/success/server-error states, no duplicate submission while a request is in flight, the real server error surfaced when the POST fails (not a canned string), and the form resetting properly after a successful create.
- Reuse Day-13's setup, `Quote` model, and styling conventions wherever possible; don't touch Day-13 itself.

I later did a review pass against the same API source specifically to catch a wrong assumption, and a second pass to re-confirm the contract (endpoint, fields, 200/1000 limits) hadn't drifted from the implementation.

## 2. What actually got built

`src/app/quote.ts` — the response shape plus the create request shape, nothing else added:

```ts
export interface Quote {
  id: number;
  author: string;
  text: string;
  isDeleted: boolean;
}

export interface CreateQuoteRequest {
  author: string;
  text: string;
}
```

`src/app/quotes.service.ts` — one method, same `baseUrl` pattern as Day-13's `QuotesService`:

```ts
@Injectable({ providedIn: 'root' })
export class QuotesService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = 'http://localhost:5062/api/quotes';

  createQuote(request: CreateQuoteRequest): Observable<Quote> {
    return this.http.post<Quote>(this.baseUrl, request);
  }
}
```

`src/app/app.ts` — the form itself. The validators line is the part that actually encodes the contract:

```ts
const AUTHOR_MAX_LENGTH = 200;
const TEXT_MAX_LENGTH = 1000;

protected readonly form: FormGroup<QuoteFormControls> = this.formBuilder.nonNullable.group({
  author: ['', [Validators.required, notBlank, Validators.maxLength(AUTHOR_MAX_LENGTH)]],
  text: ['', [Validators.required, notBlank, Validators.maxLength(TEXT_MAX_LENGTH)]]
});
```

`notBlank` is a small custom validator I added on top of the built-ins, because Angular's `Validators.required` only rejects `null`/empty string — it doesn't trim, so a value of `"   "` would pass it. The API's `[Required]` attribute does trim before checking (that's a .NET default), so without this validator the client would accept whitespace-only input that the server would then reject — an actual mismatch, caught during review, not during the first build.

The rest of `app.ts` is `submitting`/`submitError`/`created` signals, a `status` computed for which panel to show, `fieldInvalid`/`fieldError` for the per-field messages, `focusFirstInvalidField()` using `viewChild` refs on the two controls, and `extractServerError()` which reads the actual response body back (see section 4 and 5 for why that matters).

`app.html` — the form markup, labels, and ARIA wiring:

```html
<div class="form-field">
  <label for="author-input">Author</label>
  <input
    id="author-input"
    #authorInput
    type="text"
    formControlName="author"
    autocomplete="off"
    [attr.maxlength]="authorMaxLength"
    [attr.aria-invalid]="fieldInvalid('author')"
    [attr.aria-describedby]="fieldInvalid('author') ? 'author-error' : null"
  />
  @if (fieldInvalid('author')) {
    <p id="author-error" class="field-error" role="alert">{{ fieldError('author') }}</p>
  }
</div>
```

Same pattern for the `text` textarea, plus the submit button (`disabled` while `submitting()`, label swaps to "Creating…") and three state panels gated on the `status()` signal — success, server-error, and a visually-hidden "Creating quote…" line for the submitting state.

## 3. Verification log

I didn't have `node_modules` in this project (no `npm install` was run per the task constraints), and the API wasn't running on `localhost:5062` when I checked (`curl` to it timed out with no response) — so this is a source-level review plus one scoped type-check, not a live run. I'm not claiming a live POST succeeded, because it wasn't tried against a running server.

- **Empty form** — both controls start untouched/pristine, `fieldInvalid()` requires `dirty || touched`, so no red borders or error text on load. Confirmed by reading the guard condition and the initial `FormGroup` state.
- **Invalid fields** — empty author/text trips `required`; whitespace-only trips the custom `blank` error (see section 2); anything past 200/1000 chars trips `maxlength` — though in practice the native `maxlength` attribute on the input/textarea stops you from typing past that in a real browser, so the validator is really a safety net for programmatic values.
- **Valid form** — a real author + real text with no leading/trailing whitespace issues clears every validator; `form.invalid` is `false`, submit proceeds.
- **Submitting state** — `submitting()` flips the button to "Creating…" and disabled, sets `aria-busy` on the section, and shows the hidden "Creating quote…" status line. This is a one-flag state, easy to trace through the code; I didn't see a code path that could leave it stuck `true`, since both the `next` and `error` subscribe branches set it back to `false`.
- **Server-error handling** — `extractServerError()` covers every shape `QuoteEndpoints.cs` actually returns: the validation-problem `errors` map, the `ProblemDetails` `title`/`detail` pair from the caught `ArgumentException`, a 401/403 fallback (the endpoint requires `CanEditQuotes`), and a network-failure (`status === 0`) message. Checked by reading both sides — the C# that produces each shape, and the TS that parses it — side by side.
- **Keyboard navigation** — plain `<input>`/`<textarea>`/`<button type="submit">`, no custom widgets, so tab order is just DOM order: author → text → submit. Pressing Enter inside the author field submits the form natively without moving focus to the button first — this turned out to matter (section 4).
- **Accessibility** — every `<label for>` matches its input's `id` (`author-input`/`text-input`), `aria-invalid` is always present as an explicit `"true"`/`"false"` rather than being omitted, and `aria-describedby` points at the matching error paragraph only while that field is actually showing an error.
- **First-invalid-field focus** — `submit()` calls `form.markAllAsTouched()` then `focusFirstInvalidField()`, which checks `author` before `text` and focuses whichever is invalid first via the `viewChild` refs. Confirmed by reading the order of the two checks against the order the fields actually appear in the form.
- **Type-check** — I don't have this project's own `node_modules`, so I temporarily symlinked Day-13/task-1's already-installed one into this folder, ran `tsc --noEmit -p tsconfig.app.json`, got exit code 0, and removed the symlink again. No npm install happened.

## 4. The bug I actually found

While double-checking "no duplicate submit," I'd originally written `submit()` to call `this.form.disable()` before the HTTP call and `this.form.enable()` after, on top of the `submitting()` guard, thinking it added extra protection. On review it didn't add anything the guard wasn't already doing — the `if (this.submitting()) return;` check at the top of `submit()` already blocks a second call synchronously, and the button's own `[disabled]="submitting()"` already stops a second click.

What it did do was break keyboard submission: if you submit by pressing Enter while focused in the author or text field (the normal way a form submits from the keyboard — focus doesn't jump to the button first), `form.disable()` disables the exact control that still has focus. Per the HTML spec, a control that becomes disabled while focused gets blurred, so focus silently dropped to `<body>` the moment submission started. That's a real regression against the "keyboard navigation" requirement, not a hypothetical one.

Fix — removed the `disable()`/`enable()` calls, kept everything else:

```ts
this.quotesService.createQuote(request).subscribe({
  next: (quote) => {
    this.submitting.set(false);
    this.created.set(quote);
    this.form.reset();
    this.authorInput()?.nativeElement.focus();
  },
  error: (err: HttpErrorResponse) => {
    this.submitting.set(false);
    this.submitError.set(extractServerError(err));
  }
});
```

Re-ran the symlinked `tsc --noEmit` after the change — still exits 0.

## 5. What would break if the API contract changes

- If `POST /api/quotes` stopped accepting `author`/`text` and used different field names, `CreateQuoteRequest` in `quote.ts` wouldn't catch it at compile time — it's just an interface shape, not validated against the server. The request would still go out with the old field names, and the API would come back with a validation error for the "missing" fields, which `extractServerError()` would at least surface correctly rather than swallow — but the form itself would look broken to a user with no obvious reason why.
- If the 200/1000 limits changed server-side, the client and server would disagree: a lower server limit would mean users pass client-side validation and still get a real 400 back (which the app does show, via `extractServerError`, so it's not silent) — a higher server limit would just mean the client blocks input the server would have accepted, which is a false-negative UX problem, not a broken submission.
- If the route moved off `/api/quotes` or the port stopped being 5062, every request from `QuotesService.createQuote()` would fail outright, since the base URL is a hardcoded string, not pulled from config.
- If the response stopped being `{ id, author, text, isDeleted }` — say `id` got renamed or dropped — `created.set(quote)` would still run, but the success panel's `created()!.author`/`created()!.text` interpolation would just render `undefined` for whichever field changed shape, with no error surfaced, since there's no runtime validation on the response either.
