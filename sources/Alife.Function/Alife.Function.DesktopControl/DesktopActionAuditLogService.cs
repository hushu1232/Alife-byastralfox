using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alife.Function.DesktopControl;

public sealed class DesktopActionAuditLogService : IDesktopActionAuditSink, IDesktopActionAuditReader
{
    const int MaxMessageLength = 200;
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    readonly object syncRoot = new();
    readonly List<DesktopActionAuditEntry> entries = new();
    readonly string auditFilePath;
    readonly int maxRetainedEntries;

    public DesktopActionAuditLogService(string auditFilePath, int maxRetainedEntries = 256)
    {
        if (string.IsNullOrWhiteSpace(auditFilePath))
            throw new ArgumentException("Audit file path cannot be empty.", nameof(auditFilePath));

        this.auditFilePath = Path.GetFullPath(auditFilePath);
        this.maxRetainedEntries = Math.Max(1, maxRetainedEntries);
        Directory.CreateDirectory(Path.GetDirectoryName(this.auditFilePath)!);
        LoadExistingEntries();
    }

    public void Record(DesktopActionAuditEntry entry)
    {
        DesktopActionAuditEntry sanitized = Sanitize(entry);
        lock (syncRoot)
        {
            entries.Add(sanitized);
            int overflow = entries.Count - maxRetainedEntries;
            if (overflow > 0)
                entries.RemoveRange(0, overflow);

            AppendLineWithSharing(auditFilePath, JsonSerializer.Serialize(sanitized, JsonOptions));
        }
    }

    public IReadOnlyList<DesktopActionAuditEntry> GetRecentEntries(int maxCount)
    {
        if (maxCount <= 0)
            return [];

        lock (syncRoot)
        {
            return entries.TakeLast(maxCount).ToArray();
        }
    }

    static DesktopActionAuditEntry Sanitize(DesktopActionAuditEntry entry)
    {
        return entry with
        {
            ActionName = NormalizeRequired(entry.ActionName, nameof(entry.ActionName)),
            AgentId = NormalizeRequired(entry.AgentId, nameof(entry.AgentId)),
            Message = SanitizeMessage(entry.Message)
        };
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

        return normalized.Length <= MaxMessageLength
            ? normalized
            : normalized[..MaxMessageLength];
    }

    void LoadExistingEntries()
    {
        if (File.Exists(auditFilePath) == false)
            return;

        foreach (string line in ReadLinesWithSharing(auditFilePath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            DesktopActionAuditEntry? entry;
            try
            {
                entry = JsonSerializer.Deserialize<DesktopActionAuditEntry>(line, JsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            if (entry == null)
                continue;

            entries.Add(Sanitize(entry));
            int overflow = entries.Count - maxRetainedEntries;
            if (overflow > 0)
                entries.RemoveRange(0, overflow);
        }
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
