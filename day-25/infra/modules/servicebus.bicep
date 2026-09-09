@description('Service Bus namespace name. Must be globally unique.')
param namespaceName string

@description('Location for the namespace.')
param location string

@description('Tags applied to the namespace.')
param tags object = {}

@allowed([
  'Standard'
  'Premium'
])
@description('Basic does not support topics, so only Standard/Premium are allowed here.')
param skuName string = 'Standard'

param topicName string = 'quote-events'

param subscriptionNames array = [
  'sub-audit'
  'sub-notifications'
]

param maxDeliveryCount int = 3

param lockDuration string = 'PT1M'

param defaultMessageTimeToLive string = 'P14D'

resource namespace 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: namespaceName
  location: location
  tags: tags
  sku: {
    name: skuName
    tier: skuName
  }
  properties: {
    zoneRedundant: false
    minimumTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    // No shared access policy is created beyond ARM's implicit default
    // manage-rights policy - the API never authenticates with a
    // connection string or SAS key, only with Microsoft Entra ID tokens
    // via the Managed Identity role assignments in identity.bicep.
    disableLocalAuth: true
  }
}

resource topic 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: namespace
  name: topicName
  properties: {
    defaultMessageTimeToLive: defaultMessageTimeToLive
    requiresDuplicateDetection: false
  }
}

resource subscriptions 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = [
  for subName in subscriptionNames: {
    parent: topic
    name: subName
    properties: {
      maxDeliveryCount: maxDeliveryCount
      lockDuration: lockDuration
      deadLetteringOnMessageExpiration: true
      defaultMessageTimeToLive: defaultMessageTimeToLive
    }
  }
]

output namespaceName string = namespace.name
output namespaceResourceId string = namespace.id
output fullyQualifiedNamespace string = '${namespace.name}.servicebus.windows.net'
output topicName string = topic.name
output subscriptionNames array = subscriptionNames
