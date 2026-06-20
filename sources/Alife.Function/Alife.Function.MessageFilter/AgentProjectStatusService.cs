using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;
using Alife.Platform;

namespace Alife.Function.Agent;

public sealed record AgentProjectStatusSnapshot(
    DateTimeOffset Timestamp,
    string CurrentDirectory,
    IReadOnlyList<string> WorkspaceRoots,
    IReadOnlyList<AgentCommandDefinition> AllowedCommands,
    IReadOnlyList<AgentAuditLogEntry> RecentAuditEntries);

[Module(
    "Agent Project Status",
    "Summarizes the agent workspace, allowed maintenance commands, and recent audit activity before code work.",
    defaultCategory: "Alife Official/Agent",
    LaunchOrder = -61)]
public class AgentProjectStatusService(
    AgentWorkspacePolicy? workspacePolicy = null,
    AgentCommandPolicy? commandPolicy = null,
    AgentAuditLogService? auditLog = null,
    XmlFunctionCaller? functionCaller = null)
    : InteractiveModule<AgentProjectStatusService>
{
    readonly AgentWorkspacePolicy workspacePolicy = NormalizeWorkspacePolicy(workspacePolicy ?? CreateDefaultWorkspacePolicy());
    readonly AgentCommandPolicy commandPolicy = NormalizeCommandPolicy(commandPolicy ?? CreateDefaultCommandPolicy());
    readonly AgentAuditLogService auditLog = auditLog ?? new AgentAuditLogService(
        Path.Combine(AlifePath.StorageFolderPath, "AgentWorkspace", "agent-audit.jsonl"));

    [XmlFunction(FunctionMode.OneShot, name: "agent_project_status")]
    [Description("Show current project workspace roots, allowed agent commands, and recent audit entries.")]
    public void ShowAgentProjectStatus(int maxAuditEntries = 8)
    {
        Poke(FormatSnapshot(BuildSnapshot(maxAuditEntries)));
    }

    public AgentProjectStatusSnapshot BuildSnapshot(int maxAuditEntries = 8)
    {
        return new AgentProjectStatusSnapshot(
            DateTimeOffset.Now,
            Environment.CurrentDirectory,
            workspacePolicy.AllowedRoots,
            commandPolicy.AllowedCommands,
            auditLog.GetRecentEntries(maxAuditEntries));
    }

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);
        functionCaller?.RegisterHandler(this);
    }

    public static string FormatSnapshot(AgentProjectStatusSnapshot snapshot)
    {
        StringBuilder builder = new();
        builder.AppendLine("Agent project status");
        builder.AppendLine($"Time: {snapshot.Timestamp:O}");
        builder.AppendLine($"Current directory: {snapshot.CurrentDirectory}");
        builder.AppendLine("Workspace roots:");
        foreach (string root in snapshot.WorkspaceRoots)
            builder.AppendLine($"- {root}");

        builder.AppendLine("Allowed commands:");
        if (snapshot.AllowedCommands.Count == 0)
            builder.AppendLine("- none");
        else
        {
            foreach (AgentCommandDefinition command in snapshot.AllowedCommands)
                builder.AppendLine($"- {command.Id}: {command.Description}");
        }

        builder.AppendLine("Recent audit:");
        if (snapshot.RecentAuditEntries.Count == 0)
            builder.AppendLine("- none");
        else
        {
            foreach (AgentAuditLogEntry entry in snapshot.RecentAuditEntries)
            {
                string status = entry.Succeeded ? "ok" : "failed";
                builder.AppendLine($"- [{entry.RiskLevel}/{status}] {entry.Action}: {entry.Detail}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    static AgentWorkspacePolicy NormalizeWorkspacePolicy(AgentWorkspacePolicy rawPolicy)
    {
        string[] roots = rawPolicy.AllowedRoots
            .Where(root => string.IsNullOrWhiteSpace(root) == false)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (roots.Length == 0)
            throw new ArgumentException("At least one workspace root is required.", nameof(rawPolicy));

        return rawPolicy with { AllowedRoots = roots };
    }

    static AgentCommandPolicy NormalizeCommandPolicy(AgentCommandPolicy rawPolicy)
    {
        AgentCommandDefinition[] commands = rawPolicy.AllowedCommands
            .Where(command => string.IsNullOrWhiteSpace(command.Id) == false)
            .Select(command => command with
            {
                Id = command.Id.Trim(),
                Description = command.Description.Trim(),
                FileName = command.FileName.Trim(),
                Arguments = command.Arguments.Trim(),
                WorkingDirectory = Path.GetFullPath(command.WorkingDirectory),
                Timeout = command.Timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(30) : command.Timeout
            })
            .ToArray();

        return new AgentCommandPolicy(commands);
    }

    static AgentWorkspacePolicy CreateDefaultWorkspacePolicy()
    {
        string agentWorkspace = Path.Combine(AlifePath.StorageFolderPath, "AgentWorkspace");
        return new AgentWorkspacePolicy([Environment.CurrentDirectory, agentWorkspace, AlifePath.TempFolderPath]);
    }

    static AgentCommandPolicy CreateDefaultCommandPolicy()
    {
        string cwd = Environment.CurrentDirectory;
        return new AgentCommandPolicy([
            new AgentCommandDefinition("git-status", "Show repository status.", "git", "status --short", cwd, TimeSpan.FromSeconds(20)),
            new AgentCommandDefinition("git-diff", "Show unstaged repository diff.", "git", "diff --", cwd, TimeSpan.FromSeconds(20)),
            new AgentCommandDefinition("dotnet-build-solution", "Build the Alife solution without restoring packages.", "dotnet", "build Alife.slnx --no-restore", cwd, TimeSpan.FromMinutes(3)),
            new AgentCommandDefinition("dotnet-test-solution", "Run the Alife solution tests without restoring packages.", "dotnet", "test Alife.slnx --no-restore", cwd, TimeSpan.FromMinutes(5))
        ]);
    }
}
