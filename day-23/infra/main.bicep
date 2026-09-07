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
