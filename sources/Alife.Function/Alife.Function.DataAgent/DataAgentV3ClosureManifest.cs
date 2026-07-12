using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Alife.Function.DataAgent;

public enum DataAgentV3EvidenceKind
{
    DynamicReadiness,
    StaticReadiness,
    ContractTest,
    RegressionHardening,
    OperatorArtifact,
    FinalFreeze
}

public sealed record DataAgentV3MilestoneEvidence(
    string Version,
    DataAgentV3EvidenceKind EvidenceKind,
    string Purpose,
    string EvidencePath,
    string RequiredGateLabel,
    IReadOnlyList<string> RequiredDynamicCheckNames,
    IReadOnlyList<string> RequiredStaticCheckNames,
    bool ChangesDefaultRuntime,
    bool GrantsSidecarAuthority);

public sealed record DataAgentV3LedgerEntry(
    string Version,
    DataAgentV3EvidenceKind EvidenceKind,
    string Purpose,
    string EvidencePath,
    string RequiredGateLabel,
    bool ChangesDefaultRuntime,
    bool GrantsSidecarAuthority);

public sealed record DataAgentV3LedgerParseResult(
    IReadOnlyList<string> MilestoneVersions,
    IReadOnlyList<DataAgentV3LedgerEntry> Entries,
    IReadOnlyList<string> Errors);

public sealed record DataAgentV3ClosureResult(
    bool Accepted,
    int StaticRequiredCheckCount,
    int FrozenCoreCheckCount,
    IReadOnlyList<string> MissingMilestoneVersions,
    IReadOnlyList<string> DuplicateMilestoneVersions,
    IReadOnlyList<string> UnexpectedMilestoneVersions,
    IReadOnlyList<string> MissingEvidencePaths,
    IReadOnlyList<string> MissingRequiredCheckNames,
    IReadOnlyList<string> FailedRequiredCheckNames,
    IReadOnlyList<string> FailedCoreCheckNames,
    IReadOnlyList<string> DuplicateRequiredCheckNames,
    IReadOnlyList<string> UnexpectedV4CheckNames,
    IReadOnlyList<string> LedgerParseErrors,
    IReadOnlyList<string> LedgerParityMismatches,
    int AuthorityExpansionCount,
    bool OperatorEvidencePackPresent,
    bool StaticCountMatches,
    bool CoreCountMatches);

/// <summary>
/// Frozen V3 readiness identities. This is intentionally constructed by a controlled
/// caller and is never inferred from the inventories being validated.
/// </summary>
public sealed class DataAgentV3FrozenReadinessSnapshot
{
    static readonly FrozenSet<string> RequiredStaticV3Names = new[]
    {
        "GraphHandshakeDevSidecarLiveSmokeHarnessPresent",
        "LangGraphRuntimeReadinessContractPresent"
    }.ToFrozenSet(StringComparer.Ordinal);

    static readonly FrozenSet<string> RequiredCoreV3Names = new[]
    {
        "GraphHandshakeBoundaryPresent",
        "GraphHandshakeDevSidecarAdapterPresent",
        "GraphHandshakeDevSidecarProgressBridgePresent",
        "GraphHandshakeDevSidecarStreamingTransportPresent",
        "GraphHandshakeDevSidecarObservabilityContractPresent",
        "DataAgentEndToEndChainContractPresent",
        "DataAgentReplayRunbookPresent",
        "GraphHandshakeRealLangGraphSidecarSkeletonPresent",
        "GraphHandshakeReplayParityShadowComparisonPresent",
        "GraphHandshakeBoundedDiagnosticsExplanationPresent",
        "GraphHandshakeCrossModulePlannerManifestsPresent",
        "GraphHandshakeAuthorityFallbackRegressionPresent",
        "GraphHandshakeLangGraphLiveSmokeReadinessPresent",
        "GraphHandshakeLangGraphManualSmokeHarnessPresent",
        "GraphHandshakeSmokeResultArtifactFormatterPresent",
        "GraphHandshakeReplayFixturePackPresent",
        "GraphHandshakeShadowReplayReportPresent",
        "GraphHandshakeManualReplayReportArtifactWriterPresent",
        "GraphHandshakeManualArtifactIndexPresent",
        "GraphHandshakeManualAuditBundlePresent",
        "GraphHandshakeAgentAdvisoryContractPresent",
        "GraphHandshakeRealLangGraphManualShadowProviderPresent",
        "GraphHandshakeHarnessReplayDiffGatePresent",
        "GraphHandshakeOperatorEvidencePackPresent"
    }.ToFrozenSet(StringComparer.Ordinal);

    static readonly FrozenSet<string> V4OnlyNames = new[]
    {
        "GraphHandshakeRealLangGraphManualShadowIntegrationPresent",
        "GraphHandshakeRealLangGraphManualShadowContextBudgetPresent",
        "GraphHandshakeV42OperatorEvidencePacketPresent",
        "GraphHandshakeV43CrossModuleValueScorePresent",
        "GraphHandshakeV44ProductionShadowClientPresent",
        "GraphHandshakeV45ProductionClosurePresent",
        "GraphHandshakeV46RuntimeTruthPresent",
        "GraphHandshakeV47LiveCanaryClosurePresent"
    }.ToFrozenSet(StringComparer.Ordinal);

    DataAgentV3FrozenReadinessSnapshot(FrozenSet<string> expectedStaticCheckNames, FrozenSet<string> expectedCoreCheckNames)
    {
        ExpectedStaticCheckNames = expectedStaticCheckNames;
        ExpectedCoreCheckNames = expectedCoreCheckNames;
    }

