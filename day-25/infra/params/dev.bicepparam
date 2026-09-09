using '../main.bicep'

param environment = 'dev'
param location = 'centralindia'
param tags = {
  day: '25'
  environment: 'dev'
}

param sharedResourceGroup = 'thinkschool-rg'
param containerAppsEnvironmentName = 'thinkschool-env'
param containerRegistryName = 'cr2i2oapij4zsrc'
param apiContainerImage = 'cr2i2oapij4zsrc.azurecr.io/quotes-api/quotes-api-quotesapi-thinkschool:day21-hybridcache-1788429669'

param apiAppName = 'quotes-api-day25-dev'
param apiCpuCores = '0.25'
param apiMemory = '0.5Gi'
param apiMinReplicas = 0
param apiMaxReplicas = 1

// The existing QuotesApi Entra app registration - reused, not duplicated.
// Tenant and client/audience IDs are public identifiers, not secrets.
param entraTenantId = '8d46a076-d093-416d-a57b-8692cde13bf8'
param entraClientId = '953b5bcb-682b-47b4-a116-8936323f5bec'
param entraAudience = 'api://953b5bcb-682b-47b4-a116-8936323f5bec'

param sqlServerName = 'sql-quotesapi-day25-dev'
param sqlDatabaseName = 'quotesapi'
param sqlAadAdminLogin = 'ayush.sinha7@s.amity.edu'
param sqlAadAdminObjectId = '4eece2d1-e3b7-4754-a99a-afc480a96fc8'
param sqlSkuName = 'Basic'
param sqlSkuTier = 'Basic'

param serviceBusNamespaceName = 'sb-quotesapi-day25-dev'
param serviceBusTopicName = 'quote-events'
param serviceBusSubscriptionNames = [
  'sub-audit'
  'sub-notifications'
]

param keyVaultName = 'kv-quotesapi-d25-dev'

// Never written here in plain text - readEnvironmentVariable() reads it
// from the deployer's shell at deploy time and fails loudly if unset.
// Set DEV_JWT_SIGNING_KEY (32+ chars) before deploying.
param jwtSigningKey = readEnvironmentVariable('DEV_JWT_SIGNING_KEY')
