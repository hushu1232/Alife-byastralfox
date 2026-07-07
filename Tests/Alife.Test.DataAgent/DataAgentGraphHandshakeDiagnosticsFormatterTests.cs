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
