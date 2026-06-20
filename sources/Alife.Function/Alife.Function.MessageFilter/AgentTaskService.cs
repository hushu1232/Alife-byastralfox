using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;
using Alife.Platform;

namespace Alife.Function.Agent;

public enum AgentTaskStatus
{
    Planned,
    Running,
    Completed,
    Failed,
    Cancelled
}

public sealed record AgentTaskEvent(
    DateTimeOffset Timestamp,
    string Actor,
    string Kind,
    string Detail);

public sealed record AgentTaskState(
    string Id,
    string Goal,
    IReadOnlyList<string> Steps,
    AgentTaskStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<AgentTaskEvent> Events);

[Module(
    "Agent Task State",
    "Tracks multi-step agent tasks with lifecycle state and audit trail.",
    defaultCategory: "Alife Official/Agent",
    LaunchOrder = -62)]
public class AgentTaskService(
    AgentAuditLogService? auditLog = null,
    XmlFunctionCaller? functionCaller = null,
    string? taskStorePath = null)
    : InteractiveModule<AgentTaskService>
{
    readonly object syncRoot = new();
    readonly string taskStorePath = Path.GetFullPath(taskStorePath ?? Path.Combine(AlifePath.StorageFolderPath, "AgentWorkspace", "agent-tasks.json"));
    readonly Dictionary<string, AgentTaskState> tasks = LoadTasks(taskStorePath ?? Path.Combine(AlifePath.StorageFolderPath, "AgentWorkspace", "agent-tasks.json"));
    readonly AgentAuditLogService? auditLog = auditLog;

    [XmlFunction(FunctionMode.OneShot, name: "agent_task_status")]
    [Description("Show the status of a tracked agent task. If id is empty, shows the latest task.")]
    public void ShowAgentTaskStatus(string? id = null)
    {
        AgentTaskState? task = string.IsNullOrWhiteSpace(id) ? GetLatestTask() : GetTask(id);
        Poke(task == null ? "No agent task is being tracked." : FormatTask(task));
    }

    [XmlFunction(FunctionMode.OneShot, name: "agent_task_create")]
    [Description("Create a tracked multi-step agent task. Steps can be separated by new lines or semicolons.")]
    public void AgentTaskCreate(string goal, string steps = "", string actor = "agent")
    {
        ReportTask(CreateTaskFromText(actor, goal, steps));
    }

    [XmlFunction(FunctionMode.OneShot, name: "agent_task_start")]
    [Description("Mark a tracked agent task as running.")]
    public void AgentTaskStart(string id, string actor = "agent")
    {
        ReportTask(StartTask(id, actor));
    }

    [XmlFunction(FunctionMode.OneShot, name: "agent_task_progress")]
    [Description("Record progress on a tracked agent task.")]
    public void AgentTaskProgress(string id, string detail, string actor = "agent")
    {
        ReportTask(RecordProgress(id, actor, detail));
    }

    [XmlFunction(FunctionMode.OneShot, name: "agent_task_complete")]
    [Description("Mark a tracked agent task as completed.")]
    public void AgentTaskComplete(string id, string detail = "completed", string actor = "agent")
    {
        ReportTask(CompleteTask(id, actor, detail));
    }

    [XmlFunction(FunctionMode.OneShot, name: "agent_task_fail")]
    [Description("Mark a tracked agent task as failed with a reason.")]
    public void AgentTaskFail(string id, string detail, string actor = "agent")
    {
        ReportTask(FailTask(id, actor, detail));
    }

    [XmlFunction(FunctionMode.OneShot, name: "agent_task_cancel")]
    [Description("Cancel a tracked agent task with a reason.")]
    public void AgentTaskCancel(string id, string detail = "cancelled", string actor = "agent")
    {
        ReportTask(CancelTask(id, actor, detail));
    }

    public AgentTaskState CreateTaskFromText(string actor, string goal, string stepsText)
    {
        string[] steps = (stepsText ?? string.Empty)
            .Split(['\r', '\n', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return CreateTask(actor, goal, steps);
    }

    public AgentTaskState CreateTask(string actor, string goal, IReadOnlyList<string> steps)
    {
        if (string.IsNullOrWhiteSpace(goal))
            throw new ArgumentException("Task goal cannot be empty.", nameof(goal));

        string id = Guid.NewGuid().ToString("N");
        DateTimeOffset now = DateTimeOffset.Now;
        AgentTaskEvent taskEvent = new(now, NormalizeActor(actor), "created", goal.Trim());
        AgentTaskState task = new(
            id,
            goal.Trim(),
            steps.Where(step => string.IsNullOrWhiteSpace(step) == false).Select(step => step.Trim()).ToArray(),
            AgentTaskStatus.Planned,
            now,
            now,
            [taskEvent]);

        lock (syncRoot)
        {
            tasks[id] = task;
            SaveTasks();
        }

        Audit("agent.task.created", actor, goal.Trim(), true);
        return task;
    }

    public AgentTaskState StartTask(string id, string actor)
    {
        return Transition(id, actor, AgentTaskStatus.Running, "started", "Task started.", [AgentTaskStatus.Planned]);
    }

    public AgentTaskState RecordProgress(string id, string actor, string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            throw new ArgumentException("Progress detail cannot be empty.", nameof(detail));

        lock (syncRoot)
        {
            AgentTaskState task = RequireTask(id);
            if (task.Status is AgentTaskStatus.Completed or AgentTaskStatus.Failed or AgentTaskStatus.Cancelled)
                throw new InvalidOperationException($"Cannot record progress for task in {task.Status} state.");

            AgentTaskState updated = AddEvent(task, actor, "progress", detail.Trim(), task.Status);
            tasks[id] = updated;
            SaveTasks();
            Audit("agent.task.progress", actor, detail.Trim(), true);
            return updated;
        }
    }

    public AgentTaskState CompleteTask(string id, string actor, string detail)
    {
        return Transition(id, actor, AgentTaskStatus.Completed, "completed", detail, [AgentTaskStatus.Planned, AgentTaskStatus.Running]);
    }

    public AgentTaskState FailTask(string id, string actor, string detail)
    {
        return Transition(id, actor, AgentTaskStatus.Failed, "failed", detail, [AgentTaskStatus.Planned, AgentTaskStatus.Running]);
    }

    public AgentTaskState CancelTask(string id, string actor, string detail)
    {
        return Transition(id, actor, AgentTaskStatus.Cancelled, "cancelled", detail, [AgentTaskStatus.Planned, AgentTaskStatus.Running]);
    }

    public AgentTaskState? GetTask(string id)
    {
        lock (syncRoot)
        {
            return tasks.GetValueOrDefault(id);
        }
    }

    public AgentTaskState? GetLatestTask()
    {
        lock (syncRoot)
        {
            return tasks.Values.OrderBy(task => task.CreatedAt).LastOrDefault();
        }
    }

    public string FormatStatus()
    {
        AgentTaskState? latest = GetLatestTask();
        if (latest == null)
            return "当前没有活动任务。";

        AgentTaskEvent? progressEvent = latest.Events.LastOrDefault(taskEvent =>
            string.Equals(taskEvent.Kind, "progress", StringComparison.OrdinalIgnoreCase));
        string progress = progressEvent?.Detail
            ?? latest.Events.LastOrDefault()?.Detail
            ?? "暂无进度记录";

        return $"""
                当前任务：
                #{latest.Id} {latest.Goal}
                状态：{latest.Status}
                最新进度：{progress}
                """.Trim();
    }

    public IReadOnlyList<AgentTaskState> GetTasks()
    {
        lock (syncRoot)
        {
            return tasks.Values
                .OrderByDescending(task => task.UpdatedAt)
                .ToArray();
        }
    }

    public int RemoveTerminalTasksOlderThan(TimeSpan maxAge, string actor = "agent-control-ui")
    {
        TimeSpan safeMaxAge = maxAge < TimeSpan.Zero ? TimeSpan.Zero : maxAge;
        DateTimeOffset cutoff = DateTimeOffset.Now - safeMaxAge;
        string[] staleIds;
        lock (syncRoot)
        {
            staleIds = tasks.Values
                .Where(task => task.Status is AgentTaskStatus.Completed or AgentTaskStatus.Failed or AgentTaskStatus.Cancelled)
                .Where(task => task.UpdatedAt <= cutoff)
                .Select(task => task.Id)
                .ToArray();

            foreach (string id in staleIds)
                tasks.Remove(id);

            if (staleIds.Length > 0)
                SaveTasks();
        }

        if (staleIds.Length > 0)
        {
            Audit(
                "agent.task.cleanup",
                actor,
                $"removed_terminal_tasks={staleIds.Length}",
                true);
        }

        return staleIds.Length;
    }

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);
        functionCaller?.RegisterHandler(this);
    }

    AgentTaskState Transition(
        string id,
        string actor,
        AgentTaskStatus status,
        string eventKind,
        string detail,
        IReadOnlyCollection<AgentTaskStatus> allowedFrom)
    {
        lock (syncRoot)
        {
            AgentTaskState task = RequireTask(id);
            if (allowedFrom.Contains(task.Status) == false)
                throw new InvalidOperationException($"Cannot transition task from {task.Status} to {status}.");

            AgentTaskState updated = AddEvent(task, actor, eventKind, string.IsNullOrWhiteSpace(detail) ? eventKind : detail.Trim(), status);
            tasks[id] = updated;
            SaveTasks();
            Audit($"agent.task.{eventKind}", actor, updated.Events.Last().Detail, true);
            return updated;
        }
    }

    AgentTaskState RequireTask(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Task id cannot be empty.", nameof(id));
        return tasks.GetValueOrDefault(id) ?? throw new KeyNotFoundException($"Agent task was not found: {id}");
    }

    static AgentTaskState AddEvent(AgentTaskState task, string actor, string kind, string detail, AgentTaskStatus status)
    {
        DateTimeOffset now = DateTimeOffset.Now;
        AgentTaskEvent taskEvent = new(now, NormalizeActor(actor), kind, detail);
        return task with
        {
            Status = status,
            UpdatedAt = now,
            Events = task.Events.Concat([taskEvent]).ToArray()
        };
    }

    void Audit(string action, string actor, string detail, bool succeeded)
    {
        auditLog?.Record(action, NormalizeActor(actor), detail, AgentAuditRiskLevel.Medium, succeeded);
    }

    void SaveTasks()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(taskStorePath)!);
        AgentTaskState[] snapshot = tasks.Values
            .OrderBy(task => task.CreatedAt)
            .ToArray();
        File.WriteAllText(taskStorePath, JsonSerializer.Serialize(snapshot, JsonOptions));
    }

    void ReportTask(AgentTaskState task)
    {
        try
        {
            Poke(FormatTask(task));
        }
        catch (NullReferenceException)
        {
            // Unit tests exercise task methods without a running ChatBot.
        }
    }

    static string NormalizeActor(string actor) => string.IsNullOrWhiteSpace(actor) ? "unknown" : actor.Trim();

    static Dictionary<string, AgentTaskState> LoadTasks(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath) == false)
            return new Dictionary<string, AgentTaskState>(StringComparer.OrdinalIgnoreCase);

        string json = File.ReadAllText(fullPath);
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, AgentTaskState>(StringComparer.OrdinalIgnoreCase);

        AgentTaskState[]? restored = JsonSerializer.Deserialize<AgentTaskState[]>(json, JsonOptions);
        return (restored ?? [])
            .Where(task => string.IsNullOrWhiteSpace(task.Id) == false)
            .Select(CloseStaleActiveTask)
            .ToDictionary(task => task.Id, StringComparer.OrdinalIgnoreCase);
    }

    static AgentTaskState CloseStaleActiveTask(AgentTaskState task)
    {
        if (task.Status is not (AgentTaskStatus.Planned or AgentTaskStatus.Running))
            return task;

        DateTimeOffset now = DateTimeOffset.Now;
        if (now - task.UpdatedAt <= StaleActiveTaskAge)
            return task;

        AgentTaskEvent staleEvent = new(
            now,
            "system",
            "stale-closed",
            $"Automatically closed stale active task after {StaleActiveTaskAge.TotalHours:F0} hours without progress.");
        return task with
        {
            Status = AgentTaskStatus.Cancelled,
            UpdatedAt = now,
            Events = task.Events.Concat([staleEvent]).ToArray()
        };
    }

    static string FormatTask(AgentTaskState task)
    {
        StringBuilder builder = new();
        builder.AppendLine($"Agent task: {task.Id}");
        builder.AppendLine($"Goal: {task.Goal}");
        builder.AppendLine($"Status: {task.Status}");
        if (task.Steps.Count > 0)
        {
            builder.AppendLine("Steps:");
            foreach (string step in task.Steps)
                builder.AppendLine($"- {step}");
        }
        builder.AppendLine("Events:");
        foreach (AgentTaskEvent taskEvent in task.Events)
            builder.AppendLine($"- {taskEvent.Timestamp:HH:mm:ss} [{taskEvent.Kind}] {taskEvent.Actor}: {taskEvent.Detail}");
        return builder.ToString().TrimEnd();
    }

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
    static readonly TimeSpan StaleActiveTaskAge = TimeSpan.FromHours(24);
}
