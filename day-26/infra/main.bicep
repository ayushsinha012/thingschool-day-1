targetScope = 'resourceGroup'

@description('Existing shared Application Insights resource this template reuses - not created here.')
param appInsightsName string = 'appi-2i2oapij4zsrc'

@description('Existing shared Service Bus namespace this template reuses - not created here.')
param serviceBusNamespaceName string = 'sb-quotesapi-thinkschool'

@description('Existing shared Service Bus topic this template reuses - not created here.')
param serviceBusTopicName string = 'quote-events'

@description('New, additive Day 26 subscriptions on the existing topic. Existing sub-audit/sub-notifications are untouched.')
param day26SubscriptionNames array = [
  'sub-day26-audit'
  'sub-day26-notifications'
]

param location string = resourceGroup().location

param errorRateAlertName string = 'quotes-api-day26-error-rate-alert'

param errorRateQuery string = 'requests | where cloud_RoleName == \'quotes-api-day26\' | summarize Total = count(), Failed = countif(success == false) | extend ErrorRatePercent = iff(Total == 0, 0.0, round(100.0 * Failed / Total, 2)) | where ErrorRatePercent > 5'

module serviceBusSubscriptions 'modules/servicebus-subscriptions.bicep' = {
  name: 'day26-servicebus-subscriptions'
  params: {
    namespaceName: serviceBusNamespaceName
    topicName: serviceBusTopicName
    subscriptionNames: day26SubscriptionNames
  }
}

module errorRateAlert 'modules/error-rate-alert.bicep' = {
  name: 'day26-error-rate-alert'
  params: {
    appInsightsName: appInsightsName
    alertName: errorRateAlertName
    location: location
    errorRateQuery: errorRateQuery
  }
}

output alertId string = errorRateAlert.outputs.alertId
