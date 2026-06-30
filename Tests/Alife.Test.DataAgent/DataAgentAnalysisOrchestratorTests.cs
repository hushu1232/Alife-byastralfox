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

    [Test]
    public void ContinueSummarizeDoesNotRequireRouteAndDoesNotExecuteSql()
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

        DataAgentOrchestrationResult summary = orchestrator.Continue(new DataAgentOrchestrationRequest(
            "owner",
            "\u603b\u7ed3\u4e00\u4e0b",
            start.SessionId,
            RouteAllowsQuery: false));

        Assert.Multiple(() =>
        {
            Assert.That(answerCalls, Is.EqualTo(1));
            Assert.That(summary.Response.Accepted, Is.True);
            Assert.That(summary.Response.Answer, Is.Null);
            Assert.That(summary.SessionStatus, Is.EqualTo(DataAgentAnalysisSessionStatus.Summarized));
            Assert.That(summary.Steps.Select(step => step.Node), Is.EqualTo(new[]
            {
                DataAgentOrchestrationNodeKind.Summarize,
                DataAgentOrchestrationNodeKind.Checkpoint
            }));
            Assert.That(summary.Steps.Any(step => step.ExecutedSql), Is.False);
            Assert.That(summary.Checkpoint.CanContinue, Is.True);
            Assert.That(summary.Checkpoint.Terminal, Is.False);
        });
    }

    [Test]
    public void ContinueEndDoesNotRequireRouteAndProducesTerminalCheckpoint()
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

        DataAgentOrchestrationResult end = orchestrator.Continue(new DataAgentOrchestrationRequest(
            "owner",
            "\u7ed3\u675f",
            start.SessionId,
            RouteAllowsQuery: false));

        Assert.Multiple(() =>
        {
            Assert.That(answerCalls, Is.EqualTo(1));
            Assert.That(end.Response.Accepted, Is.True);
            Assert.That(end.Response.Answer, Is.Null);
            Assert.That(end.SessionStatus, Is.EqualTo(DataAgentAnalysisSessionStatus.Ended));
            Assert.That(end.Steps.Select(step => step.Node), Is.EqualTo(new[]
            {
                DataAgentOrchestrationNodeKind.End,
                DataAgentOrchestrationNodeKind.Checkpoint
            }));
            Assert.That(end.Steps.Any(step => step.ExecutedSql), Is.False);
            Assert.That(end.Checkpoint.CanContinue, Is.False);
            Assert.That(end.Checkpoint.CanSummarize, Is.False);
            Assert.That(end.Checkpoint.Terminal, Is.True);
        });
    }

    [Test]
    public void SummarizeReturnsTerminalTraceWithoutRouteGateOrQuery()
    {
        DateTimeOffset now = new(2026, 6, 30, 12, 0, 0, TimeSpan.Zero);
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

        DataAgentOrchestrationResult summary = orchestrator.Summarize(start.SessionId);

        Assert.Multiple(() =>
        {
            Assert.That(answerCalls, Is.EqualTo(1));
            Assert.That(summary.Response.Accepted, Is.True);
            Assert.That(summary.Response.Answer, Is.Null);
            Assert.That(summary.SessionStatus, Is.EqualTo(DataAgentAnalysisSessionStatus.Summarized));
            Assert.That(summary.Steps.Select(step => step.Node), Is.EqualTo(new[]
            {
                DataAgentOrchestrationNodeKind.Summarize,
                DataAgentOrchestrationNodeKind.Checkpoint
            }));
            Assert.That(summary.Steps.Any(step => step.ExecutedSql), Is.False);
            Assert.That(summary.Checkpoint.SessionId, Is.EqualTo(start.SessionId));
            Assert.That(summary.Checkpoint.TurnCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void EndReturnsTerminalTraceWithoutRouteGateOrQuery()
    {
        DateTimeOffset now = new(2026, 6, 30, 12, 0, 0, TimeSpan.Zero);
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

        DataAgentOrchestrationResult end = orchestrator.End(start.SessionId);

        Assert.Multiple(() =>
        {
            Assert.That(answerCalls, Is.EqualTo(1));
            Assert.That(end.Response.Accepted, Is.True);
            Assert.That(end.Response.Answer, Is.Null);
            Assert.That(end.SessionStatus, Is.EqualTo(DataAgentAnalysisSessionStatus.Ended));
            Assert.That(end.Steps.Select(step => step.Node), Is.EqualTo(new[]
            {
                DataAgentOrchestrationNodeKind.End,
                DataAgentOrchestrationNodeKind.Checkpoint
            }));
            Assert.That(end.Steps.Any(step => step.ExecutedSql), Is.False);
            Assert.That(end.Checkpoint.CanContinue, Is.False);
            Assert.That(end.Checkpoint.CanSummarize, Is.False);
            Assert.That(end.Checkpoint.Terminal, Is.True);
        });
    }

    [Test]
    public void ClarificationBranchRecordsClarificationWithoutExecute()
    {
        DateTimeOffset now = new(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);
        InMemoryDataAgentAnalysisSessionStore store = new();
        DataAgentAnalysisOrchestrator orchestrator = Orchestrator(
            store,
            _ => ClarificationAnswer(),
            now);

        DataAgentOrchestrationResult result = orchestrator.Start(new DataAgentOrchestrationRequest(
            "owner",
            "Show status",
            null,
            RouteAllowsQuery: true));

        Assert.Multiple(() =>
        {
            Assert.That(result.SessionStatus, Is.EqualTo(DataAgentAnalysisSessionStatus.AwaitingClarification));
            Assert.That(result.Response.Answer?.RejectedReason, Is.EqualTo("needs_clarification"));
            Assert.That(result.Steps.Select(step => step.Node), Is.EqualTo(new[]
            {
                DataAgentOrchestrationNodeKind.RouteGate,
                DataAgentOrchestrationNodeKind.SchemaContext,
                DataAgentOrchestrationNodeKind.Plan,
                DataAgentOrchestrationNodeKind.Validate,
                DataAgentOrchestrationNodeKind.Clarification,
                DataAgentOrchestrationNodeKind.Checkpoint
            }));
            Assert.That(result.Steps.Single(step => step.Node == DataAgentOrchestrationNodeKind.Validate).Status, Is.EqualTo(DataAgentOrchestrationStepStatus.Skipped));
            Assert.That(result.Steps.Single(step => step.Node == DataAgentOrchestrationNodeKind.Validate).Reason, Is.EqualTo("needs_clarification"));
            Assert.That(result.Steps.Single(step => step.Node == DataAgentOrchestrationNodeKind.Validate).ExecutedSql, Is.False);
            Assert.That(result.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.Execute), Is.False);
            Assert.That(result.Checkpoint.SessionStatus, Is.EqualTo(DataAgentAnalysisSessionStatus.AwaitingClarification));
            Assert.That(result.Checkpoint.CanContinue, Is.True);
        });
    }

    [Test]
    public void RejectedPlannerOutputRecordsRejectWithoutExecute()
    {
        DateTimeOffset now = new(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);
        InMemoryDataAgentAnalysisSessionStore store = new();
        DataAgentAnalysisOrchestrator orchestrator = Orchestrator(
            store,
            _ => RejectedAnswer(),
            now);

        DataAgentOrchestrationResult result = orchestrator.Start(new DataAgentOrchestrationRequest(
            "owner",
            "Use unsafe planner output",
            null,
            RouteAllowsQuery: true));

        Assert.Multiple(() =>
        {
            Assert.That(result.Response.Answer?.Validated, Is.False);
            Assert.That(result.Response.Answer?.RejectedReason, Is.EqualTo("unsupported_operator:starts_with"));
            Assert.That(result.Steps.Select(step => step.Node), Is.EqualTo(new[]
            {
                DataAgentOrchestrationNodeKind.RouteGate,
                DataAgentOrchestrationNodeKind.SchemaContext,
                DataAgentOrchestrationNodeKind.Plan,
                DataAgentOrchestrationNodeKind.Validate,
                DataAgentOrchestrationNodeKind.Reject,
                DataAgentOrchestrationNodeKind.Checkpoint
            }));
            Assert.That(result.Steps.Single(step => step.Node == DataAgentOrchestrationNodeKind.Validate).Status, Is.EqualTo(DataAgentOrchestrationStepStatus.Rejected));
            Assert.That(result.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.Execute), Is.False);
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

    static DataAgentAnswer ClarificationAnswer()
    {
        return new DataAgentAnswer(
            string.Empty,
            string.Empty,
            0,
            "Which dataset should I use?",
            "[data_agent_context]\nsql_status=needs_clarification\nclarification_question=Which dataset should I use?\n[/data_agent_context]",
            false,
            "needs_clarification",
            new DataAgentPlannerExplanation(
                "TestPlanner",
                "clarify",
                string.Empty,
                "low",
                ["ambiguous_dataset"],
                "ambiguous dataset"));
    }

    static DataAgentAnswer RejectedAnswer()
    {
        return new DataAgentAnswer(
            "engineering_gate",
            string.Empty,
            0,
            "DataAgent query rejected: unsupported_operator:starts_with",
            "[data_agent_context]\nsql_status=rejected\nrejected_reason=unsupported_operator:starts_with\n[/data_agent_context]",
            false,
            "unsupported_operator:starts_with",
            new DataAgentPlannerExplanation(
                "TestPlanner",
                "unsafe",
                "engineering_gate",
                "low",
                ["unsafe_operator"],
                "unsupported operator"));
    }
}
