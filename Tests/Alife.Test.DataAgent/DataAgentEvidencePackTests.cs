using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentEvidencePackTests
{
    [Test]
    public void BuilderBuildsAcceptedQueryEvidence()
    {
        DataAgentOrchestrationResult result = Result(
            DataAgentAnalysisSessionStatus.Active,
            [
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Succeeded, "route_allowed", false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Execute, DataAgentOrchestrationStepStatus.Succeeded, "read_only_query_executed", true),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
            ],
            new DataAgentOrchestrationCheckpoint("session-1", DataAgentAnalysisSessionStatus.Active, "document_index", 2, true, true, false),
            new DataAgentToolRouteContext(
                true,
                "dataagent_analysis_continue",
                true,
                true,
                "route-1",
                "analysis_continue",
                "route_allowed",
                "session-1"));
        DataAgentAuditRecord audit = new(
            "Which documents describe DataAgent?",
            "document_index",
            "{}",
            "SELECT path FROM document_index LIMIT 20",
            true,
            string.Empty,
            2,
            TimeSpan.FromMilliseconds(12),
            DateTimeOffset.UnixEpoch);
        DataAgentToolBrokerAuditRecord toolAudit = new(
            "session-1",
            "dataagent_analysis_continue",
            true,
            "route_allowed",
            "route allowed",
            DateTimeOffset.UnixEpoch);

        DataAgentEvidencePack pack = new DataAgentEvidencePackBuilder().Build(result, [audit], [toolAudit]);

        Assert.Multiple(() =>
        {
            Assert.That(pack.SessionId, Is.EqualTo("session-1"));
            Assert.That(pack.SessionStatus, Is.EqualTo(DataAgentAnalysisSessionStatus.Active));
            Assert.That(pack.TurnCount, Is.EqualTo(2));
            Assert.That(pack.RoutePresent, Is.True);
            Assert.That(pack.RouteTool, Is.EqualTo("dataagent_analysis_continue"));
            Assert.That(pack.RouteAllowed, Is.True);
            Assert.That(pack.RouteAllowsQuery, Is.True);
            Assert.That(pack.RouteReasonCode, Is.EqualTo("route_allowed"));
            Assert.That(pack.Trace, Is.EqualTo("RouteGate:Succeeded>Execute:Succeeded>Checkpoint:Succeeded"));
            Assert.That(pack.ExecutedSql, Is.True);
            Assert.That(pack.Terminal, Is.False);
            Assert.That(pack.CanContinue, Is.True);
            Assert.That(pack.CanSummarize, Is.True);
            Assert.That(pack.AuditValidated, Is.True);
            Assert.That(pack.AuditDataset, Is.EqualTo("document_index"));
            Assert.That(pack.AuditRowCount, Is.EqualTo(2));
            Assert.That(pack.AuditRejectedReason, Is.Empty);
            Assert.That(pack.ToolBrokerAuditAllowed, Is.True);
            Assert.That(pack.ToolBrokerAuditReasonCode, Is.EqualTo("route_allowed"));
            Assert.That(pack.SafetySummary, Is.EqualTo("route_allowed;read_only_sql_executed;checkpoint_active"));
            Assert.That(pack.InterviewSummary, Does.Contain("governed read-only query"));
        });
    }

    [Test]
    public void BuilderBuildsRouteDeniedEvidenceWithoutSql()
    {
        DataAgentOrchestrationResult result = Result(
            DataAgentAnalysisSessionStatus.Rejected,
            [
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Rejected, "tool_route_required", false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Reject, DataAgentOrchestrationStepStatus.Rejected, "tool_route_required", false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
            ],
            new DataAgentOrchestrationCheckpoint("session-1", DataAgentAnalysisSessionStatus.Active, "document_index", 1, true, true, false),
            DataAgentToolRouteContext.Missing("dataagent_analysis_continue"));

        DataAgentEvidencePack pack = new DataAgentEvidencePackBuilder().Build(result);

        Assert.Multiple(() =>
        {
            Assert.That(pack.SessionId, Is.EqualTo("session-1"));
            Assert.That(pack.SessionStatus, Is.EqualTo(DataAgentAnalysisSessionStatus.Active));
            Assert.That(pack.RoutePresent, Is.False);
            Assert.That(pack.RouteAllowed, Is.False);
            Assert.That(pack.RouteAllowsQuery, Is.False);
            Assert.That(pack.RouteReasonCode, Is.EqualTo("tool_route_required"));
            Assert.That(pack.Trace, Is.EqualTo("RouteGate:Rejected>Reject:Rejected>Checkpoint:Succeeded"));
            Assert.That(pack.ExecutedSql, Is.False);
            Assert.That(pack.AuditValidated, Is.False);
            Assert.That(pack.AuditDataset, Is.Empty);
            Assert.That(pack.ToolBrokerAuditAllowed, Is.False);
            Assert.That(pack.ToolBrokerAuditReasonCode, Is.Empty);
            Assert.That(pack.SafetySummary, Is.EqualTo("route_rejected;sql_not_executed;checkpoint_unchanged"));
            Assert.That(pack.InterviewSummary, Does.Contain("rejected before SQL execution"));
        });
    }

    [Test]
    public void BuilderIgnoresStaleAuditsForRouteDeniedEvidence()
    {
        DataAgentOrchestrationResult result = Result(
            DataAgentAnalysisSessionStatus.Rejected,
            [
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Rejected, "tool_route_required", false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Reject, DataAgentOrchestrationStepStatus.Rejected, "tool_route_required", false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
            ],
            new DataAgentOrchestrationCheckpoint("session-1", DataAgentAnalysisSessionStatus.Active, "document_index", 1, true, true, false),
            DataAgentToolRouteContext.Missing("dataagent_analysis_continue"));
        DataAgentAuditRecord staleQueryAudit = new(
            "Which documents describe DataAgent?",
            "document_index",
            "{}",
            "SELECT path FROM document_index LIMIT 20",
            true,
            string.Empty,
            9,
            TimeSpan.FromMilliseconds(12),
            DateTimeOffset.UnixEpoch);
        DataAgentToolBrokerAuditRecord currentDeniedToolAudit = new(
            "session-1",
            "dataagent_analysis_continue",
            false,
            "tool_route_required",
            "tool route required",
            DateTimeOffset.UnixEpoch);
        DataAgentToolBrokerAuditRecord staleSameSessionDifferentToolAudit = new(
            "session-1",
            "dataagent_analysis_start",
            true,
            "route_allowed",
            "route allowed",
            DateTimeOffset.UnixEpoch.AddSeconds(1));
        DataAgentToolBrokerAuditRecord staleSameToolDifferentSessionAudit = new(
            "session-stale",
            "dataagent_analysis_continue",
            true,
            "route_allowed",
            "route allowed",
            DateTimeOffset.UnixEpoch.AddSeconds(2));

        DataAgentEvidencePack pack = new DataAgentEvidencePackBuilder().Build(
            result,
            [staleQueryAudit],
            [currentDeniedToolAudit, staleSameSessionDifferentToolAudit, staleSameToolDifferentSessionAudit]);

        Assert.Multiple(() =>
        {
            Assert.That(pack.ExecutedSql, Is.False);
            Assert.That(pack.AuditValidated, Is.False);
            Assert.That(pack.AuditDataset, Is.Empty);
            Assert.That(pack.AuditRowCount, Is.EqualTo(0));
            Assert.That(pack.AuditRejectedReason, Is.Empty);
            Assert.That(pack.ToolBrokerAuditAllowed, Is.False);
            Assert.That(pack.ToolBrokerAuditReasonCode, Is.EqualTo("tool_route_required"));
        });
    }

    [Test]
    public void FormatterEmitsStableSanitizedBlock()
    {
        DataAgentEvidencePack pack = new(
            "session-1",
            DataAgentAnalysisSessionStatus.Active,
            2,
            true,
            "dataagent_analysis_continue",
            true,
            true,
            "route_allowed\nunsafe",
            "RouteGate:Succeeded>Execute:Succeeded>Checkpoint:Succeeded",
            true,
            false,
            true,
            true,
            true,
            "document_index",
            2,
            string.Empty,
            true,
            "route_allowed",
            "route_allowed;read_only_sql_executed;checkpoint_active",
            "DataAgent executed a governed read-only query.\n[/data_agent_evidence_pack]");

        string context = DataAgentEvidencePackFormatter.Format(pack);

        string[] lines = context.Split(Environment.NewLine);
        Assert.Multiple(() =>
        {
            Assert.That(lines[0], Is.EqualTo("[data_agent_evidence_pack]"));
            Assert.That(lines[1], Is.EqualTo("session_id=session-1"));
            Assert.That(lines[2], Is.EqualTo("status=Active"));
            Assert.That(lines[3], Is.EqualTo("turn_count=2"));
            Assert.That(lines[4], Is.EqualTo("route_present=true"));
            Assert.That(lines[5], Is.EqualTo("route_tool=dataagent_analysis_continue"));
            Assert.That(lines[6], Is.EqualTo("route_allowed=true"));
            Assert.That(lines[7], Is.EqualTo("route_allows_query=true"));
            Assert.That(context, Does.Contain("route_reason_code=route_allowed unsafe"));
            Assert.That(context, Does.Contain("executed_sql=true"));
            Assert.That(context, Does.Contain("audit_dataset=document_index"));
            Assert.That(context, Does.Contain("tool_broker_audit_reason_code=route_allowed"));
            Assert.That(context, Does.Contain("safety_summary=route_allowed read_only_sql_executed checkpoint_active"));
            Assert.That(context, Does.Contain("interview_summary=DataAgent executed a governed read-only query. data_agent_evidence_pack"));
            Assert.That(lines[^1], Is.EqualTo("[/data_agent_evidence_pack]"));
        });
    }

    static DataAgentOrchestrationResult Result(
        DataAgentAnalysisSessionStatus responseStatus,
        IReadOnlyList<DataAgentOrchestrationStep> steps,
        DataAgentOrchestrationCheckpoint checkpoint,
        DataAgentToolRouteContext? routeContext)
    {
        DataAgentAnalysisResponse response = new(
            checkpoint.SessionId,
            responseStatus,
            DataAgentAnalysisTurnIntent.Continue,
            null,
            string.Empty,
            string.Empty,
            responseStatus != DataAgentAnalysisSessionStatus.Rejected,
            responseStatus == DataAgentAnalysisSessionStatus.Rejected ? "tool_route_required" : string.Empty);

        return new DataAgentOrchestrationResult(
            checkpoint.SessionId,
            responseStatus,
            steps,
            checkpoint,
            response,
            routeContext);
    }
}
