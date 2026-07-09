using Alife.Function.DataAgent;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;
using Alife.Function.QChat;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alife.Tools.DataAgentReplay;

public static class DataAgentReplayRunner
{
    static readonly DateTimeOffset ReplayNow = new(2026, 7, 9, 9, 0, 0, TimeSpan.Zero);

    public static DataAgentReplayResult Run(DataAgentReplayFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        fixture = fixture.Normalize();

        ToolCapabilityRouter router = ToolCapabilityRouter.CreateDefault();
        ToolRouteState routeState = new(
            fixture.RouteState.ActiveDataAgentSessionId,
            fixture.RouteState.ActiveDataAgentSessionStatus,
            fixture.RouteState.IsOwner,
            fixture.RouteState.IsPrivate,
            fixture.RouteState.TrustedRuntime);
        ToolRouteDecision route = router.Route(fixture.Utterance, routeState);

        XmlFunctionExecutionPolicy policy = new();
        policy.SetGovernedToolNames(router.ToolNames);
        policy.CurrentRoute = route;
        XmlFunctionExecutionDecision xmlDecision = policy.TryConsume(
            Function("dataagent_analysis_start"),
            ContextWithSession(null));

        RecordingDataAgentStore store = new();
        FixedPlanner planner = new(ToPlan(fixture.Planner));
        DataAgentProgressRecorder progressRecorder = new();
        List<string> progressDiagnostics = [];
        DataAgentProgressDiagnosticsPublisher progressSink = new(
            progressRecorder,
            progressDiagnostics.Add,
            () => ReplayNow);
        InMemoryDataAgentAnalysisSessionStore sessionStore = new();
        DataAgentService dataAgentService = new(store, planner);
        DataAgentAnalysisService analysisService = new(dataAgentService, sessionStore, progressSink, () => ReplayNow);
        DataAgentAnalysisOrchestrator orchestrator = new(
            analysisService,
            sessionStore,
            progressSink: progressSink,
            progressClock: () => ReplayNow);

        List<string> publishedContexts = [];
        List<string> evidenceDiagnostics = [];
        List<string> traceDiagnostics = [];
        List<string> graphDiagnostics = [];
        DataAgentAnalysisToolHandler handler = new(
            orchestrator,
            publishedContexts.Add,
            new XmlPolicyDataAgentToolRouteContextAccessor(policy),
            evidenceDiagnostics.Add,
            traceDiagnostics.Add,
            new DataAgentTraceRecorder(),
            () => ReplayNow,
            graphDiagnostics.Add,
            new DataAgentGraphHandshakeCoordinator(
                DataAgentGraphHandshakeOptions.Disabled,
                DisabledDataAgentGraphSidecarClient.Instance));

        string context = xmlDecision.IsAllowed
            ? handler.Start(fixture.CallerId, fixture.Utterance)
            : string.Empty;

        XmlFunctionCaller functionCaller = new(NullLogger<XmlFunctionCaller>.Instance);
        functionCaller.UpdateDataAgentAnalysisRouteSessionFromContext(context);
        ToolRouteState activeRouteState = functionCaller.CreateToolRouteState(
            fixture.RouteState.IsOwner,
            fixture.RouteState.IsPrivate,
            fixture.RouteState.TrustedRuntime);

        string evidenceText = evidenceDiagnostics.LastOrDefault() ?? string.Empty;
        string traceText = traceDiagnostics.LastOrDefault() ?? string.Empty;
        string progressText = progressDiagnostics.LastOrDefault() ?? string.Empty;
        string graphText = graphDiagnostics.LastOrDefault() ?? string.Empty;
        QChatDiagnosticsRuntimeState diagnosticsState = new(
            RecentDataAgentEvidence: evidenceText,
            RecentDataAgentTrace: traceText,
            RecentDataAgentProgress: progressText,
            RecentDataAgentGraph: graphText);
        QChatAgentRoute qchatRoute = OwnerPrivateRoute();
        QChatAgentProfile qchatProfile = OwnerProfile();
        QChatDiagnosticsResult qchatEvidence = QChatDiagnosticsService.TryHandle("/dataagent diag evidence", qchatRoute, qchatProfile, diagnosticsState);
        QChatDiagnosticsResult qchatTrace = QChatDiagnosticsService.TryHandle("/dataagent diag trace", qchatRoute, qchatProfile, diagnosticsState);
        QChatDiagnosticsResult qchatProgress = QChatDiagnosticsService.TryHandle("/dataagent diag progress", qchatRoute, qchatProfile, diagnosticsState);
        QChatDiagnosticsResult qchatGraph = QChatDiagnosticsService.TryHandle("/dataagent diag graph", qchatRoute, qchatProfile, diagnosticsState);

        string replayEvidence = string.Join(
            Environment.NewLine,
            context,
            evidenceText,
            traceText,
            progressText,
            graphText,
            qchatEvidence.Text,
            qchatTrace.Text,
            qchatProgress.Text,
            qchatGraph.Text);
        DataAgentReplayOfflineBoundary offlineBoundary = BuildOfflineBoundary(replayEvidence, graphText);
        string combined = string.Join(
            Environment.NewLine,
            replayEvidence,
            $"sidecar_authority={LowerBool(offlineBoundary.SidecarAuthority)}",
            $"default_tests_live_runtime={LowerBool(offlineBoundary.DefaultTestsLiveRuntime)}");

        DataAgentReplayExpectedMarker[] markerResults = fixture.ExpectedMarkers
            .Select(marker => new DataAgentReplayExpectedMarker(marker, combined.Contains(marker, StringComparison.Ordinal)))
            .ToArray();
        bool allMarkersPassed = markerResults.All(marker => marker.Passed);

        DataAgentToolRouteContext routeContext = new XmlPolicyDataAgentToolRouteContextAccessor(policy)
            .Get("dataagent_analysis_start", null);
        string orchestrationTrace = ReadContextValue(context, "orchestration_trace=");
        bool orchestrationAccepted = context.Contains("accepted=true", StringComparison.Ordinal) || store.AcceptedAudit.Count > 0;
        string rejectedReason = ReadContextValue(context, "rejected_reason=");
        string sessionId = ReadContextValue(context, "session_id=");
        string sessionStatus = ReadContextValue(context, "status=");
        bool passed =
            markerResults.Length > 0 &&
            allMarkersPassed &&
            xmlDecision.IsAllowed &&
            routeContext.AllowsQuery &&
            orchestrationAccepted &&
            string.IsNullOrWhiteSpace(sessionId) == false &&
            offlineBoundary.SidecarAuthority == false &&
            offlineBoundary.DefaultTestsLiveRuntime == false;

        return new DataAgentReplayResult(
            new DataAgentReplayFixtureSummary(fixture.Version, fixture.Name, fixture.CallerId, fixture.Utterance),
            new DataAgentReplayRouteReport(
                route.Domain.ToString(),
                route.Intent,
                route.ReasonCode,
                route.Reason,
                route.AllowedTools.ToArray(),
                route.DeniedTools.Select(tool => $"{tool.Name}:{tool.Reason}").ToArray()),
            new DataAgentReplayXmlPolicyReport(xmlDecision.IsAllowed, xmlDecision.Reason ?? "allowed"),
            new DataAgentReplayRouteContextReport(
                routeContext.Present,
                routeContext.ToolName,
                routeContext.AllowsTool,
                routeContext.AllowsQuery,
                routeContext.RouteId,
                routeContext.Intent,
                routeContext.ReasonCode,
                routeContext.RouteSessionId),
            new DataAgentReplayOrchestrationReport(
                orchestrationTrace,
                orchestrationAccepted,
                rejectedReason,
                store.QueryCount),
            new DataAgentReplaySessionReport(
                sessionId,
                sessionStatus,
                activeRouteState.HasActiveDataAgentSession),
            new DataAgentReplayDiagnosticsReport(
                evidenceText,
                traceText,
                progressText,
                graphText,
                qchatEvidence.Text,
                qchatTrace.Text,
                qchatProgress.Text,
                qchatGraph.Text),
            markerResults,
            offlineBoundary,
            passed);
    }

