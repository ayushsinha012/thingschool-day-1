@description('Name of the SQL logical server. Must be globally unique.')
param sqlServerName string

@description('Location for the SQL server and database.')
param location string

@description('Tags applied to the server and database.')
param tags object = {}

@description('Name of the database.')
param sqlDatabaseName string = 'maintainxpert'

@description('Entra ID (AAD) login name (UPN) for the server admin.')
param aadAdminLogin string

@description('Entra ID (AAD) object id for the server admin principal.')
param aadAdminObjectId string

@description('Entra ID tenant id the admin principal belongs to.')
param aadAdminTenantId string = subscription().tenantId

@description('Database SKU name, e.g. Basic.')
param skuName string = 'Basic'

@description('Database SKU tier, e.g. Basic.')
param skuTier string = 'Basic'

@description('Database max size in bytes. Defaults to 2 GB, the Basic tier ceiling.')
param maxSizeBytes int = 2147483648

@allowed([
  'Enabled'
  'Disabled'
])
@description('Public network access on the SQL server. Kept Enabled until the private endpoint path is verified, then reassessed.')
param publicNetworkAccess string = 'Enabled'

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: publicNetworkAccess
    administrators: {
      administratorType: 'ActiveDirectory'
      login: aadAdminLogin
      sid: aadAdminObjectId
      tenantId: aadAdminTenantId
      principalType: 'User'
      azureADOnlyAuthentication: true
    }
  }
}

resource firewallAllowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  tags: tags
  sku: {
    name: skuName
    tier: skuTier
  }
  properties: {
    maxSizeBytes: maxSizeBytes
  }
}

output sqlServerId string = sqlServer.id
output sqlServerName string = sqlServer.name
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output sqlDatabaseName string = sqlDatabase.name
output sqlDatabaseResourceId string = sqlDatabase.id
