# Day 17 — Cloud deployment, CORS, and Managed Identity — Result

## 1. Scope of the exercise

Reconstructed from this repository's own evidence (there is no separate
assignment file committed anywhere in this repo, and the one Day-17 commit's
message, "Deploy frontend to Azure Static Web Apps", undersells what its
diff actually contains) — not a verbatim quoted brief, so it isn't presented
as one:

1. **Deploy the Angular frontend** (`Day-16/task-2`) to a real Azure Static
   Web App, reachable over the public internet.
2. **Lock down CORS** on the already-deployed `day-1/QuotesApi` Container App
   to that Static Web App's real origin — an explicit allow-list, never a
   wildcard (see the comments already in
   `day-1/QuotesApi/Extensions/InfrastructureExtensions.cs`).
3. **Prove a Managed-Identity server-side path**: a second Azure-hosted
   component (`Day-17/quotes-bff`) that acquires an Entra access token via
   `DefaultAzureCredential` — resolving to the Container App's own
   system-assigned identity in Azure, nothing else — and uses it to call
   QuotesApi. No client secret, certificate, password, or token is ever
   allowed to reach the browser.
4. **Verify the whole thing works as a real user would hit it**: login/logout,
   a protected route, real quote data, and a Lighthouse run against the live
   URL.

## 2. Live resources (real, not placeholders)

| Resource | Value |
|---|---|
| Static Web App | `thinkschool-ayush-swa`, resource group `thinkschool-rg`, SKU Free |
| Frontend URL | https://polite-mushroom-04dd5ce00.7.azurestaticapps.net |
| QuotesApi | Container App `quotes-api`, `thinkschool-rg` / `thinkschool-env`, Central India |
| QuotesApi URL | https://quotes-api.politeocean-3efec37e.centralindia.azurecontainerapps.io |
| QuotesBff | Container App `quotes-bff`, same environment |
| QuotesBff URL | https://quotes-bff.politeocean-3efec37e.centralindia.azurecontainerapps.io |
| Custom domain | **None.** `az staticwebapp hostname list` returns empty — no domain was ever registered for this exercise. Not invented. |

**CI/CD, honestly stated:** `.github/workflows/ci.yml` runs `dotnet test`
against `day-1/QuotesApi/Tests.Domain` and `Tests.Integration` (with a 70%
line-coverage gate) on every push/PR — it does **not** deploy anything. The
Static Web App's build environment shows `sourceBranch: null` and no linked
repository (`az staticwebapp show`), and both Container Apps were built and
pushed with `azd deploy` / `dotnet publish -p:PublishContainer` run by hand
from a developer machine. There is no continuous deployment pipeline for any
of Day 17's three deployed resources — every deployment in this exercise was
a manual, one-off `azd`/`az`/`dotnet` invocation.

## 3. Managed Identity — implementation and evidence

**Code** (`Day-17/quotes-bff/Program.cs`, unchanged from the Saturday
commit): `builder.Services.AddSingleton(new DefaultAzureCredential())`, then
for any forwarded request that arrives with no `Authorization` header,
`credential.GetTokenAsync(new TokenRequestContext([quotesApiScope]), ...)`
acquires a token and attaches it as `Bearer` before proxying to QuotesApi.
Bodies, methods, query strings, and any `Authorization` header the browser
already sent are passed through unchanged — no auth/business logic is
duplicated.

**Infra** (`infra/resources.bicep`): the `quotes-bff` Container App is
declared with `managedIdentities: { systemAssigned: true }` and an
`AcrPull` role assignment on the shared container registry — no
user-assigned identity, no secret, no certificate.

**What had to be fixed to make this real** (not just declared): the
Container App had been sitting in a **`Failed`** provisioning state,
`latestRevisionName: null`, still serving the placeholder
`mcr.microsoft.com/azuredocs/containerapps-helloworld:latest` image — `azd
provision` twice failed with `ContainerAppOperationError: Operation
expired` on the same resource (once before this session, once retried
during it). Worked around, without recreating the Bicep-declared shape,
by:

1. Deleting the stuck `quotes-bff` Container App (`az containerapp delete`)
   so ARM would accept a fresh write to that resource name.
