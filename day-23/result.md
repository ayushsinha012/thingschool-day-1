# Day 23 — Bicep IaC — Result

## Task

> Describe your infra as code. Author Bicep modules (parameterized) for
> the API, SQL, and Service Bus, with separate dev/prod parameter files.
> No portal click-ops.

## Exercise

> Paste the main Bicep + one module + the dev/prod params. Show a
> successful what-if/deploy output.

## Repository/Azure inspection (before writing any Bicep)

| Item | Finding |
|---|---|
| Existing Day-23 folder | None. |
| Existing Bicep anywhere | `day-1/QuotesApi/infra/{main,resources}.bicep` (canonical, `azd`-generated), `Day-17/quotes-bff/infra/` (BFF, out of scope), `day-20/infra/resources.bicep` (near-duplicate snapshot). None is a modular API/SQL/ServiceBus set with dev/prod params. |
| `az account show` | Authenticated, subscription "Azure for Students". |
| `az resource list -g thinkschool-rg` | 18 resources - see `infra/evidence/resource-verification.txt` for the full list. |
| Container App `quotes-api` | Real config captured via `az containerapp show` - image, env vars, secrets, volume all confirmed live, not assumed. |
| Service Bus | `sb-quotesapi-thinkschool` (Standard), topic `quote-events`, subscriptions `sub-audit`/`sub-notifications` - matches the task brief exactly, confirmed via `az servicebus topic subscription list`. |
| "SQL" | `thinkschool-day7-sql-0c0dda` exists in the resource group but is an unrelated Day-7 resource. QuotesApi's actual `ConnectionStrings__DefaultConnection` is `Data Source=/tmp/quotes.db` (SQLite). See `README.md`, "SQL discrepancy", for how this was handled. |

## What was built

```
day-23/
  README.md
  result.md
  infra/
    main.bicep
    modules/
      api.bicep
      sql.bicep
      servicebus.bicep
    params/
      dev.bicepparam
      prod.bicepparam
    evidence/
      bicep-validation.txt
      dev-what-if.txt
      prod-what-if.txt
      resource-verification.txt
      SCREENSHOTS.md
```

## main.bicep

