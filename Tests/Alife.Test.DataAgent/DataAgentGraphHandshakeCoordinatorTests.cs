using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentGraphHandshakeCoordinatorTests
{
    [Test]
    public void DisabledCoordinatorReturnsFallbackWithoutCallingSidecar()
    {
        RecordingSidecarClient sidecar = new(NewAcceptedResponse);
        DataAgentGraphHandshakeCoordinator coordinator = new(DataAgentGraphHandshakeOptions.Disabled, sidecar);

        DataAgentGraphHandshakeOutcome outcome = coordinator.TryHandshake(
            "owner",
            "Which gates failed?",
            AcceptedResult());

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Disabled));
            Assert.That(outcome.ReasonCode, Is.EqualTo("sidecar_disabled"));
            Assert.That(outcome.FallbackRequired, Is.True);
            Assert.That(sidecar.Requests, Is.Empty);
        });
    }

    [Test]
    public void EnabledCoordinatorAcceptsSafeSidecarResponseWithoutChangingDeterministicResult()
    {
        RecordingSidecarClient sidecar = new(NewAcceptedResponse);
        DataAgentGraphHandshakeCoordinator coordinator = new(new DataAgentGraphHandshakeOptions(true), sidecar);
        DataAgentOrchestrationResult deterministicResult = AcceptedResult();

        DataAgentGraphHandshakeOutcome outcome = coordinator.TryHandshake(
            "owner",
            "Which gates failed?",
            deterministicResult);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Accepted));
            Assert.That(outcome.FallbackRequired, Is.False);
            Assert.That(outcome.Request?.NoSqlAuthority, Is.True);
            Assert.That(outcome.Response?.ContextContribution, Does.Contain("graph_handshake=accepted"));
            Assert.That(sidecar.Requests, Has.Count.EqualTo(1));
            Assert.That(deterministicResult.Response.Accepted, Is.True);
            Assert.That(deterministicResult.Steps.Any(step => step.ExecutedSql), Is.True);
        });
    }

    [Test]
    public void EnabledCoordinatorRejectsUnsafeResponseAndRequiresFallback()
    {
        RecordingSidecarClient sidecar = new(request => NewAcceptedResponse(request) with
        {
            NoSqlAuthority = false,
            TraceSummary = "SELECT * FROM document_index"
        });
        DataAgentGraphHandshakeCoordinator coordinator = new(new DataAgentGraphHandshakeOptions(true), sidecar);

        DataAgentGraphHandshakeOutcome outcome = coordinator.TryHandshake(
            "owner",
            "Which gates failed?",
            AcceptedResult());

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Rejected));
            Assert.That(outcome.ReasonCode, Is.EqualTo("sql_authority_requested"));
            Assert.That(outcome.FallbackRequired, Is.True);
        });
    }

    [Test]
    public void EnabledCoordinatorHandlesUnavailableAndTimeoutSidecarWithoutThrowing()
    {
        DataAgentGraphHandshakeCoordinator unavailableCoordinator = new(
            new DataAgentGraphHandshakeOptions(true),
            new ThrowingSidecarClient(new InvalidOperationException("sidecar offline")));
        DataAgentGraphHandshakeCoordinator timeoutCoordinator = new(
            new DataAgentGraphHandshakeOptions(true),
            new ThrowingSidecarClient(new TimeoutException("sidecar timeout")));

        DataAgentGraphHandshakeOutcome unavailable = unavailableCoordinator.TryHandshake(
            "owner",
            "Which gates failed?",
            AcceptedResult());
        DataAgentGraphHandshakeOutcome timeout = timeoutCoordinator.TryHandshake(
            "owner",
            "Which gates failed?",
            AcceptedResult());

        Assert.Multiple(() =>
        {
            Assert.That(unavailable.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Unavailable));
            Assert.That(unavailable.ReasonCode, Is.EqualTo("sidecar_unavailable"));
            Assert.That(unavailable.FallbackRequired, Is.True);
            Assert.That(timeout.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Timeout));
            Assert.That(timeout.ReasonCode, Is.EqualTo("sidecar_timeout"));
            Assert.That(timeout.FallbackRequired, Is.True);
        });
    }

    static DataAgentGraphHandshakeResponse NewAcceptedResponse(DataAgentGraphHandshakeRequest request)
    {
        return new DataAgentGraphHandshakeResponse(
            request.RequestId,
            Accepted: true,
            ReasonCode: "handshake_accepted",
            SelectedNodes: [DataAgentWorkflowNodeNames.ScenarioKnowledge, DataAgentWorkflowNodeNames.QueryPlanner],
            NodeProgress:
            [
                new DataAgentGraphHandshakeProgress(DataAgentWorkflowNodeNames.QueryPlanner, DataAgentGraphHandshakeProgressStatus.Completed, "planner_suggested")
            ],
            TraceSummary: "ScenarioKnowledge:Completed>QueryPlanner:Completed",
            ContextContribution: "graph_handshake=accepted",
            FallbackRequired: false,
            NoSqlAuthority: true,
            ReadOnly: true,
            RequestedToolNames: [DataAgentGraphHandshakeToolNames.ProposeQueryPlan],
            RequestsCheckpointMutation: false,
            RequestsVisibleText: false);
    }

    static DataAgentOrchestrationResult AcceptedResult()
    {
        DataAgentAnswer answer = new(
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
        DataAgentAnalysisResponse response = new(
            "session-1",
            DataAgentAnalysisSessionStatus.Active,
            DataAgentAnalysisTurnIntent.NewQuestion,
            answer,
            answer.Summary,
            answer.Context,
            Accepted: true,
            RejectedReason: string.Empty);
        DataAgentOrchestrationCheckpoint checkpoint = new(
            "session-1",
            DataAgentAnalysisSessionStatus.Active,
            "document_index",
            TurnCount: 1,
            CanContinue: true,
            CanSummarize: true,
            Terminal: false);
        DataAgentToolRouteContext routeContext = new(
            true,
            "dataagent_analysis_start",
            true,
            true,
            "route-test",
            "analysis_start",
            "route_allowed",
            string.Empty);

        return new DataAgentOrchestrationResult(
            "session-1",
            DataAgentAnalysisSessionStatus.Active,
            [
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Succeeded, "route_allowed", ExecutedSql: false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.SchemaContext, DataAgentOrchestrationStepStatus.Succeeded, "schema_ready", ExecutedSql: false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Plan, DataAgentOrchestrationStepStatus.Succeeded, "plan_ready", ExecutedSql: false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Validate, DataAgentOrchestrationStepStatus.Succeeded, "validated", ExecutedSql: false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Execute, DataAgentOrchestrationStepStatus.Succeeded, "executed", ExecutedSql: true),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Explain, DataAgentOrchestrationStepStatus.Succeeded, "explained", ExecutedSql: false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_saved", ExecutedSql: false)
            ],
            checkpoint,
            response,
            routeContext);
    }

    sealed class RecordingSidecarClient(Func<DataAgentGraphHandshakeRequest, DataAgentGraphHandshakeResponse> responseFactory)
        : IDataAgentGraphSidecarClient
    {
        readonly List<DataAgentGraphHandshakeRequest> requests = [];

        public IReadOnlyList<DataAgentGraphHandshakeRequest> Requests => requests;

        public DataAgentGraphHandshakeResponse TryHandshake(DataAgentGraphHandshakeRequest request)
        {
            requests.Add(request);
            return responseFactory(request);
        }
    }

    sealed class ThrowingSidecarClient(Exception exception) : IDataAgentGraphSidecarClient
    {
        public DataAgentGraphHandshakeResponse TryHandshake(DataAgentGraphHandshakeRequest request)
        {
            throw exception;
        }
    }
}