    public FrozenSet<string> ExpectedStaticCheckNames { get; }
    public FrozenSet<string> ExpectedCoreCheckNames { get; }

    internal static DataAgentV3FrozenReadinessSnapshot CreateCanonical(
        IEnumerable<string> expectedStaticCheckNames,
        IEnumerable<string> expectedCoreCheckNames) =>
        new(
            FreezeAndValidate(expectedStaticCheckNames, DataAgentV3ClosureManifest.ExpectedFrozenStaticRequiredCount, RequiredStaticV3Names, V4OnlyNames, nameof(expectedStaticCheckNames)),
            FreezeAndValidate(expectedCoreCheckNames, DataAgentV3ClosureManifest.ExpectedFrozenCoreCount, RequiredCoreV3Names, V4OnlyNames, nameof(expectedCoreCheckNames)));

    static FrozenSet<string> FreezeAndValidate(
        IEnumerable<string> names,
        int expectedCount,
        FrozenSet<string> requiredNames,
        FrozenSet<string> excludedNames,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(names);
        string[] values = names.ToArray();
        if (values.Length != expectedCount || values.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("The canonical frozen readiness inventory has an invalid size or blank identity.", parameterName);
        }

        FrozenSet<string> frozen = values.ToFrozenSet(StringComparer.Ordinal);
        if (frozen.Count != values.Length || !requiredNames.IsSubsetOf(frozen) || frozen.Overlaps(excludedNames))
        {
            throw new ArgumentException("The canonical frozen readiness inventory is not the required unique V3 identity set.", parameterName);
        }

        return frozen;
    }
}
public static class DataAgentV3ClosureManifest
{
    const string InventoryStart = "[v3_closure_milestones]";
    const string InventoryEnd = "[/v3_closure_milestones]";
    const string EvidenceTableHeader = "| Version | Evidence kind | Purpose | Exact evidence path | Required check / gate | Runtime boundary | Sidecar authority boundary |";
    const string EvidenceTableSeparator = "|---|---|---|---|---|---|---|";
    const string RuntimeBoundary = "changes_default_runtime=false";
    const string AuthorityBoundary = "grants_sidecar_authority=false";
    const string MilestonePattern = @"^milestone=v3\.(0|[1-9]|1[0-9]|2[0-8])$";

    public const int ExpectedFrozenStaticRequiredCount = 111;
    public const int ExpectedFrozenCoreCount = 95;
    public const int MaxParseErrors = 32;
    /// <summary>Maximum ledger document size for the fixed 29-entry V3 closure ledger.</summary>
    public const int MaxLedgerChars = 32768;
    /// <summary>Maximum physical lines accepted for the fixed 29-entry V3 closure ledger.</summary>
    public const int MaxLedgerLines = 512;
    /// <summary>Maximum characters in one ledger line before structured parsing.</summary>
    public const int MaxLedgerRowChars = 4096;
    public const string ParseErrorsTruncatedSentinel = "ledger_parse_errors_truncated";

    public static ImmutableArray<string> ExpectedVersions { get; } =
        ImmutableArray.CreateRange(Enumerable.Range(0, 29).Select(index => $"v3.{index}"));

    public static FrozenSet<string> V4OnlyCheckNames { get; } = new[]
    {
        "GraphHandshakeRealLangGraphManualShadowIntegrationPresent",
        "GraphHandshakeRealLangGraphManualShadowContextBudgetPresent",
        "GraphHandshakeV44ProductionShadowClientPresent",
        "GraphHandshakeV45ProductionClosurePresent",
        "GraphHandshakeV46RuntimeTruthPresent",
        "GraphHandshakeV47LiveCanaryClosurePresent"
    }.ToFrozenSet(StringComparer.Ordinal);

    public static FrozenSet<string> PostV3StaticCheckNames { get; } = new[]
    {
        "GraphHandshakeFinalV3ReadinessFreezePresent",
        "GraphHandshakeRealLangGraphManualShadowIntegrationPresent",
        "GraphHandshakeRealLangGraphManualShadowContextBudgetPresent",
        "GraphHandshakeV42OperatorEvidencePacketPresent",
        "GraphHandshakeV43CrossModuleValueScorePresent",
        "GraphHandshakeV44ProductionShadowClientPresent",
        "GraphHandshakeV45ProductionClosurePresent",
        "GraphHandshakeV46RuntimeTruthPresent",
        "GraphHandshakeV47LiveCanaryClosurePresent"
    }.ToFrozenSet(StringComparer.Ordinal);

