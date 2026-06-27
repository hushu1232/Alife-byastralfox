Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot

function New-Check {
    param(
        [string]$Group,
        [string]$Name,
        [bool]$Passed,
        [string]$Detail
    )

    [pscustomobject]@{
        Group = $Group
        Name = $Name
        Passed = $Passed
        Detail = $Detail
    }
}

function Test-FileMarker {
    param(
        [string]$RelativePath,
        [string[]]$Markers
    )

    $fullPath = Join-Path $repoRoot $RelativePath
    if (-not (Test-Path -LiteralPath $fullPath)) {
        return $false
    }

    $content = Get-Content -LiteralPath $fullPath -Raw
    foreach ($marker in $Markers) {
        if ($content.IndexOf($marker, [System.StringComparison]::Ordinal) -lt 0) {
            return $false
        }
    }

    return $true
}

$checks = @(
    New-Check -Group "Core" -Name "DataAgentModulePresent" -Passed (Test-Path -LiteralPath (Join-Path $repoRoot "Sources/Alife.Function/Alife.Function.DataAgent/Alife.Function.DataAgent.csproj")) -Detail "Alife.Function.DataAgent project"
    New-Check -Group "Core" -Name "SqliteSchemaInitializes" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentSchemaInitializer.cs" @("engineering_gate", "query_audit")) -Detail "schema initializer markers"
    New-Check -Group "Core" -Name "FixtureDataImports" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentFixtureImporter.cs" @("Runtime readiness script", "MixuTts9881Reachable")) -Detail "fixture importer markers"
    New-Check -Group "Safety" -Name "DangerousSqlRejected" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentSqlSafetyValidator.cs" @("unsafe_keyword_rejected", "multi_statement_sql_rejected")) -Detail "SQL safety markers"
    New-Check -Group "Query" -Name "QueryPlanFixturesPass" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("QueryPlanFixturesPass", "find_missing_required_gates")) -Detail "QueryPlan readiness markers"
    New-Check -Group "Query" -Name "ReadOnlyQueryExecutes" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentQueryExecutor.cs" @("Execute", "CommandTimeout")) -Detail "query executor markers"
    New-Check -Group "Context" -Name "ContextContributionStable" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentContextProvider.cs" @("[data_agent_context]", "[/data_agent_context]")) -Detail "context wrapper markers"
)

Write-Output "DataAgent Readiness"

foreach ($group in @("Core", "Safety", "Query", "Context")) {
    Write-Output "[$group]"
    foreach ($check in ($checks | Where-Object { $_.Group -eq $group })) {
        if ($check.Passed) {
            Write-Output ("  PASS     {0}: {1}" -f $check.Name, $check.Detail)
        }
        else {
            Write-Output ("  MISSING  {0}: {1}" -f $check.Name, $check.Detail)
        }
    }
}

$requiredPassed = @($checks | Where-Object { $_.Passed }).Count
$requiredMissing = @($checks | Where-Object { -not $_.Passed }).Count

Write-Output "[Summary]"
Write-Output ("  Summary: {0} required passed, {1} required missing" -f $requiredPassed, $requiredMissing)

if ($requiredMissing -gt 0) {
    exit 1
}

exit 0
