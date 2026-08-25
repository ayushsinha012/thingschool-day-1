# Day 14 — Task 2: Result

## 1. Brief

Rebuild Day-14/task-1's create-a-quote form using Angular's Signal Forms **preview** API (`@angular/forms/signals`, installed as part of `@angular/forms@21.2.21` — a real, shipped-but-experimental subpackage, not a hypothetical future API), against the same real Week-1 endpoint as the reactive-forms version:

- `POST http://localhost:5062/api/quotes`, request body `{ author, text }` — no other fields.
- `author` max length 200, `text` max length 1000, both required and both rejecting whitespace-only input — the same `[Required, StringLength]` constraints on `CreateQuoteRequest` in `day-1/QuotesApi/DTOs/QuoteRequests.cs` that the reactive version already validates against.
- Signal Forms primitives only: `form()` wrapping a model `signal()`, `required()`/`maxLength()`/`validate()` schema rules, `[formField]`/`[formRoot]` template bindings, `submit()` for submission — no `FormGroup`/`FormControl`/`ReactiveFormsModule`.
- Exercise the same states as the reactive version: pristine, dirty, touched, validators firing, error display, a clean submit, and a failed submit.
- Same accessibility bar: labels, `aria-invalid`, `aria-describedby`, keyboard operability, focus-to-first-error on submit.
- Reuse `QuotesService`, `Quote`, `CreateQuoteRequest` and the app's existing auth/CORS setup as-is — this is a rebuild of the form, not a new backend integration.
- Land as a third tab, "Create (Signal Form)", alongside the existing Explore and Create tabs, so all of task-1's work stays intact and reachable.

## 2. What was actually verified about the API before writing any code

Signal Forms is explicitly a **developer preview** (every exported symbol in the installed package carries `@experimental 21.0.0`/`21.2.0` JSDoc tags), so before writing a line of the component I read the actual shipped type definitions and compiled bundle rather than trusting general knowledge of "signal forms" from blog posts or training data, which mostly describe earlier, unstable snapshots of this API:

- `node_modules/@angular/forms/types/signals.d.ts` and `_structure-chunk.d.ts` — the real type surface.
- `node_modules/@angular/forms/fesm2022/signals.mjs` — the real *runtime* export list, cross-checked against the `.d.ts` re-export list because the two disagree in one important way (see the bug section below).

This mattered immediately: my first instinct, going in, was that the template binding directive would be called `[field]`, based on how earlier public previews of this API were described. It is not. In this installed version the binding directive is **`[formField]`** (a `FormField` class, selector `[formField]`), and the root `<form>` binding is **`[formRoot]`** (a `FormRoot` class, selector `form[formRoot]`). `Field` does exist as a name in the `.d.ts`'s re-export list, but it is exported as a **type only** (`export type { ... Field ... }` in the compiled `.d.ts`, and absent entirely from the real `.mjs` runtime export list) — it is not a component or directive you import and use in a template. Getting this wrong would have meant a component that fails to compile, not a subtle runtime bug, so it was worth confirming from the actual shipped code before writing the template.

## 3. What was built

- **`src/app/create-signal/create-signal.ts`** — the standalone component. No `FormBuilder`, no `FormGroup`. The model is a plain `signal<{author: string; text: string}>`, and the form is:

  ```ts
  protected readonly quoteForm = form(
    this.model,
    (path) => {
      required(path.author, { message: 'Author is required.' });
      validate(path.author, ({ value }) =>
        isBlank(value()) ? { kind: 'blank', message: 'Author cannot be blank.' } : undefined
      );
      maxLength(path.author, AUTHOR_MAX_LENGTH, {
        message: `Author must be ${AUTHOR_MAX_LENGTH} characters or fewer.`
      });
      required(path.text, { message: 'Quote text is required.' });
      validate(path.text, ({ value }) =>
        isBlank(value()) ? { kind: 'blank', message: 'Quote text cannot be blank.' } : undefined
      );
      maxLength(path.text, TEXT_MAX_LENGTH, {
        message: `Quote text must be ${TEXT_MAX_LENGTH} characters or fewer.`
      });
    },
    {
      submission: {
        action: async (field) => { /* POST via QuotesService, see full file */ },
        onInvalid: () => { this.focusFirstInvalidField(); }
      }
    }
  );
  ```

  `isBlank()` is the same whitespace-only check as the reactive version's `notBlank` validator, just returning a plain `{kind, message}` object instead of an Angular `ValidationErrors` map. `extractServerError()` is ported **verbatim** from `create.ts` so both forms show identical messages for identical server responses — the comparison is about the forms layer, not about who has better error copy.

