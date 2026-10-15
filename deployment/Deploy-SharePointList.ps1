[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [uri]$SiteUrl,

    [Parameter()]
    [string]$ListName = 'AI Budget Requests',

    [Parameter(Mandatory)]
    [string]$ManagerEmail
)

$ErrorActionPreference = 'Stop'

function Invoke-GraphRequest {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('GET', 'POST', 'PATCH')]
        [string]$Method,

        [Parameter(Mandatory)]
        [string]$Uri,

        [Parameter()]
        [object]$Body
    )

    $parameters = @{
        Method = $Method
        Uri = $Uri
        Headers = $script:GraphHeaders
    }

    if ($null -ne $Body) {
        $parameters.Body = $Body | ConvertTo-Json -Depth 12
        $parameters.ContentType = 'application/json'
    }

    Invoke-RestMethod @parameters
}

function New-ColumnDefinition {
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [string]$DisplayName,

        [Parameter(Mandatory)]
        [hashtable]$Type,

        [Parameter()]
        [bool]$Required = $false,

        [Parameter()]
        [bool]$Hidden = $true,

        [Parameter()]
        [hashtable]$DefaultValue
    )

    $column = @{
        name = $Name
        displayName = $DisplayName
        required = $Required
        hidden = $Hidden
    }

    foreach ($entry in $Type.GetEnumerator()) {
        $column[$entry.Key] = $entry.Value
    }

    if ($null -ne $DefaultValue) {
        $column.defaultValue = $DefaultValue
    }

    $column
}

$token = az account get-access-token --resource-type ms-graph --query accessToken -o tsv
if ([string]::IsNullOrWhiteSpace($token)) {
    throw 'Azure CLI did not return a Microsoft Graph access token. Run az login with an account that can manage the target SharePoint site.'
}

$script:GraphHeaders = @{
    Authorization = "Bearer $token"
    Accept = 'application/json'
}

$sitePath = $SiteUrl.AbsolutePath.TrimEnd('/')
$site = Invoke-GraphRequest -Method GET -Uri "https://graph.microsoft.com/v1.0/sites/$($SiteUrl.Host):$sitePath"
$lists = Invoke-GraphRequest -Method GET -Uri "https://graph.microsoft.com/v1.0/sites/$($site.id)/lists?`$select=id,displayName,webUrl"
$list = $lists.value | Where-Object displayName -EQ $ListName | Select-Object -First 1

if ($null -eq $list) {
    if ($PSCmdlet.ShouldProcess($SiteUrl.AbsoluteUri, "Create SharePoint list '$ListName'")) {
        $list = Invoke-GraphRequest -Method POST -Uri "https://graph.microsoft.com/v1.0/sites/$($site.id)/lists" -Body @{
            displayName = $ListName
            description = 'Tracks AI credit budget intake, validation, manager approval, and fulfillment.'
            list = @{ template = 'genericList' }
        }
    }
}

if ($null -eq $list) {
    return
}

