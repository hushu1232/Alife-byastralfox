Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot

$results = New-Object System.Collections.Generic.List[object]

function Test-DirectoryOmitsMarker {
    param(
        [string]$RelativePath,
        [string]$SearchPattern,
        [System.IO.SearchOption]$SearchOption,
        [string[]]$Markers
    )

    $fullPath = Join-Path $repoRoot $RelativePath
    if (-not (Test-Path -LiteralPath $fullPath)) {
        return $false
    }

    foreach ($path in [System.IO.Directory]::EnumerateFiles($fullPath, $SearchPattern, $SearchOption)) {
        $content = [System.IO.File]::ReadAllText($path)
        foreach ($marker in $Markers) {
            if ($content.IndexOf($marker, [System.StringComparison]::Ordinal) -ge 0) {
                return $false
            }
        }
    }

    return $true
}

function Add-Check {
    param(
        [string]$Group,
        [string]$Name,
        [string]$Path,
        [string[]]$Patterns,
        [string]$AlsoPath = "",
        [string[]]$AlsoPatterns = @(),
        [string]$OmitPath = "",
        [string]$OmitSearchPattern = "*.cs",
        [System.IO.SearchOption]$OmitSearchOption = [System.IO.SearchOption]::TopDirectoryOnly,
        [string[]]$OmitPatterns = @(),
        [bool]$Required = $true
    )

    $fullPath = Join-Path $repoRoot $Path
    $ok = $true
    $detail = $Path

    if (-not (Test-Path -LiteralPath $fullPath)) {
        $ok = $false
        $detail = "$Path missing"
    }
    elseif ($Patterns -and $Patterns.Count -gt 0) {
        $content = Get-Content -LiteralPath $fullPath -Raw
        foreach ($pattern in $Patterns) {
            if ($content.IndexOf($pattern, [System.StringComparison]::Ordinal) -lt 0) {
                $ok = $false
                $detail = "$Path missing marker '$pattern'"
                break
            }
        }
    }

    if ($ok -and -not [string]::IsNullOrWhiteSpace($AlsoPath)) {
        $alsoFullPath = Join-Path $repoRoot $AlsoPath
        if (-not (Test-Path -LiteralPath $alsoFullPath)) {
            $ok = $false
            $detail = "$AlsoPath missing"
        }
        elseif ($AlsoPatterns -and $AlsoPatterns.Count -gt 0) {
            $alsoContent = Get-Content -LiteralPath $alsoFullPath -Raw
            foreach ($pattern in $AlsoPatterns) {
                if ($alsoContent.IndexOf($pattern, [System.StringComparison]::Ordinal) -lt 0) {
                    $ok = $false
                    $detail = "$AlsoPath missing marker '$pattern'"
                    break
                }
            }
        }

        if ($ok) {
            $detail = "$Path; $AlsoPath"
        }
    }

    if ($ok -and -not [string]::IsNullOrWhiteSpace($OmitPath)) {
        if (Test-DirectoryOmitsMarker $OmitPath $OmitSearchPattern $OmitSearchOption $OmitPatterns) {
            $detail = "$detail; $OmitPath omits forbidden markers"
        }
        else {
            $ok = $false
            $detail = "$OmitPath contains forbidden marker"
        }
    }

    $results.Add([pscustomobject]@{
        Group = $Group
        Name = $Name
        Ok = $ok
        Detail = $detail
        Required = $Required
    }) | Out-Null
}

