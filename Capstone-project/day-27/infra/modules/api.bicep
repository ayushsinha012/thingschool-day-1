@description('Environment name (dev), used for tagging.')
param environmentName string

@description('Location for the container app.')
param location string

@description('Tags applied to the container app.')
param tags object = {}

@description('Resource group of the existing shared Container Apps environment. This subscription has a hard quota of one Container Apps environment, so a dedicated VNet-integrated environment could not be created for Day 27 - see README for the verified quota error and the residual-risk consequence.')
param containerAppsEnvironmentResourceGroup string

@description('Name of the existing shared Container Apps environment.')
param containerAppsEnvironmentName string

@description('Resource group name of the existing shared container registry.')
param containerRegistryResourceGroup string

@description('Name of the existing container registry the image is pulled from.')
param containerRegistryName string

@description('Name of the Container App.')
param apiAppName string

@description('Container image (registry/repo:tag) to deploy.')
param apiContainerImage string

@description('Target port the container listens on.')
param apiTargetPort int = 8080

@description('CPU cores allocated to the API container.')
param apiCpuCores string = '0.25'

@description('Memory allocated to the API container.')
param apiMemory string = '0.5Gi'

@minValue(0)
param apiMinReplicas int = 0

@minValue(1)
param apiMaxReplicas int = 1

@description('Azure SQL logical server FQDN, used to compose a credential-free connection string. Not a secret.')
param sqlServerFqdn string

@description('Azure SQL database name.')
param sqlDatabaseName string

@description('Name of the existing Key Vault holding the JWT signing key and integration client secret hash.')
param keyVaultName string

@description('Resource group of the existing Key Vault.')
param keyVaultResourceGroup string

@description('Name of the integration client id. Not a secret.')
param integrationClientId string

param jwtSigningKeySecretName string = 'jwt-signing-key'

param integrationClientSecretHashSecretName string = 'integration-client-secret-hash'

resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: containerAppsEnvironmentName
  scope: resourceGroup(containerAppsEnvironmentResourceGroup)
}

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: containerRegistryName
  scope: resourceGroup(containerRegistryResourceGroup)
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
  scope: resourceGroup(keyVaultResourceGroup)
}

var sqlConnectionString = 'Server=tcp:${sqlServerFqdn},1433;Database=${sqlDatabaseName};Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;'

resource apiApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: apiAppName
  location: location
  tags: union(tags, { environment: environmentName })
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: containerAppsEnvironment.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: apiTargetPort
        transport: 'auto'
        allowInsecure: false
      }
      registries: [
        {
          server: containerRegistry.properties.loginServer
          identity: 'system'
        }
      ]
      secrets: [
        {
          name: jwtSigningKeySecretName
          keyVaultUrl: '${keyVault.properties.vaultUri}secrets/${jwtSigningKeySecretName}'
          identity: 'system'
        }
        {
          name: integrationClientSecretHashSecretName
          keyVaultUrl: '${keyVault.properties.vaultUri}secrets/${integrationClientSecretHashSecretName}'
          identity: 'system'
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
              name: 'Jwt__Key'
              secretRef: jwtSigningKeySecretName
            }
            {
              name: 'IntegrationClient__ClientId'
              value: integrationClientId
            }
            {
              name: 'IntegrationClient__ClientSecretHash'
              secretRef: integrationClientSecretHashSecretName
            }
            {
              name: 'ConnectionStrings__AzureSql'
              value: sqlConnectionString
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
output apiPrincipalId string = apiApp.identity.principalId
