targetScope = 'subscription'

@minLength(1)
@maxLength(64)
@description('Name of the environment that can be used as part of naming resource convention')
param environmentName string

@minLength(1)
@description('Primary location for all resources')
param location string

@minLength(1)
@description('Name of the pre-existing resource group QuotesApi itself already runs in (see day-1/QuotesApi/infra/main.bicep) - reused here rather than creating a second one for a service that only ever talks to QuotesApi.')
param resourceGroupName string = 'thinkschool-rg'

@minLength(1)
@description('Name of the pre-existing Container Apps environment (thinkschool-env) QuotesApi itself already runs in - reused here instead of creating a second one.')
param containerAppsEnvironmentName string = 'thinkschool-env'

@minLength(1)
@description('Name of the pre-existing container registry QuotesApi already pushes images to (see day-1/QuotesApi/infra/resources.bicep) - reused here instead of creating a second one.')
param containerRegistryName string = 'cr2i2oapij4zsrc'

param quotesBffExists bool

@description('Id of the user or app to assign application roles')
param principalId string

@description('Principal type of user or app')
param principalType string

@minLength(1)
@description('Base URL of the already-deployed QuotesApi Container App this proxy forwards every request to.')
param quotesApiBaseUrl string = 'https://quotes-api.politeocean-3efec37e.centralindia.azurecontainerapps.io'

@minLength(1)
@description('OAuth scope (an Entra App ID URI + /.default) this proxy requests a Managed Identity access token for, on the anonymous-request path - the quotes-api-day17 app registration\'s own identifier, matching its exposed "Api.Invoke" application role.')
param quotesApiScope string = 'api://729a2be3-9609-4fd1-b7c5-e658386f9bfd/.default'

var tags = {
  'azd-env-name': environmentName
}

// Deploy into the existing resource group (thinkschool-rg) rather than
// creating a new one - same reasoning as QuotesApi's own main.bicep.
resource rg 'Microsoft.Resources/resourceGroups@2021-04-01' existing = {
  name: resourceGroupName
}

module resources 'resources.bicep' = {
  scope: rg
  name: 'resources'
  params: {
    location: location
    tags: tags
    principalId: principalId
    principalType: principalType
    quotesBffExists: quotesBffExists
    containerAppsEnvironmentName: containerAppsEnvironmentName
    containerRegistryName: containerRegistryName
    quotesApiBaseUrl: quotesApiBaseUrl
    quotesApiScope: quotesApiScope
  }
}

output AZURE_RESOURCE_QUOTES_BFF_ID string = resources.outputs.AZURE_RESOURCE_QUOTES_BFF_ID