```bicep
// Day 23 - Bicep IaC for the QuotesApi Azure infrastructure.
//
// Deploys into the existing thinkschool-rg resource group (not created
// here - see day-1/QuotesApi/infra/main.bicep for the subscription-scoped
// azd template that already established it). Composes three modules:
// API (Container App), SQL, and Service Bus. Environment-specific values
// (dev vs prod) live entirely in params/dev.bicepparam and
// params/prod.bicepparam - this file has no hardcoded environment-specific
// values.

targetScope = 'resourceGroup'

@allowed([
  'dev'
  'prod'
])
@description('Deployment environment. Drives resource naming and sizing via the parameter files, not via logic in this template.')
param environment string

@description('Azure region for newly-created resources.')
param location string = resourceGroup().location

@description('Tags applied to every resource this template creates directly.')
param tags object = {
  day: '23'
  environment: environment
}

// ---------------------------------------------------------------------
// API (Container App)
// ---------------------------------------------------------------------

@description('Name of the existing Container Apps managed environment to deploy into.')
param containerAppsEnvironmentName string

@description('Name of the existing container registry the image is pulled from.')
param containerRegistryName string

@description('Name of the existing user-assigned managed identity used for ACR pull and Azure access.')
param managedIdentityName string

@description('Name of the Container App.')
param apiAppName string

@description('Container image (registry/repo:tag) to deploy.')
param apiContainerImage string

@description('Target port the container listens on.')
param apiTargetPort int = 8080

@description('Whether ingress accepts plain HTTP in addition to HTTPS.')
param apiAllowInsecure bool = false

@description('CPU cores allocated to the API container.')
param apiCpuCores string = '0.5'

@description('Memory allocated to the API container.')
param apiMemory string = '1Gi'

@minValue(0)
param apiMinReplicas int = 1

@minValue(1)
param apiMaxReplicas int = 10

@description('Container Apps environment workload profile to run on.')
param apiWorkloadProfileName string = 'Consumption'

@secure()
@minLength(32)
@description('Signing key bound to Jwt:Key. Supply via readEnvironmentVariable() when deploying - never committed in plain text.')
param jwtSigningKey string

@secure()
@description('Connection string bound to ConnectionStrings:Redis. Optional.')
param redisConnectionString string = ''

@secure()
@description('Application Insights connection string. Optional.')
param appInsightsConnectionString string = ''

@description('Allow-listed browser origin for CORS in production. Empty means no extra origin is added.')
param corsProductionOrigin string = ''

@description('Whether to mount the shared outbox Azure Files volume (only meaningful where that storage definition already exists, e.g. prod).')
param enableOutboxVolume bool = false

@description('Name of the storage definition (registered on the Container Apps environment) backing the outbox volume.')
param outboxStorageName string = 'quotesapi-outbox-data'

// ---------------------------------------------------------------------
// SQL
// ---------------------------------------------------------------------

@description('Whether to deploy the SQL module at all. False for prod: the real QuotesApi does not use Azure SQL (see day-23/README.md), so prod parameters intentionally do not stand up SQL infrastructure that nothing would use.')
param deploySql bool = true

@description('Name of the SQL logical server. Ignored when deploySql is false.')
param sqlServerName string = ''

@description('Name of the database. Ignored when deploySql is false.')
param sqlDatabaseName string = 'quotesapi'

@description('Entra ID (AAD) login name for the server admin. Ignored when deploySql is false.')
param sqlAadAdminLogin string = ''

@description('Entra ID (AAD) object id for the server admin principal. Ignored when deploySql is false.')
param sqlAadAdminObjectId string = ''

@description('Database SKU name. Ignored when deploySql is false.')
param sqlSkuName string = 'Basic'

@description('Database SKU tier. Ignored when deploySql is false.')
param sqlSkuTier string = 'Basic'

@description('Database max size in bytes. Ignored when deploySql is false.')
param sqlMaxSizeBytes int = 2147483648

// ---------------------------------------------------------------------
// Service Bus
// ---------------------------------------------------------------------

@description('Service Bus namespace name.')
param serviceBusNamespaceName string

@description('true creates a new namespace (dev); false manages the topic/subscriptions under the already-existing sb-quotesapi-thinkschool namespace (prod).')
param serviceBusCreateNamespace bool = true

@allowed([
  'Standard'
  'Premium'
])
param serviceBusSkuName string = 'Standard'

param serviceBusZoneRedundant bool = false

param serviceBusTopicName string = 'quote-events'

param serviceBusSubscriptionNames array = [
  'sub-audit'
  'sub-notifications'
]

param serviceBusMaxDeliveryCount int = 3

param serviceBusLockDuration string = 'PT1M'

param serviceBusDefaultMessageTimeToLive string = 'P14D'

param serviceBusRequiresDuplicateDetection bool = false

param serviceBusDuplicateDetectionHistoryTimeWindow string = 'PT10M'

// ---------------------------------------------------------------------
// Modules
// ---------------------------------------------------------------------

module api 'modules/api.bicep' = {
  name: 'api-${environment}'
  params: {
    environmentName: environment
    location: location
    tags: tags
    containerAppsEnvironmentName: containerAppsEnvironmentName
    containerRegistryName: containerRegistryName
    managedIdentityName: managedIdentityName
    apiAppName: apiAppName
    apiContainerImage: apiContainerImage
    apiTargetPort: apiTargetPort
    allowInsecure: apiAllowInsecure
    apiCpuCores: apiCpuCores
    apiMemory: apiMemory
    apiMinReplicas: apiMinReplicas
    apiMaxReplicas: apiMaxReplicas
    workloadProfileName: apiWorkloadProfileName
    jwtSigningKey: jwtSigningKey
    redisConnectionString: redisConnectionString
    appInsightsConnectionString: appInsightsConnectionString
    corsProductionOrigin: corsProductionOrigin
    enableOutboxVolume: enableOutboxVolume
    outboxStorageName: outboxStorageName
  }
}

module sql 'modules/sql.bicep' = if (deploySql) {
  name: 'sql-${environment}'
  params: {
    sqlServerName: sqlServerName
    location: location
    tags: tags
    sqlDatabaseName: sqlDatabaseName
    aadAdminLogin: sqlAadAdminLogin
    aadAdminObjectId: sqlAadAdminObjectId
    skuName: sqlSkuName
    skuTier: sqlSkuTier
    maxSizeBytes: sqlMaxSizeBytes
  }
}

module serviceBus 'modules/servicebus.bicep' = {
  name: 'servicebus-${environment}'
  params: {
    namespaceName: serviceBusNamespaceName
    location: location
    tags: tags
    createNamespace: serviceBusCreateNamespace
    skuName: serviceBusSkuName
    zoneRedundant: serviceBusZoneRedundant
    topicName: serviceBusTopicName
    subscriptionNames: serviceBusSubscriptionNames
    maxDeliveryCount: serviceBusMaxDeliveryCount
    lockDuration: serviceBusLockDuration
    defaultMessageTimeToLive: serviceBusDefaultMessageTimeToLive
    requiresDuplicateDetection: serviceBusRequiresDuplicateDetection
    duplicateDetectionHistoryTimeWindow: serviceBusDuplicateDetectionHistoryTimeWindow
  }
}

// ---------------------------------------------------------------------
// Outputs
// ---------------------------------------------------------------------

output apiName string = api.outputs.apiName
output apiResourceId string = api.outputs.apiResourceId
output apiFqdn string = api.outputs.apiFqdn
output sqlServerFqdn string = sql.?outputs.?sqlServerFqdn ?? ''
output sqlDatabaseResourceId string = sql.?outputs.?sqlDatabaseResourceId ?? ''
output serviceBusNamespaceName string = serviceBus.outputs.namespaceName
output serviceBusTopicName string = serviceBus.outputs.topicName
output serviceBusSubscriptionNames array = serviceBus.outputs.subscriptionNames
```

