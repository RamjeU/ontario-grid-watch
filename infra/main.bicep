// Ontario Grid Watch on Azure: App Service (Linux, .NET 10) + Azure SQL Database.
// Deploy with: az deployment group create -g <group> -f infra/main.bicep -p infra/main.parameters.json
// See docs/deploy-azure.md for the full walkthrough.

targetScope = 'resourceGroup'

@description('Azure region. Canada Central keeps the data close to Ontario.')
param location string = 'canadacentral'

@description('Short name used to build resource names. Lowercase letters and digits.')
@minLength(3)
@maxLength(12)
param appName string = 'gridwatch'

@description('App Service plan tier. F1 is free but has no Always On, so the ingestion worker sleeps when nobody visits and catches up on the next visit. B1 keeps it running.')
@allowed(['F1', 'B1'])
param appServiceSku string = 'F1'

@description('SQL administrator login.')
param sqlAdminLogin string = 'gridwatchadmin'

@description('SQL administrator password. Passed at deploy time; never committed.')
@secure()
param sqlAdminPassword string

@description('Optional key for POST /api/ingestion/run. Leave empty to disable the admin endpoint.')
@secure()
param adminApiKey string = ''

var suffix = uniqueString(resourceGroup().id)
var webAppName = '${appName}-${suffix}'
var sqlServerName = '${appName}-sql-${suffix}'
var databaseName = 'GridWatch'

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: '${appName}-plan'
  location: location
  kind: 'linux'
  sku: {
    name: appServiceSku
  }
  properties: {
    reserved: true // required for Linux plans
  }
}

resource sqlServer 'Microsoft.Sql/servers@2023-08-01' = {
  name: sqlServerName
  location: location
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

// Lets Azure services (including this App Service) reach the server. A production system would
// use a private endpoint and VNet integration instead.
resource allowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// Serverless General Purpose with the Azure SQL free offer: it pauses instead of billing
// when the monthly free allowance runs out.
resource database 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sqlServer
  name: databaseName
  location: location
  sku: {
    name: 'GP_S_Gen5_2'
    tier: 'GeneralPurpose'
  }
  properties: {
    autoPauseDelay: 60
    minCapacity: json('0.5')
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
  }
}

resource web 'Microsoft.Web/sites@2024-04-01' = {
  name: webAppName
  location: location
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: appServiceSku != 'F1'
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      http20Enabled: true
      webSocketsEnabled: true // SignalR
      healthCheckPath: '/health'
      appSettings: [
        { name: 'Database__Provider', value: 'SqlServer' }
        { name: 'Admin__ApiKey', value: adminApiKey }
        { name: 'Ingestion__SeedSampleData', value: 'true' }
      ]
      connectionStrings: [
        {
          name: 'GridWatch'
          type: 'SQLAzure'
          connectionString: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${databaseName};User ID=${sqlAdminLogin};Password=${sqlAdminPassword};Encrypt=True;TrustServerCertificate=False;Connection Timeout=60'
        }
      ]
    }
  }
}

output webAppName string = web.name
output url string = 'https://${web.properties.defaultHostName}'
output sqlServer string = sqlServer.properties.fullyQualifiedDomainName