    static DataAgentQueryPlan ToPlan(DataAgentReplayPlannerFixture planner)
    {
        return new DataAgentQueryPlan(
            planner.Dataset,
            planner.Intent,
            planner.Select.ToArray(),
            planner.Filters
                .Select(filter => new DataAgentFilter(filter.Field, filter.Operator, filter.Value))
                .ToArray(),
            [],
            planner.Limit);
    }

    static DataAgentReplayOfflineBoundary BuildOfflineBoundary(string replayEvidence, string graphDiagnostics)
    {
        bool disabledSidecarObserved = graphDiagnostics.Contains("sidecar_disabled", StringComparison.OrdinalIgnoreCase);
        bool liveRuntimeObserved = ContainsLiveRuntimeMarker(replayEvidence);
        return new DataAgentReplayOfflineBoundary(
            SidecarAuthority: disabledSidecarObserved == false || liveRuntimeObserved,
            DefaultTestsLiveRuntime: liveRuntimeObserved);
    }

    static bool ContainsLiveRuntimeMarker(string value)
    {
        string[] markers =
        [
            "http://",
            "https://",
            "127.0.0.1",
            "localhost",
            "uvicorn",
            "Start-Process",
            "DataAgentGraphHandshakeHttpClient"
        ];

        return markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    static string LowerBool(bool value) => value ? "true" : "false";

    static XmlFunction Function(string name) => new()
    {
        Name = name,
        Mode = FunctionMode.OneShot,
        Invoker = (_, _) => Task.CompletedTask
    };

    static XmlContext ContextWithSession(string? sessionId)
    {
        Dictionary<string, string> parameters = new(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(sessionId) == false)
            parameters["sessionid"] = sessionId;

        return new XmlContext
        {
            CallMode = CallMode.OneShot,
            Parameters = parameters
        };
    }

    static string ReadContextValue(string context, string prefix)
    {
        foreach (string line in context.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith(prefix, StringComparison.Ordinal))
                return trimmed[prefix.Length..].Trim();
        }

        return string.Empty;
    }

