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