- **`src/app/create-signal/create-signal.html`** — `<form [formRoot]="quoteForm">`, `<input [formField]="quoteForm.author">`, `<textarea [formField]="quoteForm.text">`. `aria-invalid`/`aria-describedby` are still hand-wired via `[attr.*]` bindings exactly as in the reactive version — see section 5, this is not automatic.
- **`src/app/create-signal/create-signal.css`** — copied verbatim from `create.css`. The two forms are visually identical; only the binding mechanism differs, which is the point of the comparison.
- **`src/app/app.routes.ts`** / **`src/app/app.html`** — added a third route (`/create-signal`) and a third nav tab, "Create (Signal Form)", alongside Explore and Create. Nothing in task-1's Explore or Create was changed.

## 4. Verification log

Driven live end to end with a headless-Chromium (`playwright`) script — `day-1/QuotesApi` running on `http://localhost:5062`, this app on `http://localhost:4203` — using **keyboard input only** (`Tab`, typed text, `Enter`/click on the submit button), plus an `axe-core` accessibility scan. Full raw log: see the commit history / re-run `docs`' verification script; summarized here with the concrete values observed.

- **Pristine** (`docs/signal-01-empty.png`) — on load, `#author-input` has `aria-invalid="false"` and no error text. Confirmed by reading the DOM attribute, not just visually.
- **Native constraints applied automatically from the schema** — `#author-input` has a real `maxlength="200"` attribute and `#text-input` has `maxlength="1000"`, despite neither being set anywhere in the template. These come from the `maxLength()` schema calls; `[formField]` applies them to the native element itself. (See section 5 for why this is actually a compile-time-enforced fact, not an assumption.)
- **Dirty + touched** (`docs/signal-02-dirty-touched.png`) — typed a character into `#author-input`, cleared it, then tabbed away. `aria-invalid` flips to `"true"` and "Author is required." appears the moment the field is *touched* — the same dirty/touched-gated error display as the reactive version (`fieldInvalid()` in both forms checks `invalid() && (dirty() || touched())`, not just `invalid()` alone, so an untouched empty field never shows red on first paint).
- **Custom "blank" validator** — filled `#author-input` with three spaces, tabbed away: error text became "Author cannot be blank." (the custom `validate()` rule, not the built-in `required()`, since a value of `"   "` is non-empty and passes `required` on its own).
- **`maxLength` validator, proven independently of the native attribute** — set `#author-input`'s value to a 210-character string via `el.value = ...; dispatchEvent(new Event('input'))` (bypassing what a real user could type through the native `maxlength="200"` attribute, to prove the *validator* fires, not just the browser's own truncation): error became "Author must be 200 characters or fewer." — confirming the schema rule is real validation logic, not merely cosmetic HTML.
- **Invalid submit + focus-first-invalid** (`docs/signal-03-invalid-submit.png`) — with author over the length limit and text empty, clicking "Create quote" did not call the network at all (verified: no `/api/quotes` request fired), both errors rendered, and focus landed on `#author-input` — the first invalid field. Fixed the author field to a valid value and submitted again: focus moved to `#text-input` instead, confirming the "first invalid" check is genuinely per-field and re-evaluated each attempt, not hardcoded to always land on author.
- **Clean submit** — filled both fields with valid data and submitted: real `201 Created`, response body `{"id":14,"author":"Jane Austen","text":"...","isDeleted":false}` (id varies by run), confirmed persisted via the same live database every other Day-14/Day-13 exercise in this repo shares.
- **Success state** (`docs/signal-04-success.png`) — panel shows "Quote created." with the correct text/author, both fields are empty, no error text under either field, and focus returned to `#author-input`. (This screenshot is *after* the fix in section 5 — see that section for what it looked like before.)
- **Failed submit, real network failure** (`docs/signal-05-network-error.png`) — stopped `QuotesApi`, submitted a fully valid quote: the request failed with `ERR_CONNECTION_REFUSED`, the button correctly returned to "Create quote" (not stuck on "Creating…"), and the error panel showed "Could not reach the server. Check your connection and try again." — `extractServerError()`'s `status === 0` branch, working identically to the reactive version because it's the same function. Unlike the reactive version, the entered values were **not** cleared on failure (a deliberate side effect of only calling `reset()` inside the success branch — see section 6).
- **Accessibility (axe)** — ran `axe-core` against the success-state DOM (same check used for the reactive Create form): **0 violations**. The `<main>` landmark fix already made in `app.html` for the reactive form's audit (Day-14/task-1, result.md §6) covers this form too, since it's the same shell.

