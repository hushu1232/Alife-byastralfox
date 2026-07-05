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

function Test-FileOrderedMarkers {
    param(
        [string]$RelativePath,
        [string[]]$Markers
    )

    $fullPath = Join-Path $repoRoot $RelativePath
    if (-not (Test-Path -LiteralPath $fullPath)) {
        return $false
    }

    $content = Get-Content -LiteralPath $fullPath -Raw
    $startIndex = 0
    foreach ($marker in $Markers) {
        $markerIndex = $content.IndexOf($marker, $startIndex, [System.StringComparison]::Ordinal)
        if ($markerIndex -lt 0) {
            return $false
        }

        $startIndex = $markerIndex + $marker.Length
    }

    return $true
}

function Test-FileOmitsMarker {
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
        if ($content.IndexOf($marker, [System.StringComparison]::Ordinal) -ge 0) {
            return $false
        }
    }

    return $true
}

function New-StringFromCodePoints {
    param(
        [int[]]$CodePoints
    )

    $builder = New-Object System.Text.StringBuilder
    foreach ($codePoint in $CodePoints) {
        [void]$builder.Append([char]$codePoint)
    }

    return $builder.ToString()
}

function Test-ScenarioPackChineseText {
    param(
        [string]$RelativePath,
        [string[]]$RequiredTerms,
        [string[]]$ForbiddenFragments
    )

    $fullPath = Join-Path $repoRoot $RelativePath
    if (-not (Test-Path -LiteralPath $fullPath)) {
        return $false
    }

    $utf8 = New-Object System.Text.UTF8Encoding($false, $true)
    $content = [System.IO.File]::ReadAllText($fullPath, $utf8)

    foreach ($term in $RequiredTerms) {
        if ($content.IndexOf($term, [System.StringComparison]::Ordinal) -lt 0) {
            return $false
        }
    }

    foreach ($fragment in $ForbiddenFragments) {
        if ($content.IndexOf($fragment, [System.StringComparison]::Ordinal) -ge 0) {
            return $false
        }
    }

    return $content.IndexOf([string][char]0xFFFD, [System.StringComparison]::Ordinal) -lt 0
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
    New-Check -Group "Planner" -Name "LlmPlannerRejectsInvalidOutput" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentQueryPlanValidator.cs" @("DataAgentQueryPlanValidator", "unsupported_operator:")) -Detail "LLM planner invalid output rejection markers"
    New-Check -Group "Planner" -Name "LlmPlannerFallbackPreservesSafety" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/LlmDataAgentQueryPlanner.cs" @("llm_invalid_output_fallback", "BuildFallbackEnvelope", "SanitizeFallbackReason")) -Detail "LLM planner safe fallback markers"
    New-Check -Group "Context" -Name "ClarificationRequestSupported" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentContextProvider.cs" @("BuildClarification", "needs_clarification", "clarification_options")) -Detail "clarification request context markers"
    New-Check -Group "Context" -Name "NaturalLanguageResultExplanationPresent" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentResultExplainer.cs" @("ExplainAccepted", "local SQLite")) -Detail "natural-language result explanation markers"
    New-Check -Group "Planner" -Name "UnsafePlannerOutputRejected" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("UnsafePlannerOutputRejected", "unsupported_operator:starts_with", "sql_status=rejected")) -Detail "unsafe planner rejection markers"
    New-Check -Group "Tool" -Name "ToolHandlerReturnsDataAgentContext" -Passed ((Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs" @("DataAgentQueryCapabilityProvider", "DataAgentCapabilityRegistrar", "RegisteredCapabilityToolNames")) -and (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentToolHandler.cs" @("dataagent_query", "DataAgentToolHandler", "service.Answer", "resultPublisher"))) -Detail "DataAgent XML query tool and module registration markers"
    New-Check -Group "ToolBroker" -Name "ToolCapabilityManifestPresent" -Passed (Test-FileMarker "sources/Alife.Function/Alife.Function.FunctionCaller/ToolCapabilityManifest.cs" @("ToolCapabilityManifest", "ToolCapabilityDomain", "ToolCapabilityPrecondition")) -Detail "tool capability manifest markers"
    New-Check -Group "ToolBroker" -Name "ToolCapabilityRouterPresent" -Passed (Test-FileMarker "sources/Alife.Function/Alife.Function.FunctionCaller/ToolCapabilityRouter.cs" @("ToolCapabilityRouter", "Route", "dataagent_analysis_continue")) -Detail "tool capability router markers"
    New-Check -Group "ToolBroker" -Name "ToolExecutionGatePresent" -Passed (Test-FileMarker "sources/Alife.Function/Alife.Function.FunctionCaller/XmlStruct.cs" @("CurrentRoute", "SetGovernedToolNames", "tool_not_allowed_in_current_route", "tool_route_required", "tool_session_not_allowed_in_current_route")) -Detail "XML function fail-closed route execution gate markers"
    New-Check -Group "ToolBroker" -Name "ToolBrokerDynamicExposurePresent" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs" @("Tool Broker contract", "PublishAnalysisContext", "UpdateDataAgentAnalysisRouteSessionFromContext", "Only use DataAgent XML tools when they appear in current [tool_route_context]")) -and (Test-FileOmitsMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs" @("{xmlHandler.FunctionDocument()}", "{analysisXmlHandler.FunctionDocument()}"))) -Detail "DataAgent prompt defers XML tool docs to per-turn Tool Broker route"
    New-Check -Group "ToolBroker" -Name "ToolRouteRuntimeWiringPresent" -Passed (Test-FileMarker "sources/Alife.Function/Alife.Function.FunctionCaller/XmlFunctionCaller.cs" @("RouteCurrentTurn", "BuildRoutedFunctionGuide", "SetGovernedToolNames", "[tool_route_context]", "ChatSend += OnChatSend")) -Detail "FunctionCaller injects routed tool guide per model turn"
    New-Check -Group "ToolBroker" -Name "QChatToolRouteStateScopePresent" -Passed (Test-FileMarker "sources/Alife.Function/Alife.Function.QChat/QChatService.cs" @("protected virtual async Task<string> DispatchToModelAsync", "functionService.CreateToolRouteState", "message.SenderRole == QChatSenderRole.Owner", "message.MessageType == OneBotMessageType.Private", "functionService.UseToolRouteState(routeState)", "ChatBot.ChatAsync(ChatTextFilter")) -Detail "QChat dispatch scopes owner/private state for Tool Broker"
    New-Check -Group "ToolBroker" -Name "ToolBrokerRuntimeTestsPresent" -Passed ((Test-FileMarker "Tests/Alife.Test.Interpreter/XmlFunctionPolicyTests.cs" @("HandleRejectsGovernedDataAgentToolWhenRouteIsMissing", "HandleRejectsSessionScopedDataAgentToolWhenRouteSessionDoesNotMatch", "HandleAllowsSessionScopedDataAgentToolWhenRouteSessionMatches")) -and (Test-FileMarker "Tests/Alife.Test.QChat/QChatToolRouteStateWiringTests.cs" @("DispatchToModelCreatesScopedToolRouteState", "functionService.CreateToolRouteState", "functionService.UseToolRouteState(routeState)"))) -Detail "runtime Tool Broker fail-closed and QChat scope tests"
    New-Check -Group "ToolBroker" -Name "ToolBrokerRouteDecisionReasonCodesPresent" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.FunctionCaller/ToolRouteModels.cs" @("ToolRouteDecision", "ReasonCode")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.FunctionCaller/ToolCapabilityRouter.cs" @("route_allowed", "intent_not_matched", "owner_private_required", "dataagent_analysis_session_missing", "dataagent_analysis_session_inactive"))) -Detail "Tool Broker route decision reason code markers"
    New-Check -Group "ToolBroker" -Name "ToolBrokerExecutionAuditPresent" -Passed (Test-FileMarker "sources/Alife.Function/Alife.Function.FunctionCaller/XmlStruct.cs" @("XmlFunctionExecutionAuditRecord", "ExecutionAudited", "ReasonCode")) -Detail "XML function execution audit markers"
    New-Check -Group "ToolBroker" -Name "ToolBrokerAuditLogPresent" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentToolBrokerAuditLog.cs" @("DataAgentToolBrokerAuditRecord", "tool_broker_audit", "ReadAll")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentSchemaInitializer.cs" @("tool_broker_audit", "reason_code"))) -Detail "DataAgent Tool Broker audit log markers"
    New-Check -Group "ToolBroker" -Name "CapabilityBoundaryPresent" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/IDataAgentCapabilityProvider.cs" @("IDataAgentCapabilityProvider", "ToolCapabilityManifest", "Register")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentCapabilityRegistry.cs" @("DataAgentCapabilityRegistry", "Duplicate DataAgent capability provider", "Duplicate DataAgent tool capability"))) -Detail "DataAgent capability provider boundary markers"
    New-Check -Group "ToolBroker" -Name "CapabilityProvidersPresent" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentQueryCapabilityProvider.cs" @("DataAgentQueryCapabilityProvider", "DataAgentToolHandler", "DataAgentToolCapabilityManifests.Query")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisCapabilityProvider.cs" @("DataAgentAnalysisCapabilityProvider", "DataAgentAnalysisToolHandler", "DataAgentToolCapabilityManifests.Analysis"))) -Detail "DataAgent built-in capability providers"
    New-Check -Group "ToolBroker" -Name "SharedToolManifestPresent" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.FunctionCaller/DataAgentToolCapabilityManifests.cs" @("DataAgentToolCapabilityManifests", "dataagent_query", "dataagent_analysis_end")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.FunctionCaller/ToolCapabilityRouter.cs" @("DataAgentToolCapabilityManifests.Create"))) -Detail "shared DataAgent Tool Broker manifest markers"
    New-Check -Group "Store" -Name "DataAgentStoreBoundaryPresent" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/IDataAgentStore.cs" @("IDataAgentStore", "DataAgentAcceptedAuditInput", "DataAgentRejectedAuditInput")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentService.cs" @("IDataAgentStore", "store.Query", "store.RecordAccepted", "store.RecordRejected"))) -Detail "provider-neutral DataAgent store boundary markers"
    New-Check -Group "Store" -Name "SqliteStoreCompatibilityPresent" -Passed (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/SqliteDataAgentStore.cs" @("SqliteDataAgentStore", "DataAgentSchemaInitializer.Initialize", "DataAgentFixtureImporter.Import", "DataAgentQueryExecutor", "DataAgentAuditLog", "DataAgentToolBrokerAuditLog")) -Detail "SQLite store compatibility markers"
    New-Check -Group "Store" -Name "PostgresStoreProviderPresent" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/PostgresDataAgentStore.cs" @("PostgresDataAgentStore", "NpgsqlConnection", "tool_broker_audit", "query_audit")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/Alife.Function.DataAgent.csproj" @("Npgsql"))) -Detail "PostgreSQL store provider markers"
    New-Check -Group "Store" -Name "PostgresLiveTestsEnvironmentGated" -Passed (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentPostgresStoreTests.cs" @("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION", "Assert.Ignore", "LivePostgresStoreInitializesImportsFixturesAndExecutesReadOnlyQuery")) -Detail "PostgreSQL live tests environment gate"
    New-Check -Group "Store" -Name "DataAgentServiceUsesStoreBoundary" -Passed (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentService.cs" @("IDataAgentStore", "new SqliteDataAgentStore", "store.Query", "store.RecordAccepted", "store.RecordRejected")) -Detail "DataAgentService store boundary usage"
    # V2.10 governance readiness gates
    New-Check -Group "Governance" -Name "DataAgentScenarioKnowledgePackPresent" -Passed ((Test-FileMarker "docs/dataagent/scenario-packs/engineering.zh-CN.json" @("engineering_readiness", "engineering_gate", "status", "required")) -and (Test-ScenarioPackChineseText "docs/dataagent/scenario-packs/engineering.zh-CN.json" @((New-StringFromCodePoints @(0x5de5,0x7a0b,0x95e8,0x7981)), (New-StringFromCodePoints @(0x6700,0x8fd1,0x5931,0x8d25,0x7684,0x6d4b,0x8bd5)), (New-StringFromCodePoints @(0x7f3a,0x5931,0x9879)), (New-StringFromCodePoints @(0x6587,0x6863,0x8bc1,0x636e)), (New-StringFromCodePoints @(0x5931,0x8d25)), (New-StringFromCodePoints @(0x5fc5,0x9700))) @((New-StringFromCodePoints @(0x5bb8,0x30e7,0x25bc)), (New-StringFromCodePoints @(0x93c8,0x20ac)), (New-StringFromCodePoints @(0x6fb6,0x8fab,0x89e6)), (New-StringFromCodePoints @(0x8e47,0x546d,0x6e36)))) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentScenarioKnowledgePackProvider.cs" @("DataAgentScenarioKnowledgePackProvider", "Load", "ResolveTerms")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("DataAgentScenarioKnowledgePackPresent", "ResolveTerms(pack", "engineering_gate", "field=status"))) -Detail "V2.10 scenario knowledge pack runtime readiness"
    New-Check -Group "Governance" -Name "DataAgentScenarioContextIntegrated" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentScenarioContext.cs" @("DataAgentScenarioContext", "scenario_context_matched", "CandidateDatasets", "CandidateFields")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentScenarioContextBuilder.cs" @("DataAgentScenarioContextBuilder", "Build", "ReasonMatched", "catalog.HasField")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/LlmDataAgentPlannerPromptFormatter.cs" @("Scenario context:", "Scenario context is a hint only", "Do not output SQL")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/LlmDataAgentQueryPlanner.cs" @("request.ScenarioContext", "formatter.Format", "llm_invalid_output_fallback")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentScenarioDiagnosticsFormatter.cs" @("DataAgentScenarioDiagnosticsFormatter", "DataAgent scenario diagnostics", "reason=")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("DataAgentScenarioContextIntegrated", "scenario_context=true;prompt_hint=true;owner_diag=true;sql_boundary=true", "DataAgentScenarioContextBuilder", "DataAgentScenarioDiagnosticsFormatter", "LlmDataAgentQueryPlanner", "llm_invalid_output_fallback", "unsupported_operator", "throwOnInvalidBytes: true", "\uFFFD")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentScenarioContextBuilderTests.cs" @("DataAgentScenarioContextBuilderTests")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentScenarioDiagnosticsFormatterTests.cs" @("DataAgentScenarioDiagnosticsFormatterTests", "DataAgent scenario diagnostics")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentV211ReadinessTests.cs" @("DataAgentV211ReadinessTests", "ScenarioContextNarrowsPlannerAttentionWithoutSqlAuthority", "ScenarioDiagnosticsAreDataAgentOwnedAndOwnerSafe", "throwOnInvalidBytes: true"))) -Detail "V2.11 scenario context prompt and owner diagnostics readiness"
    New-Check -Group "Governance" -Name "DataAgentNodeToolScopePolicyPresent" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentToolScopePolicy.cs" @("DataAgentToolScopePolicy", "QueryPlanner", "GenerateQueryPlan", "DiagnosticsRouter", "ReadProgressDiagnostics", "ExecuteReadOnlyQuery")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("DataAgentNodeToolScopePolicyPresent", "planner_generate=true", "diagnostics_progress=true"))) -Detail "V2.10 node capability scope policy markers"
    New-Check -Group "Governance" -Name "DataAgentSafetyCapabilitiesRemainDeterministic" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentToolScopePolicy.cs" @("QueryPlanValidator", "SqlCompiler", "SqlSafety", "ReadOnlyExecute", "AllowsModelCall")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("DataAgentSafetyCapabilitiesRemainDeterministic", "validator_model=false", "compiler_model=false", "safety_model=false", "execute_model=false"))) -Detail "V2.10 deterministic safety capability markers"
    New-Check -Group "Analysis" -Name "AnalysisSessionServicePresent" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisService.cs" @("DataAgentAnalysisService", "DataAgentService", "ExecuteQueryTurn", "analysis_session_ended")) -Detail "analysis session service markers"
    New-Check -Group "Analysis" -Name "AnalysisSessionStorePresent" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/InMemoryDataAgentAnalysisSessionStore.cs" @("InMemoryDataAgentAnalysisSessionStore", "ConcurrentDictionary", "IDataAgentAnalysisSessionStore")) -Detail "in-memory analysis session store markers"
    New-Check -Group "Analysis" -Name "AnalysisSessionStateMachineTransitions" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisService.cs" @("AwaitingClarification", "ReadyToSummarize", "Summarized", "Ended")) -Detail "analysis session state transition markers"
    New-Check -Group "Analysis" -Name "AnalysisFollowUpInterpreterPresent" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentFollowUpInterpreter.cs" @("DataAgentFollowUpInterpreter", "ContinuePhrases", "RefinePhrases", "SummarizePhrases", "EndPhrases")) -Detail "follow-up interpreter markers"
    New-Check -Group "Analysis" -Name "AnalysisSessionContextProviderPresent" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisContextProvider.cs" @("[data_agent_analysis_session_context]", "caller_id", "pending_summary")) -Detail "analysis context provider markers"
    New-Check -Group "Analysis" -Name "AnalysisSummaryWindowPresent" -Passed ((Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisService.cs" @("SummaryWindowValidatedTurns", "ReadyToSummarize", "ProducesQuery")) -and (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisSummarizer.cs" @("DataAgentAnalysisSummarizer", "ProducesQuery", "validated="))) -Detail "summary window markers"
    New-Check -Group "Analysis" -Name "AnalysisSessionHasNoSqliteBinding" -Passed ((Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/IDataAgentAnalysisSessionStore.cs" @("IDataAgentAnalysisSessionStore", "DataAgentAnalysisSession")) -and (Test-FileOmitsMarker "Sources/Alife.Function/Alife.Function.DataAgent/IDataAgentAnalysisSessionStore.cs" @("SqliteConnection", "Microsoft.Data.Sqlite"))) -Detail "analysis session store has no sqlite binding"
    New-Check -Group "Analysis" -Name "DataAgentOrchestratorPresent" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisOrchestrator.cs" @("DataAgentAnalysisOrchestrator", "IDataAgentAnalysisOrchestrator", "RouteAllowsQuery")) -Detail "native DataAgent analysis orchestrator markers"
    New-Check -Group "Analysis" -Name "OrchestratorNodeBoundaryPresent" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentOrchestrationModels.cs" @("DataAgentOrchestrationNodeKind", "RouteGate", "SchemaContext", "Execute", "Checkpoint")) -Detail "orchestrator node boundary markers"
    New-Check -Group "Analysis" -Name "OrchestratorCheckpointPresent" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentOrchestrationModels.cs" @("DataAgentOrchestrationCheckpoint", "CanContinue", "CanSummarize", "Terminal")) -Detail "orchestrator checkpoint markers"
    New-Check -Group "Analysis" -Name "OrchestratorRouteGateFailClosed" -Passed ((Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisOrchestrator.cs" @("tool_route_required", "RouteAllowsQuery == false", "BuildRejectedResult")) -and (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("orchestrationDeniedContinue", "answerCallsAfterDeniedContinue", "turnsAfterDeniedContinue")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentAnalysisOrchestratorTests.cs" @("StartRouteDeniedFailsClosedWithoutCallingAnswer", "ContinueRouteDeniedForQueryTurnDoesNotExecuteSqlOrMutateSession", "ContinueRouteDeniedForMissingSessionFailsClosedBeforeAnalysisService"))) -Detail "orchestrator route gate fail-closed markers"
    New-Check -Group "Analysis" -Name "OrchestratorTerminalNodesDoNotQuery" -Passed (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentAnalysisOrchestratorTests.cs" @("ContinueSummarizeRequiresToolRouteButDoesNotRequireQueryPermissionOrExecuteSql", "ContinueEndRequiresToolRouteButDoesNotRequireQueryPermissionAndProducesTerminalCheckpoint", "answerCalls")) -Detail "orchestrator terminal node no-query markers"
    New-Check -Group "Analysis" -Name "OrchestratorStateMachineTransitions" -Passed ((Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisOrchestrator.cs" @("BuildCheckpoint", "CanContinue", "CanSummarize")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentAnalysisOrchestratorTests.cs" @("DataAgentAnalysisSessionStatus.Summarized", "DataAgentAnalysisSessionStatus.Ended"))) -Detail "orchestrator state transition markers"
    New-Check -Group "Analysis" -Name "AnalysisToolHandlerUsesOrchestrator" -Passed (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs" @("IDataAgentAnalysisOrchestrator", "orchestrator.Start", "orchestrator.Continue", "orchestrator.Summarize", "orchestrator.End")) -Detail "analysis XML tool handler runtime orchestrator markers"
    New-Check -Group "Analysis" -Name "OrchestratorTraceContextPresent" -Passed (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentOrchestrationContextProvider.cs" @("orchestration_trace", "BuildTrace", "DataAgentOrchestrationStep")) -Detail "orchestration trace context markers"
    New-Check -Group "Analysis" -Name "OrchestratorCheckpointContextPresent" -Passed (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentOrchestrationContextProvider.cs" @("checkpoint_session_id", "checkpoint_can_continue", "checkpoint_terminal")) -Detail "orchestration checkpoint context markers"
    New-Check -Group "Analysis" -Name "OrchestratorRuntimeStartPathCovered" -Passed (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("OrchestratorRuntimeStartPathCovered", "orchestrationStartContext", "[data_agent_analysis_session_context]")) -Detail "runtime orchestrator start path markers"
    New-Check -Group "Analysis" -Name "OrchestratorRuntimeContinuePathCovered" -Passed (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("OrchestratorRuntimeContinuePathCovered", "orchestrationRuntimeContinue", "checkpoint_turn_count=2")) -Detail "runtime orchestrator continue path markers"
    New-Check -Group "Analysis" -Name "OrchestratorRuntimeTerminalPathCovered" -Passed (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("OrchestratorRuntimeTerminalPathCovered", "Summarize:Succeeded>Checkpoint:Succeeded", "ExecutedSql")) -Detail "runtime orchestrator terminal path markers"
    New-Check -Group "Analysis" -Name "OrchestratorRuntimeRouteDeniedFailClosed" -Passed (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("OrchestratorRuntimeRouteDeniedFailClosed", "RouteGate:Rejected>Reject:Rejected>Checkpoint:Succeeded", "turnsAfterDeniedContinue")) -Detail "runtime route-denied fail-closed markers"
    New-Check -Group "Analysis" -Name "AnalysisHandlerConsumesToolRouteContext" -Passed (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs" @("IDataAgentToolRouteContextAccessor", "routeContextAccessor.Get", "routeContext.AllowsQuery")) -Detail "analysis handler consumes Tool Broker route context"
    New-Check -Group "Analysis" -Name "OrchestrationRequestUsesRuntimeRouteDecision" -Passed ((Test-FileOrderedMarkers "sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs" @("orchestrator.Start(new DataAgentOrchestrationRequest(", "routeContext.AllowsQuery", "routeContext));")) -and (Test-FileOrderedMarkers "sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs" @("orchestrator.Continue(new DataAgentOrchestrationRequest(", "routeContext.AllowsQuery", "routeContext));")) -and (Test-FileOmitsMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs" @("RouteAllowsQuery: true"))) -Detail "orchestration request route permission is runtime-derived"
    New-Check -Group "Analysis" -Name "RouteMissingRequestFailsClosed" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentToolRouteContext.cs" @("MissingRouteReasonCode", "tool_route_required", "MissingDataAgentToolRouteContextAccessor")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentAnalysisToolHandlerTests.cs" @("StartWithoutRouteContextFailsClosedAtRequestBoundary", "SummarizeWithoutRouteContextFailsClosedAtHandlerBoundaryWithoutMutation", "EndWithoutRouteContextFailsClosedAtHandlerBoundaryWithoutMutation", "RouteAllowsQuery, Is.False"))) -Detail "missing route creates fail-closed DataAgent request"
    New-Check -Group "Analysis" -Name "RouteEvidenceContextPresent" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentOrchestrationContextProvider.cs" @("route_present", "route_allows_query", "route_reason_code", "route_session_id")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentOrchestrationContextProviderTests.cs" @("BuildAppendsSanitizedRouteEvidenceWhenPresent"))) -Detail "orchestration context emits sanitized route evidence"
    New-Check -Group "Analysis" -Name "RouteSessionScopePreserved" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentToolRouteContext.cs" @("tool_session_not_allowed_in_current_route", "IsSessionScopedDataAgentTool")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentToolRouteContextAccessorTests.cs" @("XmlPolicyAccessorRejectsSessionScopedMismatchForDefenseInDepth"))) -Detail "session scoped route context remains fail-closed"
    New-Check -Group "Analysis" -Name "TerminalRouteDoesNotQuery" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisOrchestrator.cs" @("Summarize(string sessionId, DataAgentToolRouteContext? routeContext", "End(string sessionId, DataAgentToolRouteContext? routeContext", "BuildTerminalRouteDeniedReason")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("terminalRouteDeniedSummary", "denied_terminal_fail_closed=true")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentAnalysisOrchestratorTests.cs" @("ContinueSummarizeRequiresToolRouteButDoesNotRequireQueryPermissionOrExecuteSql", "ContinueEndRequiresToolRouteButDoesNotRequireQueryPermissionAndProducesTerminalCheckpoint", "SummarizeWithoutAllowedToolRouteFailsClosedWithoutMutation", "EndWithoutAllowedToolRouteFailsClosedWithoutMutation"))) -Detail "terminal route context does not force query execution"
    New-Check -Group "Analysis" -Name "DataAgentEvidencePackPresent" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentEvidencePack.cs" @("DataAgentEvidencePack", "SafetySummary", "InterviewSummary")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentEvidencePackBuilder.cs" @("DataAgentEvidencePackBuilder", "route_rejected;sql_not_executed;checkpoint_unchanged", "terminal_no_query")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentEvidencePackFormatter.cs" @("[data_agent_evidence_pack]", "interview_summary", "DataAgentContextFieldSanitizer")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("DataAgentEvidencePackPresent", "acceptedEvidencePack", "deniedEvidencePack", "terminalEvidencePack", "accepted_route_context=runtime")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentEvidencePackTests.cs" @("BuilderBuildsAcceptedQueryEvidence", "BuilderMatchesAcceptedQueryAuditToResponseAnswer", "BuilderMatchesRejectedQueryAuditToResponseAnswerWithoutSqlExecution", "BuilderBuildsRouteDeniedEvidenceWithoutSql", "BuilderIgnoresStaleAuditsForRouteDeniedEvidence", "BuilderBuildsTerminalNoQueryEvidence", "FormatterEmitsStableSanitizedBlock", "FormatterPreservesDiagnosticPunctuationOutsideEvidencePackTag"))) -Detail "DataAgent evidence pack model, builder, formatter, tests, and runtime readiness"
    New-Check -Group "Analysis" -Name "SemanticStateEstimatorCorePresent" -Passed ((Test-FileMarker "sources/Alife/Alife.Framework/Models/StateEstimation/KalmanScalarFilter.cs" @("KalmanScalarFilter", "Predict", "Update")) -and (Test-FileMarker "Tests/Alife.Test.Framework/KalmanScalarFilterTests.cs" @("UpdateMovesValueTowardObservationWithoutJumpingAllTheWay", "HighObservationNoiseTrustsThePriorMoreThanTheObservation", "ValuesAndUncertaintyStayInValidRange")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("SemanticStateEstimatorCorePresent", "scalar_filter=true;application_layer=true"))) -Detail "application-layer scalar Kalman filter markers"
    New-Check -Group "Analysis" -Name "DataAgentAnalysisStateEstimatorPresent" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisStateEstimator.cs" @("DataAgentAnalysisStateEstimator", "route_denied_no_query", "ToolPermissionAllowed")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentEvidencePackFormatter.cs" @("analysis_confidence", "state_estimate_reason_code")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("DataAgentAnalysisStateEstimatorPresent", "accepted_stable=true;denied_no_bypass=true")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentEvidencePackTests.cs" @("AnalysisEstimatorMarksAcceptedEvidenceAsStableWithoutChangingPermission", "AnalysisEstimatorDoesNotTurnRouteDeniedEvidenceIntoAllowedQuery"))) -Detail "DataAgent analysis estimator markers"
    New-Check -Group "Analysis" -Name "DataAgentEvidenceDiagnosticsPresent" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentEvidenceDiagnosticsFormatter.cs" @("DataAgentEvidenceDiagnosticsFormatter", "DataAgent evidence diagnostics", "state_estimate_reason_code")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs" @("evidenceDiagnosticsPublisher", "DataAgentEvidencePackBuilder", "DataAgentEvidenceDiagnosticsFormatter.Format")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs" @("functionService.RecordRecentDataAgentEvidenceDiagnostics")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentEvidencePackTests.cs" @("EvidenceDiagnosticsFormatterEmitsCompactStateEstimate", "EvidenceDiagnosticsFormatterEmitsUnavailableStateWhenPackMissing", "EvidenceDiagnosticsFormatterSanitizesReasonCode")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentAnalysisToolHandlerTests.cs" @("StartCallsOrchestratorAndPublishesOrchestratedContext", "DataAgent evidence diagnostics", "executed_sql=true")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("DataAgentEvidenceDiagnosticsPresent", "owner_diag=true", "analysis_confidence=true", "risk_level=true"))) -Detail "DataAgent owner evidence diagnostics markers"
    New-Check -Group "Analysis" -Name "DataAgentEvidenceRecentDiagnosticsBridgePresent" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs" @("evidenceDiagnosticsPublisher", "DataAgentEvidenceDiagnosticsFormatter.Format")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs" @("functionService.RecordRecentDataAgentEvidenceDiagnostics")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.QChat/QChatRecentDiagnosticsCache.cs" @("QChatRecentDiagnosticsCache", "DataAgentEvidence")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticTextSanitizer.cs" @("hidden_context_redacted", "[tool_route_context]", "[data_agent_evidence_pack]"))) -Detail "DataAgent evidence diagnostics bridge to recent QChat diagnostics cache"
    New-Check -Group "Analysis" -Name "DataAgentTraceTimelinePresent" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentTraceModels.cs" @("DataAgentTraceEvent", "DataAgentTraceTimeline", "DataAgentTraceEventKind")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentTraceRecorder.cs" @("DataAgentTraceRecorder", "GetLatest", "GetRecent", "PruneExpiredLocked")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentTraceDiagnosticsFormatter.cs" @("DataAgent trace diagnostics", "trace_unavailable")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs" @("traceDiagnosticsPublisher", "DataAgentTraceTimelineBuilder", "DataAgentTraceDiagnosticsFormatter.Format")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("DataAgentTraceTimelinePresent", "sql=redacted", "owner_diag=true", "hidden_context_redacted=true")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentTraceRecorderTests.cs" @("GetLatestReturnsNewestTimelineForSession", "ReadsFilterExpiredTimelinesWithoutRemovingThem")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentTraceDiagnosticsFormatterTests.cs" @("FormatEmitsStableTimelineDiagnostics", "FormatRedactsUnsafeFactValues"))) -Detail "DataAgent trace timeline diagnostics markers"
    New-Check -Group "Analysis" -Name "DataAgentProgressStreamingPresent" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentProgressModels.cs" @("DataAgentProgressEvent", "DataAgentProgressEventKind", "RouteGate", "Planner", "Execute", "Checkpoint")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentProgressRecorder.cs" @("DataAgentProgressRecorder", "GetLatest", "GetRecent", "PruneExpiredLocked")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentProgressDiagnosticsFormatter.cs" @("DataAgent progress diagnostics", "progress_unavailable")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentProgressDiagnosticsPublisher.cs" @("DataAgentProgressDiagnosticsPublisher", "DataAgentProgressDiagnosticsFormatter.Format")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentService.cs" @("DataAgentProgressEventKind.Planner", "DataAgentProgressEventKind.Execute", "sql")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisService.cs" @("progressSink", "DataAgentProgressEventKind.Summarize", "DataAgentProgressEventKind.End")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisOrchestrator.cs" @("DataAgentProgressEventKind.RouteGate", "DataAgentProgressEventKind.Checkpoint", "PublishCheckpointProgress")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("DataAgentProgressStreamingPresent", "DataAgent progress diagnostics", "sql=redacted", "hidden_context_redacted=true")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentProgressStreamingTests.cs" @("DataAgentProgressStreamingTests", "AcceptedQueryPublishesRuntimeBoundariesAndDoesNotCallAnswerTwice")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentProgressDiagnosticsPublisherTests.cs" @("DataAgentProgressDiagnosticsPublisherTests", "XmlFunctionCallerStoresRecentProgressDiagnostics"))) -Detail "DataAgent progress stream diagnostics markers"
    New-Check -Group "Analysis" -Name "AnalysisToolHandlerPresent" -Passed (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs" @("DataAgentAnalysisToolHandler", "dataagent_analysis_start", "dataagent_analysis_continue", "dataagent_analysis_summarize", "dataagent_analysis_end")) -Detail "analysis XML tool handler markers"
    New-Check -Group "Analysis" -Name "AnalysisToolsRegisteredInModule" -Passed (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs" @("DataAgentAnalysisCapabilityProvider", "InMemoryDataAgentAnalysisSessionStore", "PublishAnalysisContext", "UpdateDataAgentAnalysisRouteSessionFromContext")) -Detail "analysis tool module dynamic registration markers"
    New-Check -Group "Analysis" -Name "AnalysisTerminalToolsDoNotQuery" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs" @("dataagent_analysis_summarize", "dataagent_analysis_end", "orchestrator.Summarize", "orchestrator.End")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentAnalysisToolHandlerTests.cs" @("SummarizeCallsOrchestratorAndPublishesTerminalContext", "EndCallsOrchestratorAndPublishesTerminalContext", "orchestration_trace=Summarize:Succeeded>Checkpoint:Succeeded", "orchestration_trace=End:Succeeded>Checkpoint:Succeeded"))) -Detail "terminal analysis tools avoid answer-boundary query calls"
)

Write-Output "DataAgent Readiness"

foreach ($group in @("Core", "Schema", "Safety", "Query", "Context", "Planner", "Tool", "ToolBroker", "Store", "Governance", "Analysis")) {
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
$expectedRequired = 80
$requiredTotal = $requiredPassed + $requiredMissing

Write-Output "[Summary]"
Write-Output ("  Summary: {0} required passed, {1} required missing" -f $requiredPassed, $requiredMissing)

if ($requiredTotal -ne $expectedRequired) {
    Write-Output ("  ERROR readiness check count mismatch: expected {0}, found {1}" -f $expectedRequired, $requiredTotal)
    exit 1
}

if ($requiredMissing -gt 0) {
    exit 1
}

exit 0
