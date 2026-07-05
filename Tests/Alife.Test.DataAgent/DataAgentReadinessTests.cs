using Alife.Function.DataAgent;
using Alife.Function.FunctionCaller;
using System.Diagnostics;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentReadinessTests
{
    [Test]
    public void CoreReadinessChecksAllPass()
    {
        string databasePath = NewDatabasePath();

        IReadOnlyList<DataAgentReadinessCheck> checks = DataAgentReadiness.CheckCore(databasePath);

        Assert.Multiple(() =>
        {
            Assert.That(checks, Has.Count.EqualTo(69));
            Assert.That(checks.All(check => check.Passed), Is.True, string.Join(Environment.NewLine, checks.Select(check => $"{check.Name}:{check.Detail}")));
            Assert.That(checks.Select(check => check.Name), Does.Contain("DataAgentModulePresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("SqliteSchemaInitializes"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("FixtureDataImports"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("QueryPlanFixturesPass"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("DangerousSqlRejected"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("ReadOnlyQueryExecutes"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("DataAgentStoreBoundaryPresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("SqliteStoreCompatibilityPresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("PostgresStoreProviderPresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("PostgresLiveTestsEnvironmentGated"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("PostgresCheckpointPersistencePresent"));
            DataAgentReadinessCheck postgresCheckpointCheck = checks.Single(check => check.Name == "PostgresCheckpointPersistencePresent");
            Assert.That(postgresCheckpointCheck.Detail, Does.Contain("session_store=true"));
            Assert.That(postgresCheckpointCheck.Detail, Does.Contain("factory=true"));
            Assert.That(postgresCheckpointCheck.Detail, Does.Contain("module_wiring=true"));
            Assert.That(postgresCheckpointCheck.Detail, Does.Contain("live_test_gated="));
            Assert.That(checks.Select(check => check.Name), Does.Contain("GraphSidecarContractPresent"));
            DataAgentReadinessCheck graphSidecarCheck = checks.Single(check => check.Name == "GraphSidecarContractPresent");
            Assert.That(graphSidecarCheck.Detail, Does.Contain("default_enabled=false"));
            Assert.That(graphSidecarCheck.Detail, Does.Contain("contract=true"));
            Assert.That(graphSidecarCheck.Detail, Does.Contain("policy=true"));
            Assert.That(graphSidecarCheck.Detail, Does.Contain("no_sql_authority=true"));
            Assert.That(graphSidecarCheck.Detail, Does.Contain("no_runtime=true"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("DataAgentServiceUsesStoreBoundary"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("ContextContributionStable"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("PlannerInterfacePresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("DeterministicPlannerPassesFixtures"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("ServiceUsesInjectedPlanner"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("UnsafePlannerOutputRejected"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("ToolHandlerReturnsDataAgentContext"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("LlmPlannerInterfacePresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("LlmPlannerPromptUsesSchemaSnapshot"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("LlmPlannerStrictJsonParser"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("LlmPlannerRejectsInvalidOutput"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("LlmPlannerFallbackPreservesSafety"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("ClarificationRequestSupported"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("NaturalLanguageResultExplanationPresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("ToolBrokerAuditLogPresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("CapabilityBoundaryPresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("DataAgentOrchestratorPresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("OrchestratorNodeBoundaryPresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("OrchestratorCheckpointPresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("OrchestratorRouteGateFailClosed"));
            DataAgentReadinessCheck routeGateCheck = checks.Single(check => check.Name == "OrchestratorRouteGateFailClosed");
            Assert.That(routeGateCheck.Detail, Does.Contain("continue"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("OrchestratorTerminalNodesDoNotQuery"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("OrchestratorStateMachineTransitions"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("AnalysisToolHandlerUsesOrchestrator"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("OrchestratorTraceContextPresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("OrchestratorCheckpointContextPresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("OrchestratorRuntimeStartPathCovered"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("OrchestratorRuntimeContinuePathCovered"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("OrchestratorRuntimeTerminalPathCovered"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("OrchestratorRuntimeRouteDeniedFailClosed"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("AnalysisHandlerConsumesToolRouteContext"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("OrchestrationRequestUsesRuntimeRouteDecision"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("RouteMissingRequestFailsClosed"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("RouteEvidenceContextPresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("RouteSessionScopePreserved"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("TerminalRouteDoesNotQuery"));
            DataAgentReadinessCheck terminalRouteCheck = checks.Single(check => check.Name == "TerminalRouteDoesNotQuery");
            Assert.That(terminalRouteCheck.Detail, Does.Contain("route_tool=dataagent_analysis_summarize"));
            Assert.That(terminalRouteCheck.Detail, Does.Contain("route_allows_query=true"));
            Assert.That(terminalRouteCheck.Detail, Does.Match("route_session_id=[0-9a-f]{32}"));
            Assert.That(terminalRouteCheck.Detail, Does.Contain("answer_calls_unchanged=true"));
            Assert.That(terminalRouteCheck.Detail, Does.Contain("denied_terminal_fail_closed=true"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("DataAgentEvidencePackPresent"));
            DataAgentReadinessCheck evidencePackCheck = checks.Single(check => check.Name == "DataAgentEvidencePackPresent");
            Assert.That(evidencePackCheck.Detail, Does.Contain("accepted=true"));
            Assert.That(evidencePackCheck.Detail, Does.Contain("accepted_route_context=runtime"));
            Assert.That(evidencePackCheck.Detail, Does.Contain("denied=true"));
            Assert.That(evidencePackCheck.Detail, Does.Contain("terminal=true"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("SemanticStateEstimatorCorePresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("DataAgentAnalysisStateEstimatorPresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("DataAgentEvidenceDiagnosticsPresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("DataAgentEvidenceRecentDiagnosticsBridgePresent"));
            DataAgentReadinessCheck recentDiagnosticsBridgeCheck = checks.Single(check => check.Name == "DataAgentEvidenceRecentDiagnosticsBridgePresent");
            Assert.That(recentDiagnosticsBridgeCheck.Detail, Does.Contain("publisher_type=Action<string>"));
            Assert.That(recentDiagnosticsBridgeCheck.Detail, Does.Contain("no_qchat_reference=true"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("DataAgentTraceTimelinePresent"));
            DataAgentReadinessCheck traceTimelineCheck = checks.Single(check => check.Name == "DataAgentTraceTimelinePresent");
            Assert.That(traceTimelineCheck.Detail, Does.Contain("trace_timeline=true"));
            Assert.That(traceTimelineCheck.Detail, Does.Contain("owner_diag=true"));
            Assert.That(traceTimelineCheck.Detail, Does.Contain("sql_redacted=true"));
            Assert.That(traceTimelineCheck.Detail, Does.Contain("hidden_context_redacted=true"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("DataAgentProgressStreamingPresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("DataAgentScenarioKnowledgePackPresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("DataAgentScenarioContextIntegrated"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("DataAgentRuntimeScenarioContextActivationPresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("DataAgentNodeToolScopePolicyPresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("DataAgentSafetyCapabilitiesRemainDeterministic"));
            DataAgentReadinessCheck progressStreamingCheck = checks.Single(check => check.Name == "DataAgentProgressStreamingPresent");
            Assert.That(progressStreamingCheck.Detail, Does.Contain("progress_stream=true"));
            Assert.That(progressStreamingCheck.Detail, Does.Contain("owner_diag=true"));
            Assert.That(progressStreamingCheck.Detail, Does.Contain("sql_redacted=true"));
            Assert.That(progressStreamingCheck.Detail, Does.Contain("hidden_context_redacted=true"));
            Assert.That(progressStreamingCheck.Detail, Does.Contain("evidence_pack_redacted=true"));
            Assert.That(progressStreamingCheck.Detail, Does.Contain("tool_route_redacted=true"));
            DataAgentReadinessCheck scenarioContextCheck = checks.Single(check => check.Name == "DataAgentScenarioContextIntegrated");
            Assert.That(scenarioContextCheck.Detail, Does.Contain("scenario_context=true"));
            Assert.That(scenarioContextCheck.Detail, Does.Contain("prompt_hint=true"));
            Assert.That(scenarioContextCheck.Detail, Does.Contain("owner_diag=true"));
            Assert.That(scenarioContextCheck.Detail, Does.Contain("sql_boundary=true"));
            DataAgentReadinessCheck runtimeScenarioCheck = checks.Single(check => check.Name == "DataAgentRuntimeScenarioContextActivationPresent");
            Assert.That(runtimeScenarioCheck.Detail, Does.Contain("service_context=true"));
            Assert.That(runtimeScenarioCheck.Detail, Does.Contain("llm_prompt=true"));
            Assert.That(runtimeScenarioCheck.Detail, Does.Contain("qchat_boundary=true"));
            Assert.That(runtimeScenarioCheck.Detail, Does.Contain("sql_boundary=true"));
            string[] readinessNames = checks.Select(check => check.Name).ToArray();
            Assert.That(Array.IndexOf(readinessNames, "DataAgentEvidenceDiagnosticsPresent"), Is.EqualTo(Array.IndexOf(readinessNames, "DataAgentAnalysisStateEstimatorPresent") + 1));
            Assert.That(Array.IndexOf(readinessNames, "DataAgentEvidenceRecentDiagnosticsBridgePresent"), Is.EqualTo(Array.IndexOf(readinessNames, "DataAgentEvidenceDiagnosticsPresent") + 1));
            Assert.That(Array.IndexOf(readinessNames, "DataAgentTraceTimelinePresent"), Is.EqualTo(Array.IndexOf(readinessNames, "DataAgentEvidenceRecentDiagnosticsBridgePresent") + 1));
            Assert.That(Array.IndexOf(readinessNames, "DataAgentProgressStreamingPresent"), Is.EqualTo(Array.IndexOf(readinessNames, "DataAgentTraceTimelinePresent") + 1));
        });
    }

    [Test]
    public void ReadinessScriptDefaultModeExitsZeroAndPrintsSummary()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1");

        ScriptResult result = RunPowerShellScript(scriptPath);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("DataAgent Readiness"));
            Assert.That(result.StandardOutput, Does.Contain("DataAgentModulePresent"));
            Assert.That(result.StandardOutput, Does.Contain("[Analysis]"));
            Assert.That(result.StandardOutput, Does.Contain("AnalysisSummaryWindowPresent"));
            Assert.That(GetSummaryLines(result.StandardOutput), Is.EqualTo(new[]
            {
                "  Summary: 83 required passed, 0 required missing"
            }));
            Assert.That(result.StandardOutput, Does.Contain("AnalysisToolHandlerUsesOrchestrator"));
            Assert.That(result.StandardOutput, Does.Contain("OrchestratorTraceContextPresent"));
            Assert.That(result.StandardOutput, Does.Contain("OrchestratorRuntimeRouteDeniedFailClosed"));
            Assert.That(result.StandardOutput, Does.Contain("AnalysisHandlerConsumesToolRouteContext"));
            Assert.That(result.StandardOutput, Does.Contain("OrchestrationRequestUsesRuntimeRouteDecision"));
            Assert.That(result.StandardOutput, Does.Contain("RouteMissingRequestFailsClosed"));
            Assert.That(result.StandardOutput, Does.Contain("RouteEvidenceContextPresent"));
            Assert.That(result.StandardOutput, Does.Contain("RouteSessionScopePreserved"));
            Assert.That(result.StandardOutput, Does.Contain("TerminalRouteDoesNotQuery"));
            Assert.That(result.StandardOutput, Does.Contain("DataAgentEvidencePackPresent"));
            Assert.That(result.StandardOutput, Does.Contain("SemanticStateEstimatorCorePresent"));
            Assert.That(result.StandardOutput, Does.Contain("DataAgentAnalysisStateEstimatorPresent"));
            Assert.That(result.StandardOutput, Does.Contain("DataAgentEvidenceDiagnosticsPresent"));
            Assert.That(result.StandardOutput, Does.Contain("DataAgentEvidenceRecentDiagnosticsBridgePresent"));
            Assert.That(result.StandardOutput, Does.Contain("DataAgentTraceTimelinePresent"));
            Assert.That(result.StandardOutput, Does.Contain("DataAgentProgressStreamingPresent"));
            Assert.That(result.StandardOutput, Does.Contain("DataAgentScenarioKnowledgePackPresent"));
            Assert.That(result.StandardOutput, Does.Contain("DataAgentScenarioContextIntegrated"));
            Assert.That(result.StandardOutput, Does.Contain("DataAgentRuntimeScenarioContextActivationPresent"));
            Assert.That(result.StandardOutput, Does.Contain("GraphSidecarContractPresent"));
            Assert.That(result.StandardOutput, Does.Contain("DataAgentNodeToolScopePolicyPresent"));
            Assert.That(result.StandardOutput, Does.Contain("DataAgentSafetyCapabilitiesRemainDeterministic"));
            Assert.That(result.StandardOutput, Does.Not.Contain("Baseline Summary"));
        });
    }

    [Test]
    public void ReadinessScriptProtectsV23RouteGateContract()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1");
        string script = File.ReadAllText(scriptPath);

        string declaration = FindNewCheckDeclaration(script, "OrchestrationRequestUsesRuntimeRouteDecision");

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("$expectedRequired = 83"));
            Assert.That(script, Does.Contain("readiness check count mismatch"));
            Assert.That(script, Does.Contain("function Test-FileOrderedMarkers"));
            Assert.That(declaration, Does.Contain("Test-FileOrderedMarkers"));
            Assert.That(declaration, Does.Contain("new DataAgentOrchestrationRequest("));
            Assert.That(declaration, Does.Contain("routeContext.AllowsQuery"));
            Assert.That(declaration, Does.Contain("routeContext))"));
        });
    }

    [Test]
    public void ReadinessScriptProtectsV26EvidenceDiagnosticsContract()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1");
        string script = File.ReadAllText(scriptPath);

        string declaration = FindNewCheckDeclaration(script, "DataAgentEvidenceDiagnosticsPresent");

        Assert.Multiple(() =>
        {
            Assert.That(declaration, Does.Contain("DataAgentEvidenceDiagnosticsFormatter.cs"));
            Assert.That(declaration, Does.Contain("DataAgentAnalysisToolHandler.cs"));
            Assert.That(declaration, Does.Contain("DataAgentModuleService.cs"));
            Assert.That(declaration, Does.Contain("DataAgent evidence diagnostics"));
            Assert.That(declaration, Does.Contain("state_estimate_reason_code"));
            Assert.That(declaration, Does.Contain("DataAgentEvidenceDiagnosticsFormatter.Format"));
            Assert.That(declaration, Does.Contain("DataAgentEvidencePackBuilder"));
            Assert.That(declaration, Does.Contain("functionService.RecordRecentDataAgentEvidenceDiagnostics"));
            Assert.That(declaration, Does.Contain("EvidenceDiagnosticsFormatterEmitsCompactStateEstimate"));
            Assert.That(declaration, Does.Contain("StartCallsOrchestratorAndPublishesOrchestratedContext"));
        });
    }

    [Test]
    public void ReadinessScriptProtectsV27RecentDiagnosticsBridgeContract()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1");
        string script = File.ReadAllText(scriptPath);

        string declaration = FindNewCheckDeclaration(script, "DataAgentEvidenceRecentDiagnosticsBridgePresent");

        Assert.Multiple(() =>
        {
            Assert.That(declaration, Does.Contain("DataAgentAnalysisToolHandler.cs"));
            Assert.That(declaration, Does.Contain("evidenceDiagnosticsPublisher"));
            Assert.That(declaration, Does.Contain("DataAgentEvidenceDiagnosticsFormatter.Format"));
            Assert.That(declaration, Does.Contain("DataAgentModuleService.cs"));
            Assert.That(declaration, Does.Contain("functionService.RecordRecentDataAgentEvidenceDiagnostics"));
            Assert.That(declaration, Does.Contain("QChatRecentDiagnosticsCache.cs"));
            Assert.That(declaration, Does.Contain("QChatDiagnosticTextSanitizer.cs"));
            Assert.That(declaration, Does.Contain("DataAgentEvidence"));
            Assert.That(declaration, Does.Contain("[tool_route_context]"));
            Assert.That(declaration, Does.Contain("[data_agent_evidence_pack]"));
            Assert.That(declaration, Does.Contain("hidden_context_redacted"));
        });
    }

    [Test]
    public void ReadinessScriptProtectsV28TraceTimelineContract()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1");
        string script = File.ReadAllText(scriptPath);

        string declaration = FindNewCheckDeclaration(script, "DataAgentTraceTimelinePresent");

        Assert.Multiple(() =>
        {
            Assert.That(declaration, Does.Contain("DataAgentTraceModels.cs"));
            Assert.That(declaration, Does.Contain("DataAgentTraceRecorder.cs"));
            Assert.That(declaration, Does.Contain("DataAgentTraceDiagnosticsFormatter.cs"));
            Assert.That(declaration, Does.Contain("DataAgentAnalysisToolHandler.cs"));
            Assert.That(declaration, Does.Contain("DataAgentTraceTimelineBuilder"));
            Assert.That(declaration, Does.Contain("DataAgent trace diagnostics"));
            Assert.That(declaration, Does.Contain("trace_unavailable"));
            Assert.That(declaration, Does.Contain("sql=redacted"));
            Assert.That(declaration, Does.Contain("hidden_context_redacted=true"));
            Assert.That(declaration, Does.Contain("GetLatestReturnsNewestTimelineForSession"));
            Assert.That(declaration, Does.Contain("FormatRedactsUnsafeFactValues"));
        });
    }

    [Test]
    public void ReadinessScriptProtectsV29ProgressStreamingContract()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1");
        string script = File.ReadAllText(scriptPath);

        string declaration = FindNewCheckDeclaration(script, "DataAgentProgressStreamingPresent");

        Assert.Multiple(() =>
        {
            Assert.That(declaration, Does.Contain("DataAgentProgressModels.cs"));
            Assert.That(declaration, Does.Contain("DataAgentProgressRecorder.cs"));
            Assert.That(declaration, Does.Contain("DataAgentProgressDiagnosticsFormatter.cs"));
            Assert.That(declaration, Does.Contain("DataAgentProgressDiagnosticsPublisher.cs"));
            Assert.That(declaration, Does.Contain("DataAgentService.cs"));
            Assert.That(declaration, Does.Contain("DataAgentAnalysisService.cs"));
            Assert.That(declaration, Does.Contain("DataAgentAnalysisOrchestrator.cs"));
            Assert.That(declaration, Does.Contain("DataAgent progress diagnostics"));
            Assert.That(declaration, Does.Contain("progress_unavailable"));
            Assert.That(declaration, Does.Contain("sql=redacted"));
            Assert.That(declaration, Does.Contain("hidden_context_redacted=true"));
            Assert.That(declaration, Does.Contain("DataAgentProgressStreamingTests"));
            Assert.That(declaration, Does.Contain("DataAgentProgressDiagnosticsPublisherTests"));
        });
    }

    [Test]
    public void ReadinessScriptProtectsV211ScenarioContextContract()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1");
        string script = File.ReadAllText(scriptPath);

        string declaration = FindNewCheckDeclaration(script, "DataAgentScenarioContextIntegrated");
        string packDeclaration = FindNewCheckDeclaration(script, "DataAgentScenarioKnowledgePackPresent");

        Assert.Multiple(() =>
        {
            Assert.That(declaration, Does.Contain("DataAgentScenarioContext.cs"));
            Assert.That(declaration, Does.Contain("DataAgentScenarioContextBuilder.cs"));
            Assert.That(declaration, Does.Contain("DataAgentScenarioDiagnosticsFormatter.cs"));
            Assert.That(declaration, Does.Contain("LlmDataAgentPlannerPromptFormatter.cs"));
            Assert.That(declaration, Does.Contain("Scenario context:"));
            Assert.That(declaration, Does.Contain("Do not output SQL"));
            Assert.That(declaration, Does.Contain("LlmDataAgentQueryPlanner"));
            Assert.That(declaration, Does.Contain("llm_invalid_output_fallback"));
            Assert.That(declaration, Does.Contain("unsupported_operator"));
            Assert.That(declaration, Does.Contain("throwOnInvalidBytes: true"));
            Assert.That(declaration, Does.Contain("\\uFFFD"));
            Assert.That(script, Does.Contain("function Test-ScenarioPackChineseText"));
            Assert.That(script, Does.Contain("function New-StringFromCodePoints"));
            Assert.That(packDeclaration, Does.Contain("Test-ScenarioPackChineseText"));
            Assert.That(packDeclaration, Does.Contain("0x5de5,0x7a0b,0x95e8,0x7981"));
            Assert.That(packDeclaration, Does.Contain("0x6700,0x8fd1,0x5931,0x8d25,0x7684,0x6d4b,0x8bd5"));
            Assert.That(packDeclaration, Does.Contain("0x7f3a,0x5931,0x9879"));
            Assert.That(packDeclaration, Does.Contain("0x6587,0x6863,0x8bc1,0x636e"));
            Assert.That(packDeclaration, Does.Contain("0x5bb8,0x30e7,0x25bc"));
            Assert.That(packDeclaration, Does.Contain("0x93c8,0x20ac"));
            Assert.That(packDeclaration, Does.Contain("0x6fb6,0x8fab,0x89e6"));
            Assert.That(packDeclaration, Does.Contain("0x8e47,0x546d,0x6e36"));
            Assert.That(declaration, Does.Contain("DataAgentScenarioContextBuilderTests"));
            Assert.That(declaration, Does.Contain("DataAgentScenarioDiagnosticsFormatterTests"));
            Assert.That(declaration, Does.Contain("DataAgentV211ReadinessTests"));
        });
    }

    [Test]
    public void ReadinessScriptProtectsV212RuntimeScenarioContextContract()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1");
        string script = File.ReadAllText(scriptPath);

        string declaration = FindNewCheckDeclaration(script, "DataAgentRuntimeScenarioContextActivationPresent");

        Assert.Multiple(() =>
        {
            Assert.That(declaration, Does.Contain("IDataAgentScenarioContextProvider.cs"));
            Assert.That(declaration, Does.Contain("DataAgentScenarioContextProvider.cs"));
            Assert.That(declaration, Does.Contain("DataAgentService.cs"));
            Assert.That(declaration, Does.Contain("scenarioContextProvider.Build"));
            Assert.That(declaration, Does.Contain("request.ScenarioContext"));
            Assert.That(declaration, Does.Contain("DataAgentRuntimeScenarioContextActivationTests"));
            Assert.That(declaration, Does.Contain("service_context=true"));
            Assert.That(declaration, Does.Contain("llm_prompt=true"));
            Assert.That(declaration, Does.Contain("qchat_boundary=true"));
            Assert.That(declaration, Does.Contain("sql_boundary=true"));
        });
    }

    [Test]
    public void ReadinessScriptProtectsV213PostgresCheckpointPersistenceContract()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1");
        string script = File.ReadAllText(scriptPath);

        string declaration = FindNewCheckDeclaration(script, "PostgresCheckpointPersistencePresent");

        Assert.Multiple(() =>
        {
            Assert.That(declaration, Does.Contain("PostgresDataAgentAnalysisSessionStore.cs"));
            Assert.That(declaration, Does.Contain("DataAgentAnalysisSessionStoreFactory.cs"));
            Assert.That(declaration, Does.Contain("DataAgentModuleService.cs"));
            Assert.That(declaration, Does.Contain("CreateAnalysisSessionStore"));
            Assert.That(declaration, Does.Contain("dataagent_analysis_session"));
            Assert.That(declaration, Does.Contain("dataagent_analysis_turn"));
            Assert.That(declaration, Does.Contain("FOR UPDATE"));
            Assert.That(declaration, Does.Contain("ALIFE_DATAAGENT_ANALYSIS_SESSION_STORE_PROVIDER"));
            Assert.That(declaration, Does.Contain("DataAgentPostgresAnalysisSessionStoreTests"));
            Assert.That(declaration, Does.Contain("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION"));
            Assert.That(declaration, Does.Contain("session_store=true"));
            Assert.That(declaration, Does.Contain("factory=true"));
            Assert.That(declaration, Does.Contain("module_wiring=true"));
            Assert.That(declaration, Does.Contain("live_test_gated="));
        });
    }

    [Test]
    public void ReadinessScriptProtectsV214GraphSidecarContract()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1");
        string script = File.ReadAllText(scriptPath);

        string declaration = FindNewCheckDeclaration(script, "GraphSidecarContractPresent");

        Assert.Multiple(() =>
        {
            Assert.That(declaration, Does.Contain("DataAgentGraphSidecarContract.cs"));
            Assert.That(declaration, Does.Contain("DataAgentGraphSidecarOptions"));
            Assert.That(declaration, Does.Contain("ALIFE_DATAAGENT_GRAPH_SIDECAR_ENABLED"));
            Assert.That(declaration, Does.Contain("DataAgentGraphSidecarPolicy"));
            Assert.That(declaration, Does.Contain("IsRuntimeAvailable"));
            Assert.That(declaration, Does.Contain("NoSqlAuthority"));
            Assert.That(declaration, Does.Contain("ExecuteSql"));
            Assert.That(declaration, Does.Contain("DataAgentGraphSidecarContractTests"));
            Assert.That(declaration, Does.Contain("default_enabled=false"));
            Assert.That(declaration, Does.Contain("policy=true"));
            Assert.That(declaration, Does.Contain("no_sql_authority=true"));
            Assert.That(declaration, Does.Contain("no_runtime=true"));
        });
    }

    [Test]
    public void FunctionCallerStoresRecentDataAgentTraceDiagnostics()
    {
        Type functionCallerType = typeof(XmlFunctionCaller);
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);

        Assert.Multiple(() =>
        {
            Assert.That(functionCallerType.GetProperty("RecentDataAgentTraceDiagnostics"), Is.Not.Null);
            Assert.That(functionCallerType.GetMethod("RecordRecentDataAgentTraceDiagnostics"), Is.Not.Null);
            Assert.That(
                File.ReadAllText(Path.Combine(repoRoot, "sources", "Alife.Function", "Alife.Function.DataAgent", "DataAgentModuleService.cs")),
                Does.Contain("functionService.RecordRecentDataAgentTraceDiagnostics"));
        });
    }

    [Test]
    public void ReadinessScriptProtectsV24EvidencePackContract()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1");
        string script = File.ReadAllText(scriptPath);

        string declaration = FindNewCheckDeclaration(script, "DataAgentEvidencePackPresent");

        Assert.Multiple(() =>
        {
            Assert.That(declaration, Does.Contain("DataAgentEvidencePack.cs"));
            Assert.That(declaration, Does.Contain("DataAgentEvidencePackBuilder.cs"));
            Assert.That(declaration, Does.Contain("DataAgentEvidencePackFormatter.cs"));
            Assert.That(declaration, Does.Contain("DataAgentEvidencePackPresent"));
            Assert.That(declaration, Does.Contain("BuilderIgnoresStaleAuditsForRouteDeniedEvidence"));
            Assert.That(declaration, Does.Contain("BuilderMatchesAcceptedQueryAuditToResponseAnswer"));
            Assert.That(declaration, Does.Contain("BuilderMatchesRejectedQueryAuditToResponseAnswerWithoutSqlExecution"));
            Assert.That(declaration, Does.Contain("FormatterPreservesDiagnosticPunctuationOutsideEvidencePackTag"));
            Assert.That(declaration, Does.Contain("accepted_route_context=runtime"));
            Assert.That(declaration, Does.Contain("BuilderBuildsTerminalNoQueryEvidence"));
        });
    }

    [Test]
    public void EngineeringMapDeclaresDataAgentReadinessAsRequired()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-qchat-engineering-map.ps1");
        string script = File.ReadAllText(scriptPath);

        string declaration = FindAddCheckDeclaration(script, "DataAgent readiness script");

        Assert.Multiple(() =>
        {
            Assert.That(declaration, Is.Not.Empty);
            Assert.That(declaration, Does.Not.Contain("-Required $false"));
            Assert.That(declaration, Does.Contain("DataAgent Readiness"));
            Assert.That(declaration, Does.Contain("QueryPlanFixturesPass"));
            Assert.That(declaration, Does.Contain("ContextContributionStable"));
            Assert.That(declaration, Does.Contain("PlannerInterfacePresent"));
            Assert.That(declaration, Does.Contain("ToolHandlerReturnsDataAgentContext"));
            Assert.That(declaration, Does.Contain("CapabilityBoundaryPresent"));
        });
    }

    [Test]
    public void EngineeringMapDeclaresDataAgentStoreBoundaryAsRequired()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-qchat-engineering-map.ps1");
        string script = File.ReadAllText(scriptPath);

        string declaration = FindAddCheckDeclaration(script, "DataAgent store provider boundary");

        Assert.Multiple(() =>
        {
            Assert.That(declaration, Is.Not.Empty);
            Assert.That(declaration, Does.Not.Contain("-Required $false"));
            Assert.That(declaration, Does.Contain("DataAgentStoreBoundaryPresent"));
            Assert.That(declaration, Does.Contain("SqliteStoreCompatibilityPresent"));
            Assert.That(declaration, Does.Contain("PostgresStoreProviderPresent"));
            Assert.That(declaration, Does.Contain("PostgresLiveTestsEnvironmentGated"));
            Assert.That(declaration, Does.Contain("DataAgentServiceUsesStoreBoundary"));
        });
    }

    [Test]
    public void EngineeringMapDeclaresDataAgentProgressDiagnosticsAsRequired()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-qchat-engineering-map.ps1");
        string script = File.ReadAllText(scriptPath);

        string declaration = FindAddCheckDeclaration(script, "DataAgent progress diagnostics");

        Assert.Multiple(() =>
        {
            Assert.That(declaration, Is.Not.Empty);
            Assert.That(declaration, Does.Not.Contain("-Required $false"));
            Assert.That(declaration, Does.Contain("RecentDataAgentProgress"));
            Assert.That(declaration, Does.Contain("diag progress"));
            Assert.That(declaration, Does.Contain("BuildDataAgentProgressDiagnosticsText"));
            Assert.That(declaration, Does.Contain("DataAgent progress diagnostics"));
        });
    }

    [Test]
    public void QChatEngineeringMapDefaultModeExitsZeroAndPrintsSummary()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-qchat-engineering-map.ps1");

        ScriptResult result = RunPowerShellScript(scriptPath);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            Assert.That(GetEngineeringMapSummaryLines(result.StandardOutput), Is.EqualTo(new[]
            {
                "Summary: 58 required passed, 0 required missing, 0 optional present, 0 optional missing"
            }));
        });
    }

    [Test]
    public void QChatEngineeringMapScriptProtectsRequiredCheckCount()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-qchat-engineering-map.ps1");
        string script = File.ReadAllText(scriptPath);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("$expectedRequired = 58"));
            Assert.That(script, Does.Contain("engineering map check count mismatch"));
            Assert.That(script, Does.Contain("$requiredTotal"));
        });
    }

    static string NewDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-readiness-tests");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
    }

    static ScriptResult RunPowerShellScript(string scriptPath)
    {
        string powerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        ProcessStartInfo startInfo = new()
        {
            FileName = powerShell,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start PowerShell.");

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();

        if (process.WaitForExit(15000) == false)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("DataAgent readiness script did not exit within 15 seconds.");
        }

        return new ScriptResult(process.ExitCode, stdout, stderr);
    }

    static string FindAddCheckDeclaration(string script, string checkName)
    {
        string marker = $"-Name \"{checkName}\"";
        int nameIndex = script.IndexOf(marker, StringComparison.Ordinal);
        if (nameIndex < 0)
            return string.Empty;

        int start = script.LastIndexOf("Add-Check", nameIndex, StringComparison.Ordinal);
        if (start < 0)
            return string.Empty;

        int next = script.IndexOf("Add-Check", nameIndex + marker.Length, StringComparison.Ordinal);
        return next < 0
            ? script[start..]
            : script[start..next];
    }

    static string FindNewCheckDeclaration(string script, string checkName)
    {
        string marker = $"-Name \"{checkName}\"";
        int nameIndex = script.LastIndexOf(marker, StringComparison.Ordinal);
        if (nameIndex < 0)
            return string.Empty;

        int start = script.LastIndexOf("New-Check", nameIndex, StringComparison.Ordinal);
        if (start < 0)
            return string.Empty;

        int next = script.IndexOf("New-Check", nameIndex + marker.Length, StringComparison.Ordinal);
        return next < 0
            ? script[start..]
            : script[start..next];
    }

    static string FindRepoRoot(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tools")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test directory.");
    }

    static string[] GetSummaryLines(string output)
    {
        return output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("  Summary:", StringComparison.Ordinal))
            .ToArray();
    }

    static string[] GetEngineeringMapSummaryLines(string output)
    {
        return output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("Summary:", StringComparison.Ordinal))
            .ToArray();
    }

    readonly record struct ScriptResult(int ExitCode, string StandardOutput, string StandardError);
}
