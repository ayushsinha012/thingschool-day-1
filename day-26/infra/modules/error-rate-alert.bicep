param appInsightsName string

param alertName string

param location string

param errorRateQuery string

param thresholdPercent int = 5

param evaluationFrequency string = 'PT5M'

param windowSize string = 'PT15M'

param severity int = 3

resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: appInsightsName
}

resource errorRateAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: alertName
  location: location
  properties: {
    description: 'Day 26: ${appInsightsName} request error rate above ${thresholdPercent} percent'
    severity: severity
    enabled: true
    evaluationFrequency: evaluationFrequency
    windowSize: windowSize
    scopes: [
      appInsights.id
    ]
    criteria: {
      allOf: [
        {
          query: errorRateQuery
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    autoMitigate: true
  }
}

output alertId string = errorRateAlert.id
