targetScope = 'resourceGroup'

@description('Deployment environment. Drives resource naming and tagging.')
param environment string = 'dev'

@description('Azure region for the data-tier resources (SQL, Key Vault) and the API (same region as the shared Container Apps environment).')
param location string = resourceGroup().location

@description('Azure region for the VNet and private endpoint. Kept as a separate parameter from location in case this subscription needs the private-networking resources in a different region than the data tier.')
param networkLocation string = location

@description('Tags applied to every resource this template creates.')
param tags object = {
  day: '27'
  environment: environment
  project: 'maintainxpert'
}

@description('Resource group of the existing shared container registry and Container Apps environment.')
param sharedResourceGroup string = 'thinkschool-rg'

param containerRegistryName string = 'cr2i2oapij4zsrc'

param apiContainerImage string

param vnetName string

param peSubnetPrefix string = '10.70.2.0/24'

@description('Name of the existing shared Container Apps environment this subscription is limited to (a hard global quota of one Container Apps environment per subscription, verified during this deployment).')
param containerAppsEnvironmentName string = 'thinkschool-env'

param apiAppName string

param apiCpuCores string = '0.25'

param apiMemory string = '0.5Gi'

@minValue(0)
param apiMinReplicas int = 0

@minValue(1)
param apiMaxReplicas int = 1

param sqlServerName string

param sqlDatabaseName string = 'maintainxpert'

param sqlAadAdminLogin string

param sqlAadAdminObjectId string

param sqlSkuName string = 'Basic'

param sqlSkuTier string = 'Basic'

@allowed([
  'Enabled'
  'Disabled'
])
@description('Public network access on the SQL server. Kept Enabled - see README for why.')
param sqlPublicNetworkAccess string = 'Enabled'

param keyVaultName string

param integrationClientId string = 'maintainxpert-integration'

@secure()
@minLength(32)
@description('JWT signing key. Supplied via readEnvironmentVariable() at deploy time, never committed in plain text.')
param jwtSigningKey string

@secure()
@minLength(16)
@description('SHA-256 hex hash of the integration client secret. Supplied via readEnvironmentVariable() at deploy time.')
param integrationClientSecretHash string

module network 'modules/network.bicep' = {
  name: 'network-${environment}'
  params: {
    vnetName: vnetName
    location: networkLocation
    tags: tags
    peSubnetPrefix: peSubnetPrefix
  }
}

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
    publicNetworkAccess: sqlPublicNetworkAccess
  }
}

module privateEndpoints 'modules/private-endpoints.bicep' = {
  name: 'pe-${environment}'
  params: {
    location: networkLocation
    tags: tags
    sqlPrivateEndpointName: 'pe-sql-${sqlServerName}'
    peSubnetId: network.outputs.peSubnetId
    sqlServerResourceId: sql.outputs.sqlServerId
    vnetId: network.outputs.vnetId
    vnetName: network.outputs.vnetName
  }
}

module keyVault 'modules/keyvault.bicep' = {
  name: 'keyvault-${environment}'
  params: {
    keyVaultName: keyVaultName
    location: location
    tags: tags
    jwtSigningKey: jwtSigningKey
    integrationClientSecretHash: integrationClientSecretHash
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
    sqlServerFqdn: sql.outputs.sqlServerFqdn
    sqlDatabaseName: sql.outputs.sqlDatabaseName
    keyVaultName: keyVault.outputs.keyVaultName
    keyVaultResourceGroup: resourceGroup().name
    integrationClientId: integrationClientId
  }
}

module identity 'modules/identity.bicep' = {
  name: 'identity-${environment}'
  params: {
    apiPrincipalId: api.outputs.apiPrincipalId
    keyVaultResourceId: keyVault.outputs.keyVaultResourceId
  }
}

module acrPull 'modules/acrpull.bicep' = {
  name: 'acrpull-${environment}'
  scope: resourceGroup(sharedResourceGroup)
  params: {
    containerRegistryName: containerRegistryName
    apiPrincipalId: api.outputs.apiPrincipalId
  }
}

output apiName string = api.outputs.apiName
output apiFqdn string = api.outputs.apiFqdn
output apiPrincipalId string = api.outputs.apiPrincipalId
output sqlServerName string = sql.outputs.sqlServerName
output sqlServerFqdn string = sql.outputs.sqlServerFqdn
output sqlDatabaseName string = sql.outputs.sqlDatabaseName
output sqlPrivateEndpointName string = privateEndpoints.outputs.sqlPrivateEndpointName
output sqlPrivateDnsZoneName string = privateEndpoints.outputs.sqlPrivateDnsZoneName
output keyVaultName string = keyVault.outputs.keyVaultName
output vnetName string = network.outputs.vnetName