Add-Check -Group "Harness" -Name "QChat service adapter harness" -Path "Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs" -Patterns @("CreateStartedService", "FakeOneBotRuntime")
Add-Check -Group "Harness" -Name "Vision readiness tests" -Path "Tests/Alife.Test.QChat/QChatVisionReadinessTests.cs" -Patterns @("QChatVisionReadiness")
Add-Check -Group "Harness" -Name "Voice warmup coordinator tests" -Path "Tests/Alife.Test.QChat/QChatVoiceWarmupCoordinatorTests.cs" -Patterns @("QChatVoiceWarmupCoordinator")
Add-Check -Group "Harness" -Name "Model reply loop live tests" -Path "Tests/Alife.Test.QChat/QChatModelReplyLoopLiveTests.cs" -Patterns @("QChatModelReplyLoopLiveTests")
Add-Check -Group "Harness" -Name "Prompt leak contract tests" -Path "Tests/Alife.Test.QChat/QChatPromptLeakContractTests.cs" -Patterns @("QChatPromptLeakContractTests", "InternalStateTextDoesNotBecomePrivateVisibleReply")
Add-Check -Group "Harness" -Name "Runtime readiness script" -Path "tools/check-qchat-runtime-readiness.ps1" -Patterns @("QChat Runtime Readiness", "AgnesVisionKeyConfigured", "XiayuTts9880Reachable", "MixuTts9881Reachable", "-Live", "-Strict", "exit 1")
Add-Check -Group "Harness" -Name "DataAgent readiness script" -Path "tools/check-dataagent-readiness.ps1" -Patterns @("DataAgent Readiness", "DataAgentModulePresent", "QueryPlanFixturesPass", "ContextContributionStable", "PlannerInterfacePresent", "ToolHandlerReturnsDataAgentContext", "CapabilityBoundaryPresent", "ToolBrokerDynamicExposurePresent", "ToolRouteRuntimeWiringPresent", "QChatToolRouteStateScopePresent", "exit 1")
Add-Check -Group "Harness" -Name "DataAgent planner/tool integration" -Path "tools/check-dataagent-readiness.ps1" -Patterns @("PlannerInterfacePresent", "ToolHandlerReturnsDataAgentContext", "dataagent_query")
Add-Check -Group "Harness" -Name "Tool broker route tests" -Path "Tests/Alife.Test.Interpreter/ToolCapabilityRouterTests.cs" -Patterns @("ToolCapabilityRouterTests", "RouterDoesNotTreatOrdinaryContinueAsDataAgentAnalysis")
Add-Check -Group "Harness" -Name "Tool broker execution gate tests" -Path "Tests/Alife.Test.Interpreter/XmlFunctionPolicyTests.cs" -Patterns @("ExecutionPolicyRejectsToolOutsideCurrentRoute", "HandleRejectsGovernedDataAgentToolWhenRouteIsMissing", "HandleRejectsSessionScopedDataAgentToolWhenRouteSessionDoesNotMatch")
Add-Check -Group "Harness" -Name "QChat tool route state wiring" -Path "Tests/Alife.Test.QChat/QChatToolRouteStateWiringTests.cs" -Patterns @("DispatchToModelCreatesScopedToolRouteState", "functionService.CreateToolRouteState", "functionService.UseToolRouteState(routeState)")
Add-Check -Group "Harness" -Name "QChat owner Tool Broker diagnostics" -Path "sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs" -Patterns @("RecentToolRouteTrace", "Tool Broker diagnostics", "SanitizeToolRouteTrace")
Add-Check -Group "Harness" -Name "QChat semantic diagnostics" -Path "sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs" -Patterns @("RecentSemanticEstimate", "diag semantic", "BuildSemanticDiagnosticsText")
Add-Check -Group "Harness" -Name "DataAgent owner evidence diagnostics" -Path "sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs" -Patterns @("DataAgentCommandPrefix", "diag evidence", "RecentDataAgentEvidence", "BuildDataAgentEvidenceDiagnosticsText", "QChatDiagnosticTextSanitizer.SanitizeDiagnosticText")
Add-Check -Group "Harness" -Name "QChat recent diagnostics cache" -Path "sources/Alife.Function/Alife.Function.QChat/QChatRecentDiagnosticsCache.cs" -Patterns @("QChatRecentDiagnosticsCache", "maxEntriesPerSession", "GetLatest", "GetRecent", "PruneExpiredLocked")
Add-Check -Group "Harness" -Name "QChat recent diagnostics command" -Path "sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs" -Patterns @("diag recent", "BuildRecentDiagnosticsText", "QChatRecentDiagnosticsFormatter.FormatSummary")
Add-Check -Group "Harness" -Name "QChat diagnostics cache redaction" -Path "sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticTextSanitizer.cs" -Patterns @("hidden_context_redacted", "HiddenContextPattern", "SqlFragmentPattern", "[tool_route_context]", "[data_agent_evidence_pack]", "Allowed XML tools", "connection_string", "Authorization", "SqlStatementPattern")
Add-Check -Group "Harness" -Name "DataAgent trace diagnostics" -Path "sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs" -Patterns @("RecentDataAgentTrace", "diag trace", "BuildDataAgentTraceDiagnosticsText", "DataAgent trace diagnostics")
Add-Check -Group "Harness" -Name "DataAgent progress diagnostics" -Path "sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs" -Patterns @("RecentDataAgentProgress", "diag progress", "BuildDataAgentProgressDiagnosticsText", "DataAgent progress diagnostics")
Add-Check -Group "Harness" -Name "DataAgent scenario context diagnostics" -Path "tools/check-dataagent-readiness.ps1" -Patterns @("DataAgentScenarioContextIntegrated", "DataAgentScenarioDiagnosticsFormatter", "DataAgent scenario diagnostics", "scenario_context_matched") -AlsoPath "Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs" -AlsoPatterns @("QChatDoesNotDirectlyImportDataAgentBoundaryTypes", "DataAgentScenarioKnowledgePackProvider", "DataAgentScenarioContextBuilder", "DataAgentToolScopePolicy") -OmitPath "sources/Alife.Function/Alife.Function.QChat" -OmitSearchPattern "*.cs" -OmitSearchOption ([System.IO.SearchOption]::AllDirectories) -OmitPatterns @("DataAgentScenarioKnowledgePackProvider", "DataAgentScenarioContextBuilder", "DataAgentToolScopePolicy")
Add-Check -Group "Harness" -Name "DataAgent runtime scenario context activation" -Path "tools/check-dataagent-readiness.ps1" -Patterns @("DataAgentRuntimeScenarioContextActivationPresent", "DataAgentRuntimeScenarioContextActivationTests", "DataAgentScenarioContextProvider", "service_context=true", "llm_prompt=true") -OmitPath "sources/Alife.Function/Alife.Function.QChat" -OmitSearchPattern "*.cs" -OmitSearchOption ([System.IO.SearchOption]::AllDirectories) -OmitPatterns @("DataAgentScenarioKnowledgePackProvider", "DataAgentScenarioContextBuilder", "DataAgentToolScopePolicy")
Add-Check -Group "Harness" -Name "DataAgent dynamic tool route contract" -Path "sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs" -Patterns @("Tool Broker contract", "PublishAnalysisContext", "UpdateDataAgentAnalysisRouteSessionFromContext", "Only use DataAgent XML tools when they appear in current [tool_route_context]")
Add-Check -Group "Harness" -Name "DataAgent capability provider boundary" -Path "sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs" -Patterns @("DataAgentCapabilityRegistry", "DataAgentQueryCapabilityProvider", "DataAgentAnalysisCapabilityProvider", "RegisteredCapabilityProviderNames", "RegisteredCapabilityToolNames")
Add-Check -Group "Harness" -Name "DataAgent store provider boundary" -Path "tools/check-dataagent-readiness.ps1" -Patterns @("DataAgentStoreBoundaryPresent", "SqliteStoreCompatibilityPresent", "PostgresStoreProviderPresent", "PostgresLiveTestsEnvironmentGated", "DataAgentServiceUsesStoreBoundary")
Add-Check -Group "Harness" -Name "DataAgent PostgreSQL checkpoint persistence" -Path "tools/check-dataagent-readiness.ps1" -Patterns @("PostgresCheckpointPersistencePresent", "PostgresDataAgentAnalysisSessionStore", "DataAgentAnalysisSessionStoreFactory", "DataAgentModuleService", "session_store=true", "factory=true", "module_wiring=true", "live_test_gated=") -OmitPath "sources/Alife.Function/Alife.Function.QChat" -OmitSearchPattern "*.cs" -OmitSearchOption ([System.IO.SearchOption]::AllDirectories) -OmitPatterns @("PostgresDataAgentAnalysisSessionStore", "DataAgentAnalysisSessionStoreFactory")
Add-Check -Group "Harness" -Name "DataAgent graph sidecar contract" -Path "tools/check-dataagent-readiness.ps1" -Patterns @("GraphSidecarContractPresent", "DataAgentGraphSidecarContract", "ALIFE_DATAAGENT_GRAPH_SIDECAR_ENABLED", "no_sql_authority=true", "no_runtime=true") -AlsoPath "Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs" -AlsoPatterns @("QChatDoesNotDirectlyImportDataAgentBoundaryTypes", "DataAgentGraphSidecarContract", "DataAgentGraphSidecarNodeKind", "DataAgentGraphSidecarAuthority", "DataAgentGraphSidecarOptions", "DataAgentGraphSidecarPolicy", "DataAgentGraphSidecarRequest", "DataAgentGraphSidecarResponse") -OmitPath "sources/Alife.Function/Alife.Function.QChat" -OmitSearchPattern "*.cs" -OmitSearchOption ([System.IO.SearchOption]::AllDirectories) -OmitPatterns @("DataAgentGraphSidecarContract", "DataAgentGraphSidecarNodeKind", "DataAgentGraphSidecarAuthority", "DataAgentGraphSidecarOptions", "DataAgentGraphSidecarPolicy", "DataAgentGraphSidecarRequest", "DataAgentGraphSidecarResponse")
Add-Check -Group "Harness" -Name "DataAgent DataQueryGraph pilot" -Path "tools/check-dataagent-readiness.ps1" -Patterns @("DataQueryGraphPilotPresent", "DataAgentDataQueryGraphPilot", "ALIFE_DATAAGENT_DATAQUERYGRAPH_PILOT_ENABLED", "no_langgraph_runtime=true", "node_scope=true", "no_sql_authority=true", "plan_shape=true", "transition_shape=true", "execute_scope=true") -AlsoPath "Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs" -AlsoPatterns @("QChatDoesNotDirectlyImportDataAgentBoundaryTypes", "DataAgentDataQueryGraphOptions", "DataAgentDataQueryGraphPilot", "DataAgentDataQueryGraphPlan", "DataAgentDataQueryGraphNode", "DataAgentDataQueryGraphTransition", "DataAgentDataQueryGraphDryRunResult", "DataAgentDataQueryGraphTraceFormatter") -OmitPath "sources/Alife.Function/Alife.Function.QChat" -OmitSearchPattern "*.cs" -OmitSearchOption ([System.IO.SearchOption]::AllDirectories) -OmitPatterns @("DataAgentDataQueryGraphOptions", "DataAgentDataQueryGraphPilot", "DataAgentDataQueryGraphPlan", "DataAgentDataQueryGraphNode", "DataAgentDataQueryGraphTransition", "DataAgentDataQueryGraphDryRunResult", "DataAgentDataQueryGraphTraceFormatter")
Add-Check -Group "Harness" -Name "DataAgent DataQueryGraph owner diagnostics" -Path "tools/check-dataagent-readiness.ps1" -Patterns @("DataQueryGraphOwnerDiagnosticsPresent", "RecordRecentDataAgentGraphDiagnostics", "RecentDataAgentGraphDiagnostics", "dataQueryGraphDiagnosticsPublisher") -AlsoPath "sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs" -AlsoPatterns @("RecentDataAgentGraph", "diag graph", "BuildDataAgentGraphDiagnosticsText", "DataAgent graph diagnostics") -OmitPath "sources/Alife.Function/Alife.Function.QChat" -OmitSearchPattern "*.cs" -OmitSearchOption ([System.IO.SearchOption]::AllDirectories) -OmitPatterns @("DataAgentDataQueryGraph")
Add-Check -Group "Harness" -Name "DataAgent diagnostics command contract" -Path "sources/Alife.Function/Alife.Function.QChat/QChatDataAgentDiagnosticsCommandContract.cs" -Patterns @("QChatDataAgentDiagnosticsCommandContract", "QChatDataAgentDiagnosticsTopic", "SupportedDataAgentCommandSuffixes", "TryParseDataAgentCommand", "TryParseDataAgentCommandSuffix", "TryParseQChatDataAgentDiagnosticsCommandSuffix") -AlsoPath "sources/Alife.Function/Alife.Function.QChat/QChatCommandAccessPolicy.cs" -AlsoPatterns @("QChatDataAgentDiagnosticsCommandContract.TryParseDataAgentCommand") -OmitPath "sources/Alife.Function/Alife.Function.QChat" -OmitSearchPattern "*.cs" -OmitSearchOption ([System.IO.SearchOption]::AllDirectories) -OmitPatterns @("DataAgentDataQueryGraph")
# V2.10 governance readiness gates
Add-Check -Group "Harness" -Name "Alife capability governance catalog" -Path "sources/Alife.Function/Alife.Function.FunctionCaller/AlifeCapabilityGovernanceCatalog.cs" -Patterns @("AlifeCapabilityGovernanceCatalog", "QChat", "DataAgent", "DesktopControl", "AgentWorkflowCandidate", "DeterministicSafetyGate")
Add-Check -Group "Harness" -Name "DataAgent node tool scope policy" -Path "sources/Alife.Function/Alife.Function.DataAgent/DataAgentToolScopePolicy.cs" -Patterns @("DataAgentToolScopePolicy", "QueryPlanner", "DiagnosticsRouter", "ExecuteReadOnlyQuery", "ReadProgressDiagnostics")

