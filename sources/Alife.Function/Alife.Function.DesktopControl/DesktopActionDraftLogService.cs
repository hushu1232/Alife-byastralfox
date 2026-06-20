using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alife.Function.DesktopControl;

public enum DesktopActionDraftStatus
{
    PendingApproval,
    Approved,
    Rejected,
    Cancelled
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

public sealed class DesktopActionDraftLogService(string draftFilePath) : IDesktopActionDraftSink
{
    const int MaxRequestedActionLength = 300;
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    readonly string draftFilePath = Path.GetFullPath(draftFilePath);
    int sequence;

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

        Directory.CreateDirectory(Path.GetDirectoryName(draftFilePath)!);
        AppendLineWithSharing(draftFilePath, JsonSerializer.Serialize(entry, JsonOptions));
        return entry;
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
}