## 5. A wrong assumption caught by the compiler, not by me

While wiring the template, I initially bound `[attr.maxlength]="authorMaxLength"`/`[attr.maxlength]="textMaxLength"` on the native `<input>`/`<textarea>`, exactly mirroring the reactive-forms version — reasonable, since the reactive version *does* need that binding (`FormControl` has no concept of a max-length HTML attribute; `maxLength()` there is purely a validator, not something that touches the DOM).

Angular's compiler rejected it outright:

```
NG8022: Binding to '[attr.maxlength]' is not allowed on nodes using the '[formField]' directive
```

This is because `[formField]` **already** manages `maxlength` on the native element directly, driven by the `maxLength()` schema rule — it's not just a validator here, it also sets the real DOM attribute, and binding it a second time is a genuine conflict the compiler catches at build time rather than letting it silently double-bind. This is a case where Signal Forms is *not* a 1:1 port of the reactive-forms pattern — copying the reactive template's binding style over verbatim doesn't compile, and the fix (removing the manual binding entirely, letting the schema rule own it) is also a case of Signal Forms being *simpler* than reactive forms once you know the API, not just different. Verified the removal was correct and not just "made the error go away" by checking the resulting native `maxlength` attribute directly in the DOM after the fix (see section 4) — it's genuinely there, just set by the framework instead of the template.

## 6. A second real bug — caught by looking at a screenshot, not by reading code

After a successful submission, the handler called `this.model.set({ author: '', text: '' })` to clear the form, mirroring the reactive version's `this.form.reset()`. The build compiled clean and the first live run's `after success: form reset (author)` check read back `""` — value-wise, correct.

The screenshot told a different story: the success panel ("Quote created.") rendered correctly, but **both fields simultaneously showed "Author is required." / "Quote text is required." in red**, immediately after a successful submission the user hadn't touched yet.

**Root cause:** in Signal Forms, a field's `value` and its `touched`/`dirty` status are tracked separately. `model.set(...)` only ever changes the *value* — the fields remained `touched`/`dirty` from the submission that had just happened, so the moment the value went back to empty, the `required()` validator failed against that empty value, and `fieldInvalid()` (which is gated on `invalid() && (dirty() || touched())`, same as the reactive version) correctly-per-its-own-logic showed the error, because the touched/dirty flags were still `true` from a form the user was done with, not a fresh one.

**Fix:** `FieldState` has a `reset(value?)` method built for exactly this — "Resets the touched and dirty state of the field and its descendants... If [a value is] passed, the value will [be changed]." Replacing `this.model.set({author:'', text:''})` with `this.quoteForm().reset({ author: '', text: '' })` resets the value **and** clears touched/dirty in one call. Re-ran the live script after the fix: the success screenshot (`docs/signal-04-success.png`) now shows two clean, untouched fields with no error text. This is documented as a caught-and-fixed bug rather than quietly rewritten, because it's exactly the kind of thing that would look fine in a code review (the diff *looks* like a correct reset) and only shows up by actually running the thing and looking at what's on screen — which is the entire point of the verification step this exercise asks for.

## 7. Signal Forms vs. reactive forms — where it's simpler, where it's still rough

