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
    public void DisabledCoordinatorDoesNotRequireValidResultAndDoesNotCallSidecar()
    {
        RecordingSidecarClient sidecar = new(NewAcceptedResponse);
        DataAgentGraphHandshakeCoordinator coordinator = new(DataAgentGraphHandshakeOptions.Disabled, sidecar);
        DataAgentOrchestrationResult malformedResult = MalformedResult();
        DataAgentGraphHandshakeOutcome? outcome = null;

        Assert.DoesNotThrow(() => outcome = coordinator.TryHandshake(
            "owner",
            "Which gates failed?",
            malformedResult));

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.Not.Null);
            Assert.That(outcome!.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Disabled));
            Assert.That(outcome.ReasonCode, Is.EqualTo("sidecar_disabled"));
            Assert.That(outcome.FallbackRequired, Is.True);
            Assert.That(outcome.Request, Is.Null);
            Assert.That(sidecar.Requests, Is.Empty);
        });
    }

    [Test]
    public void ObservabilityReasonCodesAreStableMachineTokens()
    {
        string[] reasonCodes =
        [
            DataAgentGraphSidecarObservabilityReasonCodes.Disabled,
            DataAgentGraphSidecarObservabilityReasonCodes.NotConfigured,
            DataAgentGraphSidecarObservabilityReasonCodes.RuntimeUnavailable,
            DataAgentGraphSidecarObservabilityReasonCodes.ResponseRejected,
            DataAgentGraphSidecarObservabilityReasonCodes.ProgressRejected,
            DataAgentGraphSidecarObservabilityReasonCodes.Accepted,
            DataAgentGraphSidecarObservabilityReasonCodes.FallbackUsed,
            DataAgentGraphSidecarObservabilityReasonCodes.StreamFinalResponseMissing,
            DataAgentGraphSidecarObservabilityReasonCodes.StreamFinalResponseRejected
        ];

        Assert.Multiple(() =>
        {
            Assert.That(reasonCodes, Is.Unique);
            foreach (string reasonCode in reasonCodes)
            {
                Assert.That(reasonCode, Does.Match("^[a-z][a-z0-9_]*$"), reasonCode);
                Assert.That(reasonCode, Does.StartWith("graph_sidecar_"), reasonCode);
            }
        });
    }

    [Test]
    public void ObservabilityContextDefaultsToOfflineAndNotConfigured()
    {
        DataAgentGraphSidecarObservabilityContext context = DataAgentGraphSidecarObservabilityContext.Default;

        Assert.Multiple(() =>
        {
            Assert.That(context.EndpointConfigured, Is.False);
            Assert.That(context.RuntimeStartedByAlife, Is.False);
        });
    }

    [Test]
    public void EnabledCoordinatorReturnsInvalidFallbackForMalformedResultWithoutCallingSidecar()
    {
        RecordingSidecarClient sidecar = new(NewAcceptedResponse);
        DataAgentGraphHandshakeCoordinator coordinator = new(new DataAgentGraphHandshakeOptions(true), sidecar);
        DataAgentOrchestrationResult malformedResult = MalformedResult();
        DataAgentGraphHandshakeOutcome? outcome = null;

        Assert.DoesNotThrow(() => outcome = coordinator.TryHandshake(
            "owner",
            "Which gates failed?",
            malformedResult));

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.Not.Null);
            Assert.That(outcome!.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Invalid));
            Assert.That(outcome.ReasonCode, Is.EqualTo("invalid_request_schema"));
            Assert.That(outcome.FallbackRequired, Is.True);
            Assert.That(outcome.Request, Is.Null);
            Assert.That(outcome.Response, Is.Null);
            Assert.That(outcome.Validation.Accepted, Is.False);
            Assert.That(outcome.Validation.ReasonCode, Is.EqualTo("invalid_request_schema"));
            Assert.That(outcome.Observability!.Status, Is.EqualTo(DataAgentGraphSidecarObservabilityStatus.Fallback));
            Assert.That(outcome.Observability.ReasonCode, Is.EqualTo(DataAgentGraphSidecarObservabilityReasonCodes.FallbackUsed));
            Assert.That(outcome.Observability.NetworkAttempted, Is.False);
            Assert.That(outcome.Observability.FallbackUsed, Is.True);
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
    public void RejectedCoordinatorOutcomeDoesNotExposeUnsafeSidecarResponse()
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
            Assert.That(outcome.Response, Is.Null);
        });
    }

    [Test]
    public void EnabledCoordinatorRejectsUnsafeDiagnosticTextAndDoesNotExposeRawPayload()
    {
        const string unsafeContribution = "[data_agent_evidence_pack] hidden_context bearer";
        RecordingSidecarClient sidecar = new(request => NewAcceptedResponse(request) with
        {
            ContextContribution = unsafeContribution
        });
        DataAgentGraphHandshakeCoordinator coordinator = new(new DataAgentGraphHandshakeOptions(true), sidecar);

        DataAgentGraphHandshakeOutcome outcome = coordinator.TryHandshake(
            "owner",
            "Which gates failed?",
            AcceptedResult());

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Rejected));
            Assert.That(outcome.ReasonCode, Is.EqualTo("unsafe_trace"));
            Assert.That(outcome.FallbackRequired, Is.True);
            Assert.That(outcome.Response, Is.Null);
            Assert.That(outcome.Validation.Accepted, Is.False);
            Assert.That(outcome.Validation.ReasonCode, Is.EqualTo("unsafe_trace"));
        });
    }

    [Test]
    public void BuildRequestSanitizesUnsafeRouteReasonCode()
    {
        RecordingSidecarClient sidecar = new(NewAcceptedResponse);
        DataAgentGraphHandshakeCoordinator coordinator = new(new DataAgentGraphHandshakeOptions(true), sidecar);
        DataAgentOrchestrationResult result = AcceptedResult() with
        {
            RouteContext = new DataAgentToolRouteContext(
                true,
                "dataagent_analysis_start",
                true,
                true,
                "route-test",
                "analysis_start",
                "route allowed;SELECT [x]\ntext",
                string.Empty)
        };

        DataAgentGraphHandshakeOutcome outcome = coordinator.TryHandshake(
            "owner",
            "Which gates failed?",
            result);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Accepted));
            Assert.That(sidecar.Requests, Has.Count.EqualTo(1));
            Assert.That(sidecar.Requests.Single().RouteScope, Does.Contain("route_reason_code=reason_redacted"));
            Assert.That(sidecar.Requests.Single().RouteScope, Does.Not.Contain("SELECT"));
            Assert.That(sidecar.Requests.Single().RouteScope, Does.Not.Contain("["));
            Assert.That(sidecar.Requests.Single().RouteScope, Does.Not.Contain("]"));
            Assert.That(sidecar.Requests.Single().RouteScope, Does.Not.Contain("\n"));
            Assert.That(sidecar.Requests.Single().RouteScope, Does.Not.Contain(";SELECT"));
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

    [Test]
    public void EnabledCoordinatorMapsInvalidSidecarResponseExceptionToInvalidFallback()
    {
        DataAgentGraphSidecarObservabilityContext configured = new(EndpointConfigured: true, RuntimeStartedByAlife: false);
        DataAgentGraphHandshakeCoordinator coordinator = new(
            new DataAgentGraphHandshakeOptions(true),
            new ThrowingSidecarClient(new DataAgentGraphSidecarInvalidResponseException("invalid_response_schema")),
            observabilityContext: configured);

        DataAgentGraphHandshakeOutcome outcome = coordinator.TryHandshake(
            "owner",
            "Which gates failed?",
            AcceptedResult());

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Invalid));
            Assert.That(outcome.ReasonCode, Is.EqualTo("invalid_response_schema"));
            Assert.That(outcome.FallbackRequired, Is.True);
            Assert.That(outcome.Response, Is.Null);
            Assert.That(outcome.Observability!.Status, Is.EqualTo(DataAgentGraphSidecarObservabilityStatus.Rejected));
            Assert.That(outcome.Observability.ReasonCode, Is.EqualTo(DataAgentGraphSidecarObservabilityReasonCodes.ResponseRejected));
            Assert.That(outcome.Observability.NetworkAttempted, Is.True);
            Assert.That(outcome.Observability.FallbackUsed, Is.True);
        });
    }

    [Test]
    public void EnabledCoordinatorPublishesAcceptedResponseNodeProgressThroughBridge()
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

        DataAgentProgressEvent progress = progressSink.Events.Single();
        Assert.Multiple(() =>
        {
            Assert.That(outcome.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Accepted));
            Assert.That(progress.Kind, Is.EqualTo(DataAgentProgressEventKind.Planner));
            Assert.That(progress.Status, Is.EqualTo(DataAgentProgressEventStatus.Succeeded));
            Assert.That(progress.ReasonCode, Is.EqualTo("planner_suggested"));
            Assert.That(progress.CreatedAt, Is.EqualTo(Now()));
            Assert.That(progress.ExecutedSql, Is.False);
            Assert.That(progress.Facts["source"], Is.EqualTo("graph_sidecar"));
            Assert.That(progress.Facts["node"], Is.EqualTo(DataAgentWorkflowNodeNames.QueryPlanner));
        });
    }

    [Test]
    public void EnabledCoordinatorPublishesAcceptedResponseNodeProgressMessageAndFactsThroughBridge()
    {
        RecordingSidecarClient sidecar = new(request => NewAcceptedResponse(request) with
        {
            NodeProgress =
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
            ]
        });
        RecordingProgressSink progressSink = new();
        DataAgentGraphHandshakeCoordinator coordinator = new(
            new DataAgentGraphHandshakeOptions(true),
            sidecar,
            new DataAgentGraphSidecarProgressBridge(progressSink, Now));

        DataAgentGraphHandshakeOutcome outcome = coordinator.TryHandshake(
            "owner",
            "Which gates failed?",
            AcceptedResult());

        DataAgentProgressEvent progress = progressSink.Events.Single();
        Assert.Multiple(() =>
        {
            Assert.That(outcome.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Accepted));
            Assert.That(progress.Facts["message"], Is.EqualTo("planner ready"));
            Assert.That(progress.Facts["stage"], Is.EqualTo("planner"));
            Assert.That(progress.Facts["source"], Is.EqualTo("graph_sidecar"));
            Assert.That(progress.Facts["request_id"], Is.EqualTo(outcome.Request!.RequestId));
            Assert.That(progress.ExecutedSql, Is.False);
        });
    }

    [Test]
    public void EnabledCoordinatorDoesNotPublishUnsafeResponseNodeProgressMessage()
    {
        RecordingSidecarClient sidecar = new(request => NewAcceptedResponse(request) with
        {
            NodeProgress =
            [
                new DataAgentGraphHandshakeProgress(
                    DataAgentWorkflowNodeNames.QueryPlanner,
                    DataAgentGraphHandshakeProgressStatus.Completed,
                    "planner_suggested",
                    "SELECT * FROM engineering_gate",
                    new Dictionary<string, string>())
            ]
        });
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
            Assert.That(progressSink.Events, Is.Empty);
        });
    }

    [Test]
    public void EnabledCoordinatorDoesNotPublishReservedResponseNodeProgressFacts()
    {
        RecordingSidecarClient sidecar = new(request => NewAcceptedResponse(request) with
        {
            NodeProgress =
            [
                new DataAgentGraphHandshakeProgress(
                    DataAgentWorkflowNodeNames.QueryPlanner,
                    DataAgentGraphHandshakeProgressStatus.Completed,
                    "planner_suggested",
                    "planner ready",
                    new Dictionary<string, string>
                    {
                        ["source"] = "sidecar"
                    })
            ]
        });
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
            Assert.That(progressSink.Events, Is.Empty);
        });
    }

    [Test]
    public void AcceptedCoordinatorOutcomeSurvivesProgressPublishFailure()
    {
        RecordingSidecarClient sidecar = new(NewAcceptedResponse);
        DataAgentGraphHandshakeCoordinator coordinator = new(
            new DataAgentGraphHandshakeOptions(true),
            sidecar,
            new DataAgentGraphSidecarProgressBridge(new ThrowingProgressSink(), Now));

        DataAgentGraphHandshakeOutcome outcome = coordinator.TryHandshake(
            "owner",
            "Which gates failed?",
            AcceptedResult());

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Accepted));
            Assert.That(outcome.ReasonCode, Is.EqualTo("handshake_accepted"));
            Assert.That(outcome.Response, Is.Not.Null);
            Assert.That(sidecar.Requests, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void RejectedCoordinatorOutcomeDoesNotPublishSidecarProgress()
    {
        RecordingSidecarClient sidecar = new(request => NewAcceptedResponse(request) with
        {
            NodeProgress =
            [
                new DataAgentGraphHandshakeProgress("unknown_node", DataAgentGraphHandshakeProgressStatus.Completed, "unknown_done")
            ]
        });
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
            Assert.That(outcome.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Rejected));
            Assert.That(outcome.ReasonCode, Is.EqualTo("progress_invalid"));
            Assert.That(outcome.Response, Is.Null);
            Assert.That(progressSink.Events, Is.Empty);
        });
    }

    [Test]
    public void DisabledCoordinatorDoesNotPublishSidecarProgress()
    {
        RecordingSidecarClient sidecar = new(NewAcceptedResponse);
        RecordingProgressSink progressSink = new();
        DataAgentGraphHandshakeCoordinator coordinator = new(
            DataAgentGraphHandshakeOptions.Disabled,
            sidecar,
            new DataAgentGraphSidecarProgressBridge(progressSink, Now));

        DataAgentGraphHandshakeOutcome outcome = coordinator.TryHandshake(
            "owner",
            "Which gates failed?",
            AcceptedResult());

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Disabled));
            Assert.That(sidecar.Requests, Is.Empty);
            Assert.That(progressSink.Events, Is.Empty);
        });
    }

    [Test]
    public void DisabledCoordinatorEmitsDisabledObservabilitySnapshot()
    {
        RecordingSidecarClient sidecar = new(NewAcceptedResponse);
        DataAgentGraphHandshakeCoordinator coordinator = new(DataAgentGraphHandshakeOptions.Disabled, sidecar);

        DataAgentGraphHandshakeOutcome outcome = coordinator.TryHandshake(
            "owner",
            "Which gates failed?",
            AcceptedResult());

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Observability, Is.Not.Null);
            Assert.That(outcome.Observability!.Status, Is.EqualTo(DataAgentGraphSidecarObservabilityStatus.Disabled));
            Assert.That(outcome.Observability.ReasonCode, Is.EqualTo(DataAgentGraphSidecarObservabilityReasonCodes.Disabled));
            Assert.That(outcome.Observability.SidecarEnabled, Is.False);
            Assert.That(outcome.Observability.EndpointConfigured, Is.False);
            Assert.That(outcome.Observability.NetworkAttempted, Is.False);
            Assert.That(outcome.Observability.Accepted, Is.False);
            Assert.That(outcome.Observability.FallbackUsed, Is.True);
        });
    }

    [Test]
    public void EnabledCoordinatorWithoutEndpointEmitsNotConfiguredObservabilitySnapshot()
    {
        DataAgentGraphHandshakeCoordinator coordinator = new(
            new DataAgentGraphHandshakeOptions(true),
            DisabledDataAgentGraphSidecarClient.Instance,
            observabilityContext: DataAgentGraphSidecarObservabilityContext.Default);

        DataAgentGraphHandshakeOutcome outcome = coordinator.TryHandshake(
            "owner",
            "Which gates failed?",
            AcceptedResult());

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Unavailable));
            Assert.That(outcome.Observability, Is.Not.Null);
            Assert.That(outcome.Observability!.Status, Is.EqualTo(DataAgentGraphSidecarObservabilityStatus.NotConfigured));
            Assert.That(outcome.Observability.ReasonCode, Is.EqualTo(DataAgentGraphSidecarObservabilityReasonCodes.NotConfigured));
            Assert.That(outcome.Observability.SidecarEnabled, Is.True);
            Assert.That(outcome.Observability.EndpointConfigured, Is.False);
            Assert.That(outcome.Observability.NetworkAttempted, Is.False);
            Assert.That(outcome.Observability.FallbackUsed, Is.True);
        });
    }

    [Test]
    public void UnavailableTimeoutRejectedAndAcceptedOutcomesEmitObservabilitySnapshots()
    {
        DataAgentGraphSidecarObservabilityContext configured = new(EndpointConfigured: true, RuntimeStartedByAlife: false);
        DataAgentGraphHandshakeCoordinator unavailableCoordinator = new(
            new DataAgentGraphHandshakeOptions(true),
            new ThrowingSidecarClient(new InvalidOperationException("sidecar offline")),
            observabilityContext: configured);
        DataAgentGraphHandshakeCoordinator timeoutCoordinator = new(
            new DataAgentGraphHandshakeOptions(true),
            new ThrowingSidecarClient(new TimeoutException("sidecar timeout")),
            observabilityContext: configured);
        DataAgentGraphHandshakeCoordinator rejectedCoordinator = new(
            new DataAgentGraphHandshakeOptions(true),
            new RecordingSidecarClient(request => NewAcceptedResponse(request) with { NoSqlAuthority = false }),
            observabilityContext: configured);
        DataAgentGraphHandshakeCoordinator acceptedCoordinator = new(
            new DataAgentGraphHandshakeOptions(true),
            new RecordingSidecarClient(NewAcceptedResponse),
            observabilityContext: configured);

        DataAgentGraphHandshakeOutcome unavailable = unavailableCoordinator.TryHandshake("owner", "Which gates failed?", AcceptedResult());
        DataAgentGraphHandshakeOutcome timeout = timeoutCoordinator.TryHandshake("owner", "Which gates failed?", AcceptedResult());
        DataAgentGraphHandshakeOutcome rejected = rejectedCoordinator.TryHandshake("owner", "Which gates failed?", AcceptedResult());
        DataAgentGraphHandshakeOutcome accepted = acceptedCoordinator.TryHandshake("owner", "Which gates failed?", AcceptedResult());

        Assert.Multiple(() =>
        {
            Assert.That(unavailable.Observability!.Status, Is.EqualTo(DataAgentGraphSidecarObservabilityStatus.RuntimeUnavailable));
            Assert.That(unavailable.Observability.ReasonCode, Is.EqualTo(DataAgentGraphSidecarObservabilityReasonCodes.RuntimeUnavailable));
            Assert.That(unavailable.Observability.NetworkAttempted, Is.True);
            Assert.That(timeout.Observability!.Status, Is.EqualTo(DataAgentGraphSidecarObservabilityStatus.RuntimeUnavailable));
            Assert.That(timeout.Observability.ReasonCode, Is.EqualTo(DataAgentGraphSidecarObservabilityReasonCodes.RuntimeUnavailable));
            Assert.That(rejected.Observability!.Status, Is.EqualTo(DataAgentGraphSidecarObservabilityStatus.Rejected));
            Assert.That(rejected.Observability.ReasonCode, Is.EqualTo(DataAgentGraphSidecarObservabilityReasonCodes.ResponseRejected));
            Assert.That(rejected.Observability.Accepted, Is.False);
            Assert.That(rejected.Observability.FallbackUsed, Is.True);
            Assert.That(accepted.Observability!.Status, Is.EqualTo(DataAgentGraphSidecarObservabilityStatus.Accepted));
            Assert.That(accepted.Observability.ReasonCode, Is.EqualTo(DataAgentGraphSidecarObservabilityReasonCodes.Accepted));
            Assert.That(accepted.Observability.Accepted, Is.True);
            Assert.That(accepted.Observability.FallbackUsed, Is.False);
        });
    }

    [Test]
    public void ConstructorRejectsNullOptions()
    {
        RecordingSidecarClient sidecar = new(NewAcceptedResponse);

        Assert.Throws<ArgumentNullException>(() => new DataAgentGraphHandshakeCoordinator(null!, sidecar));
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

    static DataAgentOrchestrationResult MalformedResult()
    {
        return new DataAgentOrchestrationResult(
            "session-1",
            DataAgentAnalysisSessionStatus.Active,
            null!,
            null!,
            new DataAgentAnalysisResponse(
                "session-1",
                DataAgentAnalysisSessionStatus.Active,
                DataAgentAnalysisTurnIntent.NewQuestion,
                null,
                string.Empty,
                string.Empty,
                Accepted: true,
                RejectedReason: string.Empty),
            null);
    }

    static DateTimeOffset Now()
    {
        return new DateTimeOffset(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);
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

    sealed class RecordingProgressSink : IDataAgentProgressSink
    {
        public List<DataAgentProgressEvent> Events { get; } = [];

        public void Publish(DataAgentProgressEvent? progressEvent)
        {
            if (progressEvent is not null)
                Events.Add(progressEvent);
        }
    }

    sealed class ThrowingProgressSink : IDataAgentProgressSink
    {
        public void Publish(DataAgentProgressEvent? progressEvent)
        {
            throw new InvalidOperationException("progress sink unavailable");
        }
    }
}
