using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alife.Function.DesktopControl;

public enum DesktopBusinessJobStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled
}

public sealed record DesktopBusinessJobEntry(
    DateTimeOffset Timestamp,
    string JobId,
    string DraftId,
    long ActorUserId,
    string AgentId,
    string RequestedAction,
    DesktopBusinessJobStatus Status,
    string Message);

public interface IDesktopBusinessJobReader
{
    IReadOnlyList<DesktopBusinessJobEntry> GetRecentJobs(int maxCount);
    DesktopBusinessJobEntry? GetJob(string jobId);
}

public interface IDesktopBusinessJobCompletionSink
{
    Task NotifyCompletionAsync(
        DesktopBusinessJobEntry job,
        CancellationToken cancellationToken = default);
}

public sealed class DesktopBusinessTaskQueue : IDesktopApprovedDraftExecutor, IDesktopBusinessJobReader
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    readonly object syncRoot = new();
    readonly List<DesktopBusinessJobEntry> entries = new();
    readonly SemaphoreSlim executionGate = new(1, 1);
    readonly IDesktopApprovedDraftExecutor executor;
    readonly IDesktopActionDraftController draftController;
    readonly DesktopBusinessActionRegistry actionRegistry;
    readonly IDesktopBusinessJobCompletionSink? completionSink;
    readonly string jobFilePath;
    readonly int maxRetainedJobs;
    int sequence;

    public DesktopBusinessTaskQueue(
        IDesktopApprovedDraftExecutor executor,
        IDesktopActionDraftController draftController,
        string jobFilePath,
        DesktopBusinessActionRegistry? actionRegistry = null,
        IDesktopBusinessJobCompletionSink? completionSink = null,
        int maxRetainedJobs = 256)
    {
        this.executor = executor ?? throw new ArgumentNullException(nameof(executor));
        this.draftController = draftController ?? throw new ArgumentNullException(nameof(draftController));
        if (string.IsNullOrWhiteSpace(jobFilePath))
            throw new ArgumentException("Job file path cannot be empty.", nameof(jobFilePath));

        this.jobFilePath = Path.GetFullPath(jobFilePath);
        this.actionRegistry = actionRegistry ?? DesktopBusinessActionRegistry.CreateDefault();
        this.completionSink = completionSink;
        this.maxRetainedJobs = Math.Max(1, maxRetainedJobs);
        Directory.CreateDirectory(Path.GetDirectoryName(this.jobFilePath)!);
        LoadExistingEntries();
    }

    public Task<DesktopBusinessExecutionResult> ExecuteAsync(
        DesktopActionDraftEntry draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (actionRegistry.IsSupported(draft.RequestedAction) == false)
        {
            return Task.FromResult(new DesktopBusinessExecutionResult(
                false,
                "desktop_execution=denied reason=unsupported_action")
            {
                MarksDraftExecuted = false
            });
        }

        DesktopBusinessJobEntry queued;
        lock (syncRoot)
        {
            DesktopBusinessJobEntry? existing = GetLatestJobForDraftLocked(draft.DraftId);
            if (existing is { Status: DesktopBusinessJobStatus.Queued or DesktopBusinessJobStatus.Running })
            {
                return Task.FromResult(new DesktopBusinessExecutionResult(
                    true,
                    $"desktop_execution=queued job={existing.JobId} draft={draft.DraftId} existing=true")
                {
                    MarksDraftExecuted = false
                });
            }

            queued = new DesktopBusinessJobEntry(
                DateTimeOffset.Now,
                CreateJobId(),
                draft.DraftId,
                draft.ActorUserId,
                draft.AgentId,
                draft.RequestedAction,
                DesktopBusinessJobStatus.Queued,
                $"desktop_execution=queued draft={draft.DraftId}");
            RecordLocked(queued);
        }

        _ = RunJobAsync(queued, draft);
        return Task.FromResult(new DesktopBusinessExecutionResult(
            true,
            $"desktop_execution=queued job={queued.JobId} draft={draft.DraftId}")
        {
            MarksDraftExecuted = false
        });
    }

    public IReadOnlyList<DesktopBusinessJobEntry> GetRecentJobs(int maxCount)
    {
        if (maxCount <= 0)
            return [];

        lock (syncRoot)
        {
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            List<DesktopBusinessJobEntry> jobs = [];
            for (int i = entries.Count - 1; i >= 0 && jobs.Count < maxCount; i--)
            {
                DesktopBusinessJobEntry entry = entries[i];
                if (seen.Add(entry.JobId))
                    jobs.Add(entry);
            }

            jobs.Reverse();
            return jobs.ToArray();
        }
    }

    public DesktopBusinessJobEntry? GetJob(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return null;

        lock (syncRoot)
        {
            return entries.LastOrDefault(entry =>
                entry.JobId.Equals(jobId.Trim(), StringComparison.OrdinalIgnoreCase));
        }
    }

    async Task RunJobAsync(DesktopBusinessJobEntry queued, DesktopActionDraftEntry draft)
    {
        await executionGate.WaitAsync();
        try
        {
            Record(queued with
            {
                Timestamp = DateTimeOffset.Now,
                Status = DesktopBusinessJobStatus.Running,
                Message = $"desktop_job=running job={queued.JobId} draft={queued.DraftId}"
            });

            DesktopBusinessExecutionResult execution = await executor.ExecuteAsync(draft);
            if (execution.Success == false)
            {
                DesktopBusinessJobEntry failed = RecordFailed(queued, execution.Message);
                await NotifyCompletionAsync(failed);
                return;
            }

            DesktopActionDraftUpdateResult update = draftController.UpdateStatus(
                new DesktopActionRequest(
                    DesktopReadOnlyActions.DraftExecute,
                    draft.ActorUserId,
                    draft.AgentId,
                    IsOwner: true,
                    Detail: draft.DraftId),
                DesktopActionDraftStatus.Executed);
            if (update.Success == false)
            {
                DesktopBusinessJobEntry failed = RecordFailed(queued, update.Message);
                await NotifyCompletionAsync(failed);
                return;
            }

            DesktopBusinessJobEntry succeeded = queued with
            {
                Timestamp = DateTimeOffset.Now,
                Status = DesktopBusinessJobStatus.Succeeded,
                Message = execution.Message
            };
            Record(succeeded);
            await NotifyCompletionAsync(succeeded);
        }
        catch (Exception ex)
        {
            DesktopBusinessJobEntry failed = RecordFailed(queued, $"desktop_execution=failed error={ex.GetType().Name}");
            await NotifyCompletionAsync(failed);
        }
        finally
        {
            executionGate.Release();
        }
    }

    async Task NotifyCompletionAsync(DesktopBusinessJobEntry job)
    {
        if (completionSink == null)
            return;

        try
        {
            await completionSink.NotifyCompletionAsync(job);
        }
        catch
        {
        }
    }

    DesktopBusinessJobEntry RecordFailed(DesktopBusinessJobEntry queued, string message)
    {
        DesktopBusinessJobEntry failed = queued with
        {
            Timestamp = DateTimeOffset.Now,
            Status = DesktopBusinessJobStatus.Failed,
            Message = SanitizeMessage(message)
        };
        Record(failed);
        return failed;
    }

    void Record(DesktopBusinessJobEntry entry)
    {
        lock (syncRoot)
            RecordLocked(entry);
    }

    void RecordLocked(DesktopBusinessJobEntry entry)
    {
        DesktopBusinessJobEntry sanitized = entry with
        {
            JobId = NormalizeRequired(entry.JobId, nameof(entry.JobId)),
            DraftId = NormalizeRequired(entry.DraftId, nameof(entry.DraftId)),
            AgentId = NormalizeRequired(entry.AgentId, nameof(entry.AgentId)),
            RequestedAction = SanitizeMessage(entry.RequestedAction),
            Message = SanitizeMessage(entry.Message)
        };
        entries.Add(sanitized);
        TrimOverflow();
        AppendLineWithSharing(jobFilePath, JsonSerializer.Serialize(sanitized, JsonOptions));
    }

    DesktopBusinessJobEntry? GetLatestJobForDraftLocked(string draftId)
    {
        return entries.LastOrDefault(entry =>
            entry.DraftId.Equals(draftId, StringComparison.OrdinalIgnoreCase));
    }

    string CreateJobId()
    {
        int next = Interlocked.Increment(ref sequence);
        return $"desktop-job-{DateTimeOffset.Now:yyyyMMddHHmmssfff}-{next:D4}";
    }

    void LoadExistingEntries()
    {
        if (File.Exists(jobFilePath) == false)
            return;

        foreach (string line in ReadLinesWithSharing(jobFilePath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            DesktopBusinessJobEntry? entry;
            try
            {
                entry = JsonSerializer.Deserialize<DesktopBusinessJobEntry>(line, JsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            if (entry == null)
                continue;

            entries.Add(entry);
            TrimOverflow();
        }
    }

    void TrimOverflow()
    {
        int overflow = entries.Count - maxRetainedJobs;
        if (overflow > 0)
            entries.RemoveRange(0, overflow);
    }

    static string NormalizeRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be empty.", parameterName);

        return value.Trim();
    }

    static string SanitizeMessage(string? value)
    {
        string normalized = (value ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ')
            .Trim();
        while (normalized.Contains("  ", StringComparison.Ordinal))
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);

        return normalized.Length <= 240
            ? normalized
            : normalized[..240];
    }

    static void AppendLineWithSharing(string path, string line)
    {
        using FileStream stream = new(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite);
        using StreamWriter writer = new(stream);
        writer.WriteLine(line);
    }

    static IEnumerable<string> ReadLinesWithSharing(string path)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);
        using StreamReader reader = new(stream);
        while (reader.ReadLine() is { } line)
            yield return line;
    }
}
