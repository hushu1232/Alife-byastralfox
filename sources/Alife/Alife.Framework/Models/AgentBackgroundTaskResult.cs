using System;

namespace Alife.Framework;

public enum AgentBackgroundTaskStatus
{
    Completed,
    Failed,
}

public sealed record AgentBackgroundTaskResult(
    string TaskId,
    string TaskName,
    string SourceSessionId,
    AgentBackgroundTaskStatus Status,
    string ResultText,
    string? Error,
    DateTimeOffset CompletedAt)
{
    public const string WakeResultKey = "agent.background.result";

    public static AgentBackgroundTaskResult Completed(
        string taskId,
        string taskName,
        string sourceSessionId,
        string resultText,
        DateTimeOffset? completedAt = null)
    {
        return new AgentBackgroundTaskResult(
            taskId,
            taskName,
            sourceSessionId,
            AgentBackgroundTaskStatus.Completed,
            resultText,
            Error: null,
            completedAt ?? DateTimeOffset.Now);
    }

    public static AgentBackgroundTaskResult Failed(
        string taskId,
        string taskName,
        string sourceSessionId,
        string error,
        DateTimeOffset? completedAt = null)
    {
        return new AgentBackgroundTaskResult(
            taskId,
            taskName,
            sourceSessionId,
            AgentBackgroundTaskStatus.Failed,
            ResultText: string.Empty,
            error,
            completedAt ?? DateTimeOffset.Now);
    }

    public AgentEvent ToWakeEvent()
    {
        bool failed = Status == AgentBackgroundTaskStatus.Failed;
        string text = failed
            ? $"Background task `{TaskName}` failed: {Error}"
            : $"Background task `{TaskName}` completed: {ResultText}";
        AgentEvent agentEvent = new(
            Type: failed ? "agent.background.failed" : "agent.background.completed",
            Source: "system",
            SessionId: SourceSessionId,
            ActorId: null,
            Text: text);
        agentEvent.State[WakeResultKey] = this;
        return agentEvent;
    }
}