    // These exact inventories are derived from the authoritative current V3 readiness
    // sources: DataAgentReadiness.CheckCore (excluding V3.28/V4) and the static
    // readiness ledger (excluding the same three post-V3 identities).
    static readonly string[] CanonicalStaticCheckNames =
    [
        "DataAgentModulePresent",
        "SqliteSchemaInitializes",
        "FixtureDataImports",
        "SchemaSnapshotAvailable",
        "CatalogMatchesSqliteSchema",
        "DangerousSqlRejected",
        "QueryPlanFixturesPass",
        "ReadOnlyQueryExecutes",
        "ContextContributionStable",
        "PlannerInterfacePresent",
        "DeterministicPlannerPassesFixtures",
        "PlannerExplanationInContext",
        "ServiceUsesInjectedPlanner",
        "LlmPlannerInterfacePresent",
        "LlmPlannerPromptUsesSchemaSnapshot",
        "LlmPlannerStrictJsonParser",
        "LlmPlannerRejectsInvalidOutput",
        "LlmPlannerFallbackPreservesSafety",
        "ClarificationRequestSupported",
        "NaturalLanguageResultExplanationPresent",
        "UnsafePlannerOutputRejected",
        "ToolHandlerReturnsDataAgentContext",
        "ToolCapabilityManifestPresent",
        "ToolCapabilityRouterPresent",
        "ToolExecutionGatePresent",
        "ToolBrokerDynamicExposurePresent",
        "ToolRouteRuntimeWiringPresent",
        "QChatToolRouteStateScopePresent",
        "ToolBrokerRuntimeTestsPresent",
        "ToolBrokerRouteDecisionReasonCodesPresent",
        "ToolBrokerExecutionAuditPresent",
        "ToolBrokerAuditLogPresent",
        "CapabilityBoundaryPresent",
        "CapabilityProvidersPresent",
        "SharedToolManifestPresent",
        "DataAgentStoreBoundaryPresent",
        "SqliteStoreCompatibilityPresent",
        "PostgresStoreProviderPresent",
        "PostgresLiveTestsEnvironmentGated",
        "PostgresCheckpointPersistencePresent",
        "GraphSidecarContractPresent",
        "DataQueryGraphPilotPresent",
        "GraphHandshakeBoundaryPresent",
        "GraphHandshakeDevSidecarAdapterPresent",
        "GraphHandshakeDevSidecarProgressBridgePresent",
        "GraphHandshakeDevSidecarStreamingTransportPresent",
        "GraphHandshakeDevSidecarLiveSmokeHarnessPresent",
        "GraphHandshakeDevSidecarObservabilityContractPresent",
        "GraphHandshakeRealLangGraphSidecarSkeletonPresent",
        "GraphHandshakeReplayParityShadowComparisonPresent",
        "GraphHandshakeBoundedDiagnosticsExplanationPresent",
        "GraphHandshakeCrossModulePlannerManifestsPresent",
        "GraphHandshakeAuthorityFallbackRegressionPresent",
        "GraphHandshakeLangGraphLiveSmokeReadinessPresent",
        "GraphHandshakeLangGraphManualSmokeHarnessPresent",
        "GraphHandshakeSmokeResultArtifactFormatterPresent",
        "GraphHandshakeReplayFixturePackPresent",
        "GraphHandshakeShadowReplayReportPresent",
        "GraphHandshakeManualReplayReportArtifactWriterPresent",
        "GraphHandshakeManualArtifactIndexPresent",
        "GraphHandshakeManualAuditBundlePresent",
        "GraphHandshakeAgentAdvisoryContractPresent",
        "GraphHandshakeRealLangGraphManualShadowProviderPresent",
        "GraphHandshakeHarnessReplayDiffGatePresent",
        "GraphHandshakeOperatorEvidencePackPresent",
        "DataAgentEndToEndChainContractPresent",
        "DataAgentReplayRunbookPresent",
        "LangGraphRuntimeReadinessContractPresent",
        "DataQueryGraphOwnerDiagnosticsPresent",
        "DataAgentServiceUsesStoreBoundary",
        "DataAgentScenarioKnowledgePackPresent",
        "DataAgentScenarioContextIntegrated",
        "DataAgentRuntimeScenarioContextActivationPresent",
        "DataAgentNodeToolScopePolicyPresent",
        "DataAgentSafetyCapabilitiesRemainDeterministic",
        "AnalysisSessionServicePresent",
        "AnalysisSessionStorePresent",
        "AnalysisSessionStateMachineTransitions",
        "AnalysisFollowUpInterpreterPresent",
        "AnalysisSessionContextProviderPresent",
        "AnalysisSummaryWindowPresent",
        "AnalysisSessionHasNoSqliteBinding",
        "DataAgentOrchestratorPresent",
        "OrchestratorNodeBoundaryPresent",
        "OrchestratorCheckpointPresent",
        "OrchestratorRouteGateFailClosed",
        "OrchestratorTerminalNodesDoNotQuery",
        "OrchestratorStateMachineTransitions",
        "AnalysisToolHandlerUsesOrchestrator",
        "OrchestratorTraceContextPresent",
        "OrchestratorCheckpointContextPresent",
        "OrchestratorRuntimeStartPathCovered",
        "OrchestratorRuntimeContinuePathCovered",
        "OrchestratorRuntimeTerminalPathCovered",
        "OrchestratorRuntimeRouteDeniedFailClosed",
        "AnalysisHandlerConsumesToolRouteContext",
        "OrchestrationRequestUsesRuntimeRouteDecision",
        "RouteMissingRequestFailsClosed",
        "RouteEvidenceContextPresent",
        "RouteSessionScopePreserved",
        "TerminalRouteDoesNotQuery",
        "DataAgentEvidencePackPresent",
        "SemanticStateEstimatorCorePresent",
        "DataAgentAnalysisStateEstimatorPresent",
        "DataAgentEvidenceDiagnosticsPresent",
        "DataAgentEvidenceRecentDiagnosticsBridgePresent",
        "DataAgentTraceTimelinePresent",
        "DataAgentProgressStreamingPresent",
        "AnalysisToolHandlerPresent",
        "AnalysisToolsRegisteredInModule",
        "AnalysisTerminalToolsDoNotQuery",
    ];