2. Manually granting `AcrPull` (`az role assignment create`) — the exact
   role the Bicep already declares — to the new system-assigned identity,
   since the Bicep-declared role assignment never got to run.
3. Building and pushing the real image directly
   (`dotnet publish -c Release -r linux-x64 /t:PublishContainer
   -p:ContainerRegistry=cr2i2oapij4zsrc.azurecr.io ...`) rather than via
   `azd deploy`, which failed separately with "could not determine
   container registry endpoint" because the azd environment's provisioning
   state was incomplete.
4. `az containerapp update --image ...` on the existing resource.

**Live evidence this actually works** — `quotes-bff`'s own container logs,
captured while calling it with no `Authorization` header at all:

```
Start processing HTTP request GET https://quotes-api.politeocean-3efec37e.centralindia.azurecontainerapps.io/api/quotes?*
Sending HTTP request GET https://quotes-api.politeocean-3efec37e.centralindia.azurecontainerapps.io/api/quotes?*
Received HTTP response headers after 315.87ms - 200
```

and the actual response, real data, through the BFF and not the API
directly:

```
GET https://quotes-bff.politeocean-3efec37e.centralindia.azurecontainerapps.io/api/quotes?page=1&size=10
200 {"page":1,"size":10,"total":7,"items":[{"id":1,"author":"Ada Lovelace", ...
```

For this to have reached QuotesApi at all, `credential.GetTokenAsync` had to
succeed — an unhandled exception there would have thrown before the proxied
`HttpClient.SendAsync` call the logs show completing. No token value, no
`Authorization` header value, and no credential material appear in any log
line above or anywhere this work touched.

**Open item — the `Api.Invoke` Entra app-role grant.**
`infra/grant-api-invoke-role.sh` grants `quotes-bff`'s identity the
`Api.Invoke` application role on the `quotes-api-day17` app registration via
a Microsoft Graph call (ARM/Bicep has no native resource type for this).
Both hardcoded IDs in that script were re-verified live against the tenant
before running it (not trusted blindly):

```
az ad sp show --id 729a2be3-9609-4fd1-b7c5-e658386f9bfd --query id
  → 43050566-7eed-4220-a1b7-8b2533204239   (matches script)
az ad app show --id 729a2be3-9609-4fd1-b7c5-e658386f9bfd --query appRoles
  → Api.Invoke = 428f70d1-ea0c-44d8-914a-9234db5dae42   (matches script)
```

Running it fails with:

```
ERROR: Forbidden({"error":{"code":"Authorization_RequestDenied","message":"Insufficient privileges to complete the operation." ...}})
```

The signed-in account's own directory memberships
(`GET /v1.0/me/memberOf`) are ordinary student groups ("Noida Students",
"All Users", ...) — no `Application Administrator`, `Privileged Role
Administrator`, or `Global Administrator` role. This is a genuine tenant
permission boundary, not a bug to route around: **it needs a tenant admin**.
It does not block anything working today because `GET /api/quotes` has no
`.RequireAuthorization()` on it (confirmed in
`day-1/QuotesApi/Endpoints/QuoteEndpoints.cs`) — QuotesApi accepts the
request with or without a role-bearing token. It would matter the moment any
`RequireAuthorization`-protected endpoint is routed through the BFF's
Managed-Identity path instead of a real user's bearer token.

## 4. Verification log — real states exercised against the live app

All of the following were exercised against the **live** URLs above, not a
local dev server. Screenshots are real Puppeteer captures (Chrome, headless,
`--no-sandbox`) driving actual clicks and form input against
`https://polite-mushroom-04dd5ce00.7.azurestaticapps.net`, saved under
`docs/screenshots/`:

