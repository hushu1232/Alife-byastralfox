using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Alife.Function.MessageFilter;

public sealed record AgentEditCheckpointFile(
    string OriginalPath,
    string BackupPath,
    string OriginalHash);

public sealed record AgentEditCheckpoint(
    string TaskId,
    DateTimeOffset CreatedAt,
    IReadOnlyList<AgentEditCheckpointFile> Files);

public sealed record AgentEditRollbackResult(
    string TaskId,
    int RestoredFiles,
    IReadOnlyList<string> Errors);

public sealed class AgentEditCheckpointService
{
    readonly string checkpointRoot;
    readonly ConcurrentDictionary<string, List<AgentEditCheckpointFile>> filesByTask = new();

    public AgentEditCheckpointService(string checkpointRoot)
    {
        this.checkpointRoot = Path.GetFullPath(checkpointRoot);
        Directory.CreateDirectory(this.checkpointRoot);
    }

    public void CaptureBeforeWrite(string taskId, string filePath)
    {
        string normalizedTaskId = NormalizeTaskId(taskId);
        string fullPath = Path.GetFullPath(filePath);
        List<AgentEditCheckpointFile> files = filesByTask.GetOrAdd(normalizedTaskId, _ => []);
        lock (files)
        {
            if (files.Any(file => string.Equals(file.OriginalPath, fullPath, StringComparison.OrdinalIgnoreCase)))
                return;

            string taskRoot = Path.Combine(checkpointRoot, Sanitize(normalizedTaskId));
            Directory.CreateDirectory(taskRoot);
            string backupPath = Path.Combine(taskRoot, Guid.NewGuid().ToString("N") + ".bak");
            string originalHash = ComputeSha256(fullPath);
            File.Copy(fullPath, backupPath, overwrite: false);
            files.Add(new AgentEditCheckpointFile(fullPath, backupPath, originalHash));
        }
    }

    public AgentEditCheckpoint? GetCheckpoint(string taskId)
    {
        string normalizedTaskId = NormalizeTaskId(taskId);
        if (filesByTask.TryGetValue(normalizedTaskId, out List<AgentEditCheckpointFile>? files) == false)
            return null;

        lock (files)
            return new AgentEditCheckpoint(normalizedTaskId, DateTimeOffset.Now, files.ToArray());
    }

    public AgentEditRollbackResult Rollback(string taskId)
    {
        string normalizedTaskId = NormalizeTaskId(taskId);
        AgentEditCheckpoint? checkpoint = GetCheckpoint(normalizedTaskId);
        if (checkpoint == null)
            return new AgentEditRollbackResult(normalizedTaskId, 0, [$"checkpoint {normalizedTaskId} not found"]);

        List<string> errors = [];
        int restored = 0;
        foreach (AgentEditCheckpointFile file in checkpoint.Files)
        {
            try
            {
                string? directory = Path.GetDirectoryName(file.OriginalPath);
                if (string.IsNullOrWhiteSpace(directory) == false)
                    Directory.CreateDirectory(directory);
                File.Copy(file.BackupPath, file.OriginalPath, overwrite: true);
                restored++;
            }
            catch (Exception exception)
            {
                errors.Add($"{file.OriginalPath}: {exception.Message}");
            }
        }

        return new AgentEditRollbackResult(normalizedTaskId, restored, errors);
    }

    static string NormalizeTaskId(string taskId)
    {
        if (string.IsNullOrWhiteSpace(taskId))
            throw new ArgumentException("Task id is required.", nameof(taskId));

        return taskId.Trim();
    }

    static string Sanitize(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(value) ? "task" : value;
    }

    static string ComputeSha256(string filePath)
    {
        using FileStream stream = File.OpenRead(filePath);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }
}
