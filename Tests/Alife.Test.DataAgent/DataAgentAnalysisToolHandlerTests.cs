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
        DataAgentAnalysisToolHandler handler = new(orchestrator, published.Add, routeAccessor);

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
        DataAgentAnalysisToolHandler handler = new(orchestrator, published.Add);

        string context = handler.Summarize("session-1");

        Assert.Multiple(() =>
        {
            Assert.That(orchestrator.SummarizeSessionIds, Is.EqualTo(new[] { "session-1" }));
            Assert.That(context, Does.Contain("status=Summarized"));
            Assert.That(context, Does.Contain("orchestration_trace=Summarize:Succeeded>Checkpoint:Succeeded"));
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
        DataAgentAnalysisToolHandler handler = new(orchestrator, published.Add);

        string context = handler.End("session-1");

        Assert.Multiple(() =>
        {
            Assert.That(orchestrator.EndSessionIds, Is.EqualTo(new[] { "session-1" }));
            Assert.That(context, Does.Contain("status=Ended"));
            Assert.That(context, Does.Contain("orchestration_trace=End:Succeeded>Checkpoint:Succeeded"));
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
                Terminal: status == DataAgentAnalysisSessionStatus.Ended),
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

        public DataAgentOrchestrationResult Summarize(string sessionId)
        {
            SummarizeSessionIds.Add(sessionId);
            return results["summarize"];
        }

        public DataAgentOrchestrationResult End(string sessionId)
        {
            EndSessionIds.Add(sessionId);
            return results["end"];
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