    static readonly string[] CanonicalCoreCheckNames =
    [
        "DataAgentModulePresent",
        "SqliteSchemaInitializes",
        "FixtureDataImports",
        "SchemaSnapshotAvailable",
        "CatalogMatchesSqliteSchema",
        "QueryPlanFixturesPass",
        "DangerousSqlRejected",
        "ReadOnlyQueryExecutes",
        "DataAgentStoreBoundaryPresent",
        "SqliteStoreCompatibilityPresent",
        "PostgresStoreProviderPresent",
        "PostgresLiveTestsEnvironmentGated",
        "PostgresCheckpointPersistencePresent",
        "GraphSidecarContractPresent",
        "DataQueryGraphPilotPresent",
        "GraphHandshakeBoundaryPresent",
        "GraphHandshakeDevSidecarAdapterPresent",
        "GraphHandshakeDevSidecarProgressBridgePresent",
        "GraphHandshakeDevSidecarStreamingTransportPresent",
        "GraphHandshakeDevSidecarObservabilityContractPresent",
        "GraphHandshakeRealLangGraphSidecarSkeletonPresent",
        "GraphHandshakeReplayParityShadowComparisonPresent",
        "GraphHandshakeBoundedDiagnosticsExplanationPresent",
        "GraphHandshakeCrossModulePlannerManifestsPresent",
        "GraphHandshakeAuthorityFallbackRegressionPresent",
        "GraphHandshakeLangGraphLiveSmokeReadinessPresent",
        "GraphHandshakeLangGraphManualSmokeHarnessPresent",
        "GraphHandshakeSmokeResultArtifactFormatterPresent",
        "GraphHandshakeReplayFixturePackPresent",
        "GraphHandshakeShadowReplayReportPresent",
        "GraphHandshakeManualReplayReportArtifactWriterPresent",
        "GraphHandshakeManualArtifactIndexPresent",
        "GraphHandshakeManualAuditBundlePresent",
        "GraphHandshakeAgentAdvisoryContractPresent",
        "GraphHandshakeRealLangGraphManualShadowProviderPresent",
        "GraphHandshakeHarnessReplayDiffGatePresent",
        "GraphHandshakeOperatorEvidencePackPresent",
        "DataAgentEndToEndChainContractPresent",
        "DataAgentReplayRunbookPresent",
        "DataQueryGraphOwnerDiagnosticsPresent",
        "DataAgentServiceUsesStoreBoundary",
        "ToolBrokerAuditLogPresent",
        "CapabilityBoundaryPresent",
        "ContextContributionStable",
        "PlannerExplanationInContext",
        "NaturalLanguageResultExplanationPresent",
        "LlmPlannerInterfacePresent",
        "LlmPlannerPromptUsesSchemaSnapshot",
        "LlmPlannerStrictJsonParser",
        "LlmPlannerRejectsInvalidOutput",
        "LlmPlannerFallbackPreservesSafety",
        "ClarificationRequestSupported",
        "PlannerInterfacePresent",
        "DeterministicPlannerPassesFixtures",
        "ServiceUsesInjectedPlanner",
        "UnsafePlannerOutputRejected",
        "ToolHandlerReturnsDataAgentContext",
        "AnalysisSessionServicePresent",
        "AnalysisSessionStorePresent",
        "AnalysisSessionStateMachineTransitions",
        "AnalysisFollowUpInterpreterPresent",
        "AnalysisSessionContextProviderPresent",
        "AnalysisSummaryWindowPresent",
        "AnalysisSessionHasNoSqliteBinding",
        "DataAgentOrchestratorPresent",
        "OrchestratorNodeBoundaryPresent",
        "OrchestratorCheckpointPresent",
        "OrchestratorRouteGateFailClosed",
        "OrchestratorTerminalNodesDoNotQuery",
        "OrchestratorStateMachineTransitions",
        "AnalysisToolHandlerUsesOrchestrator",
        "OrchestratorTraceContextPresent",
        "OrchestratorCheckpointContextPresent",
        "OrchestratorRuntimeStartPathCovered",
        "OrchestratorRuntimeContinuePathCovered",
        "OrchestratorRuntimeTerminalPathCovered",
        "OrchestratorRuntimeRouteDeniedFailClosed",
        "AnalysisHandlerConsumesToolRouteContext",
        "OrchestrationRequestUsesRuntimeRouteDecision",
        "RouteMissingRequestFailsClosed",
        "RouteEvidenceContextPresent",
        "RouteSessionScopePreserved",
        "TerminalRouteDoesNotQuery",
        "DataAgentEvidencePackPresent",
        "SemanticStateEstimatorCorePresent",
        "DataAgentAnalysisStateEstimatorPresent",
        "DataAgentEvidenceDiagnosticsPresent",
        "DataAgentEvidenceRecentDiagnosticsBridgePresent",
        "DataAgentTraceTimelinePresent",
        "DataAgentProgressStreamingPresent",
        "DataAgentScenarioKnowledgePackPresent",
        "DataAgentScenarioContextIntegrated",
        "DataAgentRuntimeScenarioContextActivationPresent",
        "DataAgentNodeToolScopePolicyPresent",
        "DataAgentSafetyCapabilitiesRemainDeterministic",
    ];

    public static DataAgentV3FrozenReadinessSnapshot CanonicalReadinessSnapshot { get; } =
        DataAgentV3FrozenReadinessSnapshot.CreateCanonical(CanonicalStaticCheckNames, CanonicalCoreCheckNames);

