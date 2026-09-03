# Day 21 — Azure Infra Notes (raw, not yet finalized)

## Reused (not created)

- Resource group: `thinkschool-rg` (centralindia)
- Container Apps environment: `thinkschool-env`
- Container App: `quotes-api`
- Container registry: `cr2i2oapij4zsrc`
- Managed identity: `id-quotesApi-2i2oapij4zsrc`

## Created (minimum required — no existing Redis)

`az redis create --name redis-quotesapi-thinkschool --resource-group thinkschool-rg --location centralindia --sku Basic --vm-size c0 --minimum-tls-version 1.2`

Basic C0 tier — smallest available SKU, matches "create only the minimum
Redis service required." No existing Azure Cache for Redis was found in
`thinkschool-rg` (`az redis list` returned empty) before this.
`Microsoft.Cache` resource provider had to be registered on the
subscription first (`az provider register --namespace Microsoft.Cache`) —
first Redis resource ever created on this subscription.

## Wiring

Canonical bicep (`day-1/QuotesApi/infra/{main,resources}.bicep`) updated to
add an optional `redisConnectionString` secure parameter, a
`redis-connection-string` Container App secret, and
`ConnectionStrings__Redis` env var bound to that secret — the same pattern
already used for `jwt-signing-key`/`Jwt__Key`. Defaults to empty (no live
effect) so existing deployments/parameter files that don't supply it are
unaffected.

The live container app secret/env var were set directly via `az
containerapp secret set` / `az containerapp update` (surgical, matching how
Day 19/20 evolved production without a full `azd up`/bicep redeploy each
time) — see result.md for the exact commands and their output once run.

Access-key auth (connection string with password over TLS), not Entra ID
token auth — Azure Cache for Redis Basic/Standard's Entra auth story adds
real complexity (token refresh plumbing) for a Day 21 exercise; the
container app secret is exactly the "secure Azure configuration" the task
brief asks for, and never lands in source control.

## Image

Built via `az acr build` (cloud build — this machine's Dockerfile itself
notes local `dotnet publish -p:PublishContainer` doesn't fit in available
memory), pushed to the same repo/tag convention as Day 17-20:
`quotes-api/quotes-api-quotesapi-thinkschool:day21-hybridcache-<unix-ts>`.
