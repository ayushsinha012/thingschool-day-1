@description('Name of the existing container registry, deployed in the target resource group this module is scoped to.')
param containerRegistryName string

@description('Principal ID of the API Container App system-assigned managed identity.')
param apiPrincipalId string

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: containerRegistryName
}

resource acrPullRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(containerRegistry.id, apiPrincipalId, 'AcrPull')
  scope: containerRegistry
  properties: {
    principalId: apiPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
  }
}