    public static IReadOnlyList<DataAgentV3MilestoneEvidence> CreateDefault() =>
    [
        Dynamic("v3.0", "Graph handshake boundary", "docs/dataagent/dataagent-v3.0-graph-handshake-boundary.md", "GraphHandshakeBoundaryPresent"),
        Dynamic("v3.1", "Dev sidecar adapter", "docs/dataagent/dataagent-v3.1-dev-sidecar-adapter.md", "GraphHandshakeDevSidecarAdapterPresent"),
        Dynamic("v3.2", "Progress bridge", "docs/dataagent/dataagent-v3.2-sidecar-progress-bridge.md", "GraphHandshakeDevSidecarProgressBridgePresent"),
        Dynamic("v3.3", "NDJSON streaming", "docs/dataagent/dataagent-v3.3-ndjson-streaming-transport.md", "GraphHandshakeDevSidecarStreamingTransportPresent"),
        Static("v3.4", "Manual live smoke", "docs/dataagent/dataagent-v3.4-dev-sidecar-live-smoke-harness.md", "GraphHandshakeDevSidecarLiveSmokeHarnessPresent"),
        GateOnly("v3.5", DataAgentV3EvidenceKind.RegressionHardening, "Smoke contract regression", "Tests/Alife.Test.DataAgent/DataAgentGraphSidecarSmokeScriptContractTests.cs", "inherited V3.4/V3.6 gates"),
        Dynamic("v3.6", "Sidecar observability", "sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeModels.cs", "GraphHandshakeDevSidecarObservabilityContractPresent"),
        GateOnly("v3.7", DataAgentV3EvidenceKind.RegressionHardening, "Reason-code hardening", "docs/superpowers/specs/2026-07-08-dataagent-v3.7-reason-code-stability-design.md", "inherited V3.6 gate"),
        Dynamic("v3.8", "End-to-end chain", "Tests/Alife.Test.DataAgent/DataAgentEndToEndChainContractTests.cs", "DataAgentEndToEndChainContractPresent"),
        Dynamic("v3.9", "Replay runbook", "Tests/Alife.Test.DataAgent/DataAgentReplayRunbookTests.cs", "DataAgentReplayRunbookPresent"),
        Static("v3.10", "Runtime admission contract", "docs/dataagent/dataagent-v3.10-langgraph-runtime-readiness-contract.md", "LangGraphRuntimeReadinessContractPresent"),
        Dynamic("v3.11", "Real LangGraph skeleton", "docs/dataagent/dataagent-v3.11-real-langgraph-sidecar-skeleton.md", "GraphHandshakeRealLangGraphSidecarSkeletonPresent"),
        Dynamic("v3.12", "Replay parity", "docs/dataagent/dataagent-v3.12-replay-parity-shadow-comparison.md", "GraphHandshakeReplayParityShadowComparisonPresent"),
        Dynamic("v3.13", "Bounded diagnostics", "docs/dataagent/dataagent-v3.13-bounded-diagnostics-explanation.md", "GraphHandshakeBoundedDiagnosticsExplanationPresent"),
        Dynamic("v3.14", "Cross-module manifests", "docs/dataagent/dataagent-v3.14-cross-module-planner-manifests.md", "GraphHandshakeCrossModulePlannerManifestsPresent"),
        Dynamic("v3.15", "Authority fallback regression", "docs/dataagent/dataagent-v3.15-authority-fallback-regression.md", "GraphHandshakeAuthorityFallbackRegressionPresent"),
        Dynamic("v3.16", "Live smoke readiness", "docs/dataagent/dataagent-v3.16-langgraph-live-smoke-readiness.md", "GraphHandshakeLangGraphLiveSmokeReadinessPresent"),
        Dynamic("v3.17", "Manual smoke harness", "docs/dataagent/dataagent-v3.17-langgraph-manual-smoke.md", "GraphHandshakeLangGraphManualSmokeHarnessPresent"),
        Dynamic("v3.18", DataAgentV3EvidenceKind.OperatorArtifact, "Smoke artifact", "docs/dataagent/dataagent-v3.18-smoke-result-artifact.md", "GraphHandshakeSmokeResultArtifactFormatterPresent"),
        Dynamic("v3.19", DataAgentV3EvidenceKind.OperatorArtifact, "Replay fixtures", "docs/dataagent/dataagent-v3.19-replay-fixture-pack.md", "GraphHandshakeReplayFixturePackPresent"),
        Dynamic("v3.20", DataAgentV3EvidenceKind.OperatorArtifact, "Shadow replay report", "docs/dataagent/dataagent-v3.20-shadow-replay-report.md", "GraphHandshakeShadowReplayReportPresent"),
        Dynamic("v3.21", DataAgentV3EvidenceKind.OperatorArtifact, "Replay report artifact", "docs/dataagent/dataagent-v3.21-manual-replay-report-artifact.md", "GraphHandshakeManualReplayReportArtifactWriterPresent"),
        Dynamic("v3.22", DataAgentV3EvidenceKind.OperatorArtifact, "Artifact index", "docs/dataagent/dataagent-v3.22-manual-artifact-index.md", "GraphHandshakeManualArtifactIndexPresent"),
        Dynamic("v3.23", DataAgentV3EvidenceKind.OperatorArtifact, "Audit bundle", "docs/dataagent/dataagent-v3.23-manual-audit-bundle.md", "GraphHandshakeManualAuditBundlePresent"),
        Dynamic("v3.24", "Agent advisory contract", "docs/dataagent/dataagent-v3.24-agent-advisory-contract.md", "GraphHandshakeAgentAdvisoryContractPresent"),
        Dynamic("v3.25", "Manual shadow provider", "docs/dataagent/dataagent-v3.25-real-langgraph-manual-shadow-provider.md", "GraphHandshakeRealLangGraphManualShadowProviderPresent"),
        Dynamic("v3.26", "Replay diff gate", "docs/dataagent/dataagent-v3.26-harness-replay-diff-gate.md", "GraphHandshakeHarnessReplayDiffGatePresent"),
        Dynamic("v3.27", DataAgentV3EvidenceKind.OperatorArtifact, "Operator evidence pack", "docs/dataagent/dataagent-v3.27-operator-evidence-pack.md", "GraphHandshakeOperatorEvidencePackPresent"),
        GateOnly("v3.28", DataAgentV3EvidenceKind.FinalFreeze, "Final freeze", "docs/dataagent/dataagent-v3.28-final-readiness-freeze.md", "final freeze output")
    ];

