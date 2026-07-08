using Alife.Function.DataAgent;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;
using Alife.Function.QChat;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentEndToEndChainContractTests
{
    const string SidecarAuthorityBoundary = "sidecar_authority=false";
    const string DefaultTestsLiveRuntimeBoundary = "default_tests_live_runtime=false";

    static readonly string[] AllDataAgentTools =
    [
        "dataagent_query",
        "dataagent_analysis_start",
        "dataagent_analysis_continue",
        "dataagent_analysis_summarize",
        "dataagent_analysis_end"
    ];

    [Test]
    public void AcceptedAnalysisPublishesSessionStateAndAllDiagnostics()
    {
        DateTimeOffset now = new(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);
        RecordingDataAgentStore dataStore = new();
        FixedPlanner planner = new(DocumentPlan());
        DataAgentProgressRecorder progressRecorder = new();
        List<string> progressDiagnostics = [];
        DataAgentProgressDiagnosticsPublisher progressPublisher = new(
            progressRecorder,
            progressDiagnostics.Add,
            () => now);
        InMemoryDataAgentAnalysisSessionStore sessionStore = new();
        DataAgentService dataAgentService = new(dataStore, planner);
        DataAgentAnalysisService analysisService = new(
            dataAgentService,
            sessionStore,
            progressPublisher,
            () => now);
        DataAgentAnalysisOrchestrator orchestrator = new(
            analysisService,
            sessionStore,
            progressSink: progressPublisher,
            progressClock: () => now);
        List<string> publishedContexts = [];
        List<string> evidenceDiagnostics = [];
        List<string> traceDiagnostics = [];
        List<string> graphDiagnostics = [];
        ToolCapabilityRouter router = ToolCapabilityRouter.CreateDefault();
        XmlFunctionExecutionPolicy policy = new();
        policy.SetGovernedToolNames(router.ToolNames);
        policy.CurrentRoute = router.Route(
            "DataAgent analyze project readiness",
            RouteState(isOwner: true, isPrivateChat: true, isTrustedRuntime: true));
        DataAgentAnalysisToolHandler handler = new(
            orchestrator,
            publishedContexts.Add,
            new XmlPolicyDataAgentToolRouteContextAccessor(policy),
            evidenceDiagnostics.Add,
            traceDiagnostics.Add,
            new DataAgentTraceRecorder(),
            () => now,
            graphDiagnostics.Add,
            new DataAgentGraphHandshakeCoordinator(
                DataAgentGraphHandshakeOptions.Disabled,
                DisabledDataAgentGraphSidecarClient.Instance));

        XmlFunctionExecutionDecision startDecision = policy.TryConsume(
            Function("dataagent_analysis_start"),
            ContextWithSession(null));
        string context = handler.Start("owner", "DataAgent analyze project readiness");
        string sessionId = ReadContextValue(context, "session_id=");
        XmlFunctionCaller caller = new(NullLogger<XmlFunctionCaller>.Instance);
        ToolRouteState inactiveState = caller.CreateToolRouteState(
            isOwner: true,
            isPrivateChat: true,
            isTrustedRuntime: true);
        caller.UpdateDataAgentAnalysisRouteSessionFromContext(context);
        ToolRouteState activeState = caller.CreateToolRouteState(
            isOwner: true,
            isPrivateChat: true,
            isTrustedRuntime: true);
        QChatDiagnosticsRuntimeState runtimeState = new(
            RecentDataAgentEvidence: evidenceDiagnostics.Single(),
            RecentDataAgentTrace: traceDiagnostics.Single(),
            RecentDataAgentProgress: progressDiagnostics.Last(),
            RecentDataAgentGraph: graphDiagnostics.Single());
        QChatDiagnosticsResult evidence = QChatDiagnosticsService.TryHandle(
            "/dataagent diag evidence",
            OwnerPrivateRoute(),
            OwnerProfile(),
            runtimeState);
        QChatDiagnosticsResult trace = QChatDiagnosticsService.TryHandle(
            "/dataagent diag trace",
            OwnerPrivateRoute(),
            OwnerProfile(),
            runtimeState);
        QChatDiagnosticsResult progress = QChatDiagnosticsService.TryHandle(
            "/dataagent diag progress",
            OwnerPrivateRoute(),
            OwnerProfile(),
            runtimeState);
        QChatDiagnosticsResult graph = QChatDiagnosticsService.TryHandle(
            "/dataagent diag graph",
            OwnerPrivateRoute(),
            OwnerProfile(),
            runtimeState);
        string combinedDiagnostics = string.Join(
            Environment.NewLine,
            evidenceDiagnostics.Single(),
            traceDiagnostics.Single(),
            progressDiagnostics.Last(),
            graphDiagnostics.Single(),
            evidence.Text,
            trace.Text,
            progress.Text,
            graph.Text);

        Assert.Multiple(() =>
        {
            Assert.That(startDecision.IsAllowed, Is.True);
            Assert.That(dataStore.QueryCount, Is.EqualTo(1));
            Assert.That(dataStore.AcceptedAudit, Has.Count.EqualTo(1));
            Assert.That(dataStore.RejectedAudit, Is.Empty);
            Assert.That(context, Does.Contain("[data_agent_analysis_session_context]"));
            Assert.That(context, Does.Contain("orchestration_trace=RouteGate:Succeeded>SchemaContext:Succeeded>Plan:Succeeded>Validate:Succeeded>Execute:Succeeded>Explain:Succeeded>Checkpoint:Succeeded"));
            Assert.That(context, Does.Contain("route_present=true"));
            Assert.That(context, Does.Contain("route_tool=dataagent_analysis_start"));
            Assert.That(context, Does.Contain("route_allows_query=true"));
            Assert.That(context, Does.Contain("route_reason_code=route_allowed"));
            Assert.That(context, Does.Contain("route_session_id="));
            Assert.That(context, Does.Contain("[data_agent_context]"));
            Assert.That(context, Does.Contain("sql_status=validated"));
            Assert.That(sessionId, Is.Not.Empty);
            Assert.That(publishedContexts, Is.EqualTo(new[] { context }));
            Assert.That(inactiveState.ActiveDataAgentSessionId, Is.Empty);
            Assert.That(inactiveState.HasActiveDataAgentSession, Is.False);
            Assert.That(activeState.ActiveDataAgentSessionId, Is.EqualTo(sessionId));
            Assert.That(activeState.ActiveDataAgentStatus, Is.EqualTo("Active"));
            Assert.That(activeState.HasActiveDataAgentSession, Is.True);
            Assert.That(evidenceDiagnostics.Single(), Does.Contain("DataAgent evidence diagnostics"));
            Assert.That(evidenceDiagnostics.Single(), Does.Contain("route_allowed=true"));
            Assert.That(evidenceDiagnostics.Single(), Does.Contain("route_allows_query=true"));
            Assert.That(evidenceDiagnostics.Single(), Does.Contain("executed_sql=true"));
            Assert.That(traceDiagnostics.Single(), Does.Contain("DataAgent trace diagnostics"));
            Assert.That(traceDiagnostics.Single(), Does.Contain("RouteGate Succeeded reason=route_allowed"));
            Assert.That(traceDiagnostics.Single(), Does.Contain("Checkpoint Succeeded reason=checkpoint_created"));
            Assert.That(progressDiagnostics.Last(), Does.Contain("DataAgent progress diagnostics"));
            Assert.That(progressDiagnostics.Last(), Does.Contain("Checkpoint"));
            Assert.That(graphDiagnostics.Single(), Does.Contain("DataQueryGraph dry-run"));
            Assert.That(graphDiagnostics.Single(), Does.Contain("DataAgent graph handshake"));
            Assert.That(graphDiagnostics.Single(), Does.Contain("reason=sidecar_disabled"));
            Assert.That(evidence.Handled, Is.True);
            Assert.That(trace.Handled, Is.True);
            Assert.That(progress.Handled, Is.True);
            Assert.That(graph.Handled, Is.True);
            Assert.That(evidence.Text, Does.Contain("DataAgent evidence diagnostics"));
            Assert.That(trace.Text, Does.Contain("DataAgent trace diagnostics"));
            Assert.That(progress.Text, Does.Contain("DataAgent progress diagnostics"));
            Assert.That(graph.Text, Does.Contain("DataQueryGraph dry-run"));
            Assert.That(combinedDiagnostics, Does.Not.Contain("sql.execute"));
            Assert.That(combinedDiagnostics, Does.Not.Contain("RequestsVisibleText=True"));
        });
    }

    [Test]
    public void RouteDeniedAnalysisDoesNotExecuteSql()
    {
        DateTimeOffset now = new(2026, 7, 8, 12, 5, 0, TimeSpan.Zero);
        RecordingDataAgentStore dataStore = new();
        DataAgentAnalysisOrchestrator orchestrator = Orchestrator(dataStore, now);
        DataAgentToolRouteContext routeContext = new(
            true,
            "dataagent_analysis_start",
            false,
            false,
            "tool-capability-router-v0",
            "analysis_start",
            "owner_private_required",
            string.Empty);

        DataAgentOrchestrationResult result = orchestrator.Start(new DataAgentOrchestrationRequest(
            "owner",
            "DataAgent analyze project readiness",
            null,
            RouteAllowsQuery: false,
            routeContext));

        Assert.Multiple(() =>
        {
            Assert.That(result.Steps.Select(step => step.Node), Is.EqualTo(new[]
            {
                DataAgentOrchestrationNodeKind.RouteGate,
                DataAgentOrchestrationNodeKind.Reject,
                DataAgentOrchestrationNodeKind.Checkpoint
            }));
            Assert.That(result.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.Execute), Is.False);
            Assert.That(result.Steps.Any(step => step.ExecutedSql), Is.False);
            Assert.That(result.Response.Accepted, Is.False);
            Assert.That(result.Response.RejectedReason, Is.EqualTo("owner_private_required"));
            Assert.That(dataStore.QueryCount, Is.Zero);
            Assert.That(dataStore.AcceptedAudit, Is.Empty);
            Assert.That(dataStore.RejectedAudit, Is.Empty);
        });
    }

    [Test]
    public void TerminalAnalysisActionsDoNotExecuteSqlAndRemainSessionScoped()
    {
        DateTimeOffset now = new(2026, 7, 8, 12, 10, 0, TimeSpan.Zero);
        RecordingDataAgentStore dataStore = new();
        DataAgentAnalysisOrchestrator orchestrator = Orchestrator(dataStore, now);
        DataAgentOrchestrationResult start = orchestrator.Start(new DataAgentOrchestrationRequest(
            "owner",
            "DataAgent analyze project readiness",
            null,
            RouteAllowsQuery: true,
            AllowedContext("dataagent_analysis_start", string.Empty)));
        int queryCountAfterStart = dataStore.QueryCount;

        DataAgentOrchestrationResult summary = orchestrator.Summarize(
            start.SessionId,
            AllowedContext("dataagent_analysis_summarize", start.SessionId));
        DataAgentOrchestrationResult end = orchestrator.End(
            start.SessionId,
            AllowedContext("dataagent_analysis_end", start.SessionId));
        DataAgentOrchestrationResult deniedSummary = orchestrator.Summarize(
            start.SessionId,
            DataAgentToolRouteContext.Missing("dataagent_analysis_summarize"));
        ToolCapabilityRouter router = ToolCapabilityRouter.CreateDefault();
        XmlFunctionExecutionPolicy policy = new();
        policy.SetGovernedToolNames(router.ToolNames);
        policy.CurrentRoute = router.Route(
            "continue DataAgent analysis",
            RouteState(sessionId: start.SessionId, status: "Active"));

        XmlFunctionExecutionDecision summarizeMissingSession = policy.TryConsume(
            Function("dataagent_analysis_summarize"),
            ContextWithSession(null));
        XmlFunctionExecutionDecision summarizeWrongSession = policy.TryConsume(
            Function("dataagent_analysis_summarize"),
            ContextWithSession("different-session"));
        XmlFunctionExecutionDecision summarizeMatchingSession = policy.TryConsume(
            Function("dataagent_analysis_summarize"),
            ContextWithSession(start.SessionId));
        XmlFunctionExecutionDecision endMissingSession = policy.TryConsume(
            Function("dataagent_analysis_end"),
            ContextWithSession(null));
        XmlFunctionExecutionDecision endWrongSession = policy.TryConsume(
            Function("dataagent_analysis_end"),
            ContextWithSession("different-session"));
        XmlFunctionExecutionDecision endMatchingSession = policy.TryConsume(
            Function("dataagent_analysis_end"),
            ContextWithSession(start.SessionId));

        Assert.Multiple(() =>
        {
            Assert.That(queryCountAfterStart, Is.EqualTo(1));
            Assert.That(dataStore.QueryCount, Is.EqualTo(1));
            Assert.That(summary.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.Execute), Is.False);
            Assert.That(summary.Steps.Any(step => step.ExecutedSql), Is.False);
            Assert.That(end.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.Execute), Is.False);
            Assert.That(end.Steps.Any(step => step.ExecutedSql), Is.False);
            Assert.That(summary.RouteContext?.RouteSessionId, Is.EqualTo(start.SessionId));
            Assert.That(end.RouteContext?.RouteSessionId, Is.EqualTo(start.SessionId));
            Assert.That(deniedSummary.Steps[0].Node, Is.EqualTo(DataAgentOrchestrationNodeKind.RouteGate));
            Assert.That(deniedSummary.Steps[0].Status, Is.EqualTo(DataAgentOrchestrationStepStatus.Rejected));
            Assert.That(deniedSummary.Response.Accepted, Is.False);
            Assert.That(deniedSummary.Response.RejectedReason, Is.EqualTo("tool_route_required"));
            Assert.That(summarizeMissingSession.IsAllowed, Is.False);
            Assert.That(summarizeMissingSession.Reason, Is.EqualTo("tool_session_not_allowed_in_current_route"));
            Assert.That(summarizeWrongSession.IsAllowed, Is.False);
            Assert.That(summarizeWrongSession.Reason, Is.EqualTo("tool_session_not_allowed_in_current_route"));
            Assert.That(summarizeMatchingSession.IsAllowed, Is.True);
            Assert.That(endMissingSession.IsAllowed, Is.False);
            Assert.That(endMissingSession.Reason, Is.EqualTo("tool_session_not_allowed_in_current_route"));
            Assert.That(endWrongSession.IsAllowed, Is.False);
            Assert.That(endWrongSession.Reason, Is.EqualTo("tool_session_not_allowed_in_current_route"));
            Assert.That(endMatchingSession.IsAllowed, Is.True);
        });
    }

    [Test]
    public void ToolBrokerRoutesDataAgentToolsOnlyForTrustedOwnerPrivateSurface()
    {
        ToolCapabilityRouter router = ToolCapabilityRouter.CreateDefault();

        ToolRouteDecision start = router.Route(
            "analyze project readiness for V2",
            RouteState());
        Assert.Multiple(() =>
        {
            Assert.That(start.Domain, Is.EqualTo(ToolCapabilityDomain.DataAgent));
            Assert.That(start.AllowedTools, Is.EqualTo(new[] { "dataagent_query", "dataagent_analysis_start" }));
            Assert.That(start.Intent, Is.EqualTo("analysis_start"));
            Assert.That(start.ReasonCode, Is.EqualTo("route_allowed"));
        });

        ToolRouteDecision active = router.Route(
            "continue DataAgent analysis",
            RouteState(sessionId: "session-a", status: "Active"));
        Assert.Multiple(() =>
        {
            Assert.That(active.Domain, Is.EqualTo(ToolCapabilityDomain.DataAgent));
            Assert.That(active.AllowedTools, Is.EqualTo(new[]
            {
                "dataagent_query",
                "dataagent_analysis_continue",
                "dataagent_analysis_summarize",
                "dataagent_analysis_end"
            }));
            Assert.That(active.Intent, Is.EqualTo("analysis_continue"));
            Assert.That(active.ReasonCode, Is.EqualTo("route_allowed"));
            Assert.That(active.State.ActiveDataAgentSessionId, Is.EqualTo("session-a"));
        });

        AssertDataAgentDenied(
            router.Route("analyze project readiness for V2", RouteState(isOwner: false)),
            "owner_private_required",
            "surface_not_allowed");
        AssertDataAgentDenied(
            router.Route("analyze project readiness for V2", RouteState(isPrivateChat: false)),
            "owner_private_required",
            "surface_not_allowed");
        AssertDataAgentDenied(
            router.Route("analyze project readiness for V2", RouteState(isTrustedRuntime: false)),
            "trusted_runtime_required",
            "route_state_not_trusted");

        ToolRouteDecision ordinaryChat = router.Route("hello, can you answer normally?", RouteState());
        Assert.Multiple(() =>
        {
            Assert.That(ordinaryChat.Domain, Is.EqualTo(ToolCapabilityDomain.Chat));
            Assert.That(ordinaryChat.AllowedTools, Is.Empty);
        });
        AssertDataAgentDenied(
            ordinaryChat,
            "intent_not_matched",
            "tool_not_allowed_in_current_route");
    }

    [Test]
    public void XmlExecutionPolicyEnforcesRouteAndSessionScopeForDataAgentTools()
    {
        ToolCapabilityRouter router = ToolCapabilityRouter.CreateDefault();
        XmlFunctionExecutionPolicy policy = new();
        policy.SetGovernedToolNames(router.ToolNames);

        XmlFunctionExecutionDecision noRouteStart = policy.TryConsume(Function("dataagent_analysis_start"));
        Assert.Multiple(() =>
        {
            Assert.That(noRouteStart.IsAllowed, Is.False);
            Assert.That(noRouteStart.Reason, Is.EqualTo("tool_route_required"));
        });

        policy.CurrentRoute = router.Route("analyze project readiness for V2", RouteState());
        XmlFunctionExecutionDecision startAllowed = policy.TryConsume(Function("dataagent_analysis_start"));
        XmlFunctionExecutionDecision continueDeniedOnStart = policy.TryConsume(Function("dataagent_analysis_continue"));
        Assert.Multiple(() =>
        {
            Assert.That(startAllowed.IsAllowed, Is.True);
            Assert.That(continueDeniedOnStart.IsAllowed, Is.False);
            Assert.That(continueDeniedOnStart.Reason, Is.EqualTo("tool_not_allowed_in_current_route"));
        });

        policy.CurrentRoute = router.Route(
            "continue DataAgent analysis",
            RouteState(sessionId: "session-a", status: "Active"));
        XmlFunctionExecutionDecision missingSession = policy.TryConsume(
            Function("dataagent_analysis_continue"),
            ContextWithSession(null));
        XmlFunctionExecutionDecision wrongSession = policy.TryConsume(
            Function("dataagent_analysis_continue"),
            ContextWithSession("session-b"));
        XmlFunctionExecutionDecision matchingSession = policy.TryConsume(
            Function("dataagent_analysis_continue"),
            ContextWithSession("session-a"));

        Assert.Multiple(() =>
        {
            Assert.That(missingSession.IsAllowed, Is.False);
            Assert.That(missingSession.Reason, Is.EqualTo("tool_session_not_allowed_in_current_route"));
            Assert.That(wrongSession.IsAllowed, Is.False);
            Assert.That(wrongSession.Reason, Is.EqualTo("tool_session_not_allowed_in_current_route"));
            Assert.That(matchingSession.IsAllowed, Is.True);
        });
    }

    [Test]
    public void OfflineBoundaryMarkersLockNoLiveRuntimeAndNoSidecarAuthority()
    {
        string repoRoot = FindRepoRoot();
        string testFile = Path.Combine(
            repoRoot,
            "Tests",
            "Alife.Test.DataAgent",
            nameof(DataAgentEndToEndChainContractTests) + ".cs");
        string source = File.ReadAllText(testFile);

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain(SidecarAuthorityBoundary));
            Assert.That(source, Does.Contain(DefaultTestsLiveRuntimeBoundary));
            Assert.That(source, Does.Not.Contain("Invoke-" + "WebRequest"));
            Assert.That(source, Does.Not.Contain("Start-" + "Process"));
            Assert.That(source, Does.Not.Contain("uvi" + "corn"));
            Assert.That(source, Does.Not.Contain("127.0.0." + "1:8765"));
            Assert.That(source, Does.Not.Contain("Event" + "Source"));
        });
    }

    static ToolRouteState RouteState(
        string sessionId = "",
        string status = "",
        bool isOwner = true,
        bool isPrivateChat = true,
        bool isTrustedRuntime = true)
    {
        return new ToolRouteState(
            sessionId,
            status,
            isOwner,
            isPrivateChat,
            isTrustedRuntime);
    }

    static DataAgentAnalysisOrchestrator Orchestrator(
        IDataAgentStore dataStore,
        DateTimeOffset now)
    {
        DataAgentService dataAgentService = new(dataStore, new FixedPlanner(DocumentPlan()));
        InMemoryDataAgentAnalysisSessionStore sessionStore = new();
        DataAgentAnalysisService analysisService = new(
            dataAgentService,
            sessionStore,
            clock: () => now);

        return new DataAgentAnalysisOrchestrator(
            analysisService,
            sessionStore,
            progressClock: () => now);
    }

    static DataAgentQueryPlan DocumentPlan()
    {
        return new DataAgentQueryPlan(
            "document_index",
            "find_dataagent_documents",
            ["path", "title"],
            [new DataAgentFilter("tags", "contains", "dataagent")],
            [],
            20);
    }

    static DataAgentToolRouteContext AllowedContext(string toolName, string sessionId)
    {
        return new DataAgentToolRouteContext(
            true,
            toolName,
            true,
            true,
            "tool-capability-router-v0",
            toolName.Contains("start", StringComparison.OrdinalIgnoreCase)
                ? "analysis_start"
                : "analysis_continue",
            "route_allowed",
            sessionId);
    }

    static string ReadContextValue(string context, string prefix)
    {
        foreach (string rawLine in context.Split('\n'))
        {
            string line = rawLine.Trim().TrimEnd('\r');
            if (line.StartsWith(prefix, StringComparison.Ordinal))
                return line[prefix.Length..];
        }

        return string.Empty;
    }

    static QChatAgentRoute OwnerPrivateRoute()
    {
        return new QChatAgentRoute(
            "xiayu",
            10001,
            QChatConversationKind.Private,
            20002,
            20002,
            true,
            "qq:xiayu:10001:private:20002");
    }

    static QChatAgentProfile OwnerProfile()
    {
        return new QChatAgentProfile(
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
    }

    static void AssertDataAgentDenied(
        ToolRouteDecision decision,
        string expectedReasonCode,
        string expectedDeniedToolReason)
    {
        Assert.Multiple(() =>
        {
            Assert.That(decision.AllowedTools, Is.Empty);
            Assert.That(decision.ReasonCode, Is.EqualTo(expectedReasonCode));
            Assert.That(
                decision.DeniedTools.Select(tool => tool.Name),
                Is.EqualTo(AllDataAgentTools));
            Assert.That(
                decision.DeniedTools.Select(tool => tool.Reason),
                Is.All.EqualTo(expectedDeniedToolReason));
        });
    }

    static XmlFunction Function(string name)
    {
        return new XmlFunction
        {
            Name = name,
            Mode = FunctionMode.OneShot,
            Invoker = (_, _) => Task.CompletedTask,
        };
    }

    static XmlContext ContextWithSession(string? sessionId)
    {
        Dictionary<string, string> parameters = [];
        if (string.IsNullOrWhiteSpace(sessionId) == false)
            parameters["sessionid"] = sessionId;

        return new XmlContext
        {
            CallMode = CallMode.OneShot,
            Parameters = parameters,
        };
    }

    static string FindRepoRoot()
    {
        DirectoryInfo? current = new(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Alife.slnx")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not find repository root containing Alife.slnx.");
    }

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
                    ["dataagent-v3.8", "chain-contract"],
                    "deterministic document index plan"));
        }
    }

    sealed class RecordingDataAgentStore : IDataAgentStore
    {
        readonly List<DataAgentCompiledSql> queries = [];
        readonly List<DataAgentAuditRecord> queryAudit = [];
        readonly List<DataAgentToolBrokerAuditRecord> toolBrokerAudit = [];

        public string ProviderName => "recording";
        public int QueryCount => queries.Count;
        public IReadOnlyList<DataAgentCompiledSql> Queries => queries;
        public List<DataAgentAcceptedAuditInput> AcceptedAudit { get; } = [];
        public List<DataAgentRejectedAuditInput> RejectedAudit { get; } = [];
        public IReadOnlyList<DataAgentToolBrokerAuditRecord> ToolBrokerAudit => toolBrokerAudit;

        public void Initialize() { }
        public void ImportFixtures() { }

        public DataAgentQueryResult Query(DataAgentCompiledSql compiledSql)
        {
            queries.Add(compiledSql);
            return new DataAgentQueryResult([
                new Dictionary<string, object?>
                {
                    ["path"] = "docs/dataagent/dataagent-v3.8.md",
                    ["title"] = "DataAgent V3.8 chain contract"
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
                DateTimeOffset.UnixEpoch));
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
                DateTimeOffset.UnixEpoch));
        }

        public IReadOnlyList<DataAgentAuditRecord> ReadQueryAudit()
        {
            return queryAudit;
        }

        public void RecordToolBrokerAudit(DataAgentToolBrokerAuditRecord record)
        {
            toolBrokerAudit.Add(record);
        }

        public IReadOnlyList<DataAgentToolBrokerAuditRecord> ReadToolBrokerAudit()
        {
            return toolBrokerAudit;
        }
    }
}
