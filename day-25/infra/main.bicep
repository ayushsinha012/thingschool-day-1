// Day 25 - Identity end-to-end.
//
// Deploys an isolated Day 25 resource group (thinkschool-day25-dev or
// thinkschool-day25-prod, chosen by the resource group the deployment
// targets) containing a dedicated QuotesApi Container App with a
// system-assigned managed identity, an Entra-ID-only Azure SQL Server,
// a Service Bus namespace with local (SAS/key) auth disabled, and a Key
// Vault. The Container App never receives a SQL password, a Service Bus
// key, or any plaintext secret - the one value it genuinely needs
// (the local-JWT-scheme signing key) is stored in Key Vault and wired in
// as a Container Apps Key Vault-referenced secret.
//
// Reuses the existing shared Container Apps environment and container
// registry from thinkschool-rg rather than duplicating them - nothing in
// thinkschool-rg is modified by this template.

targetScope = 'resourceGroup'

@allowed([
  'dev'
  'prod'
])
@description('Deployment environment. Drives resource naming and sizing via the parameter files.')
param environment string

@description('Azure region for all Day 25 resources.')
param location string = resourceGroup().location

@description('Tags applied to every resource this template creates.')
param tags object = {
  day: '25'
  environment: environment
}

// ---------------------------------------------------------------------
// Shared resources this template reuses, never modifies
// ---------------------------------------------------------------------

@description('Resource group of the existing shared Container Apps environment and container registry.')
param sharedResourceGroup string = 'thinkschool-rg'

param containerAppsEnvironmentName string = 'thinkschool-env'

param containerRegistryName string = 'cr2i2oapij4zsrc'

param apiContainerImage string = 'cr2i2oapij4zsrc.azurecr.io/quotes-api/quotes-api-quotesapi-thinkschool:day21-hybridcache-1788429669'

// ---------------------------------------------------------------------
// API (Container App)
// ---------------------------------------------------------------------

param apiAppName string

param apiCpuCores string = '0.25'

param apiMemory string = '0.5Gi'

@minValue(0)
param apiMinReplicas int = 0

@minValue(1)
param apiMaxReplicas int = 1

// ---------------------------------------------------------------------
// Entra ID application authentication - the existing QuotesApi app
// registration is reused, no duplicate registration is created, and no
// client secret is ever needed for bearer-token validation.
// ---------------------------------------------------------------------

@description('Microsoft Entra tenant ID. Not a secret.')
param entraTenantId string

@description('Existing QuotesApi Entra app registration (client) ID. Not a secret.')
param entraClientId string

@description('Audience the API validates Entra-issued bearer tokens against. Not a secret.')
param entraAudience string

// ---------------------------------------------------------------------
// SQL
// ---------------------------------------------------------------------

param sqlServerName string

param sqlDatabaseName string = 'quotesapi'

param sqlAadAdminLogin string

param sqlAadAdminObjectId string

param sqlSkuName string = 'Basic'

param sqlSkuTier string = 'Basic'

// ---------------------------------------------------------------------
// Service Bus
// ---------------------------------------------------------------------

param serviceBusNamespaceName string

param serviceBusTopicName string = 'quote-events'

param serviceBusSubscriptionNames array = [
  'sub-audit'
  'sub-notifications'
]

// ---------------------------------------------------------------------
// Key Vault
// ---------------------------------------------------------------------

param keyVaultName string

@secure()
@minLength(32)
@description('Local-scheme JWT signing key. Supplied via readEnvironmentVariable() at deploy time, never committed in plain text, stored only as a Key Vault secret.')
param jwtSigningKey string

// ---------------------------------------------------------------------
// Modules
// ---------------------------------------------------------------------

module sql 'modules/sql.bicep' = {
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
  }
}

module serviceBus 'modules/servicebus.bicep' = {
  name: 'servicebus-${environment}'
  params: {
    namespaceName: serviceBusNamespaceName
    location: location
    tags: tags
    topicName: serviceBusTopicName
    subscriptionNames: serviceBusSubscriptionNames
  }
}

module keyVault 'modules/keyvault.bicep' = {
  name: 'keyvault-${environment}'
  params: {
    keyVaultName: keyVaultName
    location: location
    tags: tags
    jwtSigningKey: jwtSigningKey
  }
}

module api 'modules/api.bicep' = {
  name: 'api-${environment}'
  params: {
    environmentName: environment
    location: location
    tags: tags
    containerAppsEnvironmentResourceGroup: sharedResourceGroup
    containerAppsEnvironmentName: containerAppsEnvironmentName
    containerRegistryResourceGroup: sharedResourceGroup
    containerRegistryName: containerRegistryName
    apiAppName: apiAppName
    apiContainerImage: apiContainerImage
    apiCpuCores: apiCpuCores
    apiMemory: apiMemory
    apiMinReplicas: apiMinReplicas
    apiMaxReplicas: apiMaxReplicas
    entraTenantId: entraTenantId
    entraClientId: entraClientId
    entraAudience: entraAudience
    sqlServerFqdn: sql.outputs.sqlServerFqdn
    sqlDatabaseName: sql.outputs.sqlDatabaseName
    serviceBusFullyQualifiedNamespace: serviceBus.outputs.fullyQualifiedNamespace
    keyVaultName: keyVault.outputs.keyVaultName
    keyVaultResourceGroup: resourceGroup().name
  }
}

module identity 'modules/identity.bicep' = {
  name: 'identity-${environment}'
  params: {
    apiPrincipalId: api.outputs.apiPrincipalId
    serviceBusNamespaceResourceId: serviceBus.outputs.namespaceResourceId
    keyVaultResourceId: keyVault.outputs.keyVaultResourceId
  }
}

// AcrPull is granted at the container registry's own scope, in the
// shared thinkschool-rg resource group - a cross-resource-group role
// assignment, deployed as its own module targeting that resource group.
module acrPull 'modules/acrpull.bicep' = {
  name: 'acrpull-${environment}'
  scope: resourceGroup(sharedResourceGroup)
  params: {
    containerRegistryName: containerRegistryName
    apiPrincipalId: api.outputs.apiPrincipalId
  }
}

// ---------------------------------------------------------------------
// Outputs
// ---------------------------------------------------------------------

output apiName string = api.outputs.apiName
output apiFqdn string = api.outputs.apiFqdn
output apiPrincipalId string = api.outputs.apiPrincipalId
output sqlServerFqdn string = sql.outputs.sqlServerFqdn
output sqlDatabaseName string = sql.outputs.sqlDatabaseName
output serviceBusFullyQualifiedNamespace string = serviceBus.outputs.fullyQualifiedNamespace
output keyVaultName string = keyVault.outputs.keyVaultName
output keyVaultUri string = keyVault.outputs.keyVaultUri
