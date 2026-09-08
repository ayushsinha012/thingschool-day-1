# Day 24 — Result

## Task

> Deploy the full stack with Azure Deployment Stacks (so teardown is clean
> and drift is detectable) driven by the `azd` CLI. Deploy to dev, then
> promote to prod.

## Exercise

> Paste the azd config + the deploy output for both environments. One line
> on what Deployment Stacks give you over plain deployments.

**azd configuration**

```
$ azd config set alpha.deployment.stacks on

$ azd config show
{
  "alpha": {
    "deployment": {
      "stacks": "on"
    }
  },
  "extension": {
    "sources": {
      "azd": {
        "location": "https://aka.ms/azd/extensions/registry",
        "name": "azd",
        "type": "url"
      }
    }
  }
}
```

**Dev deployment output**

```
$ azd up --environment dev
...
  (✓) Done: Service Bus Namespace: sb-quotesapi-day24-dev (18.584s)
  (✓) Done: Container App: quotes-api-day24-dev (16.817s)
  (✓) Done: Azure SQL Server: sql-quotesapi-day24-dev (1m10.263s)

SUCCESS: Your application was provisioned and deployed to Azure in 4 minutes 15 seconds.
```

**Prod deployment output**

```
$ azd up --environment prod
...
  (✓) Done: Service Bus Namespace: sb-quotesapi-day24-prod (18.727s)
  (✓) Done: Container App: quotes-api-day24-prod (17.013s)
  (✓) Done: Azure SQL Server: sql-quotesapi-day24-prod (1m12.014s)

SUCCESS: Your application was provisioned and deployed to Azure in 4 minutes 14 seconds.
```

**Deployment Stack benefit:** a stack tracks its managed resources as one auditable unit and denies out-of-band deletion of any of them (`denyDelete`), while a plain `az deployment group create` leaves every resource independently deletable with no record of which resources belong together.

![azd configuration](evidence/screenshots/01-azd-config.png)

![Dev deployment](evidence/screenshots/02-dev-deploy.png)

![Dev Deployment Stack](evidence/screenshots/03-dev-stack.png)

![Prod deployment](evidence/screenshots/04-prod-deploy.png)

![Prod Deployment Stack](evidence/screenshots/05-prod-stack.png)

![Final verification](evidence/screenshots/06-final-verification.png)

## What was implemented

- `day-24/infra/main.bicep` (resource-group scope, targets the existing `thinkschool-rg`) composing three modules carried forward from Day 23's design: `modules/api.bicep` (Container App), `modules/sql.bicep` (Azure SQL Server + Database, Entra-ID-only auth), `modules/servicebus.bicep` (namespace + topic `quote-events` + subscriptions `sub-audit`/`sub-notifications`).
- Resource names are environment-scoped and isolated from every real production resource: `quotes-api-day24-{dev,prod}`, `sql-quotesapi-day24-{dev,prod}`, `sb-quotesapi-day24-{dev,prod}` — none of these collide with the real `quotes-api`, `sb-quotesapi-thinkschool`, or `thinkschool-day7-sql-0c0dda`.
- `day-24/azure.yaml` configures the azd project with `infra.deploymentStacks`: `actionOnUnmanage.resources: delete` / `resourceGroups: detach`, `denySettings.mode: denyDelete`.
- Two azd environments, `dev` and `prod`, each with `AZURE_RESOURCE_GROUP=thinkschool-rg` and a freshly generated `JWT_SIGNING_KEY` stored only in the local (gitignored) `.azure/<env>/.env` file — no secret is committed to any Day 24 source file.
- `azd config set alpha.deployment.stacks on` enabled and verified.
- `azd up --environment dev` and, after an explicit safety what-if and user confirmation, `azd up --environment prod` — both real deployments against the live Azure subscription `708f56eb-d40f-4658-adde-d6f5866dad34`, region `centralindia`.

## Verification

- **Safety check (before deploying):** `az resource list --resource-group thinkschool-rg` captured the 18 pre-existing resources; `az deployment group what-if` against both dev and prod parameters reported `Resource changes: 8 to create, 18 to ignore` for each — zero modifies, zero deletes of anything pre-existing.
- **Dev:** `azd up --environment dev` succeeded in 4m15s. `az stack group list --resource-group thinkschool-rg` shows `azd-stack-dev` with state `succeeded`. `az stack group show` confirms all 8 resources as `"status": "managed"` with `"denyStatus": "denyDelete"`. `az resource list --tag environment=dev` confirms the 4 real resources (Container App, SQL server, SQL database, Service Bus namespace) exist in `thinkschool-rg`.
- **Prod:** `azd up --environment prod` succeeded in 4m14s. `az stack group list` shows `azd-stack-prod` with state `succeeded`, tracking its own 8 resources independently of `azd-stack-dev`, each with `denyDelete`. `az resource list --tag environment=prod` confirms the 4 real prod-tagged resources exist.
- **Drift/stack check:** a live `az deployment group what-if` re-run against the deployed dev template (`evidence/dev-drift-check.txt`) shows the real, ARM-resolved property values now differing from the template's unresolved expressions (e.g. the ACR login server, the managed identity client id, and Service Bus/Container App ARM-computed defaults) — proof the stack is comparing against genuine live state, not just the last deployment record. An out-of-band `az servicebus namespace delete` attempt against a stack-managed resource was intentionally not executed (blocked as too destructive to attempt even as a test) — the `denyDelete` status already visible on every managed resource in `az stack group show` is the real, active evidence that such a deletion would be denied.
- **After both deployments:** `az resource list --resource-group thinkschool-rg` diffed against the pre-deployment inventory shows only the 10 new Day 24 resources added; all 18 original resources remain unchanged.
- **Bicep validation:** `az bicep build --file infra/main.bicep` and `az bicep lint --file infra/main.bicep` both clean, no errors.
- Full command transcripts: `evidence/azd-config.txt`, `evidence/dev-deploy.txt`, `evidence/prod-deploy.txt`, `evidence/dev-verification.txt`, `evidence/prod-verification.txt`, `evidence/deployment-stack-dev.txt`, `evidence/deployment-stack-prod.txt`, `evidence/dev-drift-check.txt`, `evidence/what-if-dev-safety-check.txt`, `evidence/what-if-prod-safety-check.txt`, `evidence/resource-inventory-before.txt`, `evidence/resource-inventory-after.txt`, `evidence/final-verification.txt`.
