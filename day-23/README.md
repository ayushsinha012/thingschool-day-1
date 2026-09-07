# Day 23 — Bicep IaC

## Goal

Infrastructure as Code for the QuotesApi Azure infrastructure: parameterized Bicep modules for the API, SQL, and Service Bus, with separate dev/prod parameter files and no portal click-ops.

## Architecture

```
main.bicep (resource-group scope, deploys into the existing thinkschool-rg)
    |
    +-- modules/api.bicep         (Container App: quotes-api / quotes-api-dev)
    |
    +-- modules/sql.bicep         (new, dedicated SQL Server + Database - see "SQL discrepancy" below)
    |
    +-- modules/servicebus.bicep  (namespace + topic quote-events + subscriptions sub-audit/sub-notifications)
```

`main.bicep` does not create the resource group, the Container Apps environment, the container registry, or the managed identity - all four already exist in `thinkschool-rg` and are referenced as `existing` from `modules/api.bicep`, exactly as the original `azd`-generated template (`day-1/QuotesApi/infra/{main,resources}.bicep`) already does.

## Files

| File | Purpose |
|---|---|
| `infra/main.bicep` | Composes the three modules, resource-group scoped, no hardcoded environment-specific values |
| `infra/modules/api.bicep` | Parameterized Container App - image, CPU/memory, scale, ingress, secrets, optional Redis/App Insights/CORS/outbox-volume wiring |
| `infra/modules/sql.bicep` | New Azure SQL Server + Database, Entra-ID-only auth (no SQL password ever exists) |
| `infra/modules/servicebus.bicep` | Namespace (optionally created) + topic + subscriptions, works whether the namespace is new or already exists |
| `infra/params/dev.bicepparam` | Dev environment values |
| `infra/params/prod.bicepparam` | Prod environment values (what-if only - see below) |
| `infra/evidence/` | Real command output from validation, what-if, and resource inspection |

## Parameters

**dev** (`params/dev.bicepparam`): a separate, brand-new Container App (`quotes-api-dev`, 0.25 CPU/0.5Gi, 0-1 replicas) reusing the existing shared environment/registry/identity; a new SQL Server+Database (`sql-quotesapi-dev`, Basic); a new Service Bus namespace (`sb-quotesapi-dev`, Standard) with its own `quote-events` topic and `sub-audit`/`sub-notifications` subscriptions. Nothing in dev touches a real production resource.

**prod** (`params/prod.bicepparam`): models the *real* `quotes-api` Container App as closely as this exercise's scope allows (same image, CPU/memory, replica range, `allowInsecure=true`, the real CORS origin, the real outbox volume mount); `deploySql = false` (see below); Service Bus points at the real `sb-quotesapi-thinkschool` namespace/topic/subscriptions with `createNamespace = false`, values matched exactly to the live configuration (including the ARM max-duration sentinel `P10675199DT2H48M5.4775807S` Service Bus reports for a TTL that was never explicitly set).

Secrets (`jwtSigningKey`, `redisConnectionString`, `appInsightsConnectionString`) are never written to either param file - both use `readEnvironmentVariable(...)`, so a value has to be exported in the deployer's shell (`DEV_JWT_SIGNING_KEY` / `PROD_JWT_SIGNING_KEY`, etc.) before `az deployment group create`/`what-if` will run. `jwtSigningKey` has no fallback default, so a missing env var fails the deployment loudly rather than silently deploying with a placeholder.

## Azure resources modeled

Confirmed via `az resource list --resource-group thinkschool-rg` and per-resource `az` inspection before writing any Bicep (see `infra/evidence/resource-verification.txt`):

- **Container App** `quotes-api` - env `thinkschool-env` (Consumption), image `cr2i2oapij4zsrc.azurecr.io/quotes-api/quotes-api-quotesapi-thinkschool:day21-hybridcache-1788429669`, identity `id-quotesApi-2i2oapij4zsrc`, ACR `cr2i2oapij4zsrc`
- **Service Bus** `sb-quotesapi-thinkschool` (Standard, zone-redundant) - topic `quote-events`, subscriptions `sub-audit`, `sub-notifications` (maxDeliveryCount 3, lockDuration PT1M, dead-lettering on message expiration)
- **Azure SQL Server** `thinkschool-day7-sql-0c0dda` - exists in the resource group, but is an **unrelated Day-7 exercise resource**, not part of QuotesApi's data path (see below)
- Also present, referenced as `existing` where the API module needs them, otherwise untouched: `thinkschool-env`, `cr2i2oapij4zsrc`, `id-quotesApi-2i2oapij4zsrc`, `redis-quotesapi-thinkschool`, `stday20quotesapi` (outbox Azure Files share)

