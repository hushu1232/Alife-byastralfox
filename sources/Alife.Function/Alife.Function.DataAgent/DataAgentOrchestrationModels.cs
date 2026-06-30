namespace Alife.Function.DataAgent;

public enum DataAgentOrchestrationNodeKind
{
    RouteGate,
    SchemaContext,
    Plan,
    Validate,
    Execute,
    Explain,
    Clarification,
    Summarize,
    End,
    Reject,
    Checkpoint
}

public enum DataAgentOrchestrationStepStatus
{
    Succeeded,
    Skipped,
    Rejected,
    Failed
}

public sealed record DataAgentOrchestrationStep(
    DataAgentOrchestrationNodeKind Node,
    DataAgentOrchestrationStepStatus Status,
    string Reason,
    bool ExecutedSql);

public sealed record DataAgentOrchestrationCheckpoint(
    string SessionId,
    DataAgentAnalysisSessionStatus SessionStatus,
    string LastDataset,
    int TurnCount,
    bool CanContinue,
    bool CanSummarize,
    bool Terminal);

public sealed record DataAgentOrchestrationRequest(
    string CallerId,
    string Input,
    string? SessionId,
    bool RouteAllowsQuery,
    DataAgentToolRouteContext? RouteContext = null);

public sealed record DataAgentOrchestrationResult(
    string SessionId,
    DataAgentAnalysisSessionStatus SessionStatus,
    IReadOnlyList<DataAgentOrchestrationStep> Steps,
    DataAgentOrchestrationCheckpoint Checkpoint,
    DataAgentAnalysisResponse Response,
    DataAgentToolRouteContext? RouteContext = null);
