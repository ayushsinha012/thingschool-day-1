@description('Location for the private endpoint.')
param location string

@description('Tags applied to the private endpoint.')
param tags object = {}

@description('Name of the private endpoint for the SQL server.')
param sqlPrivateEndpointName string

@description('Resource ID of the subnet the private endpoint NIC is placed in.')
param peSubnetId string

@description('Resource ID of the SQL logical server the private endpoint targets.')
param sqlServerResourceId string

@description('Resource ID of the VNet the private DNS zone is linked to.')
param vnetId string

@description('Name of the VNet the private DNS zone is linked to.')
param vnetName string

var sqlPrivateDnsZoneName = 'privatelink${environment().suffixes.sqlServerHostname}'

resource sqlPrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-11-01' = {
  name: sqlPrivateEndpointName
  location: location
  tags: tags
  properties: {
    subnet: {
      id: peSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: sqlPrivateEndpointName
        properties: {
          privateLinkServiceId: sqlServerResourceId
          groupIds: [
            'sqlServer'
          ]
        }
      }
    ]
  }
}

resource sqlPrivateDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: sqlPrivateDnsZoneName
  location: 'global'
  tags: tags
}

resource sqlPrivateDnsZoneVnetLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: sqlPrivateDnsZone
  name: '${vnetName}-link'
  location: 'global'
  properties: {
    virtualNetwork: {
      id: vnetId
    }
    registrationEnabled: false
  }
}

resource sqlPrivateDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-11-01' = {
  parent: sqlPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: sqlPrivateDnsZoneName
        properties: {
          privateDnsZoneId: sqlPrivateDnsZone.id
        }
      }
    ]
  }
  dependsOn: [
    sqlPrivateDnsZoneVnetLink
  ]
}

output sqlPrivateEndpointId string = sqlPrivateEndpoint.id
output sqlPrivateEndpointName string = sqlPrivateEndpoint.name
output sqlPrivateDnsZoneId string = sqlPrivateDnsZone.id
output sqlPrivateDnsZoneName string = sqlPrivateDnsZone.name
