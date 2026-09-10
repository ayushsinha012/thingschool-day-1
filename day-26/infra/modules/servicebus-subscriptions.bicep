param namespaceName string

param topicName string

param subscriptionNames array

param maxDeliveryCount int = 3

param lockDuration string = 'PT1M'

resource namespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' existing = {
  name: namespaceName
}

resource topic 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' existing = {
  parent: namespace
  name: topicName
}

resource subscriptions 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = [for name in subscriptionNames: {
  parent: topic
  name: name
  properties: {
    maxDeliveryCount: maxDeliveryCount
    lockDuration: lockDuration
    deadLetteringOnMessageExpiration: true
  }
}]
