# MaintainXpert — STRIDE-lite threat model (Day 27)

Scope: the actual MaintainXpert architecture as it exists in this
repository — a modular monolith (`MaintainXpert.Api` host plus the
`Maintenance`, `Assets`, `Notifications`, `SharedKernel` modules), its
domain model (`WorkOrder`, `Asset`), the in-process domain-event
dispatcher, and (new for Day 27) real JWT authentication, a real Azure
SQL data tier, and private networking. Components that do not exist in
this codebase (a message broker, a real notification channel, a user
directory) are not modeled.

Components covered: Client/integration caller, `MaintainXpert.Api`,
the client-credentials JWT issuer (`/auth/token`), the `Maintenance` and
`Assets` modules, the `Notifications` module (console sink), the
in-process `InProcessDomainEventDispatcher`, Azure SQL (`maintainxpert`
database), Key Vault, the Container Apps environment, and the Day 27 VNet
/ private endpoint / private DNS layer.

## Spoofing

| Asset/component | Threat | Attack path | Existing control | Day 27 mitigation | Residual risk |
|---|---|---|---|---|---|
| Write endpoints (`POST /api/v1/work-orders`, `/assign`, `/start`, `/complete`, `POST /api/v1/assets`) | Caller impersonates an authorized integration client | Forged or replayed bearer token presented to a write endpoint | None before Day 27 — every endpoint was unauthenticated | Added a client-credentials JWT scheme (`POST /auth/token` validates a client id/secret pair, issues an HMAC-signed, 15-minute bearer token) and required it on every write endpoint via the `workorders.write` policy. Verified (evidence/authentication.txt): unauthenticated write → 401, wrong client secret → 401, valid credentials → 200 token issuance → 201/200 on the protected endpoints | The shared secret is a single client credential (no per-caller identity yet) — acceptable for this stage's one integration client, but does not yet support revoking one caller without rotating the secret for all of them |
| `/auth/token` | Credential stuffing against the client secret | Repeated `POST /auth/token` guesses | None | Not added in this pass — flagged as a follow-up (see README); the secret is compared with `CryptographicOperations.FixedTimeEquals`, which prevents timing side-channels but does not rate-limit guesses | Unbounded guess attempts against the shared secret; accepted risk for this pass, documented rather than silently left out |
| `MaintainXpert.Api` → Azure SQL | A workload other than the intended API impersonates it against the database | Compromised credential used to connect to the `maintainxpert` database | N/A - no SQL server existed before Day 27 | The new SQL server is `azureADOnlyAuthentication: true` — there is no SQL login/password to steal in the first place; the Container App's system-assigned managed identity is the only way to authenticate, via `Authentication=Active Directory Default` | Anyone with a valid Entra identity mapped as a database user AND network access to reach SQL could still connect — the intended trust boundary, not a residual gap |

## Tampering

| Asset/component | Threat | Attack path | Existing control | Day 27 mitigation | Residual risk |
|---|---|---|---|---|---|
| `CreateWorkOrderRequest`/`RegisterAssetRequest` body | Oversized or malformed payload used to corrupt state or exhaust memory | Large/invalid JSON posted to a write endpoint | None before Day 27 — `Description`/`Name` had no length limit, and an invalid `Priority` enum value crashed with a 500 (leaking a stack trace path in the exception message) | Added a global Kestrel `MaxRequestBodySize` (16 KiB — a 20 KB body is rejected with 413), `StringLength` validation on `Description`/`Name`, and a `GlobalExceptionHandler` that maps `BadHttpRequestException` (malformed JSON, including an invalid enum value) to a clean 400 instead of a 500. Verified in evidence/input-limits.txt | A single request just under 16 KiB with maximum-length fields still succeeds — a deliberate, documented ceiling |
| Work order lifecycle (`Status`) | Forcing an invalid state transition to corrupt the aggregate | `POST /start` before assignment, `POST /complete` without a technician, reassigning a completed order | The `WorkOrder` aggregate itself already rejects every invalid transition (`AssignTechnician`/`Start`/`Complete` throw `InvalidWorkOrderTransitionException`) — this was true before Day 27 | Day 27 added a `GlobalExceptionHandler` mapping that exception to a clean `409 Conflict` with the aggregate's own message, instead of an unhandled 500 with a stack trace. Verified in evidence/input-limits.txt | None known beyond the documented mapping |
| Data in transit to SQL | Tampering with query traffic between the API and the database | Network path between the Container App and SQL | N/A - no SQL server existed before Day 27 | `Encrypt=True; TrustServerCertificate=False` in the SQL connection string (TLS to SQL); the Day 27 private endpoint additionally keeps that traffic off the public internet for the private path | None known beyond the existing TLS control |

## Repudiation

