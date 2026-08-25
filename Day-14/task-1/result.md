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

## 6. Live verification pass: keyboard-driven, with axe

Everything above was a source-level review — no `node_modules`, no running API. This section supersedes that: `day-1/QuotesApi` was brought up on `http://localhost:5062` (seeded with 10 quotes), this app was served with `ng serve` on `http://localhost:4200`, and the actual Create form (now reached via a nav bar's **Create** tab, alongside an **Explore** tab added afterward — see the app's `app.routes.ts`) was driven end to end with a headless-Chromium script (`playwright`) using **keyboard input only** — `page.keyboard.press('Tab'/'Enter')`, no `.click()` on form fields — plus an in-page `axe-core` scan. Screenshots and the full log are in `docs/`.

**Empty state** (`docs/create-01-empty.png`) — both fields start with `aria-invalid="false"`, no error text, confirmed by reading the DOM attribute directly, not just visually.

**Keyboard path** — from a blank page, three `Tab` presses landed focus on `#author-input` (`Explore` link → `Create` link → author field), confirming the tab order is nav-then-form with no keyboard trap. Pressing `Enter` while focused in a single-line `<input>` submits the form natively (this only works from `<input>`, not `<textarea>` — a real detail, since Enter inside the multi-line `text` field would just insert a newline).

**Invalid state** (`docs/create-02-invalid.png`) — submitting the empty form via `Enter` produced `aria-invalid="true"` on both fields, `aria-describedby="author-error"` on the author input correctly resolving to an element whose text is "Author is required.", and focus landed back on `#author-input` — the first invalid field, exactly as designed. To check the "first invalid field" logic isn't just "always focus author," I filled only `author` (valid) and submitted again via keyboard from the submit button (`Tab`, `Tab`, `Enter`): focus moved to `#text-input` this time, confirming the check is genuinely per-field, not hardcoded.

**Submitting state** — the button's `disabled` property read back as `true` at the moment of submission (confirmed via the DOM, not just the code), but on `localhost` the round trip to the API resolves faster than a screenshot can reliably catch — by the time the screenshot fired, the response had already landed. This is being reported honestly rather than staged: the submitting state exists and is exercised (the assertion on `disabled` proves it ran), but there's no clean screenshot of it, unlike the other three states.

**Server-error state** (screenshot since replaced — see §8) — submitting a fully valid quote (`Rumi` / "The wound is the place where the light enters you.") returned a real `401` from the API and the panel showed "You are not authorized to create quotes.", which is `extractServerError()`'s 401/403 branch working correctly against a real response, not a mocked one.

### A concrete gap this surfaced: the form could never actually succeed as originally wired

`QuoteEndpoints.cs` gates `POST /api/quotes` behind `.RequireAuthorization(PermissionClaims.CanEditQuotes)`, and `QuotesService.createQuote()` sent a plain unauthenticated `HttpClient.post`. There was no login flow, no token storage, and no `Authorization` header anywhere in this Angular app. The 401 above wasn't a contrived test case — it's what **every** real submission through this UI got at the time. The error-handling code was correct (it surfaced the real reason clearly), but the form's happy path was unreachable until an auth flow was added on top. **Fixed in §8** — this section is kept as-is because it's an accurate record of a real bug caught live, not because it's still the current behavior.

### The one bug axe caught, fixed

