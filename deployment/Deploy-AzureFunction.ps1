[CmdletBinding()]
param(
    [Parameter()]
    [string]$ResourceGroup = 'rg-ai-budget-prototype',

    [Parameter()]
    [string]$Location = 'westus2',

    [Parameter()]
    [string]$FunctionAppName = 'func-ai-budget-prototype',

    [Parameter()]
    [string]$GitHubEnterprise = 'vnova'
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) {
    throw 'Set GITHUB_TOKEN to a GitHub App installation token or fine-grained token with enterprise billing read/write permission before deployment.'
}

if ([uri]::IsWellFormedUriString($GitHubEnterprise, [UriKind]::Absolute)) {
    $enterpriseUri = [uri]$GitHubEnterprise
    $GitHubEnterprise = $enterpriseUri.Segments[-1].Trim('/')
}

if ($GitHubEnterprise -notmatch '^[A-Za-z0-9-]+$') {
    throw 'GitHubEnterprise must be an enterprise slug or an enterprise URL.'
}

$subscriptionId = az account show --query id -o tsv
if ([string]::IsNullOrWhiteSpace($subscriptionId)) {
    throw 'No active Azure CLI subscription was found. Run az login first.'
}

$availabilityBody = @{
    name = $FunctionAppName
    type = 'Microsoft.Web/sites'
} | ConvertTo-Json -Compress

$managementToken = az account get-access-token `
    --resource 'https://management.azure.com/' `
    --query accessToken `
    --output tsv

if ([string]::IsNullOrWhiteSpace($managementToken)) {
    throw 'Azure CLI did not return an Azure Resource Manager access token.'
}

$availability = Invoke-RestMethod `
    -Method Post `
    -Uri "https://management.azure.com/subscriptions/$subscriptionId/providers/Microsoft.Web/checknameavailability?api-version=2024-04-01" `
    -Headers @{ Authorization = "Bearer $managementToken" } `
    -ContentType 'application/json' `
    -Body $availabilityBody

if (-not $availability.nameAvailable) {
    throw "Function App name '$FunctionAppName' is unavailable: $($availability.message)"
}

$normalizedName = ($FunctionAppName -replace '[^a-zA-Z0-9]', '').ToLowerInvariant()
$hashBytes = [System.Security.Cryptography.SHA256]::HashData(
    [System.Text.Encoding]::UTF8.GetBytes($FunctionAppName))
$hash = [Convert]::ToHexString($hashBytes).Substring(0, 6).ToLowerInvariant()
$storagePrefixLength = [Math]::Min(16, $normalizedName.Length)
$storageAccountName = "st$($normalizedName.Substring(0, $storagePrefixLength))$hash"
$artifactDirectory = Join-Path $PSScriptRoot '..\.artifacts'
$publishDirectory = Join-Path $artifactDirectory 'function'
$zipPath = Join-Path $artifactDirectory 'function.zip'
$projectPath = Join-Path $PSScriptRoot '..\src\AiBudgetAutomation.Functions\AiBudgetAutomation.Functions.csproj'

try {
    az group create `
        --name $ResourceGroup `
        --location $Location `
        --output none

    az storage account create `
        --name $storageAccountName `
        --resource-group $ResourceGroup `
        --location $Location `
        --sku Standard_LRS `
        --kind StorageV2 `
        --allow-blob-public-access false `
        --output none

    az functionapp create `
        --name $FunctionAppName `
        --resource-group $ResourceGroup `
        --storage-account $storageAccountName `
        --consumption-plan-location $Location `
        --functions-version 4 `
        --runtime dotnet-isolated `
        --runtime-version 8 `
        --os-type Windows `
        --output none

    az functionapp config appsettings set `
        --name $FunctionAppName `
        --resource-group $ResourceGroup `
        --settings `
            "GitHub__Enterprise=$GitHubEnterprise" `
            "GitHub__Token=$env:GITHUB_TOKEN" `
            'GitHub__ApiVersion=2026-03-10' `
        --output none

    dotnet publish $projectPath `
        --configuration Release `
        --output $publishDirectory

    Compress-Archive `
        -Path (Join-Path $publishDirectory '*') `
        -DestinationPath $zipPath `
        -Force

    az functionapp deployment source config-zip `
        --name $FunctionAppName `
        --resource-group $ResourceGroup `
        --src $zipPath `
        --output none

    $hostName = az functionapp show `
        --name $FunctionAppName `
        --resource-group $ResourceGroup `
        --query defaultHostName `
        --output tsv

    [pscustomobject]@{
        ResourceGroup = $ResourceGroup
        FunctionApp = $FunctionAppName
        ValidationEndpoint = "https://$hostName/api/budget-requests/validate"
        FulfillmentEndpoint = "https://$hostName/api/budget-requests/fulfill"
        FunctionKeyCommand = "az functionapp keys list --name $FunctionAppName --resource-group $ResourceGroup"
    }
}
finally {
    if (Test-Path $artifactDirectory) {
        Remove-Item $artifactDirectory -Recurse -Force
    }
}
