namespace Alife.Function.DataAgent;

public sealed record DataAgentEvidencePack(
    string SessionId,
    DataAgentAnalysisSessionStatus SessionStatus,
    int TurnCount,
    bool RoutePresent,
    string RouteTool,
    bool RouteAllowed,
    bool RouteAllowsQuery,
    string RouteReasonCode,
    string Trace,
    bool ExecutedSql,
    bool Terminal,
    bool CanContinue,
    bool CanSummarize,
    bool AuditValidated,
    string AuditDataset,
    int AuditRowCount,
    string AuditRejectedReason,
    bool ToolBrokerAuditAllowed,
    string ToolBrokerAuditReasonCode,
    string SafetySummary,
    string InterviewSummary);