Add-Check -Group "Loop" -Name "OneBot receive loop" -Path "sources/Alife.Function/Alife.Function.QChat/OneBotClient.cs" -Patterns @("ReceiveLoop", "while (ws.State == WebSocketState.Open")
Add-Check -Group "Loop" -Name "QChat event queue loop" -Path "sources/Alife.Function/Alife.Function.QChat/QChatService.cs" -Patterns @("ProcessOneBotEventQueueAsync", "oneBotEventProcessingTask")
Add-Check -Group "Loop" -Name "QChat time iterative update loop" -Path "sources/Alife.Function/Alife.Function.QChat/QChatService.cs" -Patterns @("ITimeIterative", "OnUpdate")
Add-Check -Group "Loop" -Name "Semantic settle dispatch loop" -Path "sources/Alife.Function/Alife.Function.QChat/QChatService.cs" -Patterns @("ScheduleSettledDispatch", "DispatchSettledConversationAsync")
Add-Check -Group "Loop" -Name "Continuation gate" -Path "sources/Alife.Function/Alife.Function.QChat/QChatService.cs" -Patterns @("EnableContinuationGate")
Add-Check -Group "Loop" -Name "Voice warmup retry coordinator" -Path "sources/Alife.Function/Alife.Function.QChat/QChatVoiceWarmupCoordinator.cs" -Patterns @("QChatVoiceWarmupCoordinator", "Task.Delay")
Add-Check -Group "Loop" -Name "Continuation policy" -Path "sources/Alife.Function/Alife.Function.QChat/QChatContinuationPolicy.cs" -Patterns @("QChatContinuationPolicy", "Decide")
Add-Check -Group "Loop" -Name "Continuation invariant tests" -Path "Tests/Alife.Test.QChat/QChatContinuationPolicyTests.cs" -Patterns @("DeterministicTaskWithoutFeedbackStillBlocksModelDispatch", "FeedbackFlagAloneDoesNotSuppressNormalConversation")
Add-Check -Group "Loop" -Name "Semantic settle window contract tests" -Path "Tests/Alife.Test.QChat/QChatSemanticSettleWindowTests.cs" -Patterns @("EmptyWindowNeverSettles", "MaxWindowDurationForcesIncompleteTrailingTextToSettle")
Add-Check -Group "Loop" -Name "QChat Kalman semantic state estimator" -Path "sources/Alife.Function/Alife.Function.QChat/QChatSemanticStateEstimator.cs" -Patterns @("QChatSemanticStateEstimator", "KalmanScalarFilter", "semantic_completion_stable")
Add-Check -Group "Loop" -Name "QChat Kalman settle window integration" -Path "sources/Alife.Function/Alife.Function.QChat/QChatSemanticSettleWindow.cs" -Patterns @("QChatSemanticStateEstimator.Estimate", "estimate.ShouldAnswer", "estimate.ShouldSummarize")
Add-Check -Group "Loop" -Name "Voice warmup contract tests" -Path "Tests/Alife.Test.QChat/QChatVoiceWarmupCoordinatorTests.cs" -Patterns @("WarmupAsync_MultipleProfilesTrackIndependentStatuses", "StartAsync_RetriesUntilEndpointBecomesReachable")
Add-Check -Group "Loop" -Name "XiaYu self-state machine" -Path "sources/Alife.Function/Alife.Function.QChat/XiaYuSelfStateMachine.cs" -Patterns @("XiaYuSelfStateMachine", "Apply")
Add-Check -Group "Loop" -Name "Owner event dispatcher" -Path "sources/Alife.Function/Alife.Function.QChat/QChatOwnerEventDispatcher.cs" -Patterns @("QChatOwnerEventDispatcher", "FlushAsync")
Add-Check -Group "Loop" -Name "Tool route decision model" -Path "sources/Alife.Function/Alife.Function.FunctionCaller/ToolRouteModels.cs" -Patterns @("ToolRouteDecision", "AllowedTools", "DeniedTools")
Add-Check -Group "Loop" -Name "Tool broker runtime wiring" -Path "sources/Alife.Function/Alife.Function.FunctionCaller/XmlFunctionCaller.cs" -Patterns @("RouteCurrentTurn", "BuildRoutedFunctionGuide", "SetGovernedToolNames", "[tool_route_context]", "ChatSend += OnChatSend")

