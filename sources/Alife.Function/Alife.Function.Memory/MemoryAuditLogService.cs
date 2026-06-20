using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Alife.Function.Memory;

public sealed record MemoryAuditLogEntry(
    DateTimeOffset Timestamp,
    string Action,
    string Actor,
    string MemoryName,
    string Detail,
    bool Succeeded,
    string? Error = null);

public class MemoryAuditLogService
{
    readonly object syncRoot = new();
    readonly List<MemoryAuditLogEntry> entries = new();
    readonly string auditFilePath;
    readonly int maxRetainedEntries;

    public MemoryAuditLogService(string auditFilePath, int maxRetainedEntries = 256)
    {
        if (string.IsNullOrWhiteSpace(auditFilePath))
            throw new ArgumentException("Audit file path cannot be empty.", nameof(auditFilePath));

        this.auditFilePath = Path.GetFullPath(auditFilePath);
        this.maxRetainedEntries = Math.Max(1, maxRetainedEntries);
        Directory.CreateDirectory(Path.GetDirectoryName(this.auditFilePath)!);
        LoadExistingEntries();
    }

    public MemoryAuditLogEntry Record(
        string action,
        string actor,
        string memoryName,
        string detail,
        bool succeeded,
        string? error = null)
    {
        MemoryAuditLogEntry entry = new(
            DateTimeOffset.Now,
            NormalizeRequired(action, nameof(action)),
            NormalizeRequired(actor, nameof(actor)),
            NormalizeRequired(memoryName, nameof(memoryName)),
            detail?.Trim() ?? string.Empty,
            succeeded,
            string.IsNullOrWhiteSpace(error) ? null : error.Trim());

        lock (syncRoot)
        {
            entries.Add(entry);
            int overflow = entries.Count - maxRetainedEntries;
            if (overflow > 0)
                entries.RemoveRange(0, overflow);

            AppendLineWithSharing(auditFilePath, JsonSerializer.Serialize(entry));
        }

        return entry;
    }

    public IReadOnlyList<MemoryAuditLogEntry> GetRecentEntries(int maxCount)
    {
        if (maxCount <= 0)
            return [];

        lock (syncRoot)
        {
            return entries.TakeLast(maxCount).ToArray();
        }
    }

    static string NormalizeRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be empty.", parameterName);

        return value.Trim();
    }

    void LoadExistingEntries()
    {
        if (File.Exists(auditFilePath) == false)
            return;

        foreach (string line in ReadLinesWithSharing(auditFilePath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            MemoryAuditLogEntry? entry;
            try
            {
                entry = JsonSerializer.Deserialize<MemoryAuditLogEntry>(line);
            }
            catch (JsonException)
            {
                continue;
            }

            if (entry == null)
                continue;

            entries.Add(entry);
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
