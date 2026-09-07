// API module - models the QuotesApi Azure Container App.
//
// Reuses the existing shared Container Apps environment, container
// registry, and user-assigned managed identity (already provisioned and
// already granted AcrPull - see day-1/QuotesApi/infra/resources.bicep)
// rather than creating duplicates of any of them.

@description('Environment name (dev or prod) - used only for tagging.')
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

@description('Whether ingress accepts plain HTTP in addition to HTTPS. The real production quotes-api currently has this set to true.')
param allowInsecure bool = false

@description('CPU cores allocated to the API container (Container Apps consumption plan combinations, e.g. 0.25/0.5/0.75/1).')
param apiCpuCores string = '0.5'

@description('Memory allocated to the API container, e.g. 0.5Gi/1Gi/2Gi.')
param apiMemory string = '1Gi'

@minValue(0)
@description('Minimum replica count. 0 is safe for a dev/demo environment that can scale to zero.')
param apiMinReplicas int = 1

@minValue(1)
@description('Maximum replica count.')
param apiMaxReplicas int = 10

@description('Container Apps environment workload profile to run on.')
param workloadProfileName string = 'Consumption'

@secure()
@minLength(32)
@description('Signing key bound to Jwt:Key - required, never defaulted, never written to a param file in plain text (supply via readEnvironmentVariable() at deploy time).')
param jwtSigningKey string

@secure()
@description('Connection string bound to ConnectionStrings:Redis. Optional - empty means the env var/secret is omitted entirely, same as an environment with no Redis wired up.')
param redisConnectionString string = ''

@secure()
@description('Application Insights connection string. Optional - empty means the env var/secret is omitted entirely.')
param appInsightsConnectionString string = ''

@description('Allow-listed browser origin for CORS in production. Empty means no extra origin is added (never a wildcard).')
param corsProductionOrigin string = ''

@description('Whether to mount the shared outbox Azure Files volume. Only meaningful where that storage definition already exists on the target environment (true for the real prod environment).')
param enableOutboxVolume bool = false

@description('Name of the storage definition (registered on the Container Apps environment) backing the outbox volume.')
param outboxStorageName string = 'quotesapi-outbox-data'

resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: containerAppsEnvironmentName
}

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: containerRegistryName
}

resource managedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: managedIdentityName
}

var baseEnv = [
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

var appInsightsEnv = empty(appInsightsConnectionString) ? [] : [
  {
    name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
    secretRef: 'app-insights-connection-string'
  }
]

var redisEnv = empty(redisConnectionString) ? [] : [
  {
    name: 'ConnectionStrings__Redis'
    secretRef: 'redis-connection-string'
  }
]

var corsEnv = empty(corsProductionOrigin) ? [] : [
  {
    name: 'Cors__ProductionOrigins__0'
    value: corsProductionOrigin
  }
]

var secrets = concat(
  [
    {
      name: 'jwt-signing-key'
      value: jwtSigningKey
    }
  ],
  empty(redisConnectionString) ? [] : [
    {
      name: 'redis-connection-string'
      value: redisConnectionString
    }
  ],
  empty(appInsightsConnectionString) ? [] : [
    {
      name: 'app-insights-connection-string'
      value: appInsightsConnectionString
    }
  ]
)

resource apiApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: apiAppName
  location: location
  tags: union(tags, { environment: environmentName })
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
      secrets: secrets
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
          env: concat(baseEnv, appInsightsEnv, redisEnv, corsEnv)
          volumeMounts: enableOutboxVolume ? [
            {
              volumeName: 'outbox-data'
              mountPath: '/data'
            }
          ] : []
        }
      ]
      scale: {
        minReplicas: apiMinReplicas
        maxReplicas: apiMaxReplicas
      }
      volumes: enableOutboxVolume ? [
        {
          name: 'outbox-data'
          storageType: 'AzureFile'
          storageName: outboxStorageName
        }
      ] : []
    }
  }
}

output apiName string = apiApp.name
output apiResourceId string = apiApp.id
output apiFqdn string = apiApp.properties.configuration.ingress.fqdn
