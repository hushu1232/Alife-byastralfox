using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentOrchestrationContextProviderTests
{
    [Test]
    public void BuildAppendsTraceAndCheckpointFieldsToResponseContext()
    {
        DataAgentOrchestrationResult result = Result(
            "[data_agent_analysis_session_context]\nsession_id=session-1\nturn_count=1\n[/data_agent_analysis_session_context]",
            [
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Succeeded, "route_allowed", false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Execute, DataAgentOrchestrationStepStatus.Succeeded, "read_only_query_executed", true),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
            ],
            new DataAgentOrchestrationCheckpoint("session-1", DataAgentAnalysisSessionStatus.Active, "document_index", 1, true, true, false));

        string context = DataAgentOrchestrationContextProvider.Build(result);

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Contain("[data_agent_analysis_session_context]"));
            Assert.That(context, Does.Contain("orchestration_trace=RouteGate:Succeeded>Execute:Succeeded>Checkpoint:Succeeded"));
            Assert.That(context, Does.Contain("checkpoint_session_id=session-1"));
            Assert.That(context, Does.Contain("checkpoint_status=Active"));
            Assert.That(context, Does.Contain("checkpoint_turn_count=1"));
            Assert.That(context, Does.Contain("checkpoint_can_continue=true"));
            Assert.That(context, Does.Contain("checkpoint_can_summarize=true"));
            Assert.That(context, Does.Contain("checkpoint_terminal=false"));
        });
    }

    [Test]
    public void BuildReturnsTraceAndCheckpointFieldsWhenResponseContextIsEmpty()
    {
        DataAgentOrchestrationResult result = Result(
            string.Empty,
            [
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Rejected, "tool_route_required", false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Reject, DataAgentOrchestrationStepStatus.Rejected, "tool_route_required", false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
            ],
            new DataAgentOrchestrationCheckpoint(string.Empty, DataAgentAnalysisSessionStatus.Rejected, string.Empty, 0, false, false, true));

        string context = DataAgentOrchestrationContextProvider.Build(result);

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Contain("orchestration_trace=RouteGate:Rejected>Reject:Rejected>Checkpoint:Succeeded"));
            Assert.That(context, Does.Contain("checkpoint_status=Rejected"));
            Assert.That(context, Does.Contain("checkpoint_terminal=true"));
            Assert.That(context, Does.Not.Contain("tool_route_required"));
        });
    }

    [Test]
    public void BuildAppendsSanitizedRouteEvidenceWhenPresent()
    {
        DataAgentToolRouteContext routeContext = new(
            true,
            "dataagent_analysis_continue",
            true,
            true,
            "route\nunsafe",
            "analysis_continue",
            "route_allowed",
            "session-1");
        DataAgentOrchestrationResult result = Result(
            "[data_agent_analysis_session_context]\nsession_id=session-1\n[/data_agent_analysis_session_context]",
            [
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Succeeded, "route_allowed", false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
            ],
            new DataAgentOrchestrationCheckpoint("session-1", DataAgentAnalysisSessionStatus.Active, "document_index", 2, true, true, false),
            routeContext);

        string context = DataAgentOrchestrationContextProvider.Build(result);

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Contain("route_present=true"));
            Assert.That(context, Does.Contain("route_tool=dataagent_analysis_continue"));
            Assert.That(context, Does.Contain("route_allows_tool=true"));
            Assert.That(context, Does.Contain("route_allows_query=true"));
            Assert.That(context, Does.Contain("route_id=route unsafe"));
            Assert.That(context, Does.Contain("route_intent=analysis_continue"));
            Assert.That(context, Does.Contain("route_reason_code=route_allowed"));
            Assert.That(context, Does.Contain("route_session_id=session-1"));
        });
    }

    static DataAgentOrchestrationResult Result(
        string context,
        IReadOnlyList<DataAgentOrchestrationStep> steps,
        DataAgentOrchestrationCheckpoint checkpoint,
        DataAgentToolRouteContext? routeContext = null)
    {
        DataAgentAnalysisResponse response = new(
            checkpoint.SessionId,
            checkpoint.SessionStatus,
            DataAgentAnalysisTurnIntent.NewQuestion,
            null,
            string.Empty,
            context,
            checkpoint.SessionStatus != DataAgentAnalysisSessionStatus.Rejected,
            checkpoint.SessionStatus == DataAgentAnalysisSessionStatus.Rejected ? "tool_route_required" : string.Empty);

        return new DataAgentOrchestrationResult(
            checkpoint.SessionId,
            checkpoint.SessionStatus,
            steps,
            checkpoint,
            response,
            routeContext);
    }
}
