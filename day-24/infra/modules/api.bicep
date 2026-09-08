@description('Environment name (dev or prod), used for tagging.')
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

@description('Whether ingress accepts plain HTTP in addition to HTTPS.')
param allowInsecure bool = false

@description('CPU cores allocated to the API container.')
param apiCpuCores string = '0.25'

@description('Memory allocated to the API container.')
param apiMemory string = '0.5Gi'

@minValue(0)
param apiMinReplicas int = 0

@minValue(1)
param apiMaxReplicas int = 1

@description('Container Apps environment workload profile to run on.')
param workloadProfileName string = 'Consumption'

@secure()
@minLength(32)
param jwtSigningKey string

resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: containerAppsEnvironmentName
}

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: containerRegistryName
}

resource managedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: managedIdentityName
}

resource apiApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: apiAppName
  location: location
  tags: union(tags, { environment: environmentName, 'azd-env-name': environmentName })
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
      secrets: [
        {
          name: 'jwt-signing-key'
          value: jwtSigningKey
        }
      ]
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
          env: [
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
        }
      ]
      scale: {
        minReplicas: apiMinReplicas
        maxReplicas: apiMaxReplicas
      }
    }
  }
}

output apiName string = apiApp.name
output apiResourceId string = apiApp.id
output apiFqdn string = apiApp.properties.configuration.ingress.fqdn
