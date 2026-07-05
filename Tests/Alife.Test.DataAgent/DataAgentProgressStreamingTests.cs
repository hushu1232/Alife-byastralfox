using Alife.Function.DataAgent;
using NUnit.Framework;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentProgressStreamingTests
{
    [Test]
    public void RouteRejectionPublishesRouteGateAndReject()
    {
        DateTimeOffset now = Now();
        DataAgentProgressRecorder progress = new();
        InMemoryDataAgentAnalysisSessionStore store = new();
        DataAgentAnalysisService analysisService = new(
            _ => AcceptedAnswer(),
            store,
            progressSink: progress,
            clock: () => now);
        DataAgentAnalysisOrchestrator orchestrator = new(
            analysisService,
            store,
            progressSink: progress,
            progressClock: () => now);

        DataAgentOrchestrationResult result = orchestrator.Start(new DataAgentOrchestrationRequest(
            "owner",
            "Which required gates are passing?",
            null,
            RouteAllowsQuery: false));

        IReadOnlyList<DataAgentProgressEvent> events = progress.GetRecent("pending", now);

        Assert.Multiple(() =>
        {
            Assert.That(result.Response.Accepted, Is.False);
            Assert.That(events.Select(item => (item.Kind, item.Phase, item.Status)), Is.EqualTo(new[]
            {
                (DataAgentProgressEventKind.RouteGate, DataAgentProgressEventPhase.Completed, DataAgentProgressEventStatus.Rejected),
                (DataAgentProgressEventKind.Reject, DataAgentProgressEventPhase.Completed, DataAgentProgressEventStatus.Rejected),
                (DataAgentProgressEventKind.Checkpoint, DataAgentProgressEventPhase.Completed, DataAgentProgressEventStatus.Succeeded)
            }));
            Assert.That(events.All(item => item.QueryAllowed == false), Is.True);
            Assert.That(events.Any(item => item.ExecutedSql), Is.False);
        });
    }

    [Test]
    public void AcceptedQueryPublishesRuntimeBoundariesAndDoesNotCallAnswerTwice()
    {
        DateTimeOffset now = Now();
        DataAgentProgressRecorder progress = new();
        int plannerCalls = 0;
        RecordingStore dataStore = new(new DataAgentQueryResult([
            new Dictionary<string, object?>
            {
                ["name"] = "Runtime readiness script",
                ["status"] = "passed",
                ["evidence_path"] = "tools/check-qchat-runtime-readiness.ps1"
            }
        ]));
        CountingPlanner planner = new(ValidPlan(), () => plannerCalls++);
        DataAgentService dataAgentService = new(dataStore, planner);
        InMemoryDataAgentAnalysisSessionStore store = new();
        DataAgentAnalysisService analysisService = new(
            dataAgentService,
            store,
            progressSink: progress,
            clock: () => now);
        DataAgentAnalysisOrchestrator orchestrator = new(
            analysisService,
            store,
            progressSink: progress,
            progressClock: () => now);

        DataAgentOrchestrationResult result = orchestrator.Start(new DataAgentOrchestrationRequest(
            "owner",
            "Which required gates are passing?",
            null,
            RouteAllowsQuery: true,
            RouteContext: RouteAllowed("dataagent_analysis_start", null)));

        IReadOnlyList<DataAgentProgressEvent> events = progress.GetRecent(result.SessionId, now);

        Assert.Multiple(() =>
        {
            Assert.That(plannerCalls, Is.EqualTo(1));
            Assert.That(dataStore.Queries, Has.Count.EqualTo(1));
            Assert.That(events.Select(item => item.Kind), Does.Contain(DataAgentProgressEventKind.RouteGate));
            Assert.That(events.Select(item => item.Kind), Does.Contain(DataAgentProgressEventKind.SchemaContext));
            Assert.That(events.Select(item => item.Kind), Does.Contain(DataAgentProgressEventKind.Planner));
            Assert.That(events.Select(item => item.Kind), Does.Contain(DataAgentProgressEventKind.Validate));
            Assert.That(events.Select(item => item.Kind), Does.Contain(DataAgentProgressEventKind.SqlSafety));
            Assert.That(events.Select(item => item.Kind), Does.Contain(DataAgentProgressEventKind.Execute));
            Assert.That(events.Select(item => item.Kind), Does.Contain(DataAgentProgressEventKind.Explain));
            Assert.That(events.Select(item => item.Kind), Does.Contain(DataAgentProgressEventKind.Checkpoint));
            Assert.That(events.Any(item => item.Kind == DataAgentProgressEventKind.Execute && item.ExecutedSql), Is.True);
            Assert.That(events.Any(item => item.Facts.Values.Any(value => value.Contains("SELECT", StringComparison.OrdinalIgnoreCase))), Is.False);
        });
    }

    [Test]
    public void ClarificationPublishesPlannerValidateSkippedAndClarification()
    {
        DateTimeOffset now = Now();
        DataAgentProgressRecorder progress = new();
        RecordingStore dataStore = new(new DataAgentQueryResult([]));
        DataAgentService dataAgentService = new(dataStore, new ClarificationPlanner());
        InMemoryDataAgentAnalysisSessionStore store = new();
        DataAgentAnalysisService analysisService = new(
            dataAgentService,
            store,
            progressSink: progress,
            clock: () => now);
        DataAgentAnalysisOrchestrator orchestrator = new(
            analysisService,
            store,
            progressSink: progress,
            progressClock: () => now);

        DataAgentOrchestrationResult result = orchestrator.Start(new DataAgentOrchestrationRequest(
            "owner",
            "Show status",
            null,
            RouteAllowsQuery: true));

        IReadOnlyList<DataAgentProgressEvent> events = progress.GetRecent(result.SessionId, now);

        Assert.Multiple(() =>
        {
            Assert.That(dataStore.Queries, Is.Empty);
            Assert.That(events.Any(item =>
                item.Kind == DataAgentProgressEventKind.Planner &&
                item.Phase == DataAgentProgressEventPhase.Completed &&
                item.Status == DataAgentProgressEventStatus.Succeeded), Is.True);
            Assert.That(events.Any(item =>
                item.Kind == DataAgentProgressEventKind.Validate &&
                item.Phase == DataAgentProgressEventPhase.Completed &&
                item.Status == DataAgentProgressEventStatus.Skipped), Is.True);
            Assert.That(events.Any(item =>
                item.Kind == DataAgentProgressEventKind.Clarification &&
                item.Phase == DataAgentProgressEventPhase.Completed &&
                item.Status == DataAgentProgressEventStatus.Succeeded), Is.True);
            Assert.That(events.Any(item => item.Kind == DataAgentProgressEventKind.Execute), Is.False);
        });
    }

    [Test]
    public void SummarizeAndEndPublishTerminalProgressWithoutExecute()
    {
        DateTimeOffset now = Now();
        DataAgentProgressRecorder progress = new();
        InMemoryDataAgentAnalysisSessionStore store = new();
        DataAgentAnalysisService analysisService = new(
            _ => AcceptedAnswer(),
            store,
            progressSink: progress,
            clock: () => now);
        DataAgentAnalysisOrchestrator orchestrator = new(
            analysisService,
            store,
            progressSink: progress,
            progressClock: () => now);
        DataAgentOrchestrationResult start = orchestrator.Start(new DataAgentOrchestrationRequest(
            "owner",
            "Which documents describe DataAgent?",
            null,
            RouteAllowsQuery: true));
        int beforeSummaryCount = progress.GetRecent(start.SessionId, now).Count;

        orchestrator.Summarize(start.SessionId, RouteAllowed("dataagent_analysis_summarize", start.SessionId));
        IReadOnlyList<DataAgentProgressEvent> summaryEvents = progress.GetRecent(start.SessionId, now)
            .Skip(beforeSummaryCount)
            .ToArray();
        int beforeEndCount = progress.GetRecent(start.SessionId, now).Count;

        orchestrator.End(start.SessionId, RouteAllowed("dataagent_analysis_end", start.SessionId));
        IReadOnlyList<DataAgentProgressEvent> endEvents = progress.GetRecent(start.SessionId, now)
            .Skip(beforeEndCount)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(summaryEvents.Any(item =>
                item.Kind == DataAgentProgressEventKind.Summarize &&
                item.Terminal == true), Is.True);
            Assert.That(summaryEvents.Any(item => item.Kind == DataAgentProgressEventKind.Execute), Is.False);
            Assert.That(endEvents.Any(item =>
                item.Kind == DataAgentProgressEventKind.End &&
                item.Terminal == true), Is.True);
            Assert.That(endEvents.Any(item => item.Kind == DataAgentProgressEventKind.Execute), Is.False);
        });
    }

    static DataAgentAnswer AcceptedAnswer()
    {
        return new DataAgentAnswer(
            "document_index",
            "SELECT path FROM document_index LIMIT 20",
            2,
            "Found DataAgent documentation.",
            "[data_agent_context]\nsql_status=validated\n[/data_agent_context]",
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

    static DataAgentQueryPlan ValidPlan()
    {
        return new DataAgentQueryPlan(
            "engineering_gate",
            "find_runtime_readiness_required_evidence",
            ["name", "status", "evidence_path"],
            [new DataAgentFilter("required", "=", true)],
            [],
            20);
    }

    static DataAgentToolRouteContext RouteAllowed(string toolName, string? sessionId)
    {
        return new DataAgentToolRouteContext(
            true,
            toolName,
            true,
            true,
            "route-test",
            "analysis_continue",
            "route_allowed",
            sessionId ?? string.Empty);
    }

    static DateTimeOffset Now()
    {
        return new DateTimeOffset(2026, 7, 3, 10, 0, 0, TimeSpan.Zero);
    }

    sealed class RecordingStore(DataAgentQueryResult queryResult) : IDataAgentStore
    {
        public List<DataAgentCompiledSql> Queries { get; } = [];
        public List<DataAgentAcceptedAuditInput> AcceptedAudits { get; } = [];
        public List<DataAgentRejectedAuditInput> RejectedAudits { get; } = [];
        public List<DataAgentToolBrokerAuditRecord> ToolBrokerAudits { get; } = [];
        public string ProviderName => "recording";

        public void Initialize() { }
        public void ImportFixtures() { }

        public DataAgentQueryResult Query(DataAgentCompiledSql compiledSql)
        {
            Queries.Add(compiledSql);
            return queryResult;
        }

        public void RecordAccepted(DataAgentAcceptedAuditInput input)
        {
            AcceptedAudits.Add(input);
        }

        public void RecordRejected(DataAgentRejectedAuditInput input)
        {
            RejectedAudits.Add(input);
        }

        public IReadOnlyList<DataAgentAuditRecord> ReadQueryAudit()
        {
            return [];
        }

        public void RecordToolBrokerAudit(DataAgentToolBrokerAuditRecord record)
        {
            ToolBrokerAudits.Add(record);
        }

        public IReadOnlyList<DataAgentToolBrokerAuditRecord> ReadToolBrokerAudit()
        {
            return ToolBrokerAudits;
        }
    }

    sealed class CountingPlanner(DataAgentQueryPlan plan, Action onPlan) : IDataAgentQueryPlanner
    {
        public DataAgentQueryPlanEnvelope Plan(DataAgentQueryRequest request)
        {
            onPlan();
            return new DataAgentQueryPlanEnvelope(
                plan,
                new DataAgentPlannerExplanation(
                    nameof(CountingPlanner),
                    plan.Intent,
                    plan.Dataset,
                    "high",
                    ["progress-test"],
                    "progress test plan"));
        }
    }

    sealed class ClarificationPlanner : IDataAgentQueryPlanner
    {
        public DataAgentQueryPlanEnvelope Plan(DataAgentQueryRequest request)
        {
            return new DataAgentQueryPlanEnvelope(
                null,
                new DataAgentPlannerExplanation(
                    nameof(ClarificationPlanner),
                    "clarify_ambiguous_query",
                    string.Empty,
                    "low",
                    ["ambiguous"],
                    "question is ambiguous"),
                new DataAgentClarificationRequest(
                    "Which dataset should I use?",
                    ["engineering gates", "documents"],
                    "question is ambiguous"));
        }
    }
}
