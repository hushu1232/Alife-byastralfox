namespace Alife.Function.DataAgent;

public enum DataAgentTraceEventKind
{
    RouteGate,
    SchemaContext,
    Planner,
    SqlSafety,
    Execute,
    EvidencePack,
    Checkpoint,
    Summarize,
    End,
    Answer,
    Reject,
    Explain,
    Clarification
}

public enum DataAgentTraceEventStatus
{
    Succeeded,
    Skipped,
    Rejected,
    Failed
}

public sealed record DataAgentTraceEvent(
    DataAgentTraceEventKind Kind,
    DataAgentTraceEventStatus Status,
    string ReasonCode,
    bool ExecutedSql,
    bool QueryAllowed,
    bool Terminal,
    IReadOnlyDictionary<string, string> Facts);

public sealed record DataAgentTraceTimeline(
    string SessionId,
    DataAgentAnalysisSessionStatus SessionStatus,
    int TurnCount,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    bool Terminal,
    IReadOnlyList<DataAgentTraceEvent> Events);
