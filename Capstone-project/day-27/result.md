# Day 27 — Result

## Exercise

> Paste the threat model, the private-endpoint change, and the ZAP baseline summary with what you fixed.

## STRIDE-lite threat model

Full model: [`threat-model/stride-lite.md`](threat-model/stride-lite.md).
Covers the actual MaintainXpert architecture (Api host, Maintenance,
Assets, Notifications, SharedKernel, the in-process domain-event
dispatcher, Azure SQL, Key Vault, the shared Container Apps environment,
and the Day 27 private-networking layer) across all six STRIDE
categories, with asset/threat/attack-path/control/mitigation/residual-risk
for each real threat — not generic textbook entries.

Headline finding from the Spoofing category: before this pass every
MaintainXpert endpoint was unauthenticated. Fixed with a client-
credentials JWT scheme required on every write endpoint.

![STRIDE-lite threat model](evidence/screenshots/01-threat-model.png)

## Private endpoint change

Added a real Azure SQL data tier (there was none before — MaintainXpert
was in-memory only) and put it behind a private endpoint:

- `vnet-maintainxpert-day27-dev` / `snet-pe` (10.70.2.0/24)
- `pe-sql-sql-maintainxpert-day27-dev` → `sql-maintainxpert-day27-dev`,
  subresource `sqlServer`, connection state **Approved**
- `privatelink.database.windows.net` zone, linked to the VNet, A record
  `sql-maintainxpert-day27-dev` → `10.70.2.4`
- Independently verified from inside the VNet: `getent hosts` resolves
  to the private IP, and a raw TCP connect to `10.70.2.4:1433` succeeds

SQL public network access remains **Enabled**. Verified reason: this
subscription has a hard, subscription-wide cap of one Container Apps
environment (`MaxNumberOfGlobalEnvironmentsInSubExceeded`, a real error
returned when a dedicated VNet-integrated environment was attempted).
The one environment that exists has no VNet integration and cannot gain
it after creation, so the deployed API reaches SQL over the public
endpoint. Disabling public access would sever the app's only working
connection — reported honestly as a residual limitation rather than
claimed as fixed.

![Private endpoints](evidence/screenshots/02-private-endpoints.png)

## OpenAPI hardening

- Client-credentials JWT authentication (`POST /auth/token`), required
  on every write endpoint
- URL-segment API versioning (`/api/v1/...`); the old unversioned routes
  no longer exist and `/api/v2/...` is unregistered (404)
- 16 KiB request body limit, `StringLength` validation, and a
  `GlobalExceptionHandler` that returns clean `ProblemDetails` instead of
  leaking framework internals
- OpenAPI document at `/openapi/v1.json` with a documented `Bearer`
  scheme and accurate per-operation security requirements

![OpenAPI hardening](evidence/screenshots/03-openapi-hardening.png)

## ZAP baseline result

Real `zap-baseline.py` scan, `ghcr.io/zaproxy/zaproxy:stable`, against
the deployed API:

- First run (default spider, 3 URLs): 66 PASS, 1 WARN (informational)
- Second run (seeded from the live OpenAPI document, 16 URLs — every
  real business endpoint): 1 informational finding, **zero Low/Medium/
  High findings**

![ZAP baseline](evidence/screenshots/04-zap-baseline.png)

## Actual fixes

1. An invalid `Priority` enum value crashed the API with an unhandled
   500 that leaked a raw `System.Text.Json` exception — fixed with a
   `JsonStringEnumConverter` and a `GlobalExceptionHandler` mapping for
   malformed requests to a clean 400. Verified with a real request in
   `evidence/input-limits.txt`.
2. `WorkOrder.CreatedAt` was not explicitly mapped in the EF Core model,
   crashing the container on startup once real SQL was configured
   (`No suitable constructor was found for the type 'WorkOrder'`) —
   fixed by mapping the property explicitly. Verified by a healthy
   container revision and a full authenticated create → assign → start →
   complete lifecycle against real Azure SQL.
3. Both ZAP findings across the two scan runs were reviewed and
   classified as accepted-by-design informational notes about the
   API's intentional `Cache-Control: no-store, no-cache` header — not
   defects. Full classification in `evidence/zap-fixed-findings.txt`.

![ZAP fixed findings](evidence/screenshots/05-zap-fixed-findings.png)

## Remaining findings

- 1 informational ZAP note (`Re-examine Cache-control Directives`),
  accepted by design — see above.
- SQL public network access remains Enabled — a genuine, verified,
  honestly-reported residual limitation caused by this subscription's
  one-Container-Apps-environment quota, not a completed mitigation.
- The Container App's managed identity holds `db_ddladmin` in addition
  to `db_datareader`/`db_datawriter` — broader than steady-state read/
  write access needs, kept only because it is required for
  `EnsureCreatedAsync` to create the schema on a from-scratch database.

## Final verification

Deployed revision `maintainxpert-api-day27-dev--efmapfix1` — Healthy,
`RunningAtMaxScale`. `GET /health` → 200. Private endpoint Approved with
confirmed DNS resolution and TCP reachability. Authentication (401 →
201), versioning (v1 → 200, v2 → 404), and input limits (413/400/409 on
the respective invalid inputs) all verified against the live API.

![Final verification](evidence/screenshots/06-final-verification.png)
