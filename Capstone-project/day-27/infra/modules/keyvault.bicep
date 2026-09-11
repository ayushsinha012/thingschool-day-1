@description('Name of the Key Vault. Must be globally unique.')
param keyVaultName string

@description('Location for the Key Vault.')
param location string

@description('Tags applied to the Key Vault.')
param tags object = {}

@description('Entra tenant ID that owns the vault.')
param tenantId string = subscription().tenantId

@secure()
@minLength(32)
@description('JWT signing key for the client-credentials scheme. Never written to a param file in plain text - supplied via readEnvironmentVariable() at deploy time and stored only as this Key Vault secret.')
param jwtSigningKey string

@secure()
@minLength(16)
@description('Client secret for the one integration client this pass defines. Stored only as a Key Vault secret hash reference is derived from this at deploy time.')
param integrationClientSecretHash string

param jwtSigningKeySecretName string = 'jwt-signing-key'

param integrationClientSecretHashName string = 'integration-client-secret-hash'

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    tenantId: tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    publicNetworkAccess: 'Enabled'
  }
}

resource jwtSigningKeySecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: jwtSigningKeySecretName
  properties: {
    value: jwtSigningKey
  }
}

resource integrationClientSecretHashSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: integrationClientSecretHashName
  properties: {
    value: integrationClientSecretHash
  }
}

output keyVaultName string = keyVault.name
output keyVaultUri string = keyVault.properties.vaultUri
output keyVaultResourceId string = keyVault.id