**Simpler:**
- No `submitting` signal to manage by hand — `quoteForm().submitting()` is built into the field state automatically while the configured `action` promise is in flight.
- No manual "already submitting, ignore this click" guard — `submit()` itself refuses concurrent submissions on the same field tree and returns `false` immediately instead of running the action twice. The reactive version needs an explicit `if (this.submitting()) return;` at the top of `submit()`; this version doesn't, though the `[disabled]` binding on the button is kept anyway for UX (belt-and-suspenders, not strictly required).
- No `(ngSubmit)="submit()"` handler to wire up — `<form [formRoot]="quoteForm">` listens for the native submit event itself and calls `submit()` internally using whatever `submission` options were configured when the form was created.
- `maxLength()` (and `required()`, `min()`, `max()`, `pattern()`) don't just validate — they also drive the real native `maxlength`/`required`/etc. attributes on the bound element automatically, so there's one fewer template binding to keep in sync with the validator (see section 5 for exactly how firmly the framework enforces this — it won't even compile if you try to also bind it yourself).
- Per-field state reads directly off the field itself (`quoteForm.author().invalid()`, `.touched()`, `.dirty()`, `.errors()`) instead of the reactive version's small `fieldInvalid()`/`fieldError()` helper methods that thread through `this.form.controls[name]`.

**Still rough (as of this preview, 21.2.21):**
- Every exported symbol is tagged `@experimental` — there is no guarantee the `[formField]`/`[formRoot]` selector names, or the `Field` type being type-only, survive to stable. Section 2 is the direct evidence for why that caveat isn't hypothetical.
- Value-reset and touched/dirty-reset are two different operations (`model.set()` vs. `fieldState.reset()`), and nothing stops you from calling only one of them and getting an inconsistent result that still type-checks and still runs — section 6 is exactly that trap.
- Focus-management on invalid submit is **not** automatic, despite the framework already tracking `invalid()` per field — `onInvalid` still has to be written by hand, checking fields in order and calling `.focusBoundControl()`, which is the same amount of code as the reactive version's `viewChild` + `ElementRef` approach, just with a different API to call at the end. Signal Forms gives you the state; you still own the a11y behavior on top of it.
- `aria-invalid`/`aria-describedby` are **not** wired automatically for native `<input>`/`<textarea>` elements (unlike `maxlength`) - they still have to be hand-bound with `[attr.*]`, identically to the reactive version. It would be easy to *assume* a forms library this state-aware handles ARIA for you; it doesn't, for native elements, in this preview.
- Fewer resources: no equivalent of `HttpTestingController`-driven unit tests exist yet for Signal Forms in this codebase (task-1's reactive `Create` doesn't have its own spec file either, so this isn't a regression, but it means both forms are currently verified live rather than by an automated suite).

## 8. What would break if the Week-1 API contract changed

Same fundamental exposure as the reactive version, since both call the same `QuotesService.createQuote()`:

- Renaming `author`/`text` in the request body breaks silently at the type level — `CreateQuoteRequest` is just a TypeScript interface, not validated against the live server, so a mismatch would show up as a real API validation error surfaced through `extractServerError()`, not a compile error here.
- If the 200/1000 limits changed server-side, the two `maxLength()` schema calls (and the native `maxlength` attributes they drive) would disagree with the server: a tightened server limit means the client still accepts input the server then rejects with a real 400 (correctly shown via `extractServerError`); a loosened server limit just means the client is stricter than it needs to be.
- If the response envelope stopped being `{ id, author, text, isDeleted }`, `this.created.set(quote)` would still run, and the success panel's `created()!.author`/`created()!.text` interpolation would render `undefined` for whichever field changed, exactly like the reactive version — no runtime validation on the response in either form.
- Specific to this version: the schema function's `path.author`/`path.text` property access is checked by TypeScript against `QuoteFormModel { author: string; text: string }` at compile time - renaming a *local* model field would be caught immediately (a real advantage over the reactive form's string-keyed `formControlName="author"`, which is not checked against the `FormGroup`'s shape at compile time in the same way). It does **not** protect against the *server's* field names changing, only against a typo in this file's own model interface.