| # | State | Screenshot | What it shows |
|---|---|---|---|
| 1 | Logged out, `/explore` | `01-explore-logged-out.png` | The 7 real quotes, "Log in" link in the nav |
| 2 | Protected route, logged out | `02-protected-route-redirect-to-login.png` | Navigating directly to `/create` redirects to `/login?returnUrl=%2Fcreate` |
| 3 | Invalid login | `03-login-invalid.png` | Real `nobody@example.com` / wrong password submit → the API's actual 401 `detail`, "Email or password is incorrect.", rendered in the error panel |
| 4 | Successful login → protected page | `04-login-success-protected-create-page.png` | Real login with the app's own seeded test user, lands back on `/create` (the original `returnUrl`), nav now shows "Log out" |
| 5 | After logout | `05-after-logout.png` | Clicking "Log out" flips the nav back to "Log in" |
| 6 | Protected route after logout | `06-protected-route-after-logout.png` | `/create` redirects to `/login?returnUrl=%2Fcreate` again — identical to state 2, confirming logout actually cleared the in-memory token |

**Backend checks, run directly with `curl` against the live API** (no
credentials or tokens appear in this table):

| Check | Result |
|---|---|
| `GET /api/quotes?page=1&size=10` | `200`, `total: 7`, real quotes (Ada Lovelace, Donald Knuth, Edsger W. Dijkstra, Barbara Liskov, Margaret Hamilton, Alan Turing, Grace Hopper) |
| `GET /api/quotes/1` | `200`, `{"id":1,"author":"Ada Lovelace","text":"...","display":"\"...\" — Ada Lovelace","characterCount":127}` |
| `GET /api/quotes/7` | `200`, same shape, Grace Hopper, `characterCount: 78` |
| `POST /api/quotes` (no auth) | `401`, `WWW-Authenticate: Bearer` |
| `POST /api/auth/login` (wrong password) | `401`, `{"title":"Invalid credentials","status":401,"detail":"Email or password is incorrect."}` |
| `POST /api/auth/login` (correct) | `200`, JWT decodes to `permission: can-edit-quotes` |
| `OPTIONS /api/quotes` preflight, `Origin: https://polite-mushroom-...` | `204`, `access-control-allow-origin`, `-methods`, `-headers` all present and correct |
| `GET /api/quotes`, `Origin: https://evil.example.com` | `200` but **no** `Access-Control-Allow-Origin` header — browser would block reading it |
| Frontend bundle secret scan | `chunk-*.js` (265KB) and `main-*.js` (121KB) both grepped for `client_secret`, JWT-shaped strings, and hardcoded passwords — **zero matches**. The only backend value the bundle contains is the public `apiBaseUrl` |

## 5. One real bug hit and fixed: CORS redeploy wiped production data

Fixing CORS meant a new revision of `quotes-api` (the fix was already
written in the working tree; it had never been built and deployed). Running
`azd deploy quotes-api` created that new revision — and, invisibly, wiped
every quote in production.

**Why:** `ConnectionStrings__DefaultConnection` on the Container App is
`Data Source=/tmp/quotes.db` — a path inside the container's own,
non-persistent filesystem, with no volume mount
(`properties.template.volumes` and `.volumeMounts` are both empty, confirmed
via `az containerapp show`). Every new revision is a new container instance
with an empty `/tmp`. `Program.cs` runs `db.Database.Migrate()` then
`DbSeeder.SeedAsync(db)` on startup — but `DbSeeder` only seeds a demo user,
never any quotes. So the new revision came up with a freshly-migrated,
empty `Quotes` table.

**How it was caught:** `GET /api/quotes?page=1&size=10` returned
`{"total":0,"items":[]}` immediately after the deploy, where it had
returned `total: 7` minutes earlier in this same session.