Add-Check -Group "Prompt" -Name "Stable persona prompt registration" -Path "sources/Alife.Function/Alife.Function.QChat/QChatService.cs" -Patterns @("RegisterStablePersonaPromptIfNeeded")
Add-Check -Group "Prompt" -Name "Persona intensity prompt formatter" -Path "sources/Alife.Function/Alife.Function.QChat/QChatAggressionBoundaryPolicy.cs" -Patterns @("QChatPersonaIntensityPromptFormatter", "persona_intensity")
Add-Check -Group "Prompt" -Name "Persona frame prompt" -Path "sources/Alife.Function/Alife.Function.QChat/QChatService.cs" -Patterns @("FormatPersonaFramePrompt", "[qchat persona frame]")
Add-Check -Group "Prompt" -Name "Conversation cognition prompt" -Path "sources/Alife.Function/Alife.Function.QChat/QChatConversationCognition.cs" -Patterns @("BuildInternalPrompt")
Add-Check -Group "Prompt" -Name "Address prompt" -Path "sources/Alife.Function/Alife.Function.QChat/QChatService.cs" -Patterns @("BuildAddressPrompt")
Add-Check -Group "Prompt" -Name "Quiet mode prompt" -Path "sources/Alife.Function/Alife.Function.QChat/QChatService.cs" -Patterns @("BuildQuietModeAcknowledgementPrompt")
Add-Check -Group "Prompt" -Name "XiaYu private state prompt" -Path "sources/Alife.Function/Alife.Function.QChat/XiaYuSelfStateMachine.cs" -Patterns @("XiaYuStatePromptFormatter", "[XiaYu state - private, do not quote]")
Add-Check -Group "Prompt" -Name "Semantic window summary prompt" -Path "sources/Alife.Function/Alife.Function.QChat/QChatSemanticWindowSummary.cs" -Patterns @("QChatSemanticWindowSummary", "[semantic_window]")
Add-Check -Group "Prompt" -Name "Untrusted external context wrapper" -Path "sources/Alife/Alife.Framework/Models/Module/ContextContribution.cs" -Patterns @("ExternalContextFormatter", "WrapUntrusted")
Add-Check -Group "Prompt" -Name "Context budget composer" -Path "sources/Alife/Alife.Framework/Models/Module/ContextContribution.cs" -Patterns @("ContextBudgetComposer", "Compose")
Add-Check -Group "Prompt" -Name "Visible text policy" -Path "sources/Alife.Function/Alife.Function.QChat/QChatVisibleTextPolicy.cs" -Patterns @("QChatVisibleTextPolicy", "IsHumanInvisibleStateText")
Add-Check -Group "Prompt" -Name "Visible reply policy" -Path "sources/Alife.Function/Alife.Function.QChat/QChatVisibleReplyPolicy.cs" -Patterns @("QChatVisibleReplyPolicy", "IsHumanInvisibleStateText")
Add-Check -Group "Prompt" -Name "Experience sanitizer" -Path "sources/Alife.Function/Alife.Function.QChat/QChatExperienceSanitizer.cs" -Patterns @("QChatExperienceSanitizer", "SanitizeOutgoing")
Add-Check -Group "Prompt" -Name "Dynamic tool manifest boundary" -Path "sources/Alife.Function/Alife.Function.FunctionCaller/ToolCapabilityManifest.cs" -Patterns @("ToolCapabilityManifest", "Preconditions", "StateEffect")