## modules/api.bicep (one module)

```bicep
// API module - models the QuotesApi Azure Container App.
//
// Reuses the existing shared Container Apps environment, container
// registry, and user-assigned managed identity (already provisioned and
// already granted AcrPull - see day-1/QuotesApi/infra/resources.bicep)
// rather than creating duplicates of any of them.

@description('Environment name (dev or prod) - used only for tagging.')
param environmentName string

@description('Location for the container app.')
param location string

@description('Tags applied to the container app.')
param tags object = {}

@description('Name of the existing Container Apps managed environment to deploy into.')
param containerAppsEnvironmentName string

@description('Name of the existing container registry the image is pulled from.')
param containerRegistryName string

@description('Name of the existing user-assigned managed identity used for ACR pull and Azure access.')
param managedIdentityName string

@description('Name of the Container App.')
param apiAppName string

@description('Container image (registry/repo:tag) to deploy.')
param apiContainerImage string

@description('Target port the container listens on.')
param apiTargetPort int = 8080

@description('Whether ingress accepts plain HTTP in addition to HTTPS. The real production quotes-api currently has this set to true.')
param allowInsecure bool = false

@description('CPU cores allocated to the API container (Container Apps consumption plan combinations, e.g. 0.25/0.5/0.75/1).')
param apiCpuCores string = '0.5'

@description('Memory allocated to the API container, e.g. 0.5Gi/1Gi/2Gi.')
param apiMemory string = '1Gi'

@minValue(0)
@description('Minimum replica count. 0 is safe for a dev/demo environment that can scale to zero.')
param apiMinReplicas int = 1

@minValue(1)
@description('Maximum replica count.')
param apiMaxReplicas int = 10

@description('Container Apps environment workload profile to run on.')
param workloadProfileName string = 'Consumption'

@secure()
@minLength(32)
@description('Signing key bound to Jwt:Key - required, never defaulted, never written to a param file in plain text (supply via readEnvironmentVariable() at deploy time).')
param jwtSigningKey string

@secure()
@description('Connection string bound to ConnectionStrings:Redis. Optional - empty means the env var/secret is omitted entirely, same as an environment with no Redis wired up.')
param redisConnectionString string = ''

@secure()
@description('Application Insights connection string. Optional - empty means the env var/secret is omitted entirely.')
param appInsightsConnectionString string = ''

@description('Allow-listed browser origin for CORS in production. Empty means no extra origin is added (never a wildcard).')
param corsProductionOrigin string = ''

@description('Whether to mount the shared outbox Azure Files volume. Only meaningful where that storage definition already exists on the target environment (true for the real prod environment).')
param enableOutboxVolume bool = false

@description('Name of the storage definition (registered on the Container Apps environment) backing the outbox volume.')
param outboxStorageName string = 'quotesapi-outbox-data'

resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: containerAppsEnvironmentName
}

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: containerRegistryName
}

resource managedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: managedIdentityName
}

var baseEnv = [
  {
    name: 'PORT'
    value: string(apiTargetPort)
  }
  {
    name: 'AZURE_CLIENT_ID'
    value: managedIdentity.properties.clientId
  }
  {
    name: 'Jwt__Key'
    secretRef: 'jwt-signing-key'
  }
]

var appInsightsEnv = empty(appInsightsConnectionString) ? [] : [
  {
    name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
    secretRef: 'app-insights-connection-string'
  }
]

var redisEnv = empty(redisConnectionString) ? [] : [
  {
    name: 'ConnectionStrings__Redis'
    secretRef: 'redis-connection-string'
  }
]

var corsEnv = empty(corsProductionOrigin) ? [] : [
  {
    name: 'Cors__ProductionOrigins__0'
    value: corsProductionOrigin
  }
]

var secrets = concat(
  [
    {
      name: 'jwt-signing-key'
      value: jwtSigningKey
    }
  ],
  empty(redisConnectionString) ? [] : [
    {
      name: 'redis-connection-string'
      value: redisConnectionString
    }
  ],
  empty(appInsightsConnectionString) ? [] : [
    {
      name: 'app-insights-connection-string'
      value: appInsightsConnectionString
    }
  ]
)

resource apiApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: apiAppName
  location: location
  tags: union(tags, { environment: environmentName })
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${managedIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerAppsEnvironment.id
    workloadProfileName: workloadProfileName
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: apiTargetPort
        transport: 'auto'
        allowInsecure: allowInsecure
      }
      registries: [
        {
          server: containerRegistry.properties.loginServer
          identity: managedIdentity.id
        }
      ]
      secrets: secrets
    }
    template: {
      containers: [
        {
          name: 'main'
          image: apiContainerImage
          resources: {
            cpu: json(apiCpuCores)
            memory: apiMemory
          }
          env: concat(baseEnv, appInsightsEnv, redisEnv, corsEnv)
          volumeMounts: enableOutboxVolume ? [
            {
              volumeName: 'outbox-data'
              mountPath: '/data'
            }
          ] : []
        }
      ]
      scale: {
        minReplicas: apiMinReplicas
        maxReplicas: apiMaxReplicas
      }
      volumes: enableOutboxVolume ? [
        {
          name: 'outbox-data'
          storageType: 'AzureFile'
          storageName: outboxStorageName
        }
      ] : []
    }
  }
}

output apiName string = apiApp.name
output apiResourceId string = apiApp.id
output apiFqdn string = apiApp.properties.configuration.ingress.fqdn
```

