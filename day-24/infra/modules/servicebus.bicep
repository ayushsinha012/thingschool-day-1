@description('Service Bus namespace name.')
param namespaceName string

@description('Location for the namespace.')
param location string

@description('Tags applied to the namespace.')
param tags object = {}

@allowed([
  'Standard'
  'Premium'
])
param skuName string = 'Standard'

param zoneRedundant bool = false

param topicName string = 'quote-events'

param subscriptionNames array = [
  'sub-audit'
  'sub-notifications'
]

param maxDeliveryCount int = 3

param lockDuration string = 'PT1M'

param deadLetteringOnMessageExpiration bool = true

param defaultMessageTimeToLive string = 'P14D'

param requiresDuplicateDetection bool = false

param duplicateDetectionHistoryTimeWindow string = 'PT10M'

resource namespace 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: namespaceName
  location: location
  tags: tags
  sku: {
    name: skuName
    tier: skuName
  }
  properties: {
    zoneRedundant: zoneRedundant
    minimumTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

resource topic 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: namespace
  name: topicName
  properties: {
    defaultMessageTimeToLive: defaultMessageTimeToLive
    requiresDuplicateDetection: requiresDuplicateDetection
    duplicateDetectionHistoryTimeWindow: requiresDuplicateDetection ? duplicateDetectionHistoryTimeWindow : null
  }
}

resource subscriptions 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = [
  for subName in subscriptionNames: {
    parent: topic
    name: subName
    properties: {
      maxDeliveryCount: maxDeliveryCount
      lockDuration: lockDuration
      deadLetteringOnMessageExpiration: deadLetteringOnMessageExpiration
      defaultMessageTimeToLive: defaultMessageTimeToLive
    }
  }
]

output namespaceName string = namespace.name
output topicName string = topic.name
output subscriptionNames array = subscriptionNames