    public static IReadOnlyList<string> ParseStaticCheckNames(string readinessScript)
    {
        ArgumentNullException.ThrowIfNull(readinessScript);

        return Regex.Matches(
                readinessScript,
                "(?m)^\\s*New-Check\\s+-Group\\s+\"[^\"]+\"\\s+-Name\\s+\"(?<name>[^\"]+)\"")
            .Select(match => match.Groups["name"].Value)
            .ToArray();
    }

    public static IReadOnlyList<string> ProjectValidatedV3StaticCheckNames(IEnumerable<string> staticCheckNames)
    {
        ArgumentNullException.ThrowIfNull(staticCheckNames);

        string[] names = staticCheckNames.ToArray();
        FrozenSet<string> actual = names.ToFrozenSet(StringComparer.Ordinal);
        FrozenSet<string> expected = CanonicalReadinessSnapshot.ExpectedStaticCheckNames
            .Concat(PostV3StaticCheckNames)
            .ToFrozenSet(StringComparer.Ordinal);

        if (names.Length != expected.Count || actual.Count != names.Length || !actual.SetEquals(expected))
            return [];

        return names.Where(CanonicalReadinessSnapshot.ExpectedStaticCheckNames.Contains).ToArray();
    }

    public static DataAgentV3LedgerParseResult ParseLedger(string ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ParseErrorAccumulator errors = new();
        if (ledger.Length > MaxLedgerChars)
        {
            errors.Add("The ledger document exceeds the allowed size.");
            return new([], [], errors.ToArray());
        }

        string[] lines = ledger.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        if (lines.Length > MaxLedgerLines)
        {
            errors.Add("The ledger document has too many lines.");
            return new([], [], errors.ToArray());
        }
        if (lines.Any(line => line.Length > MaxLedgerRowChars))
        {
            errors.Add("The ledger document contains an oversized row.");
            return new([], [], errors.ToArray());
        }
        List<string> milestones = [];
        List<DataAgentV3LedgerEntry> entries = [];

        int[] starts = FindLines(lines, InventoryStart);
        int[] ends = FindLines(lines, InventoryEnd);
        if (starts.Length != 1) errors.Add("The milestone inventory must have exactly one start delimiter.");
        if (ends.Length != 1) errors.Add("The milestone inventory must have exactly one end delimiter.");

        if (starts.Length == 1 && ends.Length == 1)
        {
            int start = starts[0];
            int end = ends[0];
            if (end <= start)
            {
                errors.Add("The milestone inventory delimiters are out of order.");
            }
            else
            {
                for (int index = start + 1; index < end; index++)
                {
                    if (errors.IsTruncated) break;
                    if (!Regex.IsMatch(lines[index], MilestonePattern, RegexOptions.CultureInvariant))
                    {
                        errors.Add($"Milestone inventory line {index + 1} is malformed.");
                        continue;
                    }
                    milestones.Add(lines[index]["milestone=".Length..]);
                }

                for (int index = 0; index < lines.Length; index++)
                {
                    if (errors.IsTruncated) break;
                    if ((index < start || index > end) && lines[index].TrimStart().StartsWith("milestone=", StringComparison.Ordinal))
                    {
                        errors.Add($"Milestone marker at line {index + 1} is outside the inventory.");
                    }
                }
            }
        }

        ValidateVersionInventory(milestones, "milestone inventory", errors);
        if (errors.IsTruncated) return new(milestones.ToArray(), entries.ToArray(), errors.ToArray());

        int[] headers = FindLines(lines, EvidenceTableHeader);
        if (headers.Length != 1)
        {
            errors.Add("The closure evidence table must have exactly one exact header.");
        }
        else
        {
            int header = headers[0];
            if (header + 1 >= lines.Length || lines[header + 1] != EvidenceTableSeparator)
            {
                errors.Add("The closure evidence table separator is missing or malformed.");
            }
            else
            {
                for (int index = header + 2; index < lines.Length; index++)
                {
                    if (errors.IsTruncated) break;
                    string line = lines[index].Trim();
                    if (line.Length == 0) break;
                    DataAgentV3LedgerEntry? entry = ParseEvidenceRow(line, index + 1, errors);
                    if (entry is not null) entries.Add(entry);
                }
            }
        }

        ValidateVersionInventory(entries.Select(entry => entry.Version).ToArray(), "evidence table", errors);
        return new(milestones.ToArray(), entries.ToArray(), errors.ToArray());
    }

    static DataAgentV3MilestoneEvidence Dynamic(string version, string purpose, string path, string check) =>
        Dynamic(version, DataAgentV3EvidenceKind.DynamicReadiness, purpose, path, check);

    static DataAgentV3MilestoneEvidence Dynamic(string version, DataAgentV3EvidenceKind kind, string purpose, string path, string check) =>
        new(version, kind, purpose, path, check, [check], [], false, false);

    static DataAgentV3MilestoneEvidence Static(string version, string purpose, string path, string check) =>
        new(version, DataAgentV3EvidenceKind.StaticReadiness, purpose, path, check, [], [check], false, false);

    static DataAgentV3MilestoneEvidence GateOnly(string version, DataAgentV3EvidenceKind kind, string purpose, string path, string gate) =>
        new(version, kind, purpose, path, gate, [], [], false, false);