### SQL discrepancy

The Academy exercise asks for an API/SQL/Service Bus module set. The deployed QuotesApi Container App's `ConnectionStrings__DefaultConnection` is `Data Source=/tmp/quotes.db` - **SQLite**, confirmed via `az containerapp show`, not Azure SQL. `thinkschool-day7-sql-0c0dda` does exist in `thinkschool-rg`, but its name, tags, and admin history all point to a different (Day 7) exercise; QuotesApi never connects to it.

`modules/sql.bicep` therefore models a **new**, separately-named SQL Server + Database dedicated to this exercise - what a SQL-backed QuotesApi persistence layer would look like - using Entra-ID-only authentication so no admin password parameter exists anywhere in this IaC. It is never wired into the running application, and it never references or modifies `thinkschool-day7-sql-0c0dda`. `prod.bicepparam` sets `deploySql = false`: standing up a prod SQL server that nothing in production would ever query serves no purpose and was avoided.

## What-if

```
az deployment group what-if --resource-group thinkschool-rg --template-file infra/main.bicep --parameters infra/params/dev.bicepparam
az deployment group what-if --resource-group thinkschool-rg --template-file infra/main.bicep --parameters infra/params/prod.bicepparam
```

**Dev** (`infra/evidence/dev-what-if.txt`): `Resource changes: 8 to create, 18 to ignore.` - creates `quotes-api-dev`, `sb-quotesapi-dev` + topic + 2 subscriptions, `sql-quotesapi-dev` + database + firewall rule. Nothing existing is touched.

**Prod** (`infra/evidence/prod-what-if.txt`): `Resource changes: 4 to modify, 17 to ignore.` - no creates, no deletes. The 4 "modify" entries are on `quotes-api` and the real `sb-quotesapi-thinkschool` topic/subscriptions; every diff is either a template-expression-vs-resolved-value artifact (e.g. the ACR login server shown as a literal string in the live resource vs. a `reference(...)` expression in the template - same value) or an ARM-computed/default property this exercise's Bicep doesn't set (`clientCertificateMode`, `stickySessions`, `traffic`, `maxInactiveRevisions`, `runningStatus`, the `azd-*` tags, and several Service Bus defaults like `autoDeleteOnIdle`/`enableBatchedOperations`/`maxSizeInMegabytes`). Nothing is deleted.

## Deployment

Neither environment was actually deployed. Dev's what-if is clean and safe to deploy, but doing so would create real billable resources (~$5/mo SQL Basic + ~$10/mo Service Bus Standard) on an Azure for Students subscription - asked explicitly, and the decision was to stop at what-if rather than spend the credit. Prod was never going to be deployed regardless - see below.

## Verification

`infra/evidence/resource-verification.txt` captures the resource group, full resource list, the real Service Bus topic/subscription configuration, a summary of the real `quotes-api` Container App, and confirmation of the unrelated Day-7 SQL server, all via direct `az` queries against the live subscription (not assumed from documentation).

## Screenshots

No browser/screenshot capability was available in this session, so none were fabricated. `infra/evidence/SCREENSHOTS.md` lists exactly which screenshots to capture manually and the command/portal location for each; all of the underlying output they'd show is already saved as text in `infra/evidence/`.

## Important safety notes

- No secret, password, connection string, or access key is committed anywhere in this IaC - `jwtSigningKey`/`redisConnectionString`/`appInsightsConnectionString` are read from environment variables at deploy time, and the SQL module uses Entra-ID-only auth (no SQL password exists to leak).
- `thinkschool-day7-sql-0c0dda` (an unrelated Day-7 resource) was never modeled, referenced, renamed, or modified.
- Prod `what-if` was run and is clean (no creates/deletes), but a real prod deployment was deliberately **not** performed: this exercise's Bicep doesn't model every property of the live `quotes-api` Container App (dapr config, exact revision/traffic state, the `azd-*` tracking tags), so an actual `az deployment group create` against prod would reset those to defaults - a real, if minor, side effect that isn't worth risking for an IaC exercise. What-if is the safe, complete demonstration for prod.
