namespace Alife.Function.DataAgent;

public enum DataAgentGraphSidecarProgressStatus
{
    Started,
    Completed,
    Skipped,
    Rejected,
    Failed
}

public sealed record DataAgentGraphSidecarProgressEvent(
    string RequestId,
    string SessionId,
    string NodeName,
    DataAgentGraphSidecarProgressStatus Status,
    string ReasonCode,
    string Message,
    DateTimeOffset CreatedAt,
    IReadOnlyDictionary<string, string> Facts);

public sealed record DataAgentGraphSidecarProgressBridgeResult(
    int AcceptedCount,
    int RejectedCount);
