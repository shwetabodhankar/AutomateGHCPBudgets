# AI Credit Budget Automation

This prototype uses a native SharePoint list form for intake, Power Automate for
the single manager approval, and a .NET 8 Azure Function for GitHub budget
validation and fulfillment.

## Prototype configuration

- SharePoint site: `https://mngenvmcap628198.sharepoint.com/sites/demosite/`
- SharePoint list: `AI Budget Requests`
- Fixed approver: `admin@mngenvmcap628198.onmicrosoft.com`
- Azure Function routes:
  - `POST /api/budget-requests/validate`
  - `POST /api/budget-requests/fulfill`

## SharePoint intake list

The native SharePoint **New Item** form exposes only:

- Request Summary
- GitHub Username
- Cost Center
- Requested Total USD
- Justification
- Expiration Date

Validation, approval, fulfillment, and audit columns are hidden from the native
form and are managed by automation.

To create or update the list, sign in to Azure CLI with Microsoft Graph
permission to manage lists on the target site, then run:

```powershell
.\deployment\Deploy-SharePointList.ps1 `
  -SiteUrl 'https://mngenvmcap628198.sharepoint.com/sites/demosite/' `
  -ListName 'AI Budget Requests' `
  -ManagerEmail 'admin@mngenvmcap628198.onmicrosoft.com'
```

The script is idempotent: it reuses the list and skips columns that already
exist.

## Power Automate flow

[`deployment/power-automate-flow.json`](deployment/power-automate-flow.json)
defines the actions and field mappings for the cloud flow. Configure these
environment values:

- `AI_BUDGET_VALIDATION_ENDPOINT`
- `AI_BUDGET_FULFILLMENT_ENDPOINT`
- `AI_BUDGET_FUNCTION_KEY`

The flow triggers only when an item is created, avoiding update-trigger loops.
It validates before approval and revalidates during fulfillment.

## Azure Function configuration

Copy
[`src/AiBudgetAutomation.Functions/local.settings.example.json`](src/AiBudgetAutomation.Functions/local.settings.example.json)
to `local.settings.json` for local development. Never commit the GitHub token.

Required settings:

- `GitHub__Enterprise`: GitHub Enterprise slug
- `GitHub__Token`: GitHub App installation token or fine-grained token with
  enterprise billing read/write access
- `GitHub__ApiVersion`: defaults to `2026-03-10`

The validation endpoint checks required fields, positive whole-dollar amount,
future expiration, the current individual budget, cost-center remaining
capacity, and universal remaining capacity. Fulfillment creates or updates the
user-scoped AI-credit budget and reads it back for verification.

To deploy the selected prototype configuration, set the credential only in the
current shell and run the deployment script:

```powershell
$env:GITHUB_TOKEN = '<temporary token value>'
.\deployment\Deploy-AzureFunction.ps1 `
  -ResourceGroup 'rg-ai-budget-prototype' `
  -Location 'westus2' `
  -FunctionAppName 'func-ai-budget-prototype' `
  -GitHubEnterprise 'vnova'
Remove-Item Env:\GITHUB_TOKEN
```

The script creates `rg-ai-budget-prototype` when it does not exist. The Azure
deployment identity therefore needs permission to create resource groups at
the subscription scope. If the identity is restricted to a resource group,
create the empty resource group first and grant the identity Contributor access
to that group.

The deployment script stops before provisioning if the token is missing or the
Function App name is unavailable. It does not print the token.

## Build and test

```powershell
dotnet build .\AutomateAIBudgets.slnx --configuration Release
dotnet test .\AutomateAIBudgets.slnx --configuration Release
```