Write-Output "QChat Engineering Map"

foreach ($group in @("Harness", "Loop", "Prompt")) {
    Write-Output "[$group]"
    foreach ($result in ($results | Where-Object { $_.Group -eq $group })) {
        if ($result.Required -and $result.Ok) {
            Write-Output ("  PASS     {0}: {1}" -f $result.Name, $result.Detail)
        }
        elseif ($result.Required) {
            Write-Output ("  MISSING  {0}: {1}" -f $result.Name, $result.Detail)
        }
        elseif ($result.Ok) {
            Write-Output ("  OPTIONAL {0}: {1}" -f $result.Name, $result.Detail)
        }
        else {
            Write-Output ("  OPTIONAL-MISSING {0}: {1}" -f $result.Name, $result.Detail)
        }
    }
}

$requiredPassed = @($results | Where-Object { $_.Required -and $_.Ok }).Count
$requiredMissing = @($results | Where-Object { $_.Required -and -not $_.Ok }).Count
$optionalPresent = @($results | Where-Object { -not $_.Required -and $_.Ok }).Count
$optionalMissing = @($results | Where-Object { -not $_.Required -and -not $_.Ok }).Count
$expectedRequired = 61
$requiredTotal = $requiredPassed + $requiredMissing

Write-Output ("Summary: {0} required passed, {1} required missing, {2} optional present, {3} optional missing" -f $requiredPassed, $requiredMissing, $optionalPresent, $optionalMissing)

if ($requiredTotal -ne $expectedRequired) {
    Write-Output ("ERROR engineering map check count mismatch: expected {0}, found {1}" -f $expectedRequired, $requiredTotal)
    exit 1
}

if ($requiredMissing -gt 0) {
    exit 1
}

exit 0