## params/dev.bicepparam

```bicep
using '../main.bicep'

param environment = 'dev'
param location = 'centralindia'
param tags = {
  day: '23'
  environment: 'dev'
}

// ---------------------------------------------------------------------
// API - a separate, brand-new Container App (quotes-api-dev), sized down.
// Reuses the existing shared environment/registry/identity - it does not
// touch the real quotes-api Container App.
// ---------------------------------------------------------------------
param containerAppsEnvironmentName = 'thinkschool-env'
param containerRegistryName = 'cr2i2oapij4zsrc'
param managedIdentityName = 'id-quotesApi-2i2oapij4zsrc'
param apiAppName = 'quotes-api-dev'
param apiContainerImage = 'cr2i2oapij4zsrc.azurecr.io/quotes-api/quotes-api-quotesapi-thinkschool:day21-hybridcache-1788429669'
param apiTargetPort = 8080
param apiAllowInsecure = false
param apiCpuCores = '0.25'
param apiMemory = '0.5Gi'
param apiMinReplicas = 0
param apiMaxReplicas = 1
param apiWorkloadProfileName = 'Consumption'

// Secrets are never written here in plain text - readEnvironmentVariable()
// reads them from the deployer's shell environment at deploy time, and
// deployment fails with a clear error if DEV_JWT_SIGNING_KEY isn't set
// (no fallback default - a fallback would itself be a secret-shaped value
// committed to source). Set before running `az deployment group
// create`/`what-if`:
//   DEV_JWT_SIGNING_KEY (required, 32+ chars)
//   DEV_REDIS_CONNECTION_STRING (optional)
//   DEV_APPINSIGHTS_CONNECTION_STRING (optional)
param jwtSigningKey = readEnvironmentVariable('DEV_JWT_SIGNING_KEY')
param redisConnectionString = readEnvironmentVariable('DEV_REDIS_CONNECTION_STRING', '')
param appInsightsConnectionString = readEnvironmentVariable('DEV_APPINSIGHTS_CONNECTION_STRING', '')