| Asset/component | Threat | Attack path | Existing control | Day 27 mitigation | Residual risk |
|---|---|---|---|---|---|
| Work order create/assign/start/complete | A caller denies having performed a write | No caller identity tied to a specific action | None — the client-credentials scheme issues one identity (`maintainxpert-integration`) shared by every caller of that client | Every issued token still carries a `NameIdentifier` claim, and `AuthEndpoints`/domain-event handlers log the action; but with a single shared client id this does not yet distinguish *which* caller performed a given write | Accepted for this stage: proper repudiation would need per-caller identities, which is a bigger change than this security pass; documented as a follow-up, not silently omitted |
| Notification delivery | A recipient denies having been notified | `ConsoleNotificationSink` is a stand-in, not a real channel | None — this is Day 22 scaffolding, unchanged by Day 27 | Not in scope for this pass (no real notification channel exists yet to make repudiation-resistant) | Same as before Day 27 |

## Information Disclosure

| Asset/component | Threat | Attack path | Existing control | Day 27 mitigation | Residual risk |
|---|---|---|---|---|---|
| Azure SQL (`maintainxpert`) | Database reachable directly from the public internet | Any client connecting to `sql-maintainxpert-day27-dev.database.windows.net:1433` | N/A - no SQL server existed before Day 27 | Added a private endpoint (`pe-sql-sql-maintainxpert-day27-dev`) in a dedicated subnet, a `privatelink.database.windows.net` private DNS zone linked to the VNet, and a server-level firewall limited to `AllowAzureServices` — see evidence/private-endpoints.txt, evidence/private-dns.txt | Public network access remains **Enabled** (see evidence/architecture.txt for why) — an attacker who compromises a workload already inside Azure could still reach the public endpoint; this is documented as a residual, honestly reported limitation, not a completed mitigation |
| Error responses | Stack traces or internal exception detail returned to a caller | Any request that throws (including the invalid-enum 500 found during this pass) | None before Day 27 | `GlobalExceptionHandler` now returns a generic `ProblemDetails` (title/status/detail only, no exception type or stack trace) for every exception type, verified against the actual invalid-enum case that used to leak a 500 with framework internals | None new |
| OpenAPI document | Internal implementation detail or secret leak via generated schema | `GET /openapi/v1.json` | N/A — no OpenAPI document existed before Day 27 | The new document was reviewed by hand: it exposes route shapes, parameter constraints, and the Bearer security scheme, but no connection string, key, or infrastructure identifier | Any future DTO must avoid adding secret-shaped fields — a process control, not a technical one |

## Denial of Service

| Asset/component | Threat | Attack path | Existing control | Day 27 mitigation | Residual risk |
|---|---|---|---|---|---|
| Any JSON body endpoint | Large-body resource exhaustion | A multi-megabyte request body | Kestrel's own 30 MB default `MaxRequestBodySize` | Lowered to 16 KiB globally for this API's actual payload shapes — verified in evidence/input-limits.txt | A sustained flood of small, within-limit requests is still a capacity question, not addressed by a body-size limit |
| `/auth/token` | Flooding to exhaust token-issuance capacity | Repeated requests from one caller | None | Not added in this pass (see Spoofing row above) — documented as a real, un-fixed gap rather than silently left out | Accepted risk, flagged for a follow-up pass |
| Container App | Compute exhaustion from a traffic spike | Sudden burst of requests | `apiMaxReplicas` cap (1 for Day 27, matching the cost-conscious training subscription) | Unchanged pattern from the rest of this repository's Azure days | A genuinely large spike queues/throttles rather than autoscaling past the configured ceiling — a deliberate cost/availability tradeoff for a training subscription |

## Elevation of Privilege

| Asset/component | Threat | Attack path | Existing control | Day 27 mitigation | Residual risk |
|---|---|---|---|---|---|
| Write endpoints | A caller with a valid token performs a write it shouldn't be able to | Authenticated request from a caller that should only read | None before Day 27 (everything was unauthenticated, so this question didn't yet apply) | `RequireAuthorization("workorders.write")` on every write endpoint, backed by a `scope` claim the token service always sets to `workorders.write` for the one integration client this pass defines | There is currently only one client/scope — no read-only vs. write-capable caller distinction exists yet; documented as a follow-up rather than fixed here, since the exercise's one integration client genuinely needs write access |
| Container App managed identity | The API's identity is granted more than it needs | Compromised container process pivots to other Azure resources | N/A — no managed identity existed before Day 27 | System-assigned identity scoped to exactly: `AcrPull` on the shared ACR, `Key Vault Secrets User` on its own vault, plus (post-deployment) an AAD database user mapped to `db_datareader`/`db_datawriter`/`db_ddladmin` (the last one needed only because this is a fresh database with no migrations yet — see evidence/deployment.txt) — no `Owner`/`Contributor` grant anywhere | The `db_ddladmin` grant is broader than strictly necessary for steady-state (read/write) operation; documented here rather than silently minimized, since removing it would break `EnsureCreatedAsync` on a from-scratch database |
| SQL AAD admin | Someone other than the intended administrator manages the server | Compromise of the admin's Entra account | N/A — no SQL server existed before Day 27 | `azureADOnlyAuthentication: true`, single named AAD admin (a human), no SQL login exists at all | Standard Entra account-compromise risk, outside this API's control |
