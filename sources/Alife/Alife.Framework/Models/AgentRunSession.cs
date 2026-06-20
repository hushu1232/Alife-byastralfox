using System;
using System.Collections.Generic;

namespace Alife.Framework;

public enum AgentRunStatus
{
    Running,
    Completed,
    Failed,
    Cancelled,
}

public sealed record AgentRunToolStep(
    string ToolName,
    string ResultSummary,
    DateTimeOffset Timestamp);

public sealed record AgentRunSnapshot(
    string RunId,
    AgentRunStatus Status,
    string SourceEventType,
    string SourceSessionId,
    StreamingOutputMode StreamingMode,
    DateTimeOffset StartedAt,
    DateTimeOffset? FirstContentAt,
    DateTimeOffset? EndedAt,
    TimeSpan? FirstContentLatency,
    TimeSpan? Duration,
    string? Error,
    IReadOnlyList<AgentRunToolStep> ToolSteps);

public sealed class AgentRunSession
{
    readonly AgentEvent sourceEvent;
    readonly StreamingOutputPolicy streamingPolicy;
    readonly List<AgentRunToolStep> toolSteps = [];

    AgentRunSession(AgentEvent sourceEvent, StreamingOutputPolicy streamingPolicy, DateTimeOffset startedAt)
    {
        this.sourceEvent = sourceEvent;
        this.streamingPolicy = streamingPolicy;
        StartedAt = startedAt;
    }

    public string RunId { get; } = Guid.NewGuid().ToString("N");
    public AgentRunStatus Status { get; private set; } = AgentRunStatus.Running;
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset? FirstContentAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }
    public string? Error { get; private set; }

    public static AgentRunSession Start(
        AgentEvent sourceEvent,
        StreamingOutputPolicy streamingPolicy,
        DateTimeOffset? startedAt = null)
    {
        return new AgentRunSession(sourceEvent, streamingPolicy, startedAt ?? DateTimeOffset.Now);
    }

    public void MarkFirstContent(DateTimeOffset? timestamp = null)
    {
        FirstContentAt ??= timestamp ?? DateTimeOffset.Now;
    }

    public void RecordToolStep(string toolName, string resultSummary, DateTimeOffset? timestamp = null)
    {
        toolSteps.Add(new AgentRunToolStep(toolName, resultSummary, timestamp ?? DateTimeOffset.Now));
    }

    public void Complete(DateTimeOffset? endedAt = null)
    {
        Status = AgentRunStatus.Completed;
        EndedAt = endedAt ?? DateTimeOffset.Now;
    }

    public void Fail(string error, DateTimeOffset? endedAt = null)
    {
        Status = AgentRunStatus.Failed;
        Error = error;
        EndedAt = endedAt ?? DateTimeOffset.Now;
    }

    public void Cancel(DateTimeOffset? endedAt = null)
    {
        Status = AgentRunStatus.Cancelled;
        EndedAt = endedAt ?? DateTimeOffset.Now;
    }

    public AgentRunSnapshot Snapshot()
    {
        TimeSpan? firstContentLatency = FirstContentAt == null
            ? null
            : FirstContentAt.Value - StartedAt;
        TimeSpan? duration = EndedAt == null
            ? null
            : EndedAt.Value - StartedAt;

        return new AgentRunSnapshot(
            RunId,
            Status,
            sourceEvent.Type,
            sourceEvent.SessionId,
            streamingPolicy.Mode,
            StartedAt,
            FirstContentAt,
            EndedAt,
            firstContentLatency,
            duration,
            Error,
            toolSteps.ToArray());
    }
}