    static int[] FindLines(string[] lines, string value) => lines
        .Select((line, index) => (line, index))
        .Where(item => item.line == value)
        .Select(item => item.index)
        .ToArray();

    static DataAgentV3LedgerEntry? ParseEvidenceRow(string line, int lineNumber, ParseErrorAccumulator errors)
    {
        string[] columns = line.Split('|');
        if (columns.Length != 9 || columns[0].Length != 0 || columns[^1].Length != 0)
        {
            errors.Add($"Evidence row {lineNumber} must contain exactly seven logical fields.");
            return null;
        }

        string[] values = new string[7];
        for (int index = 0; index < values.Length; index++)
        {
            string? value = UnwrapCodeSpan(columns[index + 1].Trim());
            if (value is null)
            {
                errors.Add($"Evidence row {lineNumber}, field {index + 1} has a malformed code span.");
                return null;
            }
            values[index] = value;
        }

        if (!Enum.TryParse(values[1], false, out DataAgentV3EvidenceKind kind) ||
            !Enum.IsDefined(kind) || int.TryParse(values[1], out _))
        {
            errors.Add($"Evidence row {lineNumber} has an invalid evidence kind.");
            return null;
        }
        if (values[5] != RuntimeBoundary)
        {
            errors.Add($"Evidence row {lineNumber} changes the runtime boundary.");
            return null;
        }
        if (values[6] != AuthorityBoundary)
        {
            errors.Add($"Evidence row {lineNumber} changes the authority boundary.");
            return null;
        }

        return new(values[0], kind, values[2], values[3], values[4], false, false);
    }

    static string? UnwrapCodeSpan(string value)
    {
        int backticks = value.Count(character => character == '`');
        if (backticks == 0) return value;
        if (backticks != 2 || value.Length < 3 || value[0] != '`' || value[^1] != '`') return null;
        return value[1..^1];
    }

    static void ValidateVersionInventory(IReadOnlyList<string> versions, string source, ParseErrorAccumulator errors)
    {
        string[] missing = ExpectedVersions.Except(versions, StringComparer.Ordinal).ToArray();
        string[] unexpected = versions.Except(ExpectedVersions, StringComparer.Ordinal).Distinct(StringComparer.Ordinal).ToArray();
        string[] duplicates = versions.GroupBy(version => version, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        if (missing.Length != 0) errors.Add($"The {source} is missing expected milestone versions.");
        if (unexpected.Length != 0) errors.Add($"The {source} contains unexpected milestone versions.");
        if (duplicates.Length != 0) errors.Add($"The {source} contains duplicate milestone versions.");
        if (!versions.SequenceEqual(ExpectedVersions, StringComparer.Ordinal)) errors.Add($"The {source} is not in exact V3.0-V3.28 order.");
    }

    sealed class ParseErrorAccumulator
    {
        readonly List<string> errors = [];

        public bool IsTruncated => errors.Count == MaxParseErrors && errors[^1] == ParseErrorsTruncatedSentinel;

        public void Add(string error)
        {
            if (errors.Count < MaxParseErrors)
            {
                errors.Add(error);
                return;
            }

            errors[^1] = ParseErrorsTruncatedSentinel;
        }

        public string[] ToArray() => errors.ToArray();
    }
}

public static class DataAgentV3ClosureValidator
{
    const string OperatorCheckName = "GraphHandshakeOperatorEvidencePackPresent";

    public static DataAgentV3ClosureResult Validate(
        DataAgentV3FrozenReadinessSnapshot snapshot,
        IEnumerable<DataAgentV3MilestoneEvidence> manifest,
        IEnumerable<DataAgentReadinessCheck> dynamicChecks,
        DataAgentV3LedgerParseResult ledger,
        IEnumerable<string> staticCheckNames,
        IReadOnlySet<string> existingEvidencePaths)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(dynamicChecks);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(staticCheckNames);
        ArgumentNullException.ThrowIfNull(existingEvidencePaths);

        DataAgentV3MilestoneEvidence[] manifestEntries = manifest.ToArray();
        DataAgentReadinessCheck[] dynamicEntries = dynamicChecks.ToArray();
        string[] staticNames = staticCheckNames.ToArray();
        string[] expected = DataAgentV3ClosureManifest.ExpectedVersions.ToArray();
        string[][] milestoneSources =
        [
            manifestEntries.Select(entry => entry.Version).ToArray(),
            ledger.MilestoneVersions.ToArray(),
            ledger.Entries.Select(entry => entry.Version).ToArray()
        ];

