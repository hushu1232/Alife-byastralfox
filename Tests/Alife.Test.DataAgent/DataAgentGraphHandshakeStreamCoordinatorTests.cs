using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentGraphHandshakeStreamCoordinatorTests
{
    [Test]
    public void AcceptedStreamPublishesBufferedProgressAfterFinalResponseValidation()
    {
        RecordingSidecarClient sidecar = new(NewAcceptedResponse);
        RecordingStreamClient stream = new(request => new DataAgentGraphHandshakeStreamResult(
            NewAcceptedResponse(request) with { NodeProgress = [] },
            [
                new DataAgentGraphHandshakeProgress(
                    DataAgentWorkflowNodeNames.QueryPlanner,
                    DataAgentGraphHandshakeProgressStatus.Completed,
                    "planner_suggested",
                    "planner ready",
                    new Dictionary<string, string>
                    {
                        ["stage"] = "planner"
                    })
            ]));
        RecordingProgressSink progressSink = new();
        DataAgentGraphHandshakeCoordinator coordinator = new(
            new DataAgentGraphHandshakeOptions(true),
            sidecar,
            new DataAgentGraphSidecarProgressBridge(progressSink, Now),
            stream);

        DataAgentGraphHandshakeOutcome outcome = coordinator.TryHandshake(
            "owner",
            "Which gates failed?",
            AcceptedResult());

        DataAgentProgressEvent progress = progressSink.Events.Single();
        Assert.Multiple(() =>
        {
            Assert.That(outcome.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Accepted));
            Assert.That(stream.Requests, Has.Count.EqualTo(1));
            Assert.That(sidecar.Requests, Is.Empty);
            Assert.That(progress.ReasonCode, Is.EqualTo("planner_suggested"));
            Assert.That(progress.Facts["stage"], Is.EqualTo("planner"));
            Assert.That(progress.Facts["source"], Is.EqualTo("graph_sidecar"));
        });
    }

    [Test]
    public void RejectedFinalResponseDoesNotPublishBufferedProgress()
    {
        RecordingStreamClient stream = new(request => new DataAgentGraphHandshakeStreamResult(
            NewAcceptedResponse(request) with
            {
                NoSqlAuthority = false,
                NodeProgress = []
            },
            [PlannerProgress()]));
        RecordingProgressSink progressSink = new();
        DataAgentGraphHandshakeCoordinator coordinator = NewStreamCoordinator(stream, progressSink);

        DataAgentGraphHandshakeOutcome outcome = coordinator.TryHandshake(
            "owner",
            "Which gates failed?",
            AcceptedResult());

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Rejected));
            Assert.That(outcome.ReasonCode, Is.EqualTo("sql_authority_requested"));
            Assert.That(outcome.Response, Is.Null);
            Assert.That(outcome.FallbackRequired, Is.True);
            Assert.That(progressSink.Events, Is.Empty);
        });
    }

    [TestCase("invalid_stream_schema")]
    [TestCase("missing_stream_final_response")]
    [TestCase("stream_progress_over_budget")]
    public void InvalidStreamFailuresDiscardProgressAndReturnReason(string reasonCode)
    {
        ThrowingStreamClient stream = new(new DataAgentGraphSidecarInvalidStreamException(reasonCode));
        RecordingProgressSink progressSink = new();
        DataAgentGraphHandshakeCoordinator coordinator = NewStreamCoordinator(stream, progressSink);

        DataAgentGraphHandshakeOutcome outcome = coordinator.TryHandshake(
            "owner",
            "Which gates failed?",
            AcceptedResult());

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Invalid));
            Assert.That(outcome.ReasonCode, Is.EqualTo(reasonCode));
            Assert.That(outcome.FallbackRequired, Is.True);
            Assert.That(outcome.Response, Is.Null);
            Assert.That(progressSink.Events, Is.Empty);
        });
    }

    [Test]
    public void StreamTimeoutAndUnavailableDiscardProgress()
    {
        RecordingProgressSink timeoutProgressSink = new();
        RecordingProgressSink unavailableProgressSink = new();
        DataAgentGraphHandshakeCoordinator timeoutCoordinator = NewStreamCoordinator(
            new ThrowingStreamClient(new TimeoutException("sidecar timeout")),
            timeoutProgressSink);
        DataAgentGraphHandshakeCoordinator unavailableCoordinator = NewStreamCoordinator(
            new ThrowingStreamClient(new InvalidOperationException("sidecar unavailable")),
            unavailableProgressSink);

        DataAgentGraphHandshakeOutcome timeout = timeoutCoordinator.TryHandshake(
            "owner",
            "Which gates failed?",
            AcceptedResult());
        DataAgentGraphHandshakeOutcome unavailable = unavailableCoordinator.TryHandshake(
            "owner",
            "Which gates failed?",
            AcceptedResult());

        Assert.Multiple(() =>
        {
            Assert.That(timeout.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Timeout));
            Assert.That(timeout.ReasonCode, Is.EqualTo("sidecar_timeout"));
            Assert.That(timeout.FallbackRequired, Is.True);
            Assert.That(timeoutProgressSink.Events, Is.Empty);
            Assert.That(unavailable.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Unavailable));
            Assert.That(unavailable.ReasonCode, Is.EqualTo("sidecar_unavailable"));
            Assert.That(unavailable.FallbackRequired, Is.True);
            Assert.That(unavailableProgressSink.Events, Is.Empty);
        });
    }

    [Test]
    public void DisabledCoordinatorDoesNotCallStreamClient()
    {
        RecordingStreamClient stream = new(request => new DataAgentGraphHandshakeStreamResult(
            NewAcceptedResponse(request),
            [PlannerProgress()]));
        RecordingProgressSink progressSink = new();
        DataAgentGraphHandshakeCoordinator coordinator = new(
            DataAgentGraphHandshakeOptions.Disabled,
            new RecordingSidecarClient(NewAcceptedResponse),
            new DataAgentGraphSidecarProgressBridge(progressSink, Now),
            stream);

        DataAgentGraphHandshakeOutcome outcome = coordinator.TryHandshake(
            "owner",
            "Which gates failed?",
            AcceptedResult());

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Disabled));
            Assert.That(stream.Requests, Is.Empty);
            Assert.That(progressSink.Events, Is.Empty);
        });
    }

    [Test]
    public void MissingStreamClientPreservesExistingRequestResponsePath()
    {
        RecordingSidecarClient sidecar = new(NewAcceptedResponse);
        RecordingProgressSink progressSink = new();
        DataAgentGraphHandshakeCoordinator coordinator = new(
            new DataAgentGraphHandshakeOptions(true),
            sidecar,
            new DataAgentGraphSidecarProgressBridge(progressSink, Now));

        DataAgentGraphHandshakeOutcome outcome = coordinator.TryHandshake(
            "owner",
            "Which gates failed?",
            AcceptedResult());

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Accepted));
            Assert.That(sidecar.Requests, Has.Count.EqualTo(1));
            Assert.That(progressSink.Events, Has.Count.EqualTo(1));
            Assert.That(progressSink.Events.Single().ReasonCode, Is.EqualTo("planner_suggested"));
        });
    }

    static DataAgentGraphHandshakeCoordinator NewStreamCoordinator(
        IDataAgentGraphHandshakeStreamClient streamClient,
        RecordingProgressSink progressSink)
    {
        return new DataAgentGraphHandshakeCoordinator(
            new DataAgentGraphHandshakeOptions(true),
            new RecordingSidecarClient(NewAcceptedResponse),
            new DataAgentGraphSidecarProgressBridge(progressSink, Now),
            streamClient);
    }

    static DataAgentGraphHandshakeProgress PlannerProgress()
    {
        return new DataAgentGraphHandshakeProgress(
            DataAgentWorkflowNodeNames.QueryPlanner,
            DataAgentGraphHandshakeProgressStatus.Completed,
            "planner_suggested",
            "planner ready",
            new Dictionary<string, string>
            {
                ["stage"] = "planner"
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

    static DateTimeOffset Now()
    {
        return new DateTimeOffset(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);
    }

    sealed class RecordingStreamClient(Func<DataAgentGraphHandshakeRequest, DataAgentGraphHandshakeStreamResult> resultFactory)
        : IDataAgentGraphHandshakeStreamClient
    {
        readonly List<DataAgentGraphHandshakeRequest> requests = [];

        public IReadOnlyList<DataAgentGraphHandshakeRequest> Requests => requests;

        public DataAgentGraphHandshakeStreamResult TryHandshakeStream(DataAgentGraphHandshakeRequest request)
        {
            requests.Add(request);
            return resultFactory(request);
        }
    }

    sealed class ThrowingStreamClient(Exception exception) : IDataAgentGraphHandshakeStreamClient
    {
        public DataAgentGraphHandshakeStreamResult TryHandshakeStream(DataAgentGraphHandshakeRequest request)
        {
            throw exception;
        }
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

    sealed class RecordingProgressSink : IDataAgentProgressSink
    {
        public List<DataAgentProgressEvent> Events { get; } = [];

        public void Publish(DataAgentProgressEvent? progressEvent)
        {
            if (progressEvent is not null)
                Events.Add(progressEvent);
        }
    }
}
