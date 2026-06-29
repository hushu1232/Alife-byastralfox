using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentAnalysisOrchestratorTests
{
    [Test]
    public void StartAcceptedAnalysisRecordsQueryNodesAndCheckpoint()
    {
        DateTimeOffset now = new(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);
        int answerCalls = 0;
        InMemoryDataAgentAnalysisSessionStore store = new();
        DataAgentAnalysisOrchestrator orchestrator = Orchestrator(
            store,
            _ =>
            {
                answerCalls++;
                return AcceptedAnswer();
            },
            now);

        DataAgentOrchestrationResult result = orchestrator.Start(new DataAgentOrchestrationRequest(
            "owner",
            "Which documents describe DataAgent?",
            null,
            RouteAllowsQuery: true));

        Assert.Multiple(() =>
        {
            Assert.That(answerCalls, Is.EqualTo(1));
            Assert.That(result.Response.Accepted, Is.True);
            Assert.That(result.SessionId, Is.Not.Empty);
            Assert.That(result.SessionStatus, Is.EqualTo(DataAgentAnalysisSessionStatus.Active));
            Assert.That(result.Steps.Select(step => step.Node), Is.EqualTo(new[]
            {
                DataAgentOrchestrationNodeKind.RouteGate,
                DataAgentOrchestrationNodeKind.SchemaContext,
                DataAgentOrchestrationNodeKind.Plan,
                DataAgentOrchestrationNodeKind.Validate,
                DataAgentOrchestrationNodeKind.Execute,
                DataAgentOrchestrationNodeKind.Explain,
                DataAgentOrchestrationNodeKind.Checkpoint
            }));
            Assert.That(result.Steps.Single(step => step.Node == DataAgentOrchestrationNodeKind.Execute).ExecutedSql, Is.True);
            Assert.That(result.Checkpoint.SessionId, Is.EqualTo(result.SessionId));
            Assert.That(result.Checkpoint.SessionStatus, Is.EqualTo(DataAgentAnalysisSessionStatus.Active));
            Assert.That(result.Checkpoint.LastDataset, Is.EqualTo("document_index"));
            Assert.That(result.Checkpoint.TurnCount, Is.EqualTo(1));
            Assert.That(result.Checkpoint.CanContinue, Is.True);
            Assert.That(result.Checkpoint.CanSummarize, Is.True);
            Assert.That(result.Checkpoint.Terminal, Is.False);
        });
    }

    [Test]
    public void StartRouteDeniedFailsClosedWithoutCallingAnswer()
    {
        DateTimeOffset now = new(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);
        int answerCalls = 0;
        InMemoryDataAgentAnalysisSessionStore store = new();
        DataAgentAnalysisOrchestrator orchestrator = Orchestrator(
            store,
            _ =>
            {
                answerCalls++;
                return AcceptedAnswer();
            },
            now);

        DataAgentOrchestrationResult result = orchestrator.Start(new DataAgentOrchestrationRequest(
            "owner",
            "Which documents describe DataAgent?",
            null,
            RouteAllowsQuery: false));

        Assert.Multiple(() =>
        {
            Assert.That(answerCalls, Is.Zero);
            Assert.That(result.Response.Accepted, Is.False);
            Assert.That(result.Response.RejectedReason, Is.EqualTo("tool_route_required"));
            Assert.That(result.SessionStatus, Is.EqualTo(DataAgentAnalysisSessionStatus.Rejected));
            Assert.That(result.Steps.Select(step => step.Node), Is.EqualTo(new[]
            {
                DataAgentOrchestrationNodeKind.RouteGate,
                DataAgentOrchestrationNodeKind.Reject,
                DataAgentOrchestrationNodeKind.Checkpoint
            }));
            Assert.That(result.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.Execute), Is.False);
            Assert.That(result.Checkpoint.Terminal, Is.True);
            Assert.That(result.Checkpoint.TurnCount, Is.Zero);
        });
    }

    [Test]
    public void ContinueRouteDeniedForQueryTurnDoesNotExecuteSqlOrMutateSession()
    {
        DateTimeOffset now = new(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);
        int answerCalls = 0;
        InMemoryDataAgentAnalysisSessionStore store = new();
        DataAgentAnalysisOrchestrator orchestrator = Orchestrator(
            store,
            _ =>
            {
                answerCalls++;
                return AcceptedAnswer();
            },
            now);
        DataAgentOrchestrationResult start = orchestrator.Start(new DataAgentOrchestrationRequest(
            "owner",
            "Which documents describe DataAgent?",
            null,
            RouteAllowsQuery: true));

        DataAgentOrchestrationResult denied = orchestrator.Continue(new DataAgentOrchestrationRequest(
            "owner",
            "\u7ee7\u7eed",
            start.SessionId,
            RouteAllowsQuery: false));
        DataAgentAnalysisSession session = store.Get(start.SessionId)!;

        Assert.Multiple(() =>
        {
            Assert.That(answerCalls, Is.EqualTo(1));
            Assert.That(denied.Response.Accepted, Is.False);
            Assert.That(denied.Response.RejectedReason, Is.EqualTo("tool_route_required"));
            Assert.That(denied.Response.Intent, Is.EqualTo(DataAgentAnalysisTurnIntent.Continue));
            Assert.That(denied.SessionStatus, Is.EqualTo(DataAgentAnalysisSessionStatus.Active));
            Assert.That(denied.Steps.Select(step => step.Node), Is.EqualTo(new[]
            {
                DataAgentOrchestrationNodeKind.RouteGate,
                DataAgentOrchestrationNodeKind.Reject,
                DataAgentOrchestrationNodeKind.Checkpoint
            }));
            Assert.That(denied.Steps.Select(step => step.Status), Is.EqualTo(new[]
            {
                DataAgentOrchestrationStepStatus.Rejected,
                DataAgentOrchestrationStepStatus.Rejected,
                DataAgentOrchestrationStepStatus.Succeeded
            }));
            Assert.That(denied.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.Execute), Is.False);
            Assert.That(denied.Checkpoint.SessionId, Is.EqualTo(start.SessionId));
            Assert.That(denied.Checkpoint.SessionStatus, Is.EqualTo(DataAgentAnalysisSessionStatus.Active));
            Assert.That(denied.Checkpoint.TurnCount, Is.EqualTo(1));
            Assert.That(denied.Checkpoint.CanContinue, Is.True);
            Assert.That(denied.Checkpoint.Terminal, Is.False);
            Assert.That(session.Turns, Has.Count.EqualTo(1));
        });
    }

    static DataAgentAnalysisOrchestrator Orchestrator(
        IDataAgentAnalysisSessionStore store,
        Func<string, DataAgentAnswer> answer,
        DateTimeOffset now)
    {
        DataAgentAnalysisService analysisService = new(
            answer,
            store,
            new DataAgentFollowUpInterpreter(),
            () => now);

        return new DataAgentAnalysisOrchestrator(
            analysisService,
            store,
            new DataAgentFollowUpInterpreter());
    }

    static DataAgentAnswer AcceptedAnswer(string summary = "Found DataAgent documentation.")
    {
        return new DataAgentAnswer(
            "document_index",
            "SELECT path FROM document_index LIMIT 20",
            2,
            summary,
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
}
