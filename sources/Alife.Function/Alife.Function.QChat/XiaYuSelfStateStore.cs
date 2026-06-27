using System;
using System.IO;
using System.Text.Json;
using Alife.Platform;

namespace Alife.Function.QChat;

public sealed class XiaYuSelfStateStore
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    readonly string path;

    public XiaYuSelfStateStore()
        : this(BuildDefaultPath())
    {
    }

    public XiaYuSelfStateStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("State path is empty.", nameof(path));

        this.path = path;
    }

    public XiaYuSelfState LoadOrCreate(string agentId, DateTimeOffset now)
    {
        try
        {
            if (File.Exists(path) == false)
                return XiaYuSelfState.CreateDefault(agentId, now);

            string json = File.ReadAllText(path);
            XiaYuSelfState? state = JsonSerializer.Deserialize<XiaYuSelfState>(json, JsonOptions);
            if (state == null || string.IsNullOrWhiteSpace(state.AgentId))
                return XiaYuSelfState.CreateDefault(agentId, now);

            return state;
        }
        catch
        {
            return XiaYuSelfState.CreateDefault(agentId, now);
        }
    }

    public void Save(XiaYuSelfState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory) == false)
                Directory.CreateDirectory(directory);

            string json = JsonSerializer.Serialize(state, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch
        {
            // Personality state persistence must never interrupt QQ message handling.
        }
    }

    static string BuildDefaultPath()
    {
        return Path.Combine(
            AlifePath.StorageFolderPath,
            "Character",
            "夏羽",
            "State",
            "XiaYuSelfState.json");
    }
}
