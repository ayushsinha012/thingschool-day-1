# Day 25 — Identity end-to-end

## Exercise

> No connection-string secrets anywhere. Use Managed Identity for the API→SQL and API→Service Bus paths, Entra ID for app auth, and Key Vault references for any remaining config. Prove there are zero secrets in app settings.

> Paste the MI wiring + a Key Vault reference. Show the app settings have no plaintext secrets.

## What this is built on

The real production `quotes-api` persists to SQLite (`Data Source=/tmp/quotes.db`, confirmed via `az containerapp show`, matching Day 23's finding) and its Service Bus client already uses `DefaultAzureCredential` in code (`day-19/src/backend/Extensions/MessagingExtensions.cs`), but the deployed Container App only has RBAC for ACR pull, not for SQL or Service Bus data access. Its authentication (`day-1/QuotesApi/Authentication/JwtAuthenticationExtensions.cs`) already has a dual scheme: a local self-issued JWT plus a genuine `"Entra"` `JwtBearer` handler driven entirely by config (`Entra:TenantId`/`Entra:ClientId`/`Entra:Audience`) that was simply never configured. An existing Entra app registration for `QuotesApi` (`appId 953b5bcb-682b-47b4-a116-8936323f5bec`, tenant `8d46a076-d093-416d-a57b-8692cde13bf8`) already exists and was reused — no duplicate registration was created.

Day 25 does not modify any application source. It deploys the same container image into two new, isolated resource groups — `thinkschool-day25-dev` and `thinkschool-day25-prod` — with a dedicated system-assigned managed identity per environment, real Azure SQL + Service Bus + Key Vault, and the RBAC/config wiring that switches the existing code paths on.

## Managed Identity Wiring

`infra/modules/api.bicep` gives the Container App its own system-assigned identity (not the shared user-assigned identity the real `quotes-api` uses):

```bicep
resource apiApp 'Microsoft.App/containerApps@2024-03-01' = {
  ...
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    configuration: {
      registries: [
        { server: containerRegistry.properties.loginServer, identity: 'system' }
      ]
      secrets: [
        {
          name: jwtSigningKeySecretName
          keyVaultUrl: '${keyVault.properties.vaultUri}secrets/${jwtSigningKeySecretName}'
          identity: 'system'
        }
      ]
    }
  }
}
```

`infra/modules/identity.bicep` and `infra/modules/acrpull.bicep` grant that identity exactly the roles it needs — `AcrPull` (image pull, cross-resource-group scope onto the shared `cr2i2oapij4zsrc`), `Azure Service Bus Data Sender` + `Data Receiver` (least privilege: the app both publishes and consumes), and `Key Vault Secrets User` — never Owner/Contributor.

![Managed Identity wiring](evidence/screenshots/01-managed-identity.png)

## API to SQL

`modules/sql.bicep` deploys an Entra-ID-only Azure SQL Server (`azureADOnlyAuthentication: true` — SQL logins are disabled at the server level, not just unused). The Container App's identity was created as an external database user and granted `db_datareader`/`db_datawriter` via T-SQL, executed with an AAD access token (`Invoke-Sqlcmd -AccessToken`), never a SQL login:

```sql
CREATE USER [quotes-api-day25-dev] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [quotes-api-day25-dev];
ALTER ROLE db_datawriter ADD MEMBER [quotes-api-day25-dev];
```

The app setting `ConnectionStrings__AzureSql` is credential-free — `Authentication=Active Directory Default` — no `User Id`/`Password`. The currently-deployed image still hardcodes SQLite (`day-1/QuotesApi/Extensions/InfrastructureExtensions.cs`), so this connection string is not yet read by the running binary; the identity and its database permissions were verified directly instead (see `evidence/sql-identity.txt`).

![SQL Managed Identity](evidence/screenshots/02-sql-identity.png)

## API to Service Bus

This one *is* exercised live end-to-end, because the real code already uses `DefaultAzureCredential`. `sb-quotesapi-day25-dev`/`-prod` have `disableLocalAuth: true` (SAS/connection-string auth disabled at the namespace). A real `POST /api/messaging/publish` against the deployed dev app published a message purely via the system-assigned identity's Entra token, and the app's own `SubscriptionAWorker`/`SubscriptionBWorker` (also identity-based) received and processed it off both `sub-audit` and `sub-notifications` within milliseconds — confirmed via `GET /api/messaging/activity` and `GET /api/messaging/topology` (queue drained to 0). The same sequence was repeated against prod.

![Service Bus Managed Identity](evidence/screenshots/03-servicebus-identity.png)

## Key Vault Reference

The only value the app genuinely still needs that qualifies as a secret — the local-JWT-scheme signing key — lives in Key Vault (`kv-quotesapi-d25-dev`/`-prod`, RBAC-authorized, no access policies) and is wired in as a Container Apps Key Vault-referenced secret, not a plaintext value:

```
az containerapp secret list --name quotes-api-day25-dev --resource-group thinkschool-day25-dev
[
  {
    "identity": "system",
    "keyVaultUrl": "https://kv-quotesapi-d25-dev.vault.azure.net/secrets/jwt-signing-key",
    "name": "jwt-signing-key"
  }
]
```

The command returns the reference, never the value. The identity holds `Key Vault Secrets User` only.

![Key Vault reference](evidence/screenshots/04-keyvault-reference.png)

## App Settings

```
az containerapp show --name quotes-api-day25-dev --resource-group thinkschool-day25-dev --query "properties.template.containers[0].env"
[
  { "name": "PORT", "value": "8080" },
  { "name": "Jwt__Key", "secretRef": "jwt-signing-key" },
  { "name": "Entra__TenantId", "value": "8d46a076-d093-416d-a57b-8692cde13bf8" },
  { "name": "Entra__ClientId", "value": "953b5bcb-682b-47b4-a116-8936323f5bec" },
  { "name": "Entra__Audience", "value": "api://953b5bcb-682b-47b4-a116-8936323f5bec" },
  { "name": "ServiceBus__FullyQualifiedNamespace", "value": "sb-quotesapi-day25-dev.servicebus.windows.net" },
  { "name": "ConnectionStrings__AzureSql", "value": "Server=tcp:sql-quotesapi-day25-dev.database.windows.net,1433;Database=quotesapi;Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;" }
]
```

No SQL password, no Service Bus SAS key/connection string, no registry username/password (`az containerapp show --query properties.configuration.registries` shows empty `username`/`passwordSecretRef`, pull via `identity: system`). `Entra__*` are public tenant/app identifiers, not credentials. The one `secretRef` resolves through Key Vault. A repository-wide scan (`evidence/secret-scan.txt`) found zero credential patterns in any Day 25 source, infra, or evidence file.

![App settings without plaintext secrets](evidence/screenshots/05-app-settings-no-secrets.png)

## Verification

Bicep builds and lints clean; `what-if` against both isolated resource groups showed only creates, zero modifies/deletes of anything pre-existing; both `thinkschool-day25-dev` and `thinkschool-day25-prod` deployed for real and are `Healthy`/`RunningAtMaxScale`; `thinkschool-rg` is unchanged except for two additive `AcrPull` grants. Full detail, including a real Container-Apps-plus-system-assigned-identity bootstrap ordering issue that was hit and fixed (not hidden), is in `evidence/dev-deployment.txt` and `evidence/prod-deployment.txt`. A full interactive Entra user sign-in was not attempted (unsafe to automate); the deployed Entra config, the real OIDC issuer, and a live 401 against an unauthenticated protected endpoint were verified instead — see `evidence/verification.txt`.

![Final verification](evidence/screenshots/06-final-verification.png)
