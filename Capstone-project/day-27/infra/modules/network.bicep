@description('Name of the VNet.')
param vnetName string

@description('Location for all networking resources.')
param location string

@description('Tags applied to every resource this module creates.')
param tags object = {}

@description('Address space for the VNet.')
param vnetAddressPrefix string = '10.70.0.0/16'

@description('Subnet used for private endpoints.')
param peSubnetName string = 'snet-pe'

@description('Address prefix for the private endpoint subnet.')
param peSubnetPrefix string = '10.70.2.0/24'

resource vnet 'Microsoft.Network/virtualNetworks@2023-11-01' = {
  name: vnetName
  location: location
  tags: tags
  properties: {
    addressSpace: {
      addressPrefixes: [
        vnetAddressPrefix
      ]
    }
    subnets: [
      {
        name: peSubnetName
        properties: {
          addressPrefix: peSubnetPrefix
          privateEndpointNetworkPolicies: 'Disabled'
        }
      }
    ]
  }
}

output vnetId string = vnet.id
output vnetName string = vnet.name
output peSubnetId string = vnet.properties.subnets[0].id
