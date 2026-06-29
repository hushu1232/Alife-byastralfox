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