param corsProductionOrigin = ''
param enableOutboxVolume = false

// ---------------------------------------------------------------------
// SQL - new, dedicated dev server/database (never thinkschool-day7-sql-*).
// Entra-ID-only auth, so no password parameter exists anywhere.
// ---------------------------------------------------------------------
param deploySql = true
param sqlServerName = 'sql-quotesapi-dev'
param sqlDatabaseName = 'quotesapi'
param sqlAadAdminLogin = 'ayush.sinha7@s.amity.edu'
param sqlAadAdminObjectId = '4eece2d1-e3b7-4754-a99a-afc480a96fc8'
param sqlSkuName = 'Basic'
param sqlSkuTier = 'Basic'
param sqlMaxSizeBytes = 2147483648

// ---------------------------------------------------------------------
// Service Bus - new, dedicated dev namespace mirroring the real topic/
// subscription shape, isolated from the real sb-quotesapi-thinkschool.
// ---------------------------------------------------------------------
param serviceBusNamespaceName = 'sb-quotesapi-dev'
param serviceBusCreateNamespace = true
param serviceBusSkuName = 'Standard'
param serviceBusZoneRedundant = false
param serviceBusTopicName = 'quote-events'
param serviceBusSubscriptionNames = [
  'sub-audit'
  'sub-notifications'
]
param serviceBusMaxDeliveryCount = 3
param serviceBusLockDuration = 'PT1M'
param serviceBusDefaultMessageTimeToLive = 'P14D'
param serviceBusRequiresDuplicateDetection = false
param serviceBusDuplicateDetectionHistoryTimeWindow = 'PT10M'
```

## params/prod.bicepparam

```bicep
using '../main.bicep'

param environment = 'prod'
param location = 'centralindia'
param tags = {
  day: '23'
  environment: 'prod'
}

// ---------------------------------------------------------------------
// API - models the REAL existing quotes-api Container App as closely as
// this exercise's scope allows, so a what-if against it is informative
// rather than alarming. This file is used for `what-if` only - see
// day-23/README.md for why an actual prod deployment was not performed.
// ---------------------------------------------------------------------
param containerAppsEnvironmentName = 'thinkschool-env'
param containerRegistryName = 'cr2i2oapij4zsrc'
param managedIdentityName = 'id-quotesApi-2i2oapij4zsrc'
param apiAppName = 'quotes-api'
param apiContainerImage = 'cr2i2oapij4zsrc.azurecr.io/quotes-api/quotes-api-quotesapi-thinkschool:day21-hybridcache-1788429669'
param apiTargetPort = 8080
param apiAllowInsecure = true
param apiCpuCores = '0.5'
param apiMemory = '1Gi'
param apiMinReplicas = 1
param apiMaxReplicas = 10
param apiWorkloadProfileName = 'Consumption'

// Secrets are never written here in plain text - readEnvironmentVariable()
// reads them from the deployer's shell environment at deploy time. These
// are the real production secrets; only someone authorized to operate
// prod should ever have them set locally.
param jwtSigningKey = readEnvironmentVariable('PROD_JWT_SIGNING_KEY')
param redisConnectionString = readEnvironmentVariable('PROD_REDIS_CONNECTION_STRING', '')
param appInsightsConnectionString = readEnvironmentVariable('PROD_APPINSIGHTS_CONNECTION_STRING', '')

param corsProductionOrigin = 'https://polite-mushroom-04dd5ce00.7.azurestaticapps.net'
param enableOutboxVolume = true
param outboxStorageName = 'quotesapi-outbox-data'

