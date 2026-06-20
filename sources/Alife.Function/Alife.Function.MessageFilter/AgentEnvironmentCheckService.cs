using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Alife.Framework;
using Alife.Platform;

namespace Alife.Function.Agent;

public sealed record AgentEnvironmentCheckItem(
    string Name,
    ModuleHealthStatus Status,
    string Summary);

public sealed record AgentEnvironmentCheckSnapshot(
    DateTimeOffset Timestamp,
    bool HasBlockingIssues,
    IReadOnlyList<AgentEnvironmentCheckItem> Items);

public sealed class AgentEnvironmentCheckService(Func<DateTimeOffset>? clock = null)
{
    readonly Func<DateTimeOffset> clock = clock ?? (() => DateTimeOffset.Now);

    public AgentEnvironmentCheckSnapshot BuildSnapshot()
    {
        List<AgentEnvironmentCheckItem> items =
        [
            CheckDirectory("Storage folder", AlifePath.StorageFolderPath),
            CheckDirectory("Runtime folder", AlifePath.RuntimeFolderPath),
            CheckDirectory("Temp folder", AlifePath.TempFolderPath),
            new AgentEnvironmentCheckItem(
                ".NET runtime",
                ModuleHealthStatus.Healthy,
                $".NET {Environment.Version}; OS: {Environment.OSVersion.VersionString}")
        ];

        return new AgentEnvironmentCheckSnapshot(
            clock(),
            items.Any(item => item.Status == ModuleHealthStatus.Unavailable),
            items);
    }

    static AgentEnvironmentCheckItem CheckDirectory(string name, string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            return new AgentEnvironmentCheckItem(
                name,
                ModuleHealthStatus.Healthy,
                path);
        }
        catch (Exception ex)
        {
            return new AgentEnvironmentCheckItem(
                name,
                ModuleHealthStatus.Unavailable,
                $"{path}: {ex.Message}");
        }
    }
}
