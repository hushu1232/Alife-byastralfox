[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('account-a', 'account-b')]
    [string]$AccountId,

    [string]$PlanPath = $env:ALIFE_LOCAL_PRODUCTION_PLAN,

    [string[]]$DeployPath = @(),

    [string]$DotNetPath = 'C:\Users\hu shu\.dotnet\dotnet.exe',

    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalProduction.Configuration.psm1') -Force

if ([string]::IsNullOrWhiteSpace($PlanPath)) {
    $PlanPath = [Environment]::GetEnvironmentVariable('ALIFE_LOCAL_PRODUCTION_PLAN', 'User')
}
if ([string]::IsNullOrWhiteSpace($PlanPath)) {
    throw 'PlanPath is required.'
}

$plan = Read-LocalProductionPlan (Get-Content -LiteralPath $PlanPath -Raw)
$slot = @($plan.accounts | Where-Object { $_.id -eq $AccountId }) | Select-Object -First 1
if ($null -eq $slot) {
    throw "Account '$AccountId' was not found in the local production plan."
}

$clientRoot = Join-Path $slot.runtimeRoot 'ClientBuild'
$clientDll = Join-Path $clientRoot 'Alife.Client.dll'
$deployItems = @($DeployPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object {
    Get-Item -LiteralPath $_ -ErrorAction Stop
})
foreach ($item in $deployItems) {
    if ($item.PSIsContainer -and
        -not (Test-Path -LiteralPath (Join-Path $item.FullName 'Alife.Client.dll') -PathType Leaf)) {
        throw "Deploy directory must contain Alife.Client.dll: $($item.FullName)"
    }
}

if ($DryRun) {
    return [pscustomobject]@{
        AccountId = $AccountId
        ClientDll = $clientDll
        OneBotUrl = $slot.oneBotUrl
        PlannedDeployCount = $deployItems.Count
        DryRun = $true
    }
}

$deploysClient = @($deployItems | Where-Object {
    $_.PSIsContainer -or $_.Name -ieq 'Alife.Client.dll'
}).Count -gt 0
if (-not (Test-Path -LiteralPath $clientDll -PathType Leaf) -and -not $deploysClient) {
    throw "Account client was not found and is not included in deployment: $clientDll"
}
if (-not (Test-Path -LiteralPath $DotNetPath -PathType Leaf)) {
    throw "User .NET runtime was not found: $DotNetPath"
}

$token = [Environment]::GetEnvironmentVariable($slot.oneBotTokenEnvironmentVariable, 'User')
if ([string]::IsNullOrWhiteSpace($token)) {
    $token = [Environment]::GetEnvironmentVariable($slot.oneBotTokenEnvironmentVariable, 'Process')
}
if ([string]::IsNullOrWhiteSpace($token)) {
    throw "OneBot token is unavailable for $AccountId."
}

$running = @(Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" | Where-Object {
    -not [string]::IsNullOrWhiteSpace($_.CommandLine) -and
    $_.CommandLine.IndexOf($clientDll, [StringComparison]::OrdinalIgnoreCase) -ge 0
})
foreach ($process in $running) {
    Stop-Process -Id $process.ProcessId -ErrorAction Stop
    Wait-Process -Id $process.ProcessId -Timeout 10 -ErrorAction SilentlyContinue
}

if ($deployItems.Count -gt 0) {
    [IO.Directory]::CreateDirectory($clientRoot) | Out-Null
    foreach ($item in $deployItems) {
        if ($item.PSIsContainer) {
            Get-ChildItem -LiteralPath $item.FullName -Force |
                Copy-Item -Destination $clientRoot -Recurse -Force
        }
        else {
            Copy-Item -LiteralPath $item.FullName -Destination $clientRoot -Force
        }
    }
}

if (-not (Test-Path -LiteralPath $clientDll -PathType Leaf)) {
    throw "Account client was not found: $clientDll"
}
$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = $DotNetPath
$start.Arguments = '"' + $clientDll + '"'
$start.WorkingDirectory = $clientRoot
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.Environment['ALIFE_RUNTIME_PATH'] = $slot.runtimeRoot
$start.Environment['ALIFE_STORAGE_PATH'] = $slot.storageRoot
$start.Environment['ALIFE_TEMP_PATH'] = $slot.tempRoot
$start.Environment['ALIFE_ACCOUNT_ID'] = $slot.id
$start.Environment['ALIFE_WEBVIEW2_USER_DATA_FOLDER'] = Join-Path $slot.runtimeRoot 'webview2'
$start.Environment['ALIFE_CONTROL_CENTER_WEBVIEW2_USER_DATA_FOLDER'] = Join-Path $slot.runtimeRoot 'control-center-webview2'
$start.Environment['ALIFE_ONEBOT_URL'] = $slot.oneBotUrl
$start.Environment['ALIFE_ONEBOT_TOKEN'] = $token
$start.Environment['ALIFE_QZONE_LOOPBACK_OPERATOR_URL'] = $slot.qZoneLoopbackOperatorUrl
[void]$start.Environment.Remove('ALIFE_ACCOUNT_A_ONEBOT_TOKEN')
[void]$start.Environment.Remove('ALIFE_ACCOUNT_B_ONEBOT_TOKEN')

$process = [Diagnostics.Process]::Start($start)
if ($null -eq $process) {
    throw "Failed to start Alife Client for $AccountId."
}

[pscustomobject]@{
    AccountId = $AccountId
    ProcessId = $process.Id
    ClientDll = $clientDll
    OneBotUrl = $slot.oneBotUrl
    DeployedCount = $deployItems.Count
}