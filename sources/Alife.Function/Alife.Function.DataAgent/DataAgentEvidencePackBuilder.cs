namespace Alife.Function.DataAgent;

public sealed class DataAgentEvidencePackBuilder
{
    public DataAgentEvidencePack Build(
        DataAgentOrchestrationResult result,
        IReadOnlyList<DataAgentAuditRecord>? queryAudit = null,
        IReadOnlyList<DataAgentToolBrokerAuditRecord>? toolBrokerAudit = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        DataAgentAuditRecord? latestAudit = queryAudit?.LastOrDefault();
        DataAgentToolBrokerAuditRecord? latestToolAudit = FindToolBrokerAudit(result, toolBrokerAudit);
        bool executedSql = result.Steps.Any(step => step.ExecutedSql);
        string trace = BuildTrace(result.Steps);
        string routeReasonCode = result.RouteContext?.ReasonCode ?? string.Empty;

        return new DataAgentEvidencePack(
            result.SessionId,
            result.Checkpoint.SessionStatus,
            result.Checkpoint.TurnCount,
            result.RouteContext?.Present == true,
            result.RouteContext?.ToolName ?? string.Empty,
            result.RouteContext?.AllowsTool == true,
            result.RouteContext?.AllowsQuery == true,
            routeReasonCode,
            trace,
            executedSql,
            result.Checkpoint.Terminal,
            result.Checkpoint.CanContinue,
            result.Checkpoint.CanSummarize,
            latestAudit?.Validated == true,
            latestAudit?.Dataset ?? string.Empty,
            latestAudit?.RowCount ?? 0,
            latestAudit?.RejectedReason ?? string.Empty,
            latestToolAudit?.Allowed == true,
            latestToolAudit?.ReasonCode ?? string.Empty,
            BuildSafetySummary(result, executedSql),
            BuildInterviewSummary(result, executedSql, routeReasonCode));
    }

    static DataAgentToolBrokerAuditRecord? FindToolBrokerAudit(
        DataAgentOrchestrationResult result,
        IReadOnlyList<DataAgentToolBrokerAuditRecord>? records)
    {
        if (records is null || records.Count == 0)
            return null;

        string routeTool = result.RouteContext?.ToolName ?? string.Empty;
        return records.LastOrDefault(record =>
            string.Equals(record.SessionId, result.SessionId, StringComparison.Ordinal) ||
            string.Equals(record.ToolName, routeTool, StringComparison.Ordinal));
    }

    static string BuildTrace(IEnumerable<DataAgentOrchestrationStep> steps)
    {
        return string.Join(">", steps.Select(step => $"{step.Node}:{step.Status}"));
    }

    static string BuildSafetySummary(DataAgentOrchestrationResult result, bool executedSql)
    {
        bool routeRejected = result.Steps.Any(step =>
            step.Node == DataAgentOrchestrationNodeKind.RouteGate &&
            step.Status == DataAgentOrchestrationStepStatus.Rejected);
        bool terminalNoQuery = executedSql == false &&
            result.Steps.Any(step =>
                step.Node is DataAgentOrchestrationNodeKind.Summarize or DataAgentOrchestrationNodeKind.End);

        if (routeRejected)
            return "route_rejected;sql_not_executed;checkpoint_unchanged";

        if (terminalNoQuery)
            return result.Checkpoint.Terminal
                ? "terminal_no_query;checkpoint_terminal"
                : "terminal_no_query;checkpoint_active";

        if (executedSql)
            return $"route_allowed;read_only_sql_executed;checkpoint_{StatusToken(result.Checkpoint.SessionStatus)}";

        return $"sql_not_executed;checkpoint_{StatusToken(result.Checkpoint.SessionStatus)}";
    }

    static string BuildInterviewSummary(
        DataAgentOrchestrationResult result,
        bool executedSql,
        string routeReasonCode)
    {
        bool routeRejected = result.Steps.Any(step =>
            step.Node == DataAgentOrchestrationNodeKind.RouteGate &&
            step.Status == DataAgentOrchestrationStepStatus.Rejected);
        bool terminalNoQuery = executedSql == false &&
            result.Steps.Any(step =>
                step.Node is DataAgentOrchestrationNodeKind.Summarize or DataAgentOrchestrationNodeKind.End);

        if (routeRejected)
            return $"DataAgent rejected before SQL execution because route_reason_code={routeReasonCode}.";

        if (terminalNoQuery)
            return "DataAgent completed a terminal no-query step while preserving checkpoint evidence.";

        if (executedSql)
            return "DataAgent executed a governed read-only query after route, planning, validation, and checkpoint gates.";

        return "DataAgent produced orchestration evidence without SQL execution.";
    }

    static string StatusToken(DataAgentAnalysisSessionStatus status)
    {
        return status.ToString().ToLowerInvariant();
    }
}
