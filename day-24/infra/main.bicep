targetScope = 'resourceGroup'

@allowed([
  'dev'
  'prod'
])
param environmentName string

param location string = resourceGroup().location

param tags object = {
  day: '24'
  environment: environmentName
  'azd-env-name': environmentName
}

param containerAppsEnvironmentName string = 'thinkschool-env'

param containerRegistryName string = 'cr2i2oapij4zsrc'

param managedIdentityName string = 'id-quotesApi-2i2oapij4zsrc'

param apiContainerImage string = 'cr2i2oapij4zsrc.azurecr.io/quotes-api/quotes-api-quotesapi-thinkschool:day21-hybridcache-1788429669'

@secure()
@minLength(32)
param jwtSigningKey string

param sqlAadAdminLogin string = 'ayush.sinha7@s.amity.edu'

param sqlAadAdminObjectId string = '4eece2d1-e3b7-4754-a99a-afc480a96fc8'

var isProd = environmentName == 'prod'

var apiSizing = isProd
  ? {
      cpu: '0.5'
      memory: '1Gi'
      minReplicas: 1
      maxReplicas: 3
    }
  : {
      cpu: '0.25'
      memory: '0.5Gi'
      minReplicas: 0
      maxReplicas: 1
    }

var apiAppName = 'quotes-api-day24-${environmentName}'
var sqlServerName = 'sql-quotesapi-day24-${environmentName}'
var serviceBusNamespaceName = 'sb-quotesapi-day24-${environmentName}'

module api 'modules/api.bicep' = {
  name: 'api-${environmentName}'
  params: {
    environmentName: environmentName
    location: location
    tags: tags
    containerAppsEnvironmentName: containerAppsEnvironmentName
    containerRegistryName: containerRegistryName
    managedIdentityName: managedIdentityName
    apiAppName: apiAppName
    apiContainerImage: apiContainerImage
    apiCpuCores: apiSizing.cpu
    apiMemory: apiSizing.memory
    apiMinReplicas: apiSizing.minReplicas
    apiMaxReplicas: apiSizing.maxReplicas
    jwtSigningKey: jwtSigningKey
  }
}

module sql 'modules/sql.bicep' = {
  name: 'sql-${environmentName}'
  params: {
    sqlServerName: sqlServerName
    location: location
    tags: tags
    sqlDatabaseName: 'quotesapi'
    aadAdminLogin: sqlAadAdminLogin
    aadAdminObjectId: sqlAadAdminObjectId
    skuName: 'Basic'
    skuTier: 'Basic'
  }
}

module serviceBus 'modules/servicebus.bicep' = {
  name: 'servicebus-${environmentName}'
  params: {
    namespaceName: serviceBusNamespaceName
    location: location
    tags: tags
    skuName: 'Standard'
    zoneRedundant: false
    topicName: 'quote-events'
    subscriptionNames: [
      'sub-audit'
      'sub-notifications'
    ]
  }
}

output apiName string = api.outputs.apiName
output apiResourceId string = api.outputs.apiResourceId
output apiFqdn string = api.outputs.apiFqdn
output sqlServerFqdn string = sql.outputs.sqlServerFqdn
output sqlDatabaseResourceId string = sql.outputs.sqlDatabaseResourceId
output serviceBusNamespaceName string = serviceBus.outputs.namespaceName
output serviceBusTopicName string = serviceBus.outputs.topicName
output serviceBusSubscriptionNames array = serviceBus.outputs.subscriptionNames
