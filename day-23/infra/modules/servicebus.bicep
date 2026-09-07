// Service Bus module - models the real sb-quotesapi-thinkschool namespace
// (topic quote-events, subscriptions sub-audit/sub-notifications).
//
// createNamespace controls whether this module provisions a brand-new
// namespace (dev) or manages the topic/subscriptions under an
// already-existing namespace (prod - sb-quotesapi-thinkschool, never
// recreated). Topic/subscription children are declared with their fully
// qualified '<namespace>/<child>' name rather than a `parent:` reference,
// so the same child-resource code works unchanged in both cases.

@description('Service Bus namespace name.')
param namespaceName string

@description('Location for a newly-created namespace. Ignored when createNamespace is false.')
param location string

@description('Tags applied to a newly-created namespace. Ignored when createNamespace is false.')
param tags object = {}

@description('true creates a new namespace (dev); false assumes namespaceName already exists (prod) and only manages the topic/subscriptions under it.')
param createNamespace bool = true

@allowed([
  'Standard'
  'Premium'
])
@description('SKU for a newly-created namespace. Basic does not support topics, so only Standard/Premium are allowed here.')
param skuName string = 'Standard'

@description('Whether a newly-created namespace is zone-redundant.')
param zoneRedundant bool = false

@description('Name of the topic.')
param topicName string = 'quote-events'

@description('Names of the subscriptions to create under the topic.')
param subscriptionNames array = [
  'sub-audit'
  'sub-notifications'
]

@description('Max delivery count before a message is dead-lettered.')
param maxDeliveryCount int = 3

@description('Lock duration for each subscription (ISO 8601 duration).')
param lockDuration string = 'PT1M'

@description('Whether expired messages are dead-lettered instead of dropped.')
param deadLetteringOnMessageExpiration bool = true

@description('Default message time-to-live for the topic and its subscriptions (ISO 8601 duration).')
param defaultMessageTimeToLive string = 'P14D'

@description('Whether duplicate detection is enabled on the topic.')
param requiresDuplicateDetection bool = false

@description('Duplicate detection history window (ISO 8601 duration). Only meaningful when requiresDuplicateDetection is true.')
param duplicateDetectionHistoryTimeWindow string = 'PT10M'

resource newNamespace 'Microsoft.ServiceBus/namespaces@2024-01-01' = if (createNamespace) {
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

// Fully qualified name rather than `parent: newNamespace` deliberately -
// newNamespace only exists when createNamespace is true (prod references
// an already-existing namespace instead), so there is no single resource
// symbol that could serve as `parent:` in both cases.
resource topic 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  #disable-next-line use-parent-property
  name: '${namespaceName}/${topicName}'
  properties: {
    defaultMessageTimeToLive: defaultMessageTimeToLive
    requiresDuplicateDetection: requiresDuplicateDetection
    duplicateDetectionHistoryTimeWindow: requiresDuplicateDetection ? duplicateDetectionHistoryTimeWindow : null
  }
  dependsOn: createNamespace ? [newNamespace] : []
}

// Same reasoning as `topic` above - `parent: topic` would work here since
// topic always exists, but the fully qualified name keeps both child
// resources in this file consistent with each other.
resource subscriptions 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = [
  for subName in subscriptionNames: {
    #disable-next-line use-parent-property
    name: '${namespaceName}/${topicName}/${subName}'
    properties: {
      maxDeliveryCount: maxDeliveryCount
      lockDuration: lockDuration
      deadLetteringOnMessageExpiration: deadLetteringOnMessageExpiration
      defaultMessageTimeToLive: defaultMessageTimeToLive
    }
    dependsOn: [
      topic
    ]
  }
]

output namespaceName string = namespaceName
output topicName string = topicName
output subscriptionNames array = subscriptionNames
