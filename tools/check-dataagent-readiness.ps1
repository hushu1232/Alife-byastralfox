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
    New-Check -Group "Schema" -Name "SchemaSnapshotAvailable" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentSchemaIntrospector.cs" @("DataAgentSchemaSnapshot", "Inspect", "PRAGMA table_info")) -Detail "schema introspection markers"
    New-Check -Group "Schema" -Name "CatalogMatchesSqliteSchema" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("CatalogMatchesSqliteSchema", "CatalogMatchesDatabase")) -Detail "catalog/sqlite schema match markers"
    New-Check -Group "Safety" -Name "DangerousSqlRejected" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentSqlSafetyValidator.cs" @("unsafe_keyword_rejected", "multi_statement_sql_rejected")) -Detail "SQL safety markers"
    New-Check -Group "Query" -Name "QueryPlanFixturesPass" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("QueryPlanFixturesPass", "find_missing_required_gates")) -Detail "QueryPlan readiness markers"
    New-Check -Group "Query" -Name "ReadOnlyQueryExecutes" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentQueryExecutor.cs" @("Execute", "CommandTimeout")) -Detail "query executor markers"
    New-Check -Group "Context" -Name "ContextContributionStable" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentContextProvider.cs" @("[data_agent_context]", "[/data_agent_context]")) -Detail "context wrapper markers"
    New-Check -Group "Planner" -Name "PlannerInterfacePresent" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/IDataAgentQueryPlanner.cs" @("IDataAgentQueryPlanner", "Plan")) -Detail "planner interface markers"
    New-Check -Group "Planner" -Name "DeterministicPlannerPassesFixtures" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DeterministicDataAgentQueryPlanner.cs" @("DeterministicDataAgentQueryPlanner", "find_runtime_readiness_required_evidence", "find_dataagent_documents")) -Detail "deterministic planner markers"
    New-Check -Group "Planner" -Name "PlannerExplanationInContext" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentContextProvider.cs" @("planner_confidence", "planner_reason", "planner_signals")) -Detail "planner explanation context markers"
    New-Check -Group "Planner" -Name "ServiceUsesInjectedPlanner" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentService.cs" @("IDataAgentQueryPlanner", "new DataAgentQueryRequest", "planner.Plan")) -Detail "service injection markers"
    New-Check -Group "Planner" -Name "LlmPlannerInterfacePresent" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/ILlmDataAgentPlannerClient.cs" @("ILlmDataAgentPlannerClient", "Complete")) -Detail "LLM planner client interface markers"
    New-Check -Group "Planner" -Name "LlmPlannerPromptUsesSchemaSnapshot" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/LlmDataAgentPlannerPromptFormatter.cs" @("DataAgentSchemaSnapshot", "Do not output SQL", "document_index")) -Detail "LLM planner prompt schema markers"
    New-Check -Group "Planner" -Name "LlmPlannerStrictJsonParser" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/LlmDataAgentPlannerResponseParser.cs" @("JsonDocument", "json_must_be_single_object", "DataAgentQueryPlanValidator")) -Detail "LLM planner strict JSON parser markers"
    New-Check -Group "Planner" -Name "LlmPlannerRejectsInvalidOutput" -Passed (Test-FileMarker "Tests/Alife.Test.DataAgent/LlmDataAgentPlannerResponseParserTests.cs" @("ParseRejectsInvalidOperator", "unsupported_operator:starts_with")) -Detail "LLM planner invalid output rejection markers"
    New-Check -Group "Planner" -Name "LlmPlannerFallbackPreservesSafety" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/LlmDataAgentQueryPlanner.cs" @("llm_invalid_output_fallback", "BuildFallbackEnvelope", "SanitizeFallbackReason")) -Detail "LLM planner safe fallback markers"
    New-Check -Group "Context" -Name "ClarificationRequestSupported" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentContextProvider.cs" @("BuildClarification", "needs_clarification", "clarification_options")) -Detail "clarification request context markers"
    New-Check -Group "Context" -Name "NaturalLanguageResultExplanationPresent" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentResultExplainer.cs" @("ExplainAccepted", "local SQLite")) -Detail "natural-language result explanation markers"
    New-Check -Group "Planner" -Name "UnsafePlannerOutputRejected" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("UnsafePlannerOutputRejected", "unsupported_operator:starts_with", "sql_status=rejected")) -Detail "unsafe planner rejection markers"
    New-Check -Group "Tool" -Name "ToolHandlerReturnsDataAgentContext" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs" @("DataAgentToolHandler", "DataAgentModuleService", "RegisterHandlerWithoutDocument", "dataagent_query", "dynamic data context")) -Detail "DataAgent XML tool and module registration markers"
)

Write-Output "DataAgent Readiness"

foreach ($group in @("Core", "Schema", "Safety", "Query", "Context", "Planner", "Tool")) {
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
