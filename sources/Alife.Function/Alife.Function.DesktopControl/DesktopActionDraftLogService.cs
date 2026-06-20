using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alife.Function.DesktopControl;

public enum DesktopActionDraftStatus
{
    PendingApproval,
    Approved,
    Rejected,
    Cancelled,
    Executed
}

public sealed record DesktopActionDraftEntry(
    DateTimeOffset Timestamp,
    string DraftId,
    long ActorUserId,
    string AgentId,
    string RequestedAction,
    DesktopActionDraftStatus Status);

public interface IDesktopActionDraftSink
{
    DesktopActionDraftEntry CreateDraft(DesktopActionRequest request);
}

public interface IDesktopActionDraftReader
{
    IReadOnlyList<DesktopActionDraftEntry> GetRecentDrafts(int maxCount);
    DesktopActionDraftEntry? GetDraft(string draftId);
}

public sealed record DesktopActionDraftUpdateResult(
    bool Success,
    DesktopActionDraftEntry? Entry,
    string Message);

public interface IDesktopActionDraftController
{
    DesktopActionDraftUpdateResult UpdateStatus(
        DesktopActionRequest request,
        DesktopActionDraftStatus status);
}

public sealed class DesktopActionDraftLogService : IDesktopActionDraftSink, IDesktopActionDraftReader, IDesktopActionDraftController
{
    const int MaxRequestedActionLength = 300;
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    readonly object syncRoot = new();
    readonly List<DesktopActionDraftEntry> entries = new();
    readonly string draftFilePath;
    readonly int maxRetainedDrafts;
    int sequence;

    public DesktopActionDraftLogService(string draftFilePath, int maxRetainedDrafts = 256)
    {
        if (string.IsNullOrWhiteSpace(draftFilePath))
            throw new ArgumentException("Draft file path cannot be empty.", nameof(draftFilePath));

        this.draftFilePath = Path.GetFullPath(draftFilePath);
        this.maxRetainedDrafts = Math.Max(1, maxRetainedDrafts);
        Directory.CreateDirectory(Path.GetDirectoryName(this.draftFilePath)!);
        LoadExistingEntries();
    }

    public DesktopActionDraftEntry CreateDraft(DesktopActionRequest request)
    {
        string requestedAction = SanitizeRequestedAction(request.Detail);
        DesktopActionDraftEntry entry = new(
            DateTimeOffset.Now,
            CreateDraftId(),
            request.ActorUserId,
            NormalizeRequired(request.AgentId, nameof(request.AgentId)),
            requestedAction,
            DesktopActionDraftStatus.PendingApproval);

        lock (syncRoot)
        {
            entries.Add(entry);
            TrimOverflow();
            AppendLineWithSharing(draftFilePath, JsonSerializer.Serialize(entry, JsonOptions));
        }

        return entry;
    }

    public IReadOnlyList<DesktopActionDraftEntry> GetRecentDrafts(int maxCount)
    {
        if (maxCount <= 0)
            return [];

        lock (syncRoot)
        {
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            List<DesktopActionDraftEntry> drafts = [];
            for (int i = entries.Count - 1; i >= 0 && drafts.Count < maxCount; i--)
            {
                DesktopActionDraftEntry entry = entries[i];
                if (seen.Add(entry.DraftId))
                    drafts.Add(entry);
            }

            drafts.Reverse();
            return drafts.ToArray();
        }
    }

    public DesktopActionDraftEntry? GetDraft(string draftId)
    {
        if (string.IsNullOrWhiteSpace(draftId))
            return null;

        lock (syncRoot)
        {
            return entries.LastOrDefault(entry =>
                entry.DraftId.Equals(draftId.Trim(), StringComparison.OrdinalIgnoreCase));
        }
    }

    public DesktopActionDraftUpdateResult UpdateStatus(
        DesktopActionRequest request,
        DesktopActionDraftStatus status)
    {
        string draftId = NormalizeRequired(request.Detail, nameof(DesktopActionRequest.Detail));
        lock (syncRoot)
        {
            DesktopActionDraftEntry? current = entries.LastOrDefault(entry =>
                entry.DraftId.Equals(draftId, StringComparison.OrdinalIgnoreCase));
            if (current == null)
                return new DesktopActionDraftUpdateResult(false, null, "desktop_draft=not_found");

            bool transitionAllowed = status switch
            {
                DesktopActionDraftStatus.Approved or DesktopActionDraftStatus.Rejected => current.Status == DesktopActionDraftStatus.PendingApproval,
                DesktopActionDraftStatus.Executed => current.Status == DesktopActionDraftStatus.Approved,
                _ => false
            };
            if (transitionAllowed == false)
            {
                return new DesktopActionDraftUpdateResult(
                    false,
                    current,
                    $"desktop_draft=invalid_transition id={current.DraftId} status={current.Status} target={status} execution=disabled");
            }

            DesktopActionDraftEntry updated = current with
            {
                Timestamp = DateTimeOffset.Now,
                Status = status
            };
            entries.Add(updated);
            TrimOverflow();
            AppendLineWithSharing(draftFilePath, JsonSerializer.Serialize(updated, JsonOptions));
            return new DesktopActionDraftUpdateResult(
                true,
                updated,
                $"desktop_draft=updated id={updated.DraftId} status={updated.Status} execution=disabled");
        }
    }

    string CreateDraftId()
    {
        int next = Interlocked.Increment(ref sequence);
        return $"desktop-draft-{DateTimeOffset.Now:yyyyMMddHHmmssfff}-{next:D4}";
    }

    static string SanitizeRequestedAction(string? value)
    {
        string normalized = NormalizeRequired(value ?? "", nameof(DesktopActionRequest.Detail))
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ')
            .Trim();
        while (normalized.Contains("  ", StringComparison.Ordinal))
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);

        return normalized.Length <= MaxRequestedActionLength
            ? normalized
            : normalized[..MaxRequestedActionLength];
    }

    static string NormalizeRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be empty.", parameterName);

        return value.Trim();
    }

    void LoadExistingEntries()
    {
        if (File.Exists(draftFilePath) == false)
            return;

        foreach (string line in ReadLinesWithSharing(draftFilePath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            DesktopActionDraftEntry? entry;
            try
            {
                entry = JsonSerializer.Deserialize<DesktopActionDraftEntry>(line, JsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            if (entry == null)
                continue;

            entries.Add(entry with
            {
                AgentId = NormalizeRequired(entry.AgentId, nameof(entry.AgentId)),
                RequestedAction = SanitizeRequestedAction(entry.RequestedAction)
            });
            TrimOverflow();
        }
    }

    void TrimOverflow()
    {
        int overflow = entries.Count - maxRetainedDrafts;
        if (overflow > 0)
            entries.RemoveRange(0, overflow);
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
