// SQL module - NOT wired into the running QuotesApi.
//
// The deployed QuotesApi Container App persists data in SQLite
// (ConnectionStrings__DefaultConnection = "Data Source=/tmp/quotes.db",
// confirmed via `az containerapp show`), not Azure SQL. An Azure SQL
// Server does exist in thinkschool-rg (thinkschool-day7-sql-0c0dda), but it
// is an unrelated Day-7 exercise resource - this module never references,
// modifies, or reuses it.
//
// This module exists to satisfy the Day 23 exercise's explicit requirement
// for a SQL module, modeling what a SQL-backed QuotesApi persistence layer
// would look like: a dedicated, separately-named logical server + database,
// Entra-ID-only authentication so no SQL admin password ever needs to
// exist, let alone be committed. See day-23/README.md for the full
// discrepancy writeup.

@description('Name of the SQL logical server. Must be globally unique and must never collide with the existing thinkschool-day7-sql-* server.')
param sqlServerName string

@description('Location for the SQL server and database.')
param location string

@description('Tags applied to the server and database.')
param tags object = {}

@description('Name of the database.')
param sqlDatabaseName string = 'quotesapi'

@description('Entra ID (AAD) login name (UPN or group display name) for the server admin.')
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
@description('Entra admin principal type.')
param aadAdminPrincipalType string = 'User'

@description('Database SKU name, e.g. Basic, S0, S1, GP_S_Gen5_1.')
param skuName string = 'Basic'

@description('Database SKU tier, e.g. Basic, Standard, GeneralPurpose.')
param skuTier string = 'Basic'

@description('Database max size in bytes. Defaults to 2 GB, the Basic tier ceiling.')
param maxSizeBytes int = 2147483648

@description('Adds the special 0.0.0.0-0.0.0.0 firewall rule that allows traffic from other Azure resources (e.g. Container Apps outbound) to reach this server.')
param allowAzureServices bool = true

@description('Minimum TLS version enforced by the server.')
param minimalTlsVersion string = '1.2'

// Entra-ID-only authentication - there is no SQL login/password on this
// server at all, so no secret of any kind is needed to stand it up.
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
