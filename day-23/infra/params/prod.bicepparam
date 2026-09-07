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
