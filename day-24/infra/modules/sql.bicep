@description('Name of the SQL logical server.')
param sqlServerName string

@description('Location for the SQL server and database.')
param location string

@description('Tags applied to the server and database.')
param tags object = {}

@description('Name of the database.')
param sqlDatabaseName string = 'quotesapi'

@description('Entra ID (AAD) login name for the server admin.')
param aadAdminLogin string

@description('Entra ID (AAD) object id for the server admin principal.')
param aadAdminObjectId string

@description('Entra ID tenant id the admin principal belongs to.')
param aadAdminTenantId string = subscription().tenantId

@allowed([
  'User'
  'Group'
  'Application'
])
param aadAdminPrincipalType string = 'User'

@description('Database SKU name.')
param skuName string = 'Basic'

@description('Database SKU tier.')
param skuTier string = 'Basic'

@description('Database max size in bytes.')
param maxSizeBytes int = 2147483648

@description('Adds the 0.0.0.0-0.0.0.0 firewall rule that allows traffic from other Azure resources.')
param allowAzureServices bool = true

@description('Minimum TLS version enforced by the server.')
param minimalTlsVersion string = '1.2'

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: minimalTlsVersion
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      login: aadAdminLogin
      sid: aadAdminObjectId
      tenantId: aadAdminTenantId
      principalType: aadAdminPrincipalType
      azureADOnlyAuthentication: true
    }
  }
}

resource firewallAllowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = if (allowAzureServices) {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
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

output sqlServerName string = sqlServer.name
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output sqlDatabaseName string = sqlDatabase.name
output sqlDatabaseResourceId string = sqlDatabase.id
