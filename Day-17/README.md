# Day 17 — Cloud deployment: Static Web App, CORS, and Managed Identity

This day takes the Week-1 Quotes app (API in `day-1/QuotesApi`, Angular frontend
in `Day-16/task-2`) the rest of the way to a real, publicly reachable cloud
deployment: the frontend on Azure Static Web Apps, cross-origin access locked
down to that frontend's real origin (not a wildcard), and a second,
Managed-Identity-only server-side path (`Day-17/quotes-bff`) that proves an
Entra access token can be acquired without ever putting a secret in the
browser. `result.md` in this folder is the full write-up — what was
verified, the real bug hit and fixed along the way, and what's still open.

## Live resources

| Resource | Value |
|---|---|
| Frontend (Static Web App) | https://polite-mushroom-04dd5ce00.7.azurestaticapps.net |
| QuotesApi (Container App) | https://quotes-api.politeocean-3efec37e.centralindia.azurecontainerapps.io |
| QuotesBff (Container App) | https://quotes-bff.politeocean-3efec37e.centralindia.azurecontainerapps.io |
| Resource group | `thinkschool-rg` (Central India) |

## What's here

- **`quotes-bff/`** — the Managed-Identity reverse proxy in front of QuotesApi.
  See `quotes-bff/README.md` for how it works and what Entra app registration
  it depends on.
- **`docs/screenshots/`** — real screenshots captured against the live app
  (see `result.md` §4 for what each one shows).
- **`result.md`** — the detailed write-up: verification log, Lighthouse
  result, the real bug/fix, and API-change risks.

## Architecture as actually deployed (not as originally sketched)

The frontend's production build (`Day-16/task-2/src/environments/environment.prod.ts`)
calls **QuotesApi directly** — it does not route through `quotes-bff`.
`quotes-bff` is deployed and independently verified (it acquires a real
Managed Identity token and successfully calls QuotesApi end to end — see
`result.md` §3), but nothing in the browser-facing path uses it today. This
is the honest current state, not the originally intended end state; wiring
the frontend to call the BFF instead of the API directly is listed as
follow-up work in `result.md` rather than done silently.

## Sign Up + Explore Search (follow-up work)

Two follow-up fixes on top of the original Day 17 deployment, both live at
the URLs above:

1. **Real sign-up.** `POST /api/auth/register` (new, `AuthController`) lets a
   new user create an account against the existing `Users` table — same
   BCrypt hashing, same `AppDbContext`, no second user store, no migration.
   A new `/signup` route in the frontend reuses the existing login styling.
   See `result.md` §9.
2. **Explore search fix.** `GET /api/quotes?...&search=` was already being
   sent by the frontend but silently ignored by the backend — every search
   returned the full, unfiltered page. Fixed in `QuoteRepository`/`QuoteEndpoints`
   to filter on both author and quote text, case-insensitively. See
   `result.md` §10–11 for the bug and fix, §12 for verification.

Both were verified against the live production API and Static Web App —
see `result.md` §12–13 for the full log and new screenshots.

## Known open items

See `result.md` §7 ("API-change risks" and "Remaining work") for the full
list — the two worth knowing up front:

1. **QuotesApi has no persistent storage.** Its connection string is
   `Data Source=/tmp/quotes.db` — ephemeral container-local disk, no mounted
   volume. Every new container revision starts from an empty database. This
   was hit directly during this work (see `result.md` §5), and again when
   deploying the search fix (see `result.md` §12) — recovered the same way
   both times, by re-`POST`ing the real quotes through the actual API.
2. **The `Api.Invoke` Entra app-role grant for `quotes-bff`'s Managed Identity
   is not yet in place** — blocked by `Authorization_RequestDenied` on the
   current signed-in account (a student account with no directory admin
   role). Not required for today's anonymous-read path, but needed before any
   protected call is routed through the BFF.
