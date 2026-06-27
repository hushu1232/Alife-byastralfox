using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Alife.Function.Memory;

public sealed record MemoryDirectorySanitizationReport(
    bool HistoryChanged,
    int RemovedHistoryRecords,
    int SanitizedHistoryRecords,
    int RemovedHistorySegments,
    string? HistoryBackupPath,
    MemoryStorageSanitizationReport StorageReport);

public static class MemoryDirectorySanitizer
{
    public static async Task<MemoryDirectorySanitizationReport> SanitizeAsync(
        string memoryRootPath,
        ITextVectorizer vectorizer,
        MemoryTextSanitizer? sanitizer = null,
        bool createBackups = true,
        bool revectorize = true)
    {
        sanitizer ??= MemoryTextSanitizer.Default;
        MemoryHistorySanitizationResult history = SanitizeHistoryFile(memoryRootPath, sanitizer, createBackups);
        await using MemoryStorage storage = new(memoryRootPath, vectorizer);
        MemoryStorageSanitizationReport storageReport = await storage.SanitizeAsync(sanitizer, createBackups, revectorize);
        return new MemoryDirectorySanitizationReport(
            history.Changed,
            history.RemovedRecords,
            history.SanitizedRecords,
            history.RemovedSegments,
            history.Changed ? GetLatestHistoryBackupPath(memoryRootPath) : null,
            storageReport);
    }

    static MemoryHistorySanitizationResult SanitizeHistoryFile(
        string memoryRootPath,
        MemoryTextSanitizer sanitizer,
        bool createBackups)
    {
        string historyPath = Path.Combine(memoryRootPath, "History.json");
        if (File.Exists(historyPath) == false)
        {
            return new MemoryHistorySanitizationResult(
                "[]",
                false,
                0,
                0,
                0);
        }

        string historyJson = File.ReadAllText(historyPath);
        MemoryHistorySanitizationResult result = sanitizer.SanitizeHistoryJson(historyJson);
        if (result.Changed == false)
            return result;

        if (createBackups)
            File.Copy(historyPath, BuildHistoryBackupPath(memoryRootPath), overwrite: false);

        File.WriteAllText(historyPath, result.Json);
        return result;
    }

    static string BuildHistoryBackupPath(string memoryRootPath)
    {
        return Path.Combine(memoryRootPath, $"History.json.bak-sanitize-{DateTimeOffset.Now:yyyyMMdd-HHmmss}");
    }

    static string? GetLatestHistoryBackupPath(string memoryRootPath)
    {
        string[] backups = Directory.GetFiles(memoryRootPath, "History.json.bak-sanitize-*");
        return backups.Length == 0
            ? null
            : backups.MaxBy(File.GetLastWriteTimeUtc);
    }
}
