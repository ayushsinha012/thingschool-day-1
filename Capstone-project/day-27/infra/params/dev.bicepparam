using '../main.bicep'

param environment = 'dev'
param location = 'centralindia'
param networkLocation = 'centralindia'
param tags = {
  day: '27'
  environment: 'dev'
  project: 'maintainxpert'
}

param sharedResourceGroup = 'thinkschool-rg'
param containerRegistryName = 'cr2i2oapij4zsrc'
param apiContainerImage = 'cr2i2oapij4zsrc.azurecr.io/maintainxpert/maintainxpert-api-day27-dev:latest'

param vnetName = 'vnet-maintainxpert-day27-dev'
param peSubnetPrefix = '10.70.2.0/24'

param containerAppsEnvironmentName = 'thinkschool-env'

param apiAppName = 'maintainxpert-api-day27-dev'
param apiCpuCores = '0.25'
param apiMemory = '0.5Gi'
param apiMinReplicas = 0
param apiMaxReplicas = 1

param sqlServerName = 'sql-maintainxpert-day27-dev'
param sqlDatabaseName = 'maintainxpert'
param sqlAadAdminLogin = 'ayush.sinha7@s.amity.edu'
param sqlAadAdminObjectId = '4eece2d1-e3b7-4754-a99a-afc480a96fc8'
param sqlSkuName = 'Basic'
param sqlSkuTier = 'Basic'
param sqlPublicNetworkAccess = 'Enabled'

param keyVaultName = 'kv-maintainxpert-d27-dev'

param integrationClientId = 'maintainxpert-integration'

param jwtSigningKey = readEnvironmentVariable('MX_JWT_SIGNING_KEY')
param integrationClientSecretHash = readEnvironmentVariable('MX_CLIENT_SECRET_HASH')