        string[] missingMilestones = milestoneSources
            .SelectMany(source => expected.Except(source, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal).ToArray();
        string[] duplicateMilestones = milestoneSources
            .SelectMany(source => source.GroupBy(version => version, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key))
            .Distinct(StringComparer.Ordinal).ToArray();
        string[] unexpectedMilestones = milestoneSources
            .SelectMany(source => source.Except(expected, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal).ToArray();

        string[] missingPaths = manifestEntries.Select(entry => entry.EvidencePath)
            .Where(path => !existingEvidencePaths.Contains(path)).Distinct(StringComparer.Ordinal).ToArray();
        string[] requiredDynamic = manifestEntries.SelectMany(entry => entry.RequiredDynamicCheckNames).Distinct(StringComparer.Ordinal).ToArray();
        string[] requiredStatic = manifestEntries.SelectMany(entry => entry.RequiredStaticCheckNames).Distinct(StringComparer.Ordinal).ToArray();
        string[] dynamicNames = dynamicEntries.Select(check => check.Name).ToArray();

        string[] missingRequired = requiredDynamic.Except(dynamicNames, StringComparer.Ordinal)
            .Concat(requiredStatic.Except(staticNames, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).ToArray();
        string[] failedRequired = requiredDynamic.Where(name =>
                dynamicEntries.Where(check => check.Name == name).Any(check => !check.Passed))
            .Distinct(StringComparer.Ordinal).ToArray();
        string[] failedCore = dynamicEntries.Where(check => !check.Passed)
            .Select(check => check.Name).Distinct(StringComparer.Ordinal).ToArray();
        string[] duplicateRequired = DuplicateNames(dynamicNames)
            .Concat(DuplicateNames(staticNames)).Distinct(StringComparer.Ordinal).ToArray();
        string[] unexpectedV4 = dynamicNames.Concat(staticNames)
            .Where(DataAgentV3ClosureManifest.V4OnlyCheckNames.Contains).Distinct(StringComparer.Ordinal).ToArray();

        List<string> parityMismatches = [];
        foreach (string version in expected)
        {
            DataAgentV3MilestoneEvidence[] manifestMatches = manifestEntries.Where(entry => entry.Version == version).ToArray();
            DataAgentV3LedgerEntry[] ledgerMatches = ledger.Entries.Where(entry => entry.Version == version).ToArray();
            if (manifestMatches.Length != 1 || ledgerMatches.Length != 1)
            {
                parityMismatches.Add($"{version}:{nameof(DataAgentV3LedgerEntry.Version)}");
                continue;
            }
            AddParityMismatches(manifestMatches[0], ledgerMatches[0], parityMismatches);
        }

        int authorityExpansionCount = manifestEntries.Count(entry => entry.ChangesDefaultRuntime || entry.GrantsSidecarAuthority);
        DataAgentReadinessCheck[] operatorChecks = dynamicEntries.Where(check => check.Name == OperatorCheckName).ToArray();
        bool operatorPackPresent = operatorChecks.Length == 1 && operatorChecks[0].Passed &&
            operatorChecks[0].Detail.Contains("operator_evidence_pack=true", StringComparison.Ordinal) &&
            operatorChecks[0].Detail.Contains("operator_decides=true", StringComparison.Ordinal);
        bool staticCountMatches = MatchesFrozenInventory(staticNames, snapshot.ExpectedStaticCheckNames);
        bool coreCountMatches = MatchesFrozenInventory(dynamicNames, snapshot.ExpectedCoreCheckNames);

        bool accepted = missingMilestones.Length == 0 && duplicateMilestones.Length == 0 && unexpectedMilestones.Length == 0 &&
            missingPaths.Length == 0 && missingRequired.Length == 0 && failedRequired.Length == 0 && failedCore.Length == 0 && duplicateRequired.Length == 0 &&
            unexpectedV4.Length == 0 && ledger.Errors.Count == 0 && parityMismatches.Count == 0 && authorityExpansionCount == 0 &&
            operatorPackPresent && staticCountMatches && coreCountMatches;

        return new(
            accepted,
            staticNames.Length,
            dynamicEntries.Length,
            missingMilestones,
            duplicateMilestones,
            unexpectedMilestones,
            missingPaths,
            missingRequired,
            failedRequired,
            failedCore,
            duplicateRequired,
            unexpectedV4,
            ledger.Errors.ToArray(),
            parityMismatches.ToArray(),
            authorityExpansionCount,
            operatorPackPresent,
            staticCountMatches,
            coreCountMatches);
    }

    static IEnumerable<string> DuplicateNames(IEnumerable<string> actualNames) =>
        actualNames.GroupBy(name => name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);

    static bool MatchesFrozenInventory(IEnumerable<string> actualNames, FrozenSet<string> expectedNames)
    {
        string[] actual = actualNames.ToArray();
        return actual.Length == expectedNames.Count &&
            actual.All(name => !string.IsNullOrWhiteSpace(name) && expectedNames.Contains(name)) &&
            actual.Distinct(StringComparer.Ordinal).Count() == actual.Length;
    }

    static void AddParityMismatches(DataAgentV3MilestoneEvidence manifest, DataAgentV3LedgerEntry ledger, List<string> mismatches)
    {
        AddMismatch(manifest.Version == ledger.Version, ledger.Version, nameof(DataAgentV3LedgerEntry.Version), mismatches);
        AddMismatch(manifest.EvidenceKind == ledger.EvidenceKind, ledger.Version, nameof(DataAgentV3LedgerEntry.EvidenceKind), mismatches);
        AddMismatch(manifest.Purpose == ledger.Purpose, ledger.Version, nameof(DataAgentV3LedgerEntry.Purpose), mismatches);
        AddMismatch(manifest.EvidencePath == ledger.EvidencePath, ledger.Version, nameof(DataAgentV3LedgerEntry.EvidencePath), mismatches);
        AddMismatch(manifest.RequiredGateLabel == ledger.RequiredGateLabel, ledger.Version, nameof(DataAgentV3LedgerEntry.RequiredGateLabel), mismatches);
        AddMismatch(manifest.ChangesDefaultRuntime == ledger.ChangesDefaultRuntime, ledger.Version, nameof(DataAgentV3LedgerEntry.ChangesDefaultRuntime), mismatches);
        AddMismatch(manifest.GrantsSidecarAuthority == ledger.GrantsSidecarAuthority, ledger.Version, nameof(DataAgentV3LedgerEntry.GrantsSidecarAuthority), mismatches);
    }

    static void AddMismatch(bool matches, string version, string field, List<string> mismatches)
    {
        if (!matches) mismatches.Add($"{version}:{field}");
    }
}
