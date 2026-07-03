using System.Collections.ObjectModel;
using System.Globalization;

namespace Alife.Function.DataAgent;

public sealed class DataAgentTraceTimelineBuilder
{
    public const int MaxEvents = 32;

    public DataAgentTraceTimeline Build(
        DataAgentOrchestrationResult result,
        DataAgentEvidencePack pack,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(pack);

        List<DataAgentTraceEvent> events = [];
        bool evidenceInserted = false;

        foreach (DataAgentOrchestrationStep step in result.Steps)
        {
            if (step.Node == DataAgentOrchestrationNodeKind.Checkpoint && evidenceInserted == false)
                evidenceInserted = TryAddEvent(events, BuildEvidencePackEvent(pack));

            if (events.Count >= MaxEvents)
                break;

            events.Add(BuildStepEvent(result, pack, step));
        }

        if (evidenceInserted == false && events.Count < MaxEvents)
            events.Add(BuildEvidencePackEvent(pack));

        return new DataAgentTraceTimeline(
            result.SessionId,
            result.SessionStatus,
            result.Checkpoint.TurnCount,
            now,
            now,
            result.Checkpoint.Terminal,
            events);
    }

    static bool TryAddEvent(List<DataAgentTraceEvent> events, DataAgentTraceEvent traceEvent)
    {
        if (events.Count >= MaxEvents)
            return false;

        events.Add(traceEvent);
        return true;
    }

    static DataAgentTraceEvent BuildStepEvent(
        DataAgentOrchestrationResult result,
        DataAgentEvidencePack pack,
        DataAgentOrchestrationStep step)
    {
        return new DataAgentTraceEvent(
            MapKind(step.Node),
            MapStatus(step.Status),
            NormalizeReasonCode(step.Reason),
            step.ExecutedSql,
            result.RouteContext?.AllowsQuery == true,
            result.Checkpoint.Terminal || step.Node is DataAgentOrchestrationNodeKind.Summarize or DataAgentOrchestrationNodeKind.End,
            BuildStepFacts(result, pack, step));
    }

    static IReadOnlyDictionary<string, string> BuildStepFacts(
        DataAgentOrchestrationResult result,
        DataAgentEvidencePack pack,
        DataAgentOrchestrationStep step)
    {
        Dictionary<string, string> facts = [];

        if (step.Node == DataAgentOrchestrationNodeKind.RouteGate)
        {
            facts["route_present"] = Bool(result.RouteContext?.Present == true);
            facts["route_allowed"] = Bool(result.RouteContext?.AllowsTool == true);
            facts["route_allows_query"] = Bool(result.RouteContext?.AllowsQuery == true);
            facts["route_reason"] = NormalizeReasonCode(result.RouteContext?.ReasonCode ?? string.Empty);
        }
        else if (step.Node == DataAgentOrchestrationNodeKind.Checkpoint)
        {
            facts["can_continue"] = Bool(result.Checkpoint.CanContinue);
            facts["can_summarize"] = Bool(result.Checkpoint.CanSummarize);
            facts["checkpoint_terminal"] = Bool(result.Checkpoint.Terminal);
        }
        else if (step.Node == DataAgentOrchestrationNodeKind.Execute)
        {
            facts["rows"] = pack.AuditRowCount.ToString(CultureInfo.InvariantCulture);
            facts["sql"] = "redacted";
        }

        return new ReadOnlyDictionary<string, string>(facts);
    }

    static DataAgentTraceEvent BuildEvidencePackEvent(DataAgentEvidencePack pack)
    {
        Dictionary<string, string> facts = new()
        {
            ["risk"] = pack.RiskLevel.ToString("0.###", CultureInfo.InvariantCulture),
            ["analysis_confidence"] = pack.AnalysisConfidence.ToString("0.###", CultureInfo.InvariantCulture),
            ["tool_broker_audit_allowed"] = Bool(pack.ToolBrokerAuditAllowed)
        };

        return new DataAgentTraceEvent(
            DataAgentTraceEventKind.EvidencePack,
            DataAgentTraceEventStatus.Succeeded,
            NormalizeReasonCode(pack.StateEstimateReasonCode),
            pack.ExecutedSql,
            pack.RouteAllowsQuery,
            pack.Terminal,
            new ReadOnlyDictionary<string, string>(facts));
    }

    static DataAgentTraceEventKind MapKind(DataAgentOrchestrationNodeKind node)
    {
        return node switch
        {
            DataAgentOrchestrationNodeKind.RouteGate => DataAgentTraceEventKind.RouteGate,
            DataAgentOrchestrationNodeKind.SchemaContext => DataAgentTraceEventKind.SchemaContext,
            DataAgentOrchestrationNodeKind.Plan => DataAgentTraceEventKind.Planner,
            DataAgentOrchestrationNodeKind.Validate => DataAgentTraceEventKind.SqlSafety,
            DataAgentOrchestrationNodeKind.Execute => DataAgentTraceEventKind.Execute,
            DataAgentOrchestrationNodeKind.Explain => DataAgentTraceEventKind.Explain,
            DataAgentOrchestrationNodeKind.Clarification => DataAgentTraceEventKind.Clarification,
            DataAgentOrchestrationNodeKind.Summarize => DataAgentTraceEventKind.Summarize,
            DataAgentOrchestrationNodeKind.End => DataAgentTraceEventKind.End,
            DataAgentOrchestrationNodeKind.Reject => DataAgentTraceEventKind.Reject,
            DataAgentOrchestrationNodeKind.Checkpoint => DataAgentTraceEventKind.Checkpoint,
            _ => DataAgentTraceEventKind.Answer
        };
    }

    static DataAgentTraceEventStatus MapStatus(DataAgentOrchestrationStepStatus status)
    {
        return status switch
        {
            DataAgentOrchestrationStepStatus.Succeeded => DataAgentTraceEventStatus.Succeeded,
            DataAgentOrchestrationStepStatus.Skipped => DataAgentTraceEventStatus.Skipped,
            DataAgentOrchestrationStepStatus.Rejected => DataAgentTraceEventStatus.Rejected,
            DataAgentOrchestrationStepStatus.Failed => DataAgentTraceEventStatus.Failed,
            _ => DataAgentTraceEventStatus.Failed
        };
    }

    static string Bool(bool value)
    {
        return value ? "true" : "false";
    }

    static string NormalizeReasonCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "reason_redacted";

        string trimmed = value.Trim();
        int separatorIndex = trimmed.IndexOf(':', StringComparison.Ordinal);
        if (separatorIndex > 0)
        {
            string leadingCode = trimmed[..separatorIndex];
            return IsSafeReasonCode(leadingCode) ? leadingCode : "reason_redacted";
        }

        return IsSafeReasonCode(trimmed) ? trimmed : "reason_redacted";
    }

    static bool IsSafeReasonCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        foreach (char current in value)
        {
            if (char.IsAsciiLetterOrDigit(current) || current is '_' or '-')
                continue;

            return false;
        }

        return true;
    }
}
