# Deploying to Azure

This guide puts Ontario Grid Watch on **Azure App Service** (Linux, .NET 10) with an **Azure SQL Database**, using the Bicep template in [`infra/main.bicep`](../infra/main.bicep).

## What gets created

| Resource | Tier | Notes |
| --- | --- | --- |
| App Service plan | F1 (free) by default | Set `appServiceSku=B1` for Always On (about US$13/month). |
| Web App | Linux, `DOTNETCORE\|10.0` | HTTPS only, TLS 1.2, FTP off, WebSockets on (SignalR), health check on `/health`. |
| Azure SQL logical server | - | TLS 1.2 minimum; firewall allows Azure services only. |
| Azure SQL Database | Serverless GP_S_Gen5_2 with the **free offer** | Auto-pauses when the monthly free allowance runs out rather than billing. |

On the **F1** tier there is no Always On, so App Service unloads the app after about 20 minutes without traffic and the ingestion worker stops with it. When the next visitor arrives, the app starts, re-checks the last 48 hours of real-time data and reloads the daily reports, so gaps fill themselves. On **B1** the worker runs continuously.

> Check current pricing and free-offer terms on the Azure website before deploying. They change.

## Prerequisites

- An Azure subscription (a free account works).
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) 2.60 or later (it includes Bicep).
- .NET 10 SDK and Node.js 22, to build the app.

## 1. Sign in and create a resource group

```bash
az login
az group create --name rg-gridwatch --location canadacentral
```

## 2. Create the infrastructure

Choose a strong SQL password (at least 8 characters, with upper case, lower case, a digit and a symbol). The password is passed on the command line and stored only in Azure, never in the repo.

```bash
az deployment group create \
  --resource-group rg-gridwatch \
  --template-file infra/main.bicep \
  --parameters infra/main.parameters.json \
  --parameters sqlAdminPassword='<your-strong-password>' adminApiKey='<optional-random-key>'
```

The output includes `webAppName` and `url`. Save the app name for the next step:

```bash
APP=$(az deployment group show -g rg-gridwatch -n main --query properties.outputs.webAppName.value -o tsv)
```

To generate a random admin key: `openssl rand -base64 32`.

## 3. Build and deploy the app

```bash
# Build the React UI into src/GridWatch.Api/wwwroot
cd web && npm ci && npm run build && cd ..

# Publish the API (it includes the UI) and zip it
dotnet publish src/GridWatch.Api -c Release -o publish
(cd publish && zip -r ../app.zip .)

az webapp deploy --resource-group rg-gridwatch --name "$APP" --src-path app.zip --type zip
```

On first start the app applies the EF Core migration to Azure SQL, loads the bundled two-week sample, then starts pulling live data from the IESO.

## 4. Check it

```bash
curl https://$APP.azurewebsites.net/health                     # Healthy
curl https://$APP.azurewebsites.net/api/ingestion/runs          # recent ingestion attempts
az webapp log tail --resource-group rg-gridwatch --name "$APP"  # live logs
```

Open the URL from step 2 for the dashboard, or `/docs` for the interactive API reference.

## Clean up

```bash
az group delete --name rg-gridwatch --yes --no-wait
```

## Going further

These are deliberate simplifications for a portfolio deployment:

- **Passwordless database access.** The Web App already has a system-assigned managed identity. Make it a database user (`CREATE USER [<app-name>] FROM EXTERNAL PROVIDER`) and switch the connection string to `Authentication=Active Directory Default`. The SQL password then disappears from the configuration.
- **Key Vault.** Store `Admin__ApiKey` in Azure Key Vault and reference it from the app setting.
- **Private networking.** Replace the "allow Azure services" firewall rule with a private endpoint and VNet integration.
- **Continuous deployment.** Add a GitHub Actions job that signs in with OpenID Connect (federated credentials, no stored secrets) and runs step 3 on every push to `main`.
