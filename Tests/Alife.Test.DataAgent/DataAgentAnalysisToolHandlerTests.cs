using Alife.Function.DataAgent;
using Alife.Function.Interpreter;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentAnalysisToolHandlerTests
{
    [Test]
    public void StartCallsOrchestratorAndPublishesOrchestratedContext()
    {
        List<string> published = [];
        List<string> evidenceDiagnostics = [];
        RecordingOrchestrator orchestrator = new(new Dictionary<string, DataAgentOrchestrationResult>
        {
            ["start"] = OrchestratedResult(
                "session-1",
                DataAgentAnalysisSessionStatus.Active,
                DataAgentAnalysisTurnIntent.NewQuestion,
                [
                    new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Succeeded, "route_allowed", false),
                    new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Execute, DataAgentOrchestrationStepStatus.Succeeded, "read_only_query_executed", true),
                    new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
                ],
                1,
                "[data_agent_analysis_session_context]\nsession_id=session-1\ncaller_id=xiayu\n[/data_agent_analysis_session_context]")
        });
        RecordingRouteContextAccessor routeAccessor = new(new DataAgentToolRouteContext(
            true,
            "dataagent_analysis_start",
            true,
            true,
            "route-1",
            "analysis_start",
            "route_allowed",
            string.Empty));
        DataAgentAnalysisToolHandler handler = new(orchestrator, published.Add, routeAccessor, evidenceDiagnostics.Add);

        string context = handler.Start("xiayu", "Which documents describe DataAgent?");

        Assert.Multiple(() =>
        {
            Assert.That(orchestrator.StartRequests, Has.Count.EqualTo(1));
            Assert.That(orchestrator.StartRequests[0].CallerId, Is.EqualTo("xiayu"));
            Assert.That(orchestrator.StartRequests[0].Input, Is.EqualTo("Which documents describe DataAgent?"));
            Assert.That(orchestrator.StartRequests[0].SessionId, Is.Null);
            Assert.That(orchestrator.StartRequests[0].RouteAllowsQuery, Is.True);
            Assert.That(routeAccessor.Requests, Is.EqualTo(new[] { ("dataagent_analysis_start", (string?)null) }));
            Assert.That(orchestrator.StartRequests[0].RouteContext?.RouteId, Is.EqualTo("route-1"));
            Assert.That(context, Does.Contain("[data_agent_analysis_session_context]"));
            Assert.That(context, Does.Contain("orchestration_trace=RouteGate:Succeeded>Execute:Succeeded>Checkpoint:Succeeded"));
            Assert.That(context, Does.Contain("checkpoint_session_id=session-1"));
            Assert.That(context, Does.Contain("route_reason_code=route_allowed"));
            Assert.That(published, Is.EqualTo(new[] { context }));
            Assert.That(evidenceDiagnostics, Has.Count.EqualTo(1));
            Assert.That(evidenceDiagnostics.Single(), Does.Contain("DataAgent evidence diagnostics"));
            Assert.That(evidenceDiagnostics.Single(), Does.Contain("analysis_confidence="));
            Assert.That(evidenceDiagnostics.Single(), Does.Contain("route_allowed=true"));
            Assert.That(evidenceDiagnostics.Single(), Does.Contain("route_allows_query=true"));
            Assert.That(evidenceDiagnostics.Single(), Does.Contain("executed_sql=true"));
            Assert.That(evidenceDiagnostics.Single(), Does.Not.Contain("[data_agent_evidence_pack]"));
            Assert.That(evidenceDiagnostics.Single(), Does.Not.Contain("[data_agent_context]"));
        });
    }

    [Test]
    public void ContinueCallsOrchestratorAndPublishesOrchestratedContext()
    {
        List<string> published = [];
        RecordingOrchestrator orchestrator = new(new Dictionary<string, DataAgentOrchestrationResult>
        {
            ["continue"] = OrchestratedResult(
                "session-1",
                DataAgentAnalysisSessionStatus.Active,
                DataAgentAnalysisTurnIntent.Continue,
                [
                    new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Succeeded, "route_allowed", false),
                    new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Execute, DataAgentOrchestrationStepStatus.Succeeded, "read_only_query_executed", true),
                    new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
                ],
                2,
                "[data_agent_analysis_session_context]\nsession_id=session-1\nturn_count=2\n[/data_agent_analysis_session_context]")
        });
        RecordingRouteContextAccessor routeAccessor = new(new DataAgentToolRouteContext(
            true,
            "dataagent_analysis_continue",
            true,
            true,
            "route-2",
            "analysis_continue",
            "route_allowed",
            "session-1"));
        DataAgentAnalysisToolHandler handler = new(orchestrator, published.Add, routeAccessor);

        string context = handler.Continue("session-1", "continue");

        Assert.Multiple(() =>
        {
            Assert.That(orchestrator.ContinueRequests, Has.Count.EqualTo(1));
            Assert.That(orchestrator.ContinueRequests[0].CallerId, Is.EqualTo("local"));
            Assert.That(orchestrator.ContinueRequests[0].SessionId, Is.EqualTo("session-1"));
            Assert.That(orchestrator.ContinueRequests[0].Input, Is.EqualTo("continue"));
            Assert.That(orchestrator.ContinueRequests[0].RouteAllowsQuery, Is.True);
            Assert.That(routeAccessor.Requests, Is.EqualTo(new[] { ("dataagent_analysis_continue", (string?)"session-1") }));
            Assert.That(orchestrator.ContinueRequests[0].RouteContext?.RouteSessionId, Is.EqualTo("session-1"));
            Assert.That(context, Does.Contain("turn_count=2"));
            Assert.That(context, Does.Contain("orchestration_trace=RouteGate:Succeeded>Execute:Succeeded>Checkpoint:Succeeded"));
            Assert.That(context, Does.Contain("route_session_id=session-1"));
            Assert.That(published, Is.EqualTo(new[] { context }));
        });
    }

    [Test]
    public void StartPublishesTraceDiagnosticsForAcceptedQuery()
    {
        List<string> traceDiagnostics = [];
        RecordingOrchestrator orchestrator = new(new Dictionary<string, DataAgentOrchestrationResult>
        {
            ["start"] = OrchestratedResult(
                "session-1",
                DataAgentAnalysisSessionStatus.Active,
                DataAgentAnalysisTurnIntent.NewQuestion,
                [
                    new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Succeeded, "route_allowed", false),
                    new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.SchemaContext, DataAgentOrchestrationStepStatus.Succeeded, "schema_context_ready", false),
                    new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Plan, DataAgentOrchestrationStepStatus.Succeeded, "plan_created", false),
                    new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Validate, DataAgentOrchestrationStepStatus.Succeeded, "read_only_sql_validated", false),
                    new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Execute, DataAgentOrchestrationStepStatus.Succeeded, "read_only_query_executed", true),
                    new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
                ],
                1,
                "[data_agent_analysis_session_context]\nsession_id=session-1\n[/data_agent_analysis_session_context]")
        });
        RecordingRouteContextAccessor routeAccessor = new(new DataAgentToolRouteContext(
            true,
            "dataagent_analysis_start",
            true,
            true,
            "route-1",
            "analysis_start",
            "route_allowed",
            string.Empty));
        DataAgentAnalysisToolHandler handler = new(
            orchestrator,
            routeContextAccessor: routeAccessor,
            traceDiagnosticsPublisher: traceDiagnostics.Add);

        handler.Start("xiayu", "Which documents describe DataAgent?");

        Assert.Multiple(() =>
        {
            Assert.That(traceDiagnostics, Has.Count.EqualTo(1));
            string trace = traceDiagnostics.Single();
            Assert.That(trace, Does.Contain("DataAgent trace diagnostics"));
            Assert.That(trace, Does.Contain("session=session-1"));
            Assert.That(trace, Does.Contain("RouteGate Succeeded reason=route_allowed"));
            Assert.That(trace, Does.Contain("SchemaContext Succeeded reason=schema_context_ready"));
            Assert.That(trace, Does.Contain("Planner Succeeded reason=plan_created"));
            Assert.That(trace, Does.Contain("SqlSafety Succeeded reason=read_only_sql_validated"));
            Assert.That(trace, Does.Contain("Execute Succeeded reason=read_only_query_executed"));
            Assert.That(trace, Does.Contain("EvidencePack Succeeded"));
            Assert.That(trace, Does.Contain("Checkpoint Succeeded reason=checkpoint_created"));
            Assert.That(trace, Does.Contain("executed_sql=true"));
            Assert.That(trace, Does.Not.Contain("[data_agent_evidence_pack]"));
        });
    }

    [Test]
    public void StartRouteDeniedTraceDoesNotContainExecuteEvent()
    {
        List<string> traceDiagnostics = [];
        RecordingOrchestrator orchestrator = new(new Dictionary<string, DataAgentOrchestrationResult>
        {
            ["start"] = OrchestratedResult(
                "session-1",
                DataAgentAnalysisSessionStatus.Active,
                DataAgentAnalysisTurnIntent.NewQuestion,
                [
                    new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Rejected, "tool_route_required", false),
                    new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Reject, DataAgentOrchestrationStepStatus.Rejected, "tool_route_required", false),
                    new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
                ],
                0,
                "[data_agent_analysis_session_context]\nsession_id=session-1\n[/data_agent_analysis_session_context]")
        });
        RecordingRouteContextAccessor routeAccessor = new(new DataAgentToolRouteContext(
            true,
            "dataagent_analysis_start",
            true,
            false,
            "route-denied",
            "analysis_start",
            "tool_route_required",
            string.Empty));
        DataAgentAnalysisToolHandler handler = new(
            orchestrator,
            routeContextAccessor: routeAccessor,
            traceDiagnosticsPublisher: traceDiagnostics.Add);

        handler.Start("xiayu", "Which documents describe DataAgent?");

        Assert.Multiple(() =>
        {
            string trace = traceDiagnostics.Single();
            Assert.That(trace, Does.Contain("RouteGate Rejected reason=tool_route_required"));
            Assert.That(trace, Does.Contain("Reject Rejected reason=tool_route_required"));
            Assert.That(trace, Does.Contain("Checkpoint Succeeded reason=checkpoint_created"));
            Assert.That(trace, Does.Not.Contain("Execute Succeeded"));
            Assert.That(trace, Does.Contain("query_allowed=false"));
        });
    }

    [Test]
    public void SummarizePublishesTerminalTraceWithoutSqlExecution()
    {
        List<string> traceDiagnostics = [];
        RecordingOrchestrator orchestrator = new(new Dictionary<string, DataAgentOrchestrationResult>
        {
            ["summarize"] = OrchestratedResult(
                "session-1",
                DataAgentAnalysisSessionStatus.Summarized,
                DataAgentAnalysisTurnIntent.Summarize,
                [
                    new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Summarize, DataAgentOrchestrationStepStatus.Succeeded, "terminal_summary", false),
                    new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
                ],
                2,
                "[data_agent_analysis_session_context]\nsession_id=session-1\nstatus=Summarized\n[/data_agent_analysis_session_context]")
        });
        RecordingRouteContextAccessor routeAccessor = new(new DataAgentToolRouteContext(
            true,
            "dataagent_analysis_summarize",
            true,
            true,
            "route-summary",
            "analysis_summarize",
            "route_allowed",
            "session-1"));
        DataAgentAnalysisToolHandler handler = new(
            orchestrator,
            routeContextAccessor: routeAccessor,
            traceDiagnosticsPublisher: traceDiagnostics.Add);

        handler.Summarize("session-1");

        Assert.Multiple(() =>
        {
            string trace = traceDiagnostics.Single();
            Assert.That(trace, Does.Contain("Summarize Succeeded reason=terminal_summary"));
            Assert.That(trace, Does.Contain("terminal=true"));
            Assert.That(trace, Does.Contain("executed_sql=false"));
            Assert.That(trace, Does.Not.Contain("Execute Succeeded"));
        });
    }

    [Test]
    public void TraceDiagnosticsSanitizesStructuredReasonCodes()
    {
        const string RawReason = "unsupported_operator_for_field:contains:engineering_gate.required";
        List<string> traceDiagnostics = [];
        RecordingOrchestrator orchestrator = new(new Dictionary<string, DataAgentOrchestrationResult>
        {
            ["start"] = OrchestratedResult(
                "session-1",
                DataAgentAnalysisSessionStatus.Active,
                DataAgentAnalysisTurnIntent.NewQuestion,
                [
                    new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Succeeded, "route_allowed", false),
                    new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Validate, DataAgentOrchestrationStepStatus.Rejected, RawReason, false),
                    new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Reject, DataAgentOrchestrationStepStatus.Rejected, RawReason, false),
                    new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
                ],
                1,
                "[data_agent_analysis_session_context]\nsession_id=session-1\n[/data_agent_analysis_session_context]")
        });
        RecordingRouteContextAccessor routeAccessor = new(new DataAgentToolRouteContext(
            true,
            "dataagent_analysis_start",
            true,
            true,
            "route-1",
            "analysis_start",
            "route_allowed",
            string.Empty));
        DataAgentAnalysisToolHandler handler = new(
            orchestrator,
            routeContextAccessor: routeAccessor,
            traceDiagnosticsPublisher: traceDiagnostics.Add);

        handler.Start("xiayu", "Which documents describe DataAgent?");

        Assert.Multiple(() =>
        {
            string trace = traceDiagnostics.Single();
            Assert.That(trace, Does.Contain("reason=unsupported_operator_for_field"));
            Assert.That(trace, Does.Not.Contain(RawReason));
            Assert.That(trace, Does.Not.Contain("engineering_gate"));
            Assert.That(trace, Does.Not.Contain("required"));
        });
    }

    [Test]
    public void StartWithoutRouteContextFailsClosedAtRequestBoundary()
    {
        RecordingOrchestrator orchestrator = CreateOrchestrator();
        DataAgentAnalysisToolHandler handler = new(orchestrator);

        handler.Start("xiayu", "Which documents describe DataAgent?");

        Assert.Multiple(() =>
        {
            Assert.That(orchestrator.StartRequests, Has.Count.EqualTo(1));
            Assert.That(orchestrator.StartRequests[0].RouteAllowsQuery, Is.False);
            Assert.That(orchestrator.StartRequests[0].RouteContext?.Present, Is.False);
            Assert.That(orchestrator.StartRequests[0].RouteContext?.ReasonCode, Is.EqualTo("tool_route_required"));
        });
    }

    [Test]
    public void SummarizeWithoutRouteContextFailsClosedAtHandlerBoundaryWithoutMutation()
    {
        int answerCalls = 0;
        InMemoryDataAgentAnalysisSessionStore store = new();
        DataAgentAnalysisOrchestrator orchestrator = RealOrchestrator(
            store,
            _ =>
            {
                answerCalls++;
                return AcceptedAnswer();
            });
        DataAgentOrchestrationResult start = orchestrator.Start(new DataAgentOrchestrationRequest(
            "xiayu",
            "Which documents describe DataAgent?",
            null,
            RouteAllowsQuery: true));
        DataAgentAnalysisToolHandler handler = new(orchestrator);

        string context = handler.Summarize(start.SessionId);
        DataAgentAnalysisSession session = store.Get(start.SessionId)!;

        Assert.Multiple(() =>
        {
            Assert.That(answerCalls, Is.EqualTo(1));
            Assert.That(session.Status, Is.EqualTo(DataAgentAnalysisSessionStatus.Active));
            Assert.That(session.Turns, Has.Count.EqualTo(1));
            Assert.That(context, Does.Contain("orchestration_trace=RouteGate:Rejected>Reject:Rejected>Checkpoint:Succeeded"));
            Assert.That(context, Does.Contain("checkpoint_status=Active"));
            Assert.That(context, Does.Contain("checkpoint_turn_count=1"));
            Assert.That(context, Does.Contain("route_present=false"));
            Assert.That(context, Does.Contain("route_tool=dataagent_analysis_summarize"));
            Assert.That(context, Does.Contain("route_reason_code=tool_route_required"));
        });
    }

    [Test]
    public void EndWithoutRouteContextFailsClosedAtHandlerBoundaryWithoutMutation()
    {
        int answerCalls = 0;
        InMemoryDataAgentAnalysisSessionStore store = new();
        DataAgentAnalysisOrchestrator orchestrator = RealOrchestrator(
            store,
            _ =>
            {
                answerCalls++;
                return AcceptedAnswer();
            });
        DataAgentOrchestrationResult start = orchestrator.Start(new DataAgentOrchestrationRequest(
            "xiayu",
            "Which documents describe DataAgent?",
            null,
            RouteAllowsQuery: true));
        DataAgentAnalysisToolHandler handler = new(orchestrator);

        string context = handler.End(start.SessionId);
        DataAgentAnalysisSession session = store.Get(start.SessionId)!;

        Assert.Multiple(() =>
        {
            Assert.That(answerCalls, Is.EqualTo(1));
            Assert.That(session.Status, Is.EqualTo(DataAgentAnalysisSessionStatus.Active));
            Assert.That(session.Turns, Has.Count.EqualTo(1));
            Assert.That(context, Does.Contain("orchestration_trace=RouteGate:Rejected>Reject:Rejected>Checkpoint:Succeeded"));
            Assert.That(context, Does.Contain("checkpoint_status=Active"));
            Assert.That(context, Does.Contain("checkpoint_turn_count=1"));
            Assert.That(context, Does.Contain("route_present=false"));
            Assert.That(context, Does.Contain("route_tool=dataagent_analysis_end"));
            Assert.That(context, Does.Contain("route_reason_code=tool_route_required"));
        });
    }

    [Test]
    public void SummarizeSessionMismatchFailsClosedAtHandlerBoundaryWithoutMutation()
    {
        int answerCalls = 0;
        InMemoryDataAgentAnalysisSessionStore store = new();
        DataAgentAnalysisOrchestrator orchestrator = RealOrchestrator(
            store,
            _ =>
            {
                answerCalls++;
                return AcceptedAnswer();
            });
        DataAgentOrchestrationResult start = orchestrator.Start(new DataAgentOrchestrationRequest(
            "xiayu",
            "Which documents describe DataAgent?",
            null,
            RouteAllowsQuery: true));
        RecordingRouteContextAccessor routeAccessor = new(new DataAgentToolRouteContext(
            true,
            "dataagent_analysis_summarize",
            false,
            false,
            "route-summary",
            "analysis_summarize",
            DataAgentToolRouteContext.SessionNotAllowedReasonCode,
            "other-session"));
        DataAgentAnalysisToolHandler handler = new(orchestrator, null, routeAccessor);

        string context = handler.Summarize(start.SessionId);
        DataAgentAnalysisSession session = store.Get(start.SessionId)!;

        Assert.Multiple(() =>
        {
            Assert.That(answerCalls, Is.EqualTo(1));
            Assert.That(routeAccessor.Requests, Is.EqualTo(new[] { ("dataagent_analysis_summarize", (string?)start.SessionId) }));
            Assert.That(session.Status, Is.EqualTo(DataAgentAnalysisSessionStatus.Active));
            Assert.That(session.Turns, Has.Count.EqualTo(1));
            Assert.That(context, Does.Contain("orchestration_trace=RouteGate:Rejected>Reject:Rejected>Checkpoint:Succeeded"));
            Assert.That(context, Does.Contain("route_present=true"));
            Assert.That(context, Does.Contain("route_tool=dataagent_analysis_summarize"));
            Assert.That(context, Does.Contain("route_reason_code=tool_session_not_allowed_in_current_route"));
            Assert.That(context, Does.Contain("route_session_id=other-session"));
        });
    }

    [Test]
    public void SummarizeCallsOrchestratorAndPublishesTerminalContext()
    {
        List<string> published = [];
        RecordingOrchestrator orchestrator = new(new Dictionary<string, DataAgentOrchestrationResult>
        {
            ["summarize"] = OrchestratedResult(
                "session-1",
                DataAgentAnalysisSessionStatus.Summarized,
                DataAgentAnalysisTurnIntent.Summarize,
                [
                    new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Summarize, DataAgentOrchestrationStepStatus.Succeeded, "terminal_summary", false),
                    new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
                ],
                2,
                "[data_agent_analysis_session_context]\nsession_id=session-1\nstatus=Summarized\n[/data_agent_analysis_session_context]")
        });
        RecordingRouteContextAccessor routeAccessor = new(new DataAgentToolRouteContext(
            true,
            "dataagent_analysis_summarize",
            true,
            true,
            "route-summary",
            "analysis_summarize",
            "route_allowed",
            "session-1"));
        DataAgentAnalysisToolHandler handler = new(orchestrator, published.Add, routeAccessor);

        string context = handler.Summarize("session-1");

        Assert.Multiple(() =>
        {
            Assert.That(orchestrator.SummarizeSessionIds, Is.EqualTo(new[] { "session-1" }));
            Assert.That(orchestrator.SummarizeRequests[0].RouteContext?.RouteId, Is.EqualTo("route-summary"));
            Assert.That(context, Does.Contain("status=Summarized"));
            Assert.That(context, Does.Contain("orchestration_trace=Summarize:Succeeded>Checkpoint:Succeeded"));
            Assert.That(context, Does.Contain("route_tool=dataagent_analysis_summarize"));
            Assert.That(published, Is.EqualTo(new[] { context }));
        });
    }

    [Test]
    public void EndCallsOrchestratorAndPublishesTerminalContext()
    {
        List<string> published = [];
        RecordingOrchestrator orchestrator = new(new Dictionary<string, DataAgentOrchestrationResult>
        {
            ["end"] = OrchestratedResult(
                "session-1",
                DataAgentAnalysisSessionStatus.Ended,
                DataAgentAnalysisTurnIntent.End,
                [
                    new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.End, DataAgentOrchestrationStepStatus.Succeeded, "terminal_end", false),
                    new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
                ],
                2,
                "[data_agent_analysis_session_context]\nsession_id=session-1\nstatus=Ended\n[/data_agent_analysis_session_context]")
        });
        RecordingRouteContextAccessor routeAccessor = new(new DataAgentToolRouteContext(
            true,
            "dataagent_analysis_end",
            true,
            true,
            "route-end",
            "analysis_end",
            "route_allowed",
            "session-1"));
        DataAgentAnalysisToolHandler handler = new(orchestrator, published.Add, routeAccessor);

        string context = handler.End("session-1");

        Assert.Multiple(() =>
        {
            Assert.That(orchestrator.EndSessionIds, Is.EqualTo(new[] { "session-1" }));
            Assert.That(orchestrator.EndRequests[0].RouteContext?.RouteId, Is.EqualTo("route-end"));
            Assert.That(context, Does.Contain("status=Ended"));
            Assert.That(context, Does.Contain("orchestration_trace=End:Succeeded>Checkpoint:Succeeded"));
            Assert.That(context, Does.Contain("route_tool=dataagent_analysis_end"));
            Assert.That(published, Is.EqualTo(new[] { context }));
        });
    }

    [Test]
    public void AnalysisMethodsAreRegisteredAsXmlFunctions()
    {
        XmlHandler xmlHandler = new(new DataAgentAnalysisToolHandler(CreateOrchestrator()));

        Assert.Multiple(() =>
        {
            Assert.That(xmlHandler.Functions.Select(function => function.Name), Is.EquivalentTo(new[]
            {
                "dataagent_analysis_start",
                "dataagent_analysis_continue",
                "dataagent_analysis_summarize",
                "dataagent_analysis_end"
            }));
            Assert.That(xmlHandler.Functions, Has.All.Matches<XmlFunction>(
                function => function.Mode == FunctionMode.OneShot));
            Assert.That(xmlHandler.FunctionDocument(), Does.Contain("<dataagent_analysis_start"));
            Assert.That(xmlHandler.FunctionDocument(), Does.Contain("<dataagent_analysis_continue"));
            Assert.That(xmlHandler.FunctionDocument(), Does.Contain("<dataagent_analysis_summarize"));
            Assert.That(xmlHandler.FunctionDocument(), Does.Contain("<dataagent_analysis_end"));
        });
    }

    [Test]
    public void AnalysisMethodsRejectBlankRequiredArguments()
    {
        DataAgentAnalysisToolHandler handler = new(CreateOrchestrator());

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => handler.Start(" ", "question"));
            Assert.Throws<ArgumentException>(() => handler.Start("caller", " "));
            Assert.Throws<ArgumentException>(() => handler.Continue(" ", "question"));
            Assert.Throws<ArgumentException>(() => handler.Continue("session", " "));
            Assert.Throws<ArgumentException>(() => handler.Summarize(" "));
            Assert.Throws<ArgumentException>(() => handler.End(" "));
        });
    }

    static RecordingOrchestrator CreateOrchestrator()
    {
        return new RecordingOrchestrator(new Dictionary<string, DataAgentOrchestrationResult>
        {
            ["start"] = OrchestratedResult("session-1", DataAgentAnalysisSessionStatus.Active, DataAgentAnalysisTurnIntent.NewQuestion, [], 1, string.Empty),
            ["continue"] = OrchestratedResult("session-1", DataAgentAnalysisSessionStatus.Active, DataAgentAnalysisTurnIntent.Continue, [], 2, string.Empty),
            ["summarize"] = OrchestratedResult("session-1", DataAgentAnalysisSessionStatus.Summarized, DataAgentAnalysisTurnIntent.Summarize, [], 2, string.Empty),
            ["end"] = OrchestratedResult("session-1", DataAgentAnalysisSessionStatus.Ended, DataAgentAnalysisTurnIntent.End, [], 2, string.Empty)
        });
    }

    static DataAgentAnalysisOrchestrator RealOrchestrator(
        IDataAgentAnalysisSessionStore store,
        Func<string, DataAgentAnswer> answer)
    {
        DataAgentAnalysisService analysisService = new(
            answer,
            store,
            new DataAgentFollowUpInterpreter(),
            () => new DateTimeOffset(2026, 6, 30, 12, 0, 0, TimeSpan.Zero));

        return new DataAgentAnalysisOrchestrator(
            analysisService,
            store,
            new DataAgentFollowUpInterpreter());
    }

    static DataAgentAnswer AcceptedAnswer()
    {
        return new DataAgentAnswer(
            "document_index",
            "SELECT path FROM document_index LIMIT 20",
            2,
            "Found DataAgent documentation.",
            "[data_agent_context]\nsql_status=validated\nresult_explanation=Found DataAgent documentation.\n[/data_agent_context]",
            true,
            string.Empty,
            new DataAgentPlannerExplanation(
                "TestPlanner",
                "find_documents",
                "document_index",
                "high",
                ["test"],
                "test accepted answer"));
    }

    static DataAgentOrchestrationResult OrchestratedResult(
        string sessionId,
        DataAgentAnalysisSessionStatus status,
        DataAgentAnalysisTurnIntent intent,
        IReadOnlyList<DataAgentOrchestrationStep> steps,
        int turnCount,
        string baseContext)
    {
        DataAgentAnalysisResponse response = new(
            sessionId,
            status,
            intent,
            null,
            string.Empty,
            baseContext,
            true,
            string.Empty);

        return new DataAgentOrchestrationResult(
            sessionId,
            status,
            steps,
            new DataAgentOrchestrationCheckpoint(
                sessionId,
                status,
                "document_index",
                turnCount,
                CanContinue: status != DataAgentAnalysisSessionStatus.Ended,
                CanSummarize: turnCount > 0 && status != DataAgentAnalysisSessionStatus.Ended,
                Terminal: status is DataAgentAnalysisSessionStatus.Summarized or DataAgentAnalysisSessionStatus.Ended),
            response);
    }

    sealed class RecordingOrchestrator : IDataAgentAnalysisOrchestrator
    {
        readonly Dictionary<string, DataAgentOrchestrationResult> results;

        public RecordingOrchestrator(Dictionary<string, DataAgentOrchestrationResult> results)
        {
            this.results = results;
        }

        public List<DataAgentOrchestrationRequest> StartRequests { get; } = [];
        public List<DataAgentOrchestrationRequest> ContinueRequests { get; } = [];
        public List<string> SummarizeSessionIds { get; } = [];
        public List<string> EndSessionIds { get; } = [];
        public List<(string SessionId, DataAgentToolRouteContext? RouteContext)> SummarizeRequests { get; } = [];
        public List<(string SessionId, DataAgentToolRouteContext? RouteContext)> EndRequests { get; } = [];

        public DataAgentOrchestrationResult Start(DataAgentOrchestrationRequest request)
        {
            StartRequests.Add(request);
            return results["start"] with { RouteContext = request.RouteContext };
        }

        public DataAgentOrchestrationResult Continue(DataAgentOrchestrationRequest request)
        {
            ContinueRequests.Add(request);
            return results["continue"] with { RouteContext = request.RouteContext };
        }

        public DataAgentOrchestrationResult Summarize(string sessionId, DataAgentToolRouteContext? routeContext = null)
        {
            SummarizeSessionIds.Add(sessionId);
            SummarizeRequests.Add((sessionId, routeContext));
            return results["summarize"] with { RouteContext = routeContext };
        }

        public DataAgentOrchestrationResult End(string sessionId, DataAgentToolRouteContext? routeContext = null)
        {
            EndSessionIds.Add(sessionId);
            EndRequests.Add((sessionId, routeContext));
            return results["end"] with { RouteContext = routeContext };
        }
    }

    sealed class RecordingRouteContextAccessor(DataAgentToolRouteContext routeContext) : IDataAgentToolRouteContextAccessor
    {
        public List<(string ToolName, string? SessionId)> Requests { get; } = [];

        public DataAgentToolRouteContext Get(string toolName, string? sessionId)
        {
            Requests.Add((toolName, sessionId));
            return routeContext with { ToolName = toolName };
        }
    }
}
