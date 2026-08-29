// Identical in shape to day-1/QuotesApi/infra/modules/fetch-container-image.bicep
// (kept as its own copy rather than a cross-project reference so this azd
// project stays self-contained - it is a separate azd project/environment
// from QuotesApi's, sharing only the pre-existing resource group,
// environment, and registry those already point at).
param exists bool
param name string

resource existingApp 'Microsoft.App/containerApps@2023-05-02-preview' existing = if (exists) {
  name: name
}

output containers array = exists ? existingApp!.properties.template.containers : []
