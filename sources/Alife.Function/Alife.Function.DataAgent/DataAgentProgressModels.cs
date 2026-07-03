namespace Alife.Function.DataAgent;

public enum DataAgentProgressEventKind
{
    RouteGate,
    SchemaContext,
    Planner,
    Validate,
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

public enum DataAgentProgressEventPhase
{
    Started,
    Completed
}

public enum DataAgentProgressEventStatus
{
    Running,
    Succeeded,
    Skipped,
    Rejected,
    Failed
}

public sealed record DataAgentProgressEvent(
    string SessionId,
    DataAgentProgressEventKind Kind,
    DataAgentProgressEventPhase Phase,
    DataAgentProgressEventStatus Status,
    string ReasonCode,
    int TurnCount,
    DateTimeOffset CreatedAt,
    bool ExecutedSql,
    bool QueryAllowed,
    bool Terminal,
    IReadOnlyDictionary<string, string> Facts);
