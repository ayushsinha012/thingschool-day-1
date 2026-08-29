@description('The location used for all deployed resources')
param location string = resourceGroup().location

@description('Tags that will be applied to all resources')
param tags object = {}

param quotesBffExists bool

@description('Id of the user or app to assign application roles')
param principalId string

@description('Principal type of user or app')
param principalType string

@minLength(1)
@description('Name of the pre-existing Container Apps environment to deploy into.')
param containerAppsEnvironmentName string

@minLength(1)
@description('Name of the pre-existing container registry to push/pull this service\'s image through.')
param containerRegistryName string

@minLength(1)
@description('Base URL of the already-deployed QuotesApi Container App.')
param quotesApiBaseUrl string

@description('OAuth scope this proxy requests a Managed Identity access token for.')
param quotesApiScope string

// Existing Container Apps environment (thinkschool-env) and container
// registry, already provisioned for QuotesApi (see
// day-1/QuotesApi/infra/resources.bicep) - reused instead of creating
// duplicates for a service that only ever talks to QuotesApi.
resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2023-05-01' existing = {
  name: containerAppsEnvironmentName
}

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-01-01-preview' existing = {
  name: containerRegistryName
}

module quotesBffFetchLatestImage './modules/fetch-container-image.bicep' = {
  name: 'quotesBff-fetch-image'
  params: {
    exists: quotesBffExists
    name: 'quotes-bff'
  }
}

module quotesBff 'br/public:avm/res/app/container-app:0.8.0' = {
  name: 'quotesBff'
  params: {
    name: 'quotes-bff'
    ingressTargetPort: 8080
    ingressExternal: true
    scaleMinReplicas: 1
    scaleMaxReplicas: 3
    containers: [
      {
        image: quotesBffFetchLatestImage.outputs.?containers[?0].?image ?? 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'
        name: 'main'
        resources: {
          cpu: json('0.25')
          memory: '0.5Gi'
        }
        env: [
          {
            name: 'PORT'
            value: '8080'
          }
          {
            name: 'QuotesApi__BaseUrl'
            value: quotesApiBaseUrl
          }
          {
            name: 'QuotesApi__Scope'
            value: quotesApiScope
          }
        ]
      }
    ]
    // System-assigned only, deliberately - no user-assigned identity, no
    // client secret, no certificate. This is the identity that later gets
    // granted the quotes-api-day17 Entra app registration's "Api.Invoke"
    // application role (infra/grant-api-invoke-role.sh, run as this azd
    // project's postprovision hook - see azure.yaml) - ARM/Bicep has no
    // native resource type for that grant, since Microsoft.Graph is not an
    // ARM provider.
    managedIdentities: {
      systemAssigned: true
    }
    registries: [
      {
        server: containerRegistry.properties.loginServer
        identity: 'system'
      }
    ]
    environmentResourceId: containerAppsEnvironment.id
    location: location
    tags: union(tags, { 'azd-service-name': 'quotes-bff' })
  }
}

// Lets the Container App's own system-assigned identity pull its image from
// the shared registry (the same role QuotesApi's user-assigned identity
// already holds there - see day-1/QuotesApi/infra/resources.bicep) - granted
// directly rather than reusing that identity, so quotes-bff's Managed
// Identity is exactly the one principal that both pulls its image and later
// gets the Api.Invoke application role, with nothing shared between the two
// Container Apps.
resource acrPullRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(containerRegistry.id, 'quotes-bff', 'AcrPull')
  scope: containerRegistry
  properties: {
    principalId: quotesBff.outputs.systemAssignedMIPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      '7f951dda-4ed3-4680-a7ca-43fe172d538d'
    )
  }
}

output AZURE_RESOURCE_QUOTES_BFF_ID string = quotesBff.outputs.resourceId