**The fix applied:** logged in as the app's own seeded test user
(`ayush.test@example.com`, a synthetic fixture already committed in
`day-1/QuotesApi/Data/DbSeeder.cs` — not a real person's credential) and
re-created the same 7 real, public-domain, non-sensitive quotes through the
actual production write path, `POST /api/quotes`, one call per quote — not
by touching the database directly. They landed back on IDs 1–7 (a fresh
auto-increment sequence on an empty table), so the API's observable state is
identical to before.

**Why this isn't actually fixed, just recovered from:** the underlying
cause — ephemeral storage — is still there. `day-1/QuotesApi/Migrations.SqlServer/`
already exists in this repo, suggesting a real (persistent) SQL Server
target was planned but never wired into the deployed connection string.
Any future `quotes-api` redeploy will wipe the quotes table again until that
connection string points somewhere durable. Flagged in `README.md` and left
unfixed here — out of this exercise's scope, and not something to
silently paper over.

## 6. API-change risks

- **Any future `quotes-api` redeploy wipes all quotes** (see §5) — the
  single biggest operational risk from this exercise, and the one most
  likely to bite silently, since the app itself gives no error when this
  happens (it just quietly returns `total: 0`).
- **CORS is an explicit origin allow-list, not a wildcard**
  (`Cors:ProductionOrigins`) — if the Static Web App's hostname ever changes
  (e.g. it's deleted and recreated, which mints a new random hostname), every
  browser call breaks with no server-side error at all; the failure is
  entirely client-side (`Access-Control-Allow-Origin` missing) and easy to
  misdiagnose as an API outage.
- **`quotes-bff`'s Managed Identity currently authenticates every anonymous
  request as itself**, regardless of which quote or route was requested,
  because `GET /api/quotes` requires no specific permission claim today. If
  a future QuotesApi change adds a `RequireAuthorization` to a currently-
  anonymous GET, that endpoint would start rejecting the BFF's own token
  unless the `Api.Invoke` role grant (§3, still open) is completed first.
- **The frontend calls QuotesApi directly, not through the BFF**
  (`environment.prod.ts`) — so the CORS allow-list on `quotes-api` is what
  the browser actually depends on today, not `quotes-bff`'s CORS policy
  (which exists, in the same shape, but currently has no live traffic to
  protect).

## 7. Lighthouse result

Run once against the live URL (`npx lighthouse
https://polite-mushroom-04dd5ce00.7.azurestaticapps.net`, default mobile
throttling profile, headless Chrome):

| Category | Score |
|---|---:|
| Performance | **38** |
| Accessibility | **100** |
| Best Practices | **100** |
| SEO | **91** |

**Target (≥95) not met on Performance or SEO. Reported as measured — not
adjusted or re-run under different conditions to produce a better number.**


On Performance: total page weight is small (131KB across 2 scripts, 1
stylesheet, no fonts/images beyond a favicon) and total measured main-thread
work was ~3s, yet the mobile-throttled run reported a 6.6-second Total
Blocking Time — internally larger than the total task time measured in the
same report, which is not physically consistent for an unthrottled trace and
points at simulated-throttling noise from running headless Chrome on a
shared, non-dedicated machine rather than a genuine 6.6s of blocking
JavaScript. A `--preset=desktop` run against the same URL, same code,
scored Performance 90 — offered here as a diagnostic data point, not as a
replacement for the officially recorded 38, since the exercise's Lighthouse
target was measured under default (mobile) settings and that is the number
reported above.

## 8. Remaining work (explicitly not done)

- Persistent storage for `quotes-api` (real SQL target, not ephemeral SQLite).
- The `Api.Invoke` Entra app-role grant for `quotes-bff` — needs a tenant
  admin.
- `robots.txt` fix in `Day-16/task-2` (SEO).
- Performance work to close the Lighthouse gap for real (once the
  measurement-noise question above is resolved with a trustworthy lab
  environment — e.g. PageSpeed Insights against the live URL).
- No custom domain exists; none was invented.
- Wiring the frontend to actually call `quotes-bff` instead of `quotes-api`
  directly, if the Managed-Identity path is meant to be load-bearing rather
  than a proven-but-unused side path.

## 9. Sign Up

**Was there already a registration endpoint?** No. `AuthController` only had
`login`, `refresh`, and `logout`. `POST /api/auth/register` did not exist
anywhere in `day-1/QuotesApi` before this work.

**What was added** — the smallest registration flow inside the existing
architecture, no new user store, no second database:

- `DTOs/RegisterRequest.cs`: `{ Email, Password }`, `[Required, EmailAddress]`
  / `[Required, MinLength(8)]` — validated automatically by `[ApiController]`'s
  filter, the same mechanism `LoginRequest` already relies on.
- `AuthController.Register` (`POST /api/auth/register`): trims the email,
  checks for an existing account case-insensitively (`Email.ToLower() ==
  email.ToLower()`), hashes the password with the project's existing
  `BCrypt.Net.BCrypt.HashPassword` (the same call `DbSeeder` and `Login` use),
  saves a `User` row through the existing `AppDbContext`/`Users` table, and
  returns `201 { id, email }` — `PasswordHash` is never put on the response
  object at all, not merely omitted after the fact.
- A `DbUpdateException` catch around `SaveChangesAsync` turns a raced
  duplicate insert (two requests for the same email landing between the
  existence check and the save) into the same `409` a non-racing duplicate
  gets, backed by the real unique index on `Users.Email`
  (`AppDbContext.OnModelCreating`) — not just the application-level check.

**No migration.** `User` already had exactly the two columns registration
needs (`Email`, `PasswordHash`); the schema was not touched.

**Real request/response shapes** (see §12 for the full commands):

| Case | Status | Body |
|---|---|---|
| New email | `201` | `{"id":<n>,"email":"<email>"}` |
| Duplicate email (exact or different case) | `409` | `{"title":"Email already registered","status":409,"detail":"An account with this email address already exists."}` |
| Password under 8 characters | `400` | `{"title":"One or more validation errors occurred.","status":400,"errors":{"Password":["Password must be at least 8 characters."]}}` |

**Frontend** (`Day-16/task-2/src/app/signup/`): a new `Signup` component/route
(`/signup`), styled with the exact same panel/form/field/button CSS classes
`login/login.css` and `create/create.css` already use — no new visual
language. Email, password, and confirm-password fields; a cross-field
validator (`passwordsMatch`) flags a mismatched confirmation; the submit
button disables and re-labels itself while the request is in flight. `AuthService.register()`
posts to `/api/auth/register` and deliberately does **not** log the new user
in itself — success shows an "Account created" panel with a link to
`/login?registered=1`, and `Login` shows a small confirmation banner when
arriving with that query param, matching how a real signup→login handoff
should feel without silently reusing the login code path for two different
actions.

`AppError`/`toAppError` (`http/app-error.ts`, `http/problem-details.ts`)
gained a `'conflict'` kind for real `409` responses — the only place this
app returns `409` — so the duplicate-email message shown to the user comes
from the same typed error model every other form in this app already uses,
not a second, one-off error parser.

## 10. Explore Search

**What it does:** typing in Explore's search box filters the quote list by
**both** author name and quote text, case-insensitively, debounced (300ms)
while typing, and resets to page 1 on every new search term.

**Client or server side?** Server-side, and this was already the frontend's
design going in — `QuotesService.getQuotes` already sent `search` as a query
parameter, `QuotesStateService.load` already threaded a `search` argument
through to it, and `Explore`'s constructor already ran a debounced signal
into `state.load(1, size, search)`. None of that frontend code needed to
change.

**The actual endpoint:** `GET /api/quotes?page=N&size=N&search=<term>` (day-1/QuotesApi/Endpoints/QuoteEndpoints.cs`,
`Repositories/QuoteRepository.cs`). `search` is optional; omitted or blank
means "no filter," and pagination fields behave exactly as before.

**Matching:** `EF.Functions.Like(quote.Author, $"%{term}%") ||
EF.Functions.Like(quote.Text, $"%{term}%")` against both columns in one SQL
query — SQLite's `LIKE` is case-insensitive for ASCII by default, and the
term is trimmed before building the pattern so leading/trailing whitespace
in the query box doesn't prevent an otherwise-matching row.

**Pagination interaction:** `total`/`page`/`size` in the response describe
the *filtered* result set, so "Page X of Y" and Previous/Next in the UI stay
correct against however many quotes actually matched, not the full
unfiltered count.

See §12 for real `curl` output proving author search, quote-text search,
case-insensitivity, whitespace tolerance, the empty-result state, and
pagination still working — and §14 for the live browser screenshots of the
same.

## 11. Bug Found and Fixed

**The bug:** `GET /api/quotes` never bound or used a `search` query
parameter at all. `QuoteEndpoints.MapGet("/")`'s handler only accepted
`int? page, int? size` — no `string? search` parameter — and
`IQuoteRepository.GetPagedAsync`/`QuoteRepository.GetPagedAsync` had no
search argument in their signature or query. The frontend was already
sending `?...&search=<term>` (`QuotesService.getQuotes`, confirmed by the
regression test added in `http/quotes-contract.spec.ts`), but the backend
silently ignored it and always returned the full, unfiltered page — so
Explore's search box visibly did nothing for either an author name or a
distinctive word from a quote's text, exactly as reported.

**Why it's real and not a symptom of something else:** confirmed directly
against a running instance before any fix — `GET /api/quotes?search=Einstein`
returned the same unfiltered page as `GET /api/quotes` with no `search` at
all.

**The fix:**
1. `IQuoteRepository.GetPagedAsync` gained a `string? search` parameter.
2. `QuoteRepository.GetPagedAsync` applies the `EF.Functions.Like` filter
   described in §10 whenever `search` is non-blank.
3. `QuoteEndpoints.cs`'s `GET /` handler now binds `string? search` from the
   query string and passes it through to the repository call.
4. `Tests.Domain/TestDoubles/InMemoryQuoteRepository.cs` (used by the
   command-handler unit tests) was updated to the new interface shape and
   given a matching in-memory filter, so it keeps exercising the same
   contract the real repository does.

**Verified:** `Tests.Domain/QuoteRepositoryTests.cs` (6 new tests: no-search,
author match, quote-text match, case-insensitive, whitespace-tolerant,
no-match) against a real in-memory-SQLite `AppDbContext` — the actual EF
query, not a hand-rolled stand-in — plus the live `curl` evidence and
browser screenshots in §12/§14.

## 12. Verification Log (Sign Up + Search)

All of the following are real results from this work, run against either a
disposable local instance (throwaway SQLite file, torn down afterward, never
touching `/tmp/quotes.db`) or the **live** production API/Static Web App —
labeled per row. No output below is fabricated or edited.

**Backend unit tests** (`dotnet test Tests.Domain`, no Docker required):
`Passed! - Failed: 0, Passed: 81, Skipped: 0, Total: 81`. Includes 4 new
`Register`/`Login` tests in `AuthControllerTests.cs` and 6 new tests in the
new `QuoteRepositoryTests.cs`.
`Tests.Integration` was intentionally not run for this change — it requires
a Testcontainers-provisioned SQL Server via Docker, and no code path it
covers changed.

**Frontend unit tests** (`ng test`, jsdom, no live backend):
`Test Files: 4 passed (4)`, `Tests: 19 passed (19)` — includes 4 new tests in
`signup/signup.spec.ts` and 2 new contract tests in `http/quotes-contract.spec.ts`
pinning that `search` is sent as a query parameter (and omitted when empty).

**Frontend production build** (`ng build --configuration production`):
succeeds; bundle sizes unchanged in shape (one new lazy-loaded-free `Signup`
component, no new dependencies).

**Local, disposable-instance verification** (before touching production):

| Check | Result |
|---|---|
| `POST /api/auth/register` (new email) | `201 {"id":2,"email":"verify-signup-test@example.com"}` |
| `POST /api/auth/register` (same email again) | `409`, `"Email already registered"` |
| `POST /api/auth/register` (same email, different case) | `409` |
| `POST /api/auth/register` (7-char password) | `400`, field error `Password: ["Password must be at least 8 characters."]` |
| `POST /api/auth/login` (the new user) | `200`, real access token, `expires_in: 900` |
| `POST /api/quotes` (new user's token) | `201`, quote created |
| `POST /api/quotes` (no token) | `401` |
| `GET /api/quotes?search=Einstein` | matches only the Einstein quote by author |
| `GET /api/quotes?search=Imagination` | matches only the quote containing that word |
| `GET /api/quotes?search=einstein` (lowercase) | same match — case-insensitive |
| `GET /api/quotes?search=  twain  ` (padded) | matches Twain — whitespace-tolerant |
| `GET /api/quotes?search=nonexistentxyz` | `200 {"total":0,"items":[]}` |
| `GET /api/quotes` (no search) | full list restored |
| `GET /api/quotes?page=1&size=1` / `page=2&size=1` | pagination unaffected |
| `GET /api/quotes?page=1&size=1&search=getting` | pagination + search combine correctly |

**Live production verification** (`quotes-api` Container App and the real
Static Web App, `https://polite-mushroom-04dd5ce00.7.azurestaticapps.net`):

Deploying the fix meant a new `quotes-api` revision — this **did** wipe
`/tmp/quotes.db` again, exactly as §5 describes and README warns. Sequence,
in order:

1. Captured the live `GET /api/quotes` response (9 quotes — the original 7
   from §5's recovery plus 2 more since added: "Ayush Sinha", "Ayu") before
   touching anything.
2. Built and pushed a new image (`dotnet publish -r linux-x64
   /t:PublishContainer -p:ContainerRegistry=cr2i2oapij4zsrc.azurecr.io ...` —
   `az acr build` was tried first and rejected by the subscription with
   `TasksOperationsNotAllowed`, so this fell back to the same direct-publish
   path §3 used for `quotes-bff`), then `az containerapp update --image ...`
   on `quotes-api`.
3. Confirmed the wipe directly: immediately after the new revision came up,
   `GET /api/quotes` returned `{"total":0,"items":[]}`, and the new
   revision's own logs showed every migration re-applying from scratch
   (`Applying migration 'InitialCreate'...`) — proof the container really
   started with an empty database, not a stale response.
4. Restored the exact same 9 quotes (same author, same text, same order) via
   9 real `POST /api/quotes` calls authenticated as the app's seeded
   `ayush.test@example.com` user — the same recovery method §5 already used,
   not a database edit. A byte-for-byte comparison of the "before" and
   "after" `GET /api/quotes` JSON (id, author, text, isDeleted for all 9)
   came back identical.
5. Registered **one** real test account directly against the live API,
   `day17-live-test@example.com` (password not reproduced here) —
   `201`, then confirmed a repeat registration of the same email correctly
   returns `409`, then logged in with it (`200`, real token) and confirmed
   the CORS preflight from the real Static Web App origin still returns the
   expected `access-control-allow-origin`/`-methods` headers.
6. Deployed the updated frontend build to the existing Static Web App
   (`swa deploy dist/task-1/browser --env production`) — same resource, same
   URL, no new Static Web App created. Confirmed the live `index.html`
   references the exact bundle hash produced by the local production build,
   and that `/signup` resolves via the SPA fallback.
7. Live search, via `curl` against the production URL: `search=Ada` → 1
   result (Ada Lovelace); `search=pioneers` → 1 result (Margaret Hamilton's
   quote, matched on text, not author).

No quote data, user account, or endpoint was left in a broken or
inconsistent state by this sequence — the final `GET /api/quotes` against
production shows all 9 original quotes, unchanged, plus the code fix live.

## 13. Screenshots (Sign Up + Search)

Captured with a real headless Chrome (`google-chrome-stable` via
`pyppeteer`, `--no-sandbox`) driving actual clicks/typing against the live
Static Web App — the same style of evidence as the original 6 screenshots in
§4, using the one real test account from §12 (no second account created,
per this work's own constraint of exactly one test user):

| # | File | What it shows |
|---|---|---|
| 7 | `07-signup-page.png` | `/signup`, nav now shows both "Log in" and "Sign up" while logged out |
| 8 | `08-signup-duplicate-email-error.png` | Submitting the already-registered test email → real `409` rendered as "An account with this email address already exists." |
| 9 | `09-protected-route-redirect-to-login.png` | Logged out, clicking "Create" → `authGuard` redirects to `/login` (returnUrl preserved) |
| 10 | `10-login-success-new-user.png` | Logging in with the real signed-up account lands back on `/create` — "Add a Quote" form visible, nav shows "Log out" |
| 11 | `11-logout-new-user.png` | After "Log out" — nav flips back to "Log in"/"Sign up" |
| 12 | `12-protected-route-blocked-after-logout.png` | `/create` redirects to `/login` again — identical to #9, confirming logout actually cleared the token |
| 13 | `13-explore-search-author.png` | Searching "Ada Lovelace" → the one matching quote, by author |
| 14 | `14-explore-search-quote-text.png` | Searching "pioneers" → the one matching quote, by quote text (not author) |
| 15 | `15-explore-search-no-results.png` | Searching a nonsense term → the real "No quotes found." empty state |
