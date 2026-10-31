# Deploying to Azure

> **Status:** deployment-ready, not currently deployed. The template compiles and lints cleanly, and the production database path (EF Core migrations on SQL Server) is tested in CI against a real SQL Server 2022. The steps below have not been run against a live subscription.

[`infra/main.bicep`](../infra/main.bicep) deploys the app to **Azure App Service** (Linux, .NET 10) in one of two modes.

| | Free mode (`databaseMode=Sqlite`, default) | Full mode (`databaseMode=AzureSql`) |
| --- | --- | --- |
| Web | App Service **F1** (free tier) | App Service F1 or B1 |
| Database | SQLite file in App Service's persistent `/home` storage | **Azure SQL Database**, serverless, on the free offer |
| Ongoing cost | None, within F1's limits | None until the SQL free allowance runs out; then the database pauses for the rest of the month |
| Best for | Keeping a demo online indefinitely | Showing the production architecture |

Both modes use HTTPS only, TLS 1.2, FTP disabled, WebSockets for SignalR, a `/health` check and a system-assigned managed identity.

## Cost notes: why there are two modes

**Azure SQL free offer.** Each subscription can have up to 10 free databases, each with 100,000 vCore-seconds of compute and 32 GB of storage a month, for the life of the subscription. When the allowance runs out, the database either pauses until the next month or starts billing; this template chooses **pause**. A free database also pauses itself after 15 minutes without activity ([Microsoft Learn](https://learn.microsoft.com/en-us/azure/azure-sql/database/free-offer)).

This app's ingestion worker touches the database every 5 minutes, so the database never reaches 15 idle minutes and never pauses. At the minimum 0.5 vCore:

```
100,000 vCore-seconds ÷ 0.5 vCore = 200,000 seconds ≈ 55 hours
```

Running continuously, the month's allowance would last about **two and a half days**, and the database would then be offline until the next month. On F1 the app (and the worker) sleeps when nobody visits, which stretches the allowance, but not predictably. This is why full mode suits a short demonstration and free mode suits keeping a demo online.

Ways to make full mode last, for a real deployment:
- Poll less often (`Ingestion__PollMinutes`), so the database can reach its idle pause between ticks.
- Move ingestion out of the web app into a timer-triggered **Azure Function** that writes in short bursts.
- Accept a small bill: pay for serverless usage, or use a provisioned Basic tier.

**App Service F1.** F1 has no Always On, so App Service unloads the app after a period without traffic, and the ingestion worker stops with it. On the next visit the app starts, re-checks the last 48 hours of 5-minute data and reloads the daily reports, so gaps fill themselves. F1 also has a daily CPU quota. Check the current F1 limits before relying on them.

**Subscriptions and credits.** A free trial or Azure for Students subscription is disabled when its credit runs out or expires, and the app goes offline with it ([Microsoft Learn](https://learn.microsoft.com/azure/cost-management-billing/manage/azurestudents-subscription-disabled)). To keep free mode running long-term, the subscription must be pay-as-you-go, using only free-tier resources. Set a **budget alert** (for example, $1) so any unexpected charge is noticed immediately.

## Steps

### Prerequisites

- An Azure subscription.
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli), which includes Bicep.
- .NET 10 SDK and Node.js 22, to build the app.

### 1. Sign in and create a resource group

```bash
az login
az group create --name rg-gridwatch --location canadacentral
```

### 2. Create the infrastructure

Free mode:

```bash
az deployment group create \
  --resource-group rg-gridwatch \
  --template-file infra/main.bicep \
  --parameters infra/main.parameters.json
```

Full mode (choose a strong SQL password; it is stored only in Azure):

```bash
az deployment group create \
  --resource-group rg-gridwatch \
  --template-file infra/main.bicep \
  --parameters infra/main.parameters.json \
  --parameters databaseMode=AzureSql sqlAdminPassword='<strong-password>'
```

Either command can also take `adminApiKey='<random-key>'` to enable the admin endpoint. Generate a key with `openssl rand -base64 32`.

Save the app name for the next step:

```bash
APP=$(az deployment group show -g rg-gridwatch -n main --query properties.outputs.webAppName.value -o tsv)
```

### 3. Build and deploy the app

```bash
cd web && npm ci && npm run build && cd ..            # React build into src/GridWatch.Api/wwwroot
dotnet publish src/GridWatch.Api -c Release -o publish
(cd publish && zip -r ../app.zip .)
az webapp deploy --resource-group rg-gridwatch --name "$APP" --src-path app.zip --type zip
```

On first start the app creates the database (or applies the EF Core migration on Azure SQL), loads the bundled two-week sample, then starts pulling live IESO data.

### 4. Check it

```bash
curl https://$APP.azurewebsites.net/health
curl https://$APP.azurewebsites.net/api/ingestion/runs
az webapp log tail --resource-group rg-gridwatch --name "$APP"
```

### Clean up

```bash
az group delete --name rg-gridwatch --yes --no-wait
```

## Production hardening (not implemented)

- **Passwordless database access:** grant the Web App's managed identity a database user (`CREATE USER [<app-name>] FROM EXTERNAL PROVIDER`) and connect with `Authentication=Active Directory Default`, removing the SQL password.
- **Key Vault** for the admin key, referenced from app settings.
- **Private networking:** a private endpoint and VNet integration instead of the "allow Azure services" firewall rule.
- **Continuous deployment:** a GitHub Actions job that signs in with OpenID Connect (no stored secrets) and runs step 3 on each push to `main`.
- **Data retention:** a scheduled clean-up of 5-minute data older than a set period. The app currently keeps everything it ingests (roughly 600 small rows a day).