$columns = @(
    (New-ColumnDefinition 'RequestId' 'Request ID' @{ text = @{ maxLength = 255 } }),
    (New-ColumnDefinition 'RequesterEmail' 'Requester Email' @{ text = @{ maxLength = 255 } }),
    (New-ColumnDefinition 'GitHubUsername' 'GitHub Username' @{ text = @{ maxLength = 255 } } -Required $true -Hidden $false),
    (New-ColumnDefinition 'CostCenter' 'Cost Center' @{ text = @{ maxLength = 100 } } -Required $true -Hidden $false),
    (New-ColumnDefinition 'RequestedTotalUsd' 'Requested Total USD' @{ currency = @{ locale = 'en-US' } } -Required $true -Hidden $false),
    (New-ColumnDefinition 'Justification' 'Justification' @{ text = @{ allowMultipleLines = $true; linesForEditing = 6 } } -Required $true -Hidden $false),
    (New-ColumnDefinition 'ExpirationDate' 'Expiration Date' @{ dateTime = @{ displayAs = 'default'; format = 'dateOnly' } } -Required $true -Hidden $false),
    (New-ColumnDefinition 'RequestStatus' 'Status' @{ choice = @{ choices = @('Submitted', 'Validating', 'Validation Failed', 'Pending Manager Approval', 'Rejected', 'Approved', 'Fulfilling', 'Completed', 'Failed'); displayAs = 'dropDownMenu' } } -DefaultValue @{ value = 'Submitted' }),
    (New-ColumnDefinition 'ValidationPassed' 'Validation Passed' @{ boolean = @{} }),
    (New-ColumnDefinition 'ValidationReasons' 'Validation Reasons' @{ text = @{ allowMultipleLines = $true; linesForEditing = 4 } }),
    (New-ColumnDefinition 'CurrentBudgetUsd' 'Current Budget USD' @{ currency = @{ locale = 'en-US' } }),
    (New-ColumnDefinition 'AdditionalCommitmentUsd' 'Additional Commitment USD' @{ currency = @{ locale = 'en-US' } }),
    (New-ColumnDefinition 'CostCenterRemainingUsd' 'Cost Center Remaining USD' @{ currency = @{ locale = 'en-US' } }),
    (New-ColumnDefinition 'UniversalRemainingUsd' 'Universal Remaining USD' @{ currency = @{ locale = 'en-US' } }),
    (New-ColumnDefinition 'ValidatedAt' 'Validated At' @{ dateTime = @{ displayAs = 'default'; format = 'dateTime' } }),
    (New-ColumnDefinition 'ValidationId' 'Validation ID' @{ text = @{ maxLength = 255 } }),
    (New-ColumnDefinition 'ManagerEmail' 'Manager Email' @{ text = @{ maxLength = 255 } } -DefaultValue @{ value = $ManagerEmail }),
    (New-ColumnDefinition 'ApprovalId' 'Approval ID' @{ text = @{ maxLength = 255 } }),
    (New-ColumnDefinition 'ApprovalOutcome' 'Approval Outcome' @{ choice = @{ choices = @('Pending', 'Approved', 'Rejected', 'Expired'); displayAs = 'dropDownMenu' } }),
    (New-ColumnDefinition 'ApprovalComments' 'Approval Comments' @{ text = @{ allowMultipleLines = $true; linesForEditing = 4 } }),
    (New-ColumnDefinition 'ApprovedAt' 'Approved At' @{ dateTime = @{ displayAs = 'default'; format = 'dateTime' } }),
    (New-ColumnDefinition 'GitHubBudgetId' 'GitHub Budget ID' @{ text = @{ maxLength = 255 } }),
    (New-ColumnDefinition 'OperationId' 'Operation ID' @{ text = @{ maxLength = 255 } }),
    (New-ColumnDefinition 'PreviousState' 'Previous State' @{ text = @{ allowMultipleLines = $true; linesForEditing = 6 } }),
    (New-ColumnDefinition 'ResultingState' 'Resulting State' @{ text = @{ allowMultipleLines = $true; linesForEditing = 6 } }),
    (New-ColumnDefinition 'ErrorDetails' 'Error Details' @{ text = @{ allowMultipleLines = $true; linesForEditing = 6 } }),
    (New-ColumnDefinition 'FulfilledAt' 'Fulfilled At' @{ dateTime = @{ displayAs = 'default'; format = 'dateTime' } })
)

$existingColumns = Invoke-GraphRequest -Method GET -Uri "https://graph.microsoft.com/v1.0/sites/$($site.id)/lists/$($list.id)/columns"

foreach ($column in $columns) {
    if ($existingColumns.value.name -contains $column.name) {
        continue
    }

    if ($PSCmdlet.ShouldProcess($ListName, "Create column '$($column.displayName)'")) {
        Invoke-GraphRequest -Method POST -Uri "https://graph.microsoft.com/v1.0/sites/$($site.id)/lists/$($list.id)/columns" -Body $column | Out-Null
    }
}

$titleColumn = $existingColumns.value | Where-Object name -EQ 'Title' | Select-Object -First 1
if ($null -ne $titleColumn -and $PSCmdlet.ShouldProcess($ListName, 'Rename Title column to Request Summary')) {
    Invoke-GraphRequest -Method PATCH -Uri "https://graph.microsoft.com/v1.0/sites/$($site.id)/lists/$($list.id)/columns/$($titleColumn.id)" -Body @{
        displayName = 'Request Summary'
        required = $true
    } | Out-Null
}

[pscustomobject]@{
    Site = $site.webUrl
    ListName = $ListName
    ListUrl = $list.webUrl
    ManagerEmail = $ManagerEmail
}