An `axe-core` scan of the invalid-state DOM (chosen deliberately over the empty state, since that's the state with the most ARIA wiring active) reported exactly one violation:

```json
{ "id": "landmark-one-main", "impact": "moderate", "help": "Document should have one main landmark" }
```

`app.html` rendered `<router-outlet>` directly inside a plain `<div class="app-shell">` — no `<main>` anywhere in the document. Screen reader users rely on landmark navigation (e.g. "jump to main content") to skip the nav bar; without one, that shortcut does nothing. Fixed by wrapping the outlet:

```html
<main>
  <router-outlet></router-outlet>
</main>
```

Re-ran the same axe scan after the fix: **0 violations**. This is the "screen-reader or axe" check the exercise asks for, and the bug it caught (missing landmark) is a different class of mistake than the disable/enable focus bug in section 4 — that one came from reading the code, this one came from a tool that models what assistive tech actually announces.

## 7. What this live pass didn't cover

- No screen-reader software (NVDA/VoiceOver) was run against this build — the a11y check here is axe's static analysis plus manual `aria-*` attribute inspection, not a listened-to announcement. Axe catches missing/malformed landmarks, labels, and roles; it does not catch a "sounds confusing when read aloud" problem.
- Only Chromium was used; no cross-browser check.
- (Originally listed here: "the success path is unverified live." No longer true — see §8.)

## 8. Fixing the auth gap, and verifying the real success path

Section 6 found that this form could never actually succeed, because `QuotesService` never sent an `Authorization` header. Building a full login screen is out of scope for a forms/a11y exercise, so the fix taken was the smallest one that makes the real request succeed:

- **`src/app/auth.service.ts`** — a thin `AuthService` with one `login(email, password)` method that calls the real `POST /api/auth/login` and stores the returned access token in an in-memory `signal` (not `localStorage`/`sessionStorage` — a refresh means logging in again, which is an acceptable trade for a dev-only flow and avoids stashing a token in persistent browser storage).
- **`src/app/auth.interceptor.ts`** — a functional `HttpInterceptorFn` that reads the token from `AuthService` and attaches `Authorization: Bearer <token>` to any request whose URL starts with `http://localhost:5062/api`. Requests before login succeeds (or to other origins) pass through unchanged.
- **`src/app/app.config.ts`** — registers the interceptor via `provideHttpClient(withInterceptors([authInterceptor]))`, and uses `provideAppInitializer(...)` to call `AuthService.login(...)` **before the app renders**, using the exact seeded test user `day-1/QuotesApi/Data/DbSeeder.cs` already creates (`ayush.test@example.com` / the seeded password) — the same account used earlier in this project's history to seed the 10 demo quotes via `curl`. The login call is wrapped in `.catch(() => undefined)` so a backend that isn't up yet doesn't hang app bootstrap; it just means the first real POST will 401 again until the API is reachable.

This is explicitly a **dev-only convenience**, not a real auth flow, and the code says so in a comment at both the config and the service. A real app would need an actual login screen, token refresh, and secure storage — none of that is in scope here.

**Re-ran the exact same keyboard-driven script from §6** against the fixed app:

- Empty state — unchanged, still `aria-invalid="false"` on load (`docs/create-01-empty.png`).
- Invalid state — unchanged, still focuses `#author-input` first with `aria-describedby` correctly wired (`docs/create-02-invalid.png`).
- **Real submission** — filled `Maya Angelou` / "Try to be a rainbow in someone else's cloud." via keyboard, submitted with `Enter` on the focused submit button. Response: **`201`**, body `{ "id": 11, "author": "Maya Angelou", "text": "...", "isDeleted": false }`. Confirmed independently with `curl http://localhost:5062/api/quotes/11` and by checking the list total: `GET /api/quotes` went from `total: 10` to `total: 11`. This is a real, persisted row, not a mocked response.
- **Success state** (`docs/create-03-success.png`) — the success panel rendered "Quote created." with the correct quote text and author, the form fields both read back as empty strings (confirmed `form.reset()` ran), and `document.activeElement.id` was `author-input` — confirming the "refocus author on success" behavior from `submit()`'s `next` handler actually fires, not just that the code exists.
- **axe re-scan on the success state**: 0 violations — the `<main>` fix from §6 holds across routes/states, not just the one DOM snapshot it was originally caught on.

The `docs/create-03-server-error.png` screenshot from §6 was removed and replaced with `docs/create-03-success.png`, since the server-error screenshot no longer reflects this app's actual behavior — keeping a stale "it's broken" screenshot next to a fixed app would be more misleading than useful.