    static QChatAgentRoute OwnerPrivateRoute() => new(
        "xiayu",
        10001,
        QChatConversationKind.Private,
        20002,
        20002,
        true,
        "qq:xiayu:10001:private:20002");

    static QChatAgentProfile OwnerProfile() => new(
        "xiayu",
        "XiaYu",
        "persona.md",
        "owner",
        "test-model",
        "owner",
        [],
        new QChatAgentCapabilities(
            AllowComputerFileTools: true,
            AllowProjectModification: true,
            AllowRecall: true,
            AllowPoke: true));

    sealed class FixedPlanner(DataAgentQueryPlan plan) : IDataAgentQueryPlanner
    {
        public DataAgentQueryPlanEnvelope Plan(DataAgentQueryRequest request)
        {
            return new DataAgentQueryPlanEnvelope(
                plan,
                new DataAgentPlannerExplanation(
                    nameof(FixedPlanner),
                    plan.Intent,
                    plan.Dataset,
                    "high",
                    ["v3_9_replay_runbook"],
                    "V3.9 replay runbook fixed planner"));
        }
    }

    sealed class RecordingDataAgentStore : IDataAgentStore
    {
        readonly List<DataAgentAuditRecord> queryAudit = [];
        readonly List<DataAgentToolBrokerAuditRecord> toolBrokerAudit = [];

        public string ProviderName => "recording";
        public int QueryCount { get; private set; }
        public List<DataAgentAcceptedAuditInput> AcceptedAudit { get; } = [];
        public List<DataAgentRejectedAuditInput> RejectedAudit { get; } = [];

        public void Initialize() { }

        public void ImportFixtures() { }

        public DataAgentQueryResult Query(DataAgentCompiledSql compiledSql)
        {
            QueryCount++;
            return new DataAgentQueryResult([
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["path"] = "docs/dataagent/dataagent-v3.9.md",
                    ["title"] = "DataAgent V3.9 replay runbook"
                }
            ]);
        }

        public void RecordAccepted(DataAgentAcceptedAuditInput input)
        {
            AcceptedAudit.Add(input);
            queryAudit.Add(new DataAgentAuditRecord(
                input.Question,
                input.Dataset,
                input.QueryPlanJson,
                input.GeneratedSql,
                true,
                string.Empty,
                input.RowCount,
                input.Elapsed,
                ReplayNow));
        }

        public void RecordRejected(DataAgentRejectedAuditInput input)
        {
            RejectedAudit.Add(input);
            queryAudit.Add(new DataAgentAuditRecord(
                input.Question,
                input.Dataset,
                input.QueryPlanJson,
                input.GeneratedSql,
                false,
                input.RejectedReason,
                0,
                input.Elapsed,
                ReplayNow));
        }

        public IReadOnlyList<DataAgentAuditRecord> ReadQueryAudit() => queryAudit;

        public void RecordToolBrokerAudit(DataAgentToolBrokerAuditRecord record)
        {
            toolBrokerAudit.Add(record);
        }

        public IReadOnlyList<DataAgentToolBrokerAuditRecord> ReadToolBrokerAudit() => toolBrokerAudit;
    }
}
