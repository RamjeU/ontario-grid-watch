// Ontario Grid Watch on Azure App Service (Linux, .NET 10), in one of two modes:
//   databaseMode = 'Sqlite'   (default) free: SQLite file in App Service's persistent /home storage
//   databaseMode = 'AzureSql' full: Azure SQL Database (serverless, free offer)
// Deploy with: az deployment group create -g <group> -f infra/main.bicep -p infra/main.parameters.json
// See docs/deploy-azure.md for the walkthrough and the cost trade-offs.

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

@description('Where data is stored. Sqlite costs nothing; AzureSql uses Azure SQL Database. See docs/deploy-azure.md.')
@allowed(['Sqlite', 'AzureSql'])
param databaseMode string = 'Sqlite'

@description('SQL administrator login (AzureSql mode only).')
param sqlAdminLogin string = 'gridwatchadmin'

@description('SQL administrator password (AzureSql mode only). Passed at deploy time; never committed.')
@secure()
param sqlAdminPassword string = ''

@description('Optional key for POST /api/ingestion/run. Leave empty to disable the admin endpoint.')
@secure()
param adminApiKey string = ''

var useAzureSql = databaseMode == 'AzureSql'
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

resource sqlServer 'Microsoft.Sql/servers@2023-08-01' = if (useAzureSql) {
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
resource allowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01' = if (useAzureSql) {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// Serverless General Purpose with the Azure SQL free offer. When the monthly free compute runs out
// the database pauses until the next month instead of billing (see the cost notes in the guide).
resource database 'Microsoft.Sql/servers/databases@2023-08-01' = if (useAzureSql) {
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

var commonSettings = [
  { name: 'Admin__ApiKey', value: adminApiKey }
  { name: 'Ingestion__SeedSampleData', value: 'true' }
]

// /home is App Service's persistent storage: it survives restarts and redeploys.
var sqliteSettings = [
  { name: 'Database__Provider', value: 'Sqlite' }
  { name: 'ConnectionStrings__GridWatch', value: 'Data Source=/home/data/gridwatch.db' }
]

var sqlConnectionStrings = useAzureSql
  ? [
      {
        name: 'GridWatch'
        type: 'SQLAzure'
        connectionString: 'Server=tcp:${sqlServer!.properties.fullyQualifiedDomainName},1433;Database=${databaseName};User ID=${sqlAdminLogin};Password=${sqlAdminPassword};Encrypt=True;TrustServerCertificate=False;Connection Timeout=60'
      }
    ]
  : []

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
      appSettings: concat(commonSettings, useAzureSql ? [{ name: 'Database__Provider', value: 'SqlServer' }] : sqliteSettings)
      connectionStrings: sqlConnectionStrings
    }
  }
}

output webAppName string = web.name
output url string = 'https://${web.properties.defaultHostName}'
output databaseMode string = databaseMode
