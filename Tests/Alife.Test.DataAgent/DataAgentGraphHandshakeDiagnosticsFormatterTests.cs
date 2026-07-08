using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentGraphHandshakeDiagnosticsFormatterTests
{
    [Test]
    public void FormatDisabledOutcomeEmitsFallbackAndNoSqlAuthority()
    {
        DataAgentGraphHandshakeRequest request = NewRequest();
        DataAgentGraphHandshakeOutcome outcome = new(
            DataAgentGraphHandshakeStatus.Disabled,
            "sidecar_disabled",
            FallbackRequired: true,
            request,
            Response: null,
            new DataAgentGraphHandshakeValidationResult(false, "sidecar_disabled"));

        string formatted = DataAgentGraphHandshakeDiagnosticsFormatter.Format(outcome);

        Assert.Multiple(() =>
        {
            Assert.That(formatted, Does.Contain("DataAgent graph handshake"));
            Assert.That(formatted, Does.Contain("status=disabled"));
            Assert.That(formatted, Does.Contain("reason=sidecar_disabled"));
            Assert.That(formatted, Does.Contain("fallback_required=true"));
            Assert.That(formatted, Does.Contain("no_sql_authority=true"));
            Assert.That(formatted, Does.Contain("scoped_node_manifest=true"));
            Assert.That(formatted, Does.Contain("runtime_required=false"));
        });
    }

    [Test]
    public void FormatOutcomeWithObservabilityEmitsStableSidecarFields()
    {
        DataAgentGraphHandshakeRequest request = NewRequest();
        DataAgentGraphHandshakeOutcome outcome = new(
            DataAgentGraphHandshakeStatus.Rejected,
            "sql_authority_requested",
            FallbackRequired: true,
            request,
            Response: null,
            new DataAgentGraphHandshakeValidationResult(false, "sql_authority_requested"),
            new DataAgentGraphSidecarObservabilitySnapshot(
                DataAgentGraphSidecarObservabilityReasonCodes.ResponseRejected,
                DataAgentGraphSidecarObservabilityStatus.Rejected,
                SidecarEnabled: true,
                EndpointConfigured: true,
                RuntimeStartedByAlife: false,
                NetworkAttempted: true,
                Accepted: false,
                FallbackUsed: true,
                SafeSummary: "graph_sidecar_response_rejected"));

        string formatted = DataAgentGraphHandshakeDiagnosticsFormatter.Format(outcome);

        Assert.Multiple(() =>
        {
            Assert.That(formatted, Does.Contain("graph_sidecar status=rejected"));
            Assert.That(formatted, Does.Contain("reason=graph_sidecar_response_rejected"));
            Assert.That(formatted, Does.Contain("enabled=true"));
            Assert.That(formatted, Does.Contain("endpoint_configured=true"));
            Assert.That(formatted, Does.Contain("runtime_started_by_alife=false"));
            Assert.That(formatted, Does.Contain("network_attempted=true"));
            Assert.That(formatted, Does.Contain("accepted=false"));
            Assert.That(formatted, Does.Contain("fallback=true"));
        });
    }

    [Test]
    public void FormatOutcomeWithObservabilityRedactsUnsafeSummary()
    {
        DataAgentGraphHandshakeRequest request = NewRequest();
        DataAgentGraphHandshakeOutcome outcome = new(
            DataAgentGraphHandshakeStatus.Rejected,
            "unsafe_trace",
            FallbackRequired: true,
            request,
            Response: null,
            new DataAgentGraphHandshakeValidationResult(false, "unsafe_trace"),
            new DataAgentGraphSidecarObservabilitySnapshot(
                DataAgentGraphSidecarObservabilityReasonCodes.ResponseRejected,
                DataAgentGraphSidecarObservabilityStatus.Rejected,
                SidecarEnabled: true,
                EndpointConfigured: true,
                RuntimeStartedByAlife: false,
                NetworkAttempted: true,
                Accepted: false,
                FallbackUsed: true,
                SafeSummary: "SELECT * FROM document_index"));

        string formatted = DataAgentGraphHandshakeDiagnosticsFormatter.Format(outcome);

        Assert.Multiple(() =>
        {
            Assert.That(formatted, Does.Contain("summary=redacted"));
            Assert.That(formatted, Does.Not.Contain("SELECT"));
            Assert.That(formatted, Does.Not.Contain("document_index"));
        });
    }

    [Test]
    public void FormatAcceptedOutcomeEmitsSelectedNodesAndBoundsTrace()
    {
        DataAgentGraphHandshakeRequest request = NewRequest();
        DataAgentGraphHandshakeResponse response = NewResponse(request);
        DataAgentGraphHandshakeOutcome outcome = new(
            DataAgentGraphHandshakeStatus.Accepted,
            "handshake_accepted",
            FallbackRequired: false,
            request,
            response,
            new DataAgentGraphHandshakeValidationResult(true, "handshake_accepted"));

        string formatted = DataAgentGraphHandshakeDiagnosticsFormatter.Format(outcome, maxChars: 600);

        Assert.Multiple(() =>
        {
            Assert.That(formatted, Does.Contain("status=accepted"));
            Assert.That(formatted, Does.Contain("selected_nodes=scenario_knowledge,query_planner"));
            Assert.That(formatted, Does.Contain("progress=query_planner:Completed:planner_suggested"));
            Assert.That(formatted, Does.Contain("trace=ScenarioKnowledge:Completed>QueryPlanner:Completed"));
            Assert.That(formatted.Length, Is.LessThanOrEqualTo(600));
        });
    }

    [Test]
    public void FormatRejectedOutcomeRedactsSqlLikeTrace()
    {
        DataAgentGraphHandshakeRequest request = NewRequest();
        DataAgentGraphHandshakeResponse unsafeResponse = NewResponse(request) with
        {
            Accepted = false,
            ReasonCode = "unsafe_trace",
            TraceSummary = "SELECT * FROM document_index",
            ContextContribution = "DROP TABLE document_index",
            FallbackRequired = true
        };
        DataAgentGraphHandshakeOutcome outcome = new(
            DataAgentGraphHandshakeStatus.Rejected,
            "unsafe_trace",
            FallbackRequired: true,
            request,
            unsafeResponse,
            new DataAgentGraphHandshakeValidationResult(false, "unsafe_trace"));

        string formatted = DataAgentGraphHandshakeDiagnosticsFormatter.Format(outcome);

        Assert.Multiple(() =>
        {
            Assert.That(formatted, Does.Contain("status=rejected"));
            Assert.That(formatted, Does.Contain("reason=unsafe_trace"));
            Assert.That(formatted, Does.Contain("trace=redacted"));
            Assert.That(formatted, Does.Not.Contain("SELECT"));
            Assert.That(formatted, Does.Not.Contain("DROP TABLE"));
        });
    }

    [Test]
    public void FormatRejectedOutcomeRedactsLiteralSqlToken()
    {
        DataAgentGraphHandshakeRequest request = NewRequest();
        DataAgentGraphHandshakeResponse unsafeResponse = NewResponse(request) with
        {
            Accepted = false,
            ReasonCode = "unsafe_trace",
            TraceSummary = "raw SQL requested",
            ContextContribution = "owner requested SQL graph details",
            FallbackRequired = true
        };
        DataAgentGraphHandshakeOutcome outcome = new(
            DataAgentGraphHandshakeStatus.Rejected,
            "unsafe_trace",
            FallbackRequired: true,
            request,
            unsafeResponse,
            new DataAgentGraphHandshakeValidationResult(false, "unsafe_trace"));

        string formatted = DataAgentGraphHandshakeDiagnosticsFormatter.Format(outcome);

        Assert.Multiple(() =>
        {
            Assert.That(formatted, Does.Contain("status=rejected"));
            Assert.That(formatted, Does.Contain("trace=redacted"));
            Assert.That(formatted, Does.Contain("context=redacted"));
            Assert.That(formatted, Does.Not.Contain("SQL"));
        });
    }

    [Test]
    public void FormatAcceptedOutcomeRedactsUnsafeMarkerContextText()
    {
        DataAgentGraphHandshakeRequest request = NewRequest();
        DataAgentGraphHandshakeResponse unsafeResponse = NewResponse(request) with
        {
            TraceSummary = "[data_agent_context] hidden_context bearer",
            ContextContribution = "Allowed XML tools include api_key=sk-test"
        };
        DataAgentGraphHandshakeOutcome outcome = new(
            DataAgentGraphHandshakeStatus.Accepted,
            "handshake_accepted",
            FallbackRequired: false,
            request,
            unsafeResponse,
            new DataAgentGraphHandshakeValidationResult(true, "handshake_accepted"));

        string formatted = DataAgentGraphHandshakeDiagnosticsFormatter.Format(outcome);

        Assert.Multiple(() =>
        {
            Assert.That(formatted, Does.Contain("status=accepted"));
            Assert.That(formatted, Does.Contain("trace=redacted"));
            Assert.That(formatted, Does.Contain("context=redacted"));
            Assert.That(formatted, Does.Not.Contain("[data_agent_context]"));
            Assert.That(formatted, Does.Not.Contain("hidden_context"));
            Assert.That(formatted, Does.Not.Contain("Allowed XML tools"));
            Assert.That(formatted, Does.Not.Contain("api_key"));
            Assert.That(formatted, Does.Not.Contain("sk-"));
        });
    }

    [TestCase("EXECUTE refresh_index", "EXECUTE")]
    [TestCase("CALL refresh_index()", "CALL")]
    [TestCase("MERGE INTO audit_log", "MERGE")]
    [TestCase("GRANT SELECT", "GRANT")]
    [TestCase("REVOKE SELECT", "REVOKE")]
    [TestCase("PRAGMA table_info", "PRAGMA")]
    [TestCase("BEGIN TRANSACTION", "BEGIN")]
    [TestCase("BEGIN WORK", "BEGIN")]
    [TestCase("COMMIT", "COMMIT")]
    [TestCase("ROLLBACK", "ROLLBACK")]
    public void FormatRejectedOutcomeRedactsSqlCommandVariants(string commandText, string commandToken)
    {
        DataAgentGraphHandshakeRequest request = NewRequest();
        DataAgentGraphHandshakeResponse unsafeResponse = NewResponse(request) with
        {
            Accepted = false,
            ReasonCode = "unsafe_trace",
            TraceSummary = $"sidecar requested {commandText}",
            ContextContribution = $"context contains {commandText}",
            FallbackRequired = true
        };
        DataAgentGraphHandshakeOutcome outcome = new(
            DataAgentGraphHandshakeStatus.Rejected,
            "unsafe_trace",
            FallbackRequired: true,
            request,
            unsafeResponse,
            new DataAgentGraphHandshakeValidationResult(false, "unsafe_trace"));

        string formatted = DataAgentGraphHandshakeDiagnosticsFormatter.Format(outcome);

        Assert.Multiple(() =>
        {
            Assert.That(formatted, Does.Contain("trace=redacted"));
            Assert.That(formatted, Does.Contain("context=redacted"));
            Assert.That(formatted, Does.Not.Contain(commandText));
            Assert.That(formatted, Does.Not.Contain(commandToken));
        });
    }

    [Test]
    public void FormatBoundsUntrustedListsAndTextBeforeRendering()
    {
        DataAgentGraphHandshakeRequest request = NewRequest();
        string oversizedTail = "TAIL_SHOULD_NOT_SURVIVE";
        DataAgentGraphHandshakeResponse response = NewResponse(request) with
        {
            SelectedNodes = Enumerable
                .Range(0, DataAgentGraphHandshakeLimits.MaxNodeManifests + 3)
                .Select(index => $"node_{index}")
                .ToArray(),
            NodeProgress = Enumerable
                .Range(0, DataAgentGraphHandshakeLimits.MaxProgressEvents + 3)
                .Select(index => new DataAgentGraphHandshakeProgress(
                    $"progress_node_{index}",
                    DataAgentGraphHandshakeProgressStatus.Completed,
                    $"progress_{index}"))
                .ToArray(),
            TraceSummary = new string('a', DataAgentGraphHandshakeLimits.MaxTraceSummaryChars * 2) + oversizedTail,
            ContextContribution = new string('b', DataAgentGraphHandshakeLimits.MaxContextContributionChars * 2) + oversizedTail
        };
        DataAgentGraphHandshakeOutcome outcome = new(
            DataAgentGraphHandshakeStatus.Accepted,
            "handshake_accepted",
            FallbackRequired: false,
            request,
            response,
            new DataAgentGraphHandshakeValidationResult(true, "handshake_accepted"));

        string formatted = DataAgentGraphHandshakeDiagnosticsFormatter.Format(outcome, maxChars: 6000);
        string finalBounded = DataAgentGraphHandshakeDiagnosticsFormatter.Format(outcome, maxChars: 180);

        Assert.Multiple(() =>
        {
            Assert.That(formatted, Does.Contain($"node_{DataAgentGraphHandshakeLimits.MaxNodeManifests - 1}"));
            Assert.That(formatted, Does.Not.Contain($"node_{DataAgentGraphHandshakeLimits.MaxNodeManifests}"));
            Assert.That(formatted, Does.Contain($"progress_node_{DataAgentGraphHandshakeLimits.MaxProgressEvents - 1}"));
            Assert.That(formatted, Does.Not.Contain($"progress_node_{DataAgentGraphHandshakeLimits.MaxProgressEvents}"));
            Assert.That(formatted, Does.Not.Contain(oversizedTail));
            Assert.That(formatted.Length, Is.LessThanOrEqualTo(6000));
            Assert.That(finalBounded.Length, Is.LessThanOrEqualTo(180));
        });
    }

    [Test]
    public void FormatHandlesNullUntrustedResponseFields()
    {
        DataAgentGraphHandshakeRequest request = NewRequest();
        DataAgentGraphHandshakeResponse response = NewResponse(request) with
        {
            SelectedNodes = null!,
            NodeProgress = null!,
            TraceSummary = null!,
            ContextContribution = null!,
            RequestedToolNames = null!
        };
        DataAgentGraphHandshakeOutcome outcome = new(
            DataAgentGraphHandshakeStatus.Accepted,
            "handshake_accepted",
            FallbackRequired: false,
            request,
            response,
            new DataAgentGraphHandshakeValidationResult(true, "handshake_accepted"));

        string? formatted = null;

        Assert.DoesNotThrow(() => formatted = DataAgentGraphHandshakeDiagnosticsFormatter.Format(outcome));
        Assert.Multiple(() =>
        {
            Assert.That(formatted, Does.Contain("selected_nodes=empty"));
            Assert.That(formatted, Does.Contain("progress=empty"));
            Assert.That(formatted, Does.Contain("trace=empty"));
            Assert.That(formatted, Does.Contain("context=empty"));
        });
    }

    static DataAgentGraphHandshakeRequest NewRequest()
    {
        return new DataAgentGraphHandshakeRequest(
            "request-1",
            "session-1",
            "turn-1",
            "owner",
            "Which required gates failed?",
            "scenario_context=true",
            "route_allowed",
            "dataset=engineering_gate;limit<=50",
            DataAgentGraphHandshakeManifestFactory.CreateDefault(),
            NoSqlAuthority: true,
            ReadOnly: true,
            FallbackAvailable: true,
            TraceBudgetChars: DataAgentGraphHandshakeLimits.MaxTraceSummaryChars,
            ProgressBudget: DataAgentGraphHandshakeLimits.MaxProgressEvents);
    }

    static DataAgentGraphHandshakeResponse NewResponse(DataAgentGraphHandshakeRequest request)
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
}