// ---------------------------------------------------------------------
// SQL - intentionally disabled for prod. The real QuotesApi persists to
// SQLite (Data Source=/tmp/quotes.db), not Azure SQL - standing up a new
// SQL server here would be infrastructure nothing actually uses. See
// day-23/README.md, "SQL discrepancy".
// ---------------------------------------------------------------------
param deploySql = false

// ---------------------------------------------------------------------
// Service Bus - references the REAL existing namespace/topic/subscriptions
// (sb-quotesapi-thinkschool) rather than creating a second one. Values
// below match the live configuration exactly (captured via `az servicebus
// topic show` / `topic subscription list`) so a what-if against it shows
// no unexpected changes.
// ---------------------------------------------------------------------
param serviceBusNamespaceName = 'sb-quotesapi-thinkschool'
param serviceBusCreateNamespace = false
param serviceBusSkuName = 'Standard'
param serviceBusZoneRedundant = true
param serviceBusTopicName = 'quote-events'
param serviceBusSubscriptionNames = [
  'sub-audit'
  'sub-notifications'
]
param serviceBusMaxDeliveryCount = 3
param serviceBusLockDuration = 'PT1M'
// The real topic/subscriptions were created without an explicit TTL, so
// Service Bus reports ARM's max-duration sentinel - matched exactly here
// so what-if doesn't show a spurious "modify" on this property.
param serviceBusDefaultMessageTimeToLive = 'P10675199DT2H48M5.4775807S'
param serviceBusRequiresDuplicateDetection = false
param serviceBusDuplicateDetectionHistoryTimeWindow = 'PT10M'
```

## Bicep validation

```
$ az bicep version
Bicep CLI version 0.46.1 (545b338e2c)

$ az bicep build --file main.bicep
(no output - clean)

$ az bicep lint --file main.bicep
(no output - clean)

$ DEV_JWT_SIGNING_KEY=<32+ chars> az bicep build-params --file params/dev.bicepparam
(no output - clean)

$ PROD_JWT_SIGNING_KEY=<32+ chars> az bicep build-params --file params/prod.bicepparam
(no output - clean)
```
Full transcript, including the `@minLength(32)` constraint correctly rejecting a too-short key on the first attempt: `infra/evidence/bicep-validation.txt`.

## What-if (real, against thinkschool-rg)

```
az deployment group what-if --resource-group thinkschool-rg --template-file main.bicep --parameters params/dev.bicepparam --result-format FullResourcePayloads
```
Result: **`Resource changes: 8 to create, 18 to ignore.`** - creates `quotes-api-dev`, `sb-quotesapi-dev` + topic `quote-events` + subscriptions `sub-audit`/`sub-notifications`, `sql-quotesapi-dev` + database `quotesapi` + the `AllowAllWindowsAzureIps` firewall rule. No modifies, no deletes. Full output: `infra/evidence/dev-what-if.txt`.

```
az deployment group what-if --resource-group thinkschool-rg --template-file main.bicep --parameters params/prod.bicepparam --result-format FullResourcePayloads
```
Result: **`Resource changes: 4 to modify, 17 to ignore.`** - no creates, no deletes. The 4 modifies are on `quotes-api` and the real Service Bus topic/subscriptions, and are all either a resolved-value-vs-expression artifact or an ARM-default property this exercise's Bicep doesn't set (see README.md for the exact list). Full output: `infra/evidence/prod-what-if.txt`.

## Deployment

Neither environment was actually deployed - `what-if` only, by design:

- **Prod**: `what-if` is clean, but this Bicep doesn't model every property of the live `quotes-api` Container App (dapr config, revision/traffic state, `azd-*` tags), so an actual deployment would reset those - a real side effect not worth risking for this exercise.
- **Dev**: `what-if` is clean (8 creates, no deletes) and would be safe to actually deploy, but doing so provisions real billable resources (~$5/mo SQL Basic + ~$10/mo Service Bus Standard) on an Azure for Students subscription. Asked explicitly; the answer was to stop at `what-if` and not spend the credit. No `az deployment group create` was run for either environment.

## Verification

`infra/evidence/resource-verification.txt` - resource group, full resource list, real Service Bus topic/subscription config, real `quotes-api` summary, and confirmation that `thinkschool-day7-sql-0c0dda` is the unrelated Day-7 resource, all captured via direct `az` calls against the live subscription.
