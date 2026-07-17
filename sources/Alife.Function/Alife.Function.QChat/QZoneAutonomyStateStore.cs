using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Alife.Platform;

namespace Alife.Function.QChat;

public sealed class QZoneAutonomyStateStore
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public QZoneAutonomyStateStore()
        : this(Path.Combine(AlifePath.StorageFolderPath, "QZoneAutonomy"))
    {
    }

    public QZoneAutonomyStateStore(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            throw new ArgumentException("State directory is empty.", nameof(directoryPath));

        DirectoryPath = directoryPath;
    }

    public string DirectoryPath { get; }

    public QZoneAutonomyState Load(QZoneAutonomyAgentKey agentKey)
    {
        ValidateAgentKey(agentKey);
        string path = BuildStatePath(agentKey);
        if (File.Exists(path) == false)
            return QZoneAutonomyState.Create(agentKey);

        try
        {
            string json = File.ReadAllText(path);
            QZoneAutonomyState? state = JsonSerializer.Deserialize<QZoneAutonomyState>(json, JsonOptions);
            if (state == null || state.AgentKey != agentKey)
                return QZoneAutonomyState.Create(agentKey);

            return state.NormalizeForPersistence();
        }
        catch (JsonException)
        {
            return QZoneAutonomyState.Create(agentKey);
        }
        catch (IOException)
        {
            return QZoneAutonomyState.Create(agentKey);
        }
        catch (UnauthorizedAccessException)
        {
            return QZoneAutonomyState.Create(agentKey);
        }
    }

    public void Save(QZoneAutonomyState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateAgentKey(state.AgentKey);

        QZoneAutonomyState safeState = state.NormalizeForPersistence();
        Directory.CreateDirectory(DirectoryPath);
        string destinationPath = BuildStatePath(safeState.AgentKey);
        string temporaryPath = Path.Combine(
            DirectoryPath,
            $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(safeState, JsonOptions));
            if (File.Exists(destinationPath))
                File.Replace(temporaryPath, destinationPath, destinationBackupFileName: null);
            else
                File.Move(temporaryPath, destinationPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    string BuildStatePath(QZoneAutonomyAgentKey agentKey)
    {
        byte[] keyBytes = Encoding.UTF8.GetBytes(agentKey.Value);
        string hashedKey = Convert.ToHexString(SHA256.HashData(keyBytes)).ToLowerInvariant();
        return Path.Combine(DirectoryPath, $"{hashedKey}.json");
    }

    static void ValidateAgentKey(QZoneAutonomyAgentKey agentKey)
    {
        if (string.IsNullOrWhiteSpace(agentKey.Value))
            throw new ArgumentException("Autonomy agent key is empty.", nameof(agentKey));
    }
}
