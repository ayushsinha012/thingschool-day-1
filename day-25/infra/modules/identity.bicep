@description('Principal ID of the API Container App system-assigned managed identity.')
param apiPrincipalId string

@description('Resource ID of the Service Bus namespace the API identity needs data-plane access to.')
param serviceBusNamespaceResourceId string

@description('Resource ID of the Key Vault the API identity needs to read the JWT signing key secret from.')
param keyVaultResourceId string

var serviceBusDataSenderRoleId = '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39'
var serviceBusDataReceiverRoleId = '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0'
var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2024-01-01' existing = {
  name: last(split(serviceBusNamespaceResourceId, '/'))
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: last(split(keyVaultResourceId, '/'))
}

// Least-privilege Service Bus data-plane access: the API both publishes
// (QuoteEventPublisher / OutboxRelayWorker) and consumes
// (SubscriptionAWorker / SubscriptionBWorker) via DefaultAzureCredential,
// so it genuinely needs both Sender and Receiver - never Owner/Manage,
// and never a connection string or SAS key.
resource serviceBusDataSender 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(serviceBusNamespace.id, apiPrincipalId, 'ServiceBusDataSender')
  scope: serviceBusNamespace
  properties: {
    principalId: apiPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', serviceBusDataSenderRoleId)
  }
}

resource serviceBusDataReceiver 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(serviceBusNamespace.id, apiPrincipalId, 'ServiceBusDataReceiver')
  scope: serviceBusNamespace
  properties: {
    principalId: apiPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', serviceBusDataReceiverRoleId)
  }
}

resource keyVaultSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, apiPrincipalId, 'KeyVaultSecretsUser')
  scope: keyVault
  properties: {
    principalId: apiPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
  }
}
