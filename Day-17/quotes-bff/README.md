# QuotesBff

A minimal reverse proxy in front of the real Week-1 QuotesApi
(`day-1/QuotesApi`, deployed at
`https://quotes-api.politeocean-3efec37e.centralindia.azurecontainerapps.io`).

It exists for exactly one reason: **Managed Identity token acquisition must
happen on an Azure-hosted server-side component, never in the browser.**
QuotesApi already validates Entra ID (Azure AD) access tokens via its
existing dual-JWT scheme
(`day-1/QuotesApi/Authentication/JwtAuthenticationExtensions.cs`) - this
service is the "Azure-hosted server-side component" that acquires one and
forwards it, so QuotesApi's existing validation logic is reused unchanged.

## What it does (and doesn't do)

`Program.cs` is a pure pass-through: every method, path, query string, and
body it receives is forwarded to QuotesApi unchanged. No quote or auth
business logic is duplicated here - that stays exactly where it already
lives (`QuoteEndpoints.cs`, `AuthController.cs`). The only decision this
proxy makes is which `Authorization` header rides along:

- A request that already carries one (the browser's own bearer token from a
  real `/api/auth/login` - see `Day-16/task-2/src/app/auth.service.ts`) is
  forwarded as-is. That token already carries whatever claims QuotesApi's
  existing authorization policies check (e.g.
  `PermissionClaims.CanEditQuotes` for `POST /api/quotes`) - forwarding it
  unchanged preserves that authorization exactly as it already works today,
  with **no change to QuotesApi's authorization code**.
- A request with no `Authorization` header at all (anonymous reads) gets
  this service's own Managed-Identity-acquired Entra token instead, so
  QuotesApi's Entra scheme has a real, validated token to see end-to-end
  even on endpoints that don't themselves require one.

It never stores a client secret, certificate, password, or any other
credential. `DefaultAzureCredential` resolves to the Container App's
system-assigned Managed Identity in Azure and, locally, to the developer's
own `az login` session - either way, a token is requested fresh from Azure
AD for each call that needs one.

## The Entra app registration this depends on

Discovered already provisioned in this exercise's own Azure AD tenant
(queried via `az ad app show` / `az ad sp show`, not guessed - see Day 17
Part 3's inspection):

| Value | |
|---|---|
| Tenant ID | `8d46a076-d093-416d-a57b-8692cde13bf8` |
| App registration | `quotes-api-day17` (`729a2be3-9609-4fd1-b7c5-e658386f9bfd`) |
| App ID URI / Audience | `api://729a2be3-9609-4fd1-b7c5-e658386f9bfd` |
| Application role | `Api.Invoke` (`428f70d1-ea0c-44d8-914a-9234db5dae42`) - "Allows a service to call the Quotes API on its own behalf" |
| API SP object ID | `43050566-7eed-4220-a1b7-8b2533204239` (used by `infra/grant-api-invoke-role.sh`) |

QuotesApi's own `Entra` configuration
(`day-1/QuotesApi/appsettings.json`) has been filled in with the tenant and
audience above, so its existing Entra JWT scheme actually validates tokens
minted for this app registration - no code changes there, only
configuration.

A pre-existing reference identity named `quotes-bff` (a system-assigned
Managed Identity on a Container App in a separate, inaccessible subscription)
already holds the `Api.Invoke` role assignment - this project's naming and
architecture mirrors that on purpose.

## Deploying (not done yet - Day 17 Part 3 stops before this)

1. `azd provision` (or `azd up`) from this directory - creates the
   `quotes-bff` Container App in the existing `thinkschool-rg` /
   `thinkschool-env` (see `infra/resources.bicep`), with a system-assigned
   Managed Identity and AcrPull on the shared registry.
2. The `postprovision` hook (`infra/grant-api-invoke-role.sh`) then grants
   that identity the `Api.Invoke` role on `quotes-api-day17` via Microsoft
   Graph - ARM/Bicep has no native resource type for an Entra app role
   assignment, so this can't be done in `resources.bicep` itself.
3. Once the real Static Web App origin is known (a later part), add it to
   both this service's and QuotesApi's `Cors:ProductionOrigins` - neither
   uses a wildcard.
