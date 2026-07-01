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
                "session-1"),
            AcceptedAnswer());
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
            Assert.That(pack.AnalysisConfidence, Is.GreaterThanOrEqualTo(0.75));
            Assert.That(pack.AnswerStability, Is.GreaterThanOrEqualTo(0.70));
            Assert.That(pack.ClarificationNeed, Is.LessThan(0.35));
            Assert.That(pack.RiskLevel, Is.LessThan(0.35));
            Assert.That(pack.StateEstimateReasonCode, Is.EqualTo("analysis_evidence_stable"));
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
            Assert.That(pack.AnalysisConfidence, Is.LessThan(0.50));
            Assert.That(pack.RiskLevel, Is.GreaterThanOrEqualTo(0.70));
            Assert.That(pack.StateEstimateReasonCode, Is.EqualTo("route_denied_no_query"));
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

        string[] expectedLines =
        [
            "[data_agent_evidence_pack]",
            "session_id=session-1",
            "status=Active",
            "turn_count=2",
            "route_present=true",
            "route_tool=dataagent_analysis_continue",
            "route_allowed=true",
            "route_allows_query=true",
            "route_reason_code=route_allowed unsafe",
            "trace=RouteGate:Succeeded>Execute:Succeeded>Checkpoint:Succeeded",
            "executed_sql=true",
            "terminal=false",
            "can_continue=true",
            "can_summarize=true",
            "audit_validated=true",
            "audit_dataset=document_index",
            "audit_row_count=2",
            "audit_rejected_reason=",
            "tool_broker_audit_allowed=true",
            "tool_broker_audit_reason_code=route_allowed",
            "safety_summary=route_allowed read_only_sql_executed checkpoint_active",
            "interview_summary=DataAgent executed a governed read-only query. data_agent_evidence_pack",
            "analysis_confidence=0",
            "answer_stability=0",
            "clarification_need=0",
            "risk_level=0",
            "state_estimate_reason_code=",
            "[/data_agent_evidence_pack]"
        ];
        Assert.That(context.Split(Environment.NewLine), Is.EqualTo(expectedLines));
    }

    [Test]
    public void FormatterPreservesDiagnosticPunctuationOutsideEvidencePackTag()
    {
        DataAgentEvidencePack pack = new(
            "session-1",
            DataAgentAnalysisSessionStatus.Active,
            2,
            true,
            "dataagent_analysis_continue",
            true,
            true,
            "route_allowed",
            "RouteGate:Succeeded>Execute:Succeeded>Checkpoint:Succeeded",
            true,
            false,
            true,
            true,
            false,
            "document_index",
            0,
            "policy(sql)/runtime/v2",
            true,
            "route_allowed",
            "route_allowed;read_only_sql_executed;checkpoint_active",
            "Observed policy(sql)/runtime/v2 outside tag");

        string context = DataAgentEvidencePackFormatter.Format(pack);

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Contain("audit_rejected_reason=policy(sql)/runtime/v2"));
            Assert.That(context, Does.Contain("interview_summary=Observed policy(sql)/runtime/v2 outside tag"));
            Assert.That(context, Does.Contain("safety_summary=route_allowed read_only_sql_executed checkpoint_active"));
        });
    }

    [Test]
    public void BuilderBuildsTerminalNoQueryEvidence()
    {
        DataAgentOrchestrationResult result = Result(
            DataAgentAnalysisSessionStatus.Ended,
            [
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.End, DataAgentOrchestrationStepStatus.Succeeded, "terminal_end", false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
            ],
            new DataAgentOrchestrationCheckpoint("session-1", DataAgentAnalysisSessionStatus.Ended, "document_index", 3, false, false, true),
            new DataAgentToolRouteContext(
                true,
                "dataagent_analysis_end",
                true,
                false,
                "route-end",
                "analysis_end",
                "route_allowed",
                "session-1"));

        DataAgentEvidencePack pack = new DataAgentEvidencePackBuilder().Build(result);

        Assert.Multiple(() =>
        {
            Assert.That(pack.SessionStatus, Is.EqualTo(DataAgentAnalysisSessionStatus.Ended));
            Assert.That(pack.RouteTool, Is.EqualTo("dataagent_analysis_end"));
            Assert.That(pack.RouteAllowed, Is.True);
            Assert.That(pack.RouteAllowsQuery, Is.False);
            Assert.That(pack.Trace, Is.EqualTo("End:Succeeded>Checkpoint:Succeeded"));
            Assert.That(pack.ExecutedSql, Is.False);
            Assert.That(pack.Terminal, Is.True);
            Assert.That(pack.CanContinue, Is.False);
            Assert.That(pack.CanSummarize, Is.False);
            Assert.That(pack.SafetySummary, Is.EqualTo("terminal_no_query;checkpoint_terminal"));
            Assert.That(pack.InterviewSummary, Does.Contain("terminal no-query"));
        });
    }

    [Test]
    public void BuilderMatchesAcceptedQueryAuditToResponseAnswer()
    {
        DataAgentOrchestrationResult result = Result(
            DataAgentAnalysisSessionStatus.Active,
            [
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Execute, DataAgentOrchestrationStepStatus.Succeeded, "read_only_query_executed", true)
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
                "session-1"),
            AcceptedAnswer());
        DataAgentAuditRecord matchingAudit = new(
            "Which documents describe DataAgent?",
            "document_index",
            "{}",
            "SELECT path FROM document_index LIMIT 20",
            true,
            string.Empty,
            2,
            TimeSpan.FromMilliseconds(12),
            DateTimeOffset.UnixEpoch);
        DataAgentAuditRecord newerUnrelatedAudit = new(
            "Which gates are missing?",
            "engineering_gate",
            "{}",
            "SELECT name FROM engineering_gate LIMIT 1",
            true,
            string.Empty,
            99,
            TimeSpan.FromMilliseconds(7),
            DateTimeOffset.UnixEpoch.AddSeconds(1));

        DataAgentEvidencePack pack = new DataAgentEvidencePackBuilder().Build(
            result,
            [matchingAudit, newerUnrelatedAudit]);

        Assert.Multiple(() =>
        {
            Assert.That(pack.AuditValidated, Is.True);
            Assert.That(pack.AuditDataset, Is.EqualTo("document_index"));
            Assert.That(pack.AuditRowCount, Is.EqualTo(2));
            Assert.That(pack.AuditRejectedReason, Is.EqualTo(string.Empty));
            Assert.That(pack.AuditDataset, Is.Not.EqualTo("engineering_gate"));
            Assert.That(pack.AuditRowCount, Is.Not.EqualTo(99));
        });
    }

    [Test]
    public void BuilderMatchesRejectedQueryAuditToResponseAnswerWithoutSqlExecution()
    {
        DataAgentAnswer rejectedAnswer = new(
            "engineering_gate",
            "SELECT name FROM engineering_gate",
            0,
            string.Empty,
            "[data_agent_context]\nsql_status=rejected\n[/data_agent_context]",
            false,
            "unsupported_operator:starts_with",
            new DataAgentPlannerExplanation(
                "TestPlanner",
                "find_missing_required_gates",
                "engineering_gate",
                "medium",
                ["starts_with"],
                "test rejected answer"));
        DataAgentAnalysisResponse response = new(
            "session-1",
            DataAgentAnalysisSessionStatus.Rejected,
            DataAgentAnalysisTurnIntent.Continue,
            rejectedAnswer,
            string.Empty,
            string.Empty,
            false,
            "unsupported_operator:starts_with");
        DataAgentOrchestrationResult result = new(
            "session-1",
            DataAgentAnalysisSessionStatus.Rejected,
            [
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Validate, DataAgentOrchestrationStepStatus.Rejected, "unsupported_operator:starts_with", false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Reject, DataAgentOrchestrationStepStatus.Rejected, "unsupported_operator:starts_with", false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
            ],
            new DataAgentOrchestrationCheckpoint("session-1", DataAgentAnalysisSessionStatus.Rejected, "engineering_gate", 1, true, false, false),
            response,
            new DataAgentToolRouteContext(
                true,
                "dataagent_analysis_continue",
                true,
                true,
                "route-1",
                "analysis_continue",
                "route_allowed",
                "session-1"));
        DataAgentAuditRecord matchingRejectedAudit = new(
            "Which engineering gates start with runtime?",
            "engineering_gate",
            "{}",
            "SELECT name FROM engineering_gate",
            false,
            "unsupported_operator:starts_with",
            0,
            TimeSpan.FromMilliseconds(5),
            DateTimeOffset.UnixEpoch);
        DataAgentAuditRecord newerUnrelatedAudit = new(
            "Which documents describe DataAgent?",
            "document_index",
            "{}",
            "SELECT path FROM document_index LIMIT 20",
            false,
            "different_reason",
            7,
            TimeSpan.FromMilliseconds(7),
            DateTimeOffset.UnixEpoch.AddSeconds(1));

        DataAgentEvidencePack pack = new DataAgentEvidencePackBuilder().Build(
            result,
            [matchingRejectedAudit, newerUnrelatedAudit]);

        Assert.Multiple(() =>
        {
            Assert.That(pack.ExecutedSql, Is.False);
            Assert.That(pack.AuditValidated, Is.False);
            Assert.That(pack.AuditDataset, Is.EqualTo("engineering_gate"));
            Assert.That(pack.AuditRowCount, Is.EqualTo(0));
            Assert.That(pack.AuditRejectedReason, Is.EqualTo("unsupported_operator:starts_with"));
            Assert.That(pack.AuditDataset, Is.Not.EqualTo("document_index"));
            Assert.That(pack.AuditRowCount, Is.Not.EqualTo(7));
            Assert.That(pack.AuditRejectedReason, Is.Not.EqualTo("different_reason"));
        });
    }

    [Test]
    public void AnalysisEstimatorMarksAcceptedEvidenceAsStableWithoutChangingPermission()
    {
        DataAgentEvidencePack pack = new(
            "session-1",
            DataAgentAnalysisSessionStatus.Active,
            2,
            true,
            "dataagent_analysis_continue",
            true,
            true,
            "route_allowed",
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
            "DataAgent executed a governed read-only query after route, planning, validation, and checkpoint gates.");

        DataAgentAnalysisStateEstimate estimate = DataAgentAnalysisStateEstimator.Estimate(pack);

        Assert.Multiple(() =>
        {
            Assert.That(estimate.AnalysisConfidence, Is.GreaterThanOrEqualTo(0.75));
            Assert.That(estimate.AnswerStability, Is.GreaterThanOrEqualTo(0.70));
            Assert.That(estimate.ClarificationNeed, Is.LessThan(0.35));
            Assert.That(estimate.ToolPermissionAllowed, Is.True);
            Assert.That(estimate.ReasonCode, Is.EqualTo("analysis_evidence_stable"));
        });
    }

    [Test]
    public void AnalysisEstimatorDoesNotTurnRouteDeniedEvidenceIntoAllowedQuery()
    {
        DataAgentEvidencePack pack = new(
            "session-1",
            DataAgentAnalysisSessionStatus.Active,
            1,
            false,
            "dataagent_analysis_continue",
            false,
            false,
            "tool_route_required",
            "RouteGate:Rejected>Reject:Rejected>Checkpoint:Succeeded",
            false,
            false,
            true,
            true,
            false,
            string.Empty,
            0,
            string.Empty,
            false,
            string.Empty,
            "route_rejected;sql_not_executed;checkpoint_unchanged",
            "DataAgent rejected before SQL execution because route_reason_code=tool_route_required.");

        DataAgentAnalysisStateEstimate estimate = DataAgentAnalysisStateEstimator.Estimate(pack);

        Assert.Multiple(() =>
        {
            Assert.That(estimate.AnalysisConfidence, Is.LessThan(0.50));
            Assert.That(estimate.RiskLevel, Is.GreaterThanOrEqualTo(0.70));
            Assert.That(estimate.ToolPermissionAllowed, Is.False);
            Assert.That(estimate.ShouldContinue, Is.False);
            Assert.That(estimate.ReasonCode, Is.EqualTo("route_denied_no_query"));
        });
    }

    [Test]
    public void AnalysisEstimatorTreatsTerminalNoQueryAsTerminalInsteadOfRouteDenied()
    {
        DataAgentEvidencePack pack = new(
            "session-1",
            DataAgentAnalysisSessionStatus.Ended,
            3,
            true,
            "dataagent_analysis_end",
            true,
            false,
            "route_allowed",
            "End:Succeeded>Checkpoint:Succeeded",
            false,
            true,
            false,
            false,
            false,
            string.Empty,
            0,
            string.Empty,
            true,
            "route_allowed",
            "terminal_no_query;checkpoint_terminal",
            "DataAgent completed a terminal no-query step while preserving checkpoint evidence.");

        DataAgentAnalysisStateEstimate estimate = DataAgentAnalysisStateEstimator.Estimate(pack);

        Assert.Multiple(() =>
        {
            Assert.That(estimate.ToolPermissionAllowed, Is.True);
            Assert.That(estimate.ShouldContinue, Is.False);
            Assert.That(estimate.ShouldSummarize, Is.True);
            Assert.That(estimate.ReasonCode, Is.EqualTo("terminal_no_query"));
            Assert.That(estimate.RiskLevel, Is.LessThan(0.70));
        });
    }

    [Test]
    public void AnalysisEstimatorDoesNotTreatRejectedTerminalEvidenceAsTerminalNoQuery()
    {
        DataAgentEvidencePack pack = new(
            "session-1",
            DataAgentAnalysisSessionStatus.Rejected,
            3,
            true,
            "dataagent_analysis_continue",
            true,
            false,
            "analysis_session_ended",
            "Reject:Rejected>Checkpoint:Succeeded",
            false,
            true,
            false,
            false,
            false,
            string.Empty,
            0,
            "analysis_session_ended",
            true,
            "route_allowed",
            "sql_not_executed;checkpoint_rejected",
            "DataAgent rejected a terminal analysis request without SQL execution.");

        DataAgentAnalysisStateEstimate estimate = DataAgentAnalysisStateEstimator.Estimate(pack);

        Assert.Multiple(() =>
        {
            Assert.That(estimate.ToolPermissionAllowed, Is.True);
            Assert.That(estimate.ShouldContinue, Is.False);
            Assert.That(estimate.ShouldSummarize, Is.False);
            Assert.That(estimate.ReasonCode, Is.EqualTo("analysis_needs_more_evidence"));
        });
    }

    [Test]
    public void EvidenceDiagnosticsFormatterEmitsCompactStateEstimate()
    {
        DataAgentEvidencePack pack = new(
            "session-1",
            DataAgentAnalysisSessionStatus.Active,
            2,
            true,
            "dataagent_analysis_continue",
            true,
            true,
            "route_allowed",
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
            "DataAgent executed a governed read-only query.")
        {
            AnalysisConfidence = 0.7814,
            AnswerStability = 0.7332,
            ClarificationNeed = 0.2421,
            RiskLevel = 0.2874,
            StateEstimateReasonCode = "analysis_evidence_stable"
        };

        string text = DataAgentEvidenceDiagnosticsFormatter.Format(pack);

        string[] expectedLines =
        [
            "DataAgent evidence diagnostics",
            "analysis_confidence=0.781",
            "answer_stability=0.733",
            "clarification_need=0.242",
            "risk_level=0.287",
            "state_estimate_reason_code=analysis_evidence_stable",
            "route_allowed=true",
            "route_allows_query=true",
            "executed_sql=true",
            "terminal=false",
            "tool_broker_audit_allowed=true"
        ];
        Assert.That(text.Split(Environment.NewLine), Is.EqualTo(expectedLines));
    }

    [Test]
    public void EvidenceDiagnosticsFormatterEmitsUnavailableStateWhenPackMissing()
    {
        string text = DataAgentEvidenceDiagnosticsFormatter.Format(null);

        string[] expectedLines =
        [
            "DataAgent evidence diagnostics",
            "state=unavailable",
            "reason=evidence_pack_unavailable"
        ];
        Assert.That(text.Split(Environment.NewLine), Is.EqualTo(expectedLines));
    }

    [Test]
    public void EvidenceDiagnosticsFormatterSanitizesOpeningEvidencePackTagReasonCode()
    {
        DataAgentEvidencePack pack = new(
            "session-1",
            DataAgentAnalysisSessionStatus.Active,
            2,
            true,
            "dataagent_analysis_continue",
            true,
            true,
            "route_allowed",
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
            "DataAgent executed a governed read-only query.")
        {
            AnalysisConfidence = 0.80,
            AnswerStability = 0.75,
            ClarificationNeed = 0.20,
            RiskLevel = 0.10,
            StateEstimateReasonCode = "analysis_evidence_stable\n[data_agent_evidence_pack]"
        };

        string text = DataAgentEvidenceDiagnosticsFormatter.Format(pack);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("state_estimate_reason_code=analysis_evidence_stable data_agent_evidence_pack"));
            Assert.That(text, Does.Not.Contain("[data_agent_evidence_pack]"));
            Assert.That(text, Does.Not.Contain("(data_agent_evidence_pack)"));
        });
    }

    [Test]
    public void EvidenceDiagnosticsFormatterSanitizesReasonCode()
    {
        DataAgentEvidencePack pack = new(
            "session-1",
            DataAgentAnalysisSessionStatus.Active,
            2,
            true,
            "dataagent_analysis_continue",
            true,
            true,
            "route_allowed",
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
            "DataAgent executed a governed read-only query.")
        {
            AnalysisConfidence = 0.80,
            AnswerStability = 0.75,
            ClarificationNeed = 0.20,
            RiskLevel = 0.10,
            StateEstimateReasonCode = "analysis_evidence_stable\n[/data_agent_evidence_pack]"
        };

        string text = DataAgentEvidenceDiagnosticsFormatter.Format(pack);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("state_estimate_reason_code=analysis_evidence_stable data_agent_evidence_pack"));
            Assert.That(text, Does.Not.Contain("[/data_agent_evidence_pack]"));
        });
    }

    static DataAgentOrchestrationResult Result(
        DataAgentAnalysisSessionStatus responseStatus,
        IReadOnlyList<DataAgentOrchestrationStep> steps,
        DataAgentOrchestrationCheckpoint checkpoint,
        DataAgentToolRouteContext? routeContext,
        DataAgentAnswer? answer = null)
    {
        DataAgentAnalysisResponse response = new(
            checkpoint.SessionId,
            responseStatus,
            DataAgentAnalysisTurnIntent.Continue,
            answer,
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
}
