[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$StoragePath = "",
    [string]$CharacterName = "",
    [switch]$CheckOnly
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($CharacterName)) {
    $CharacterName = [string][char]0x771F + [string][char]0x592E
}

if ([string]::IsNullOrWhiteSpace($StoragePath)) {
    $scriptRoot = if ([string]::IsNullOrWhiteSpace($PSScriptRoot)) {
        Split-Path -Parent $MyInvocation.MyCommand.Path
    }
    else {
        $PSScriptRoot
    }
    $StoragePath = Join-Path $scriptRoot "..\Storage"
}

# Repairs only the Storage\Character\<name>\index.json Modules list.
$RequiredModules = @(
    "Alife.Function.MessageFilter.LifeEventStreamService",
    "Alife.Function.MessageFilter.SystemHealthService",
    "Alife.Function.MessageFilter.SelfContextService",
    "Alife.Function.Agent.AgentDiagnosticsService",
    "Alife.Function.Agent.AgentCapabilityInventoryService",
    "Alife.Function.Agent.AgentSelfModelService",
    "Alife.Function.Agent.AgentIssueReportService",
    "Alife.Function.Agent.AgentTaskService",
    "Alife.Function.Agent.AgentWorkspaceService",
    "Alife.Function.Agent.AgentCommandService",
    "Alife.Function.Agent.AgentProjectStatusService",
    "Alife.Function.Agent.AgentMaintenanceService",
    "Alife.Function.Agent.AgentProactiveBehaviorService",
    "Alife.Function.Agent.AgentControlCenterService",
    "Alife.Function.MessageFilter.EmbodiedActionService",
    "Alife.Function.QChat.QChatRelationCacheService",
    "Alife.Function.Memory.AutobiographicalMemoryService"
)

$KnownModuleRenames = @{
    "Alife.Function.MessageFilter.AgentDiagnosticsService" = "Alife.Function.Agent.AgentDiagnosticsService"
    "Alife.Function.MessageFilter.AgentCapabilityInventoryService" = "Alife.Function.Agent.AgentCapabilityInventoryService"
    "Alife.Function.MessageFilter.AgentSelfModelService" = "Alife.Function.Agent.AgentSelfModelService"
    "Alife.Function.MessageFilter.AgentIssueReportService" = "Alife.Function.Agent.AgentIssueReportService"
    "Alife.Function.MessageFilter.AgentTaskService" = "Alife.Function.Agent.AgentTaskService"
    "Alife.Function.MessageFilter.AgentWorkspaceService" = "Alife.Function.Agent.AgentWorkspaceService"
    "Alife.Function.MessageFilter.AgentCommandService" = "Alife.Function.Agent.AgentCommandService"
    "Alife.Function.MessageFilter.AgentProjectStatusService" = "Alife.Function.Agent.AgentProjectStatusService"
    "Alife.Function.MessageFilter.AgentMaintenanceService" = "Alife.Function.Agent.AgentMaintenanceService"
    "Alife.Function.MessageFilter.AgentProactiveBehaviorService" = "Alife.Function.Agent.AgentProactiveBehaviorService"
    "Alife.Function.MessageFilter.AgentControlCenterService" = "Alife.Function.Agent.AgentControlCenterService"
}

function Write-Step {
    param([string]$Message)
    Write-Host "[AstralFox] $Message"
}

function Get-UniqueModules {
    param([string[]]$Modules)

    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $result = [System.Collections.Generic.List[string]]::new()
    foreach ($module in $Modules) {
        if ([string]::IsNullOrWhiteSpace($module)) {
            continue
        }

        $normalized = $module.Trim()
        if ($KnownModuleRenames.ContainsKey($normalized)) {
            $normalized = $KnownModuleRenames[$normalized]
        }

        if ($seen.Add($normalized)) {
            $result.Add($normalized)
        }
    }

    $result.ToArray()
}

$indexPath = Join-Path $StoragePath "Character\$CharacterName\index.json"
if (-not (Test-Path -LiteralPath $indexPath)) {
    throw "Character index was not found: $indexPath"
}

Write-Step "Character index: $indexPath"
Write-Step "This script supports -WhatIf and -CheckOnly."

$character = Get-Content -LiteralPath $indexPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($null -eq $character.Modules) {
    $character | Add-Member -MemberType NoteProperty -Name Modules -Value @()
}

$currentModules = Get-UniqueModules -Modules @($character.Modules)
$moduleSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($module in $currentModules) {
    [void]$moduleSet.Add($module)
}

$missing = @($RequiredModules | Where-Object { -not $moduleSet.Contains($_) })
$renamed = @($character.Modules | Where-Object { $KnownModuleRenames.ContainsKey([string]$_) })

if ($missing.Count -eq 0 -and $renamed.Count -eq 0) {
    Write-Step "No repair needed."
    exit 0
}

if ($renamed.Count -gt 0) {
    Write-Step "Will normalize renamed module id(s): $($renamed -join ', ')"
}

if ($missing.Count -gt 0) {
    Write-Step "Will add missing module id(s): $($missing -join ', ')"
}

if ($CheckOnly) {
    exit 2
}

$repairedModules = [System.Collections.Generic.List[string]]::new()
foreach ($module in @($currentModules)) {
    $repairedModules.Add($module)
}
foreach ($module in @($missing)) {
    $repairedModules.Add($module)
}
if ($PSCmdlet.ShouldProcess($indexPath, "Repair character Modules list")) {
    $character.Modules = $repairedModules.ToArray()
    $character |
        ConvertTo-Json -Depth 64 |
        Set-Content -LiteralPath $indexPath -Encoding UTF8
    Write-Step "Character module list repaired."
}
