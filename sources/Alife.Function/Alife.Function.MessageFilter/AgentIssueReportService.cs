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
using Autofac;

namespace Alife.Function.Agent;

public sealed record AgentIssueReportSnapshot(
    DateTimeOffset Timestamp,
    string? LastError,
    IReadOnlyList<ChatRuntimeEvent> RuntimeErrors,
    IReadOnlyList<AgentAuditLogEntry> FailedAuditEntries,
    IReadOnlyList<ModuleHealth> UnhealthyModules);

[Module(
    "Agent Issue Report",
    "Collects recent runtime errors, failed audit entries, and unhealthy modules for self-repair workflows.",
    defaultCategory: "Alife Official/Agent",
    LaunchOrder = -60)]
public class AgentIssueReportService(
    AgentAuditLogService? auditLog = null,
    XmlFunctionCaller? functionCaller = null)
    : InteractiveModule<AgentIssueReportService>
{
    readonly AgentAuditLogService auditLog = auditLog ?? new AgentAuditLogService(
        Path.Combine(AlifePath.StorageFolderPath, "AgentWorkspace", "agent-audit.jsonl"));
    public IEnumerable<IModuleHealthReporter>? HealthReporterSourceOverride { get; set; }

    [XmlFunction(FunctionMode.OneShot, name: "agent_issue_report")]
    [Description("Show recent runtime errors, failed agent audit entries, and unhealthy modules before debugging or self-repair.")]
    public void ShowAgentIssueReport(int maxEntries = 8)
    {
        Poke(FormatSnapshot(BuildSnapshot(ChatBot.GetRuntimeState(), maxEntries)));
    }

    public AgentIssueReportSnapshot BuildSnapshot(ChatRuntimeState runtimeState, int maxAuditEntries = 8)
    {
        ChatRuntimeEvent[] runtimeErrors = runtimeState.RecentEvents
            .Where(runtimeEvent => runtimeEvent.Kind.Equals("Error", StringComparison.OrdinalIgnoreCase))
            .TakeLast(Math.Max(1, maxAuditEntries))
            .ToArray();

        AgentAuditLogEntry[] failedAuditEntries = auditLog.GetRecentEntries(Math.Max(1, maxAuditEntries * 2))
            .Where(entry => entry.Succeeded == false)
            .TakeLast(Math.Max(1, maxAuditEntries))
            .ToArray();

        ModuleHealth[] unhealthyModules = ResolveHealthReporters()
            .Where(reporter => ReferenceEquals(reporter, this) == false)
            .Select(GetHealthSafely)
            .Where(health => health.Status != ModuleHealthStatus.Healthy)
            .OrderBy(health => health.Status)
            .ThenBy(health => health.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new AgentIssueReportSnapshot(
            DateTimeOffset.Now,
            runtimeState.LastError,
            runtimeErrors,
            failedAuditEntries,
            unhealthyModules);
    }

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);
        functionCaller?.RegisterHandler(this);
    }

    public static string FormatSnapshot(AgentIssueReportSnapshot snapshot)
    {
        StringBuilder builder = new();
        builder.AppendLine("Agent issue report");
        builder.AppendLine($"Time: {snapshot.Timestamp:O}");
        builder.AppendLine($"Last error: {snapshot.LastError ?? "none"}");

        builder.AppendLine("Runtime errors:");
        if (snapshot.RuntimeErrors.Count == 0)
            builder.AppendLine("- none");
        else
        {
            foreach (ChatRuntimeEvent runtimeEvent in snapshot.RuntimeErrors)
                builder.AppendLine($"- {runtimeEvent.Timestamp:HH:mm:ss} {runtimeEvent.Detail}");
        }

        builder.AppendLine("Failed audit entries:");
        if (snapshot.FailedAuditEntries.Count == 0)
            builder.AppendLine("- none");
        else
        {
            foreach (AgentAuditLogEntry entry in snapshot.FailedAuditEntries)
            {
                string error = string.IsNullOrWhiteSpace(entry.Error) ? "" : $" Error: {entry.Error}";
                builder.AppendLine($"- [{entry.RiskLevel}] {entry.Action}: {entry.Detail}.{error}");
            }
        }

        builder.AppendLine("Unhealthy modules:");
        if (snapshot.UnhealthyModules.Count == 0)
            builder.AppendLine("- none");
        else
        {
            foreach (ModuleHealth health in snapshot.UnhealthyModules)
                builder.AppendLine($"- [{health.Status}] {health.Name}: {health.Summary}");
        }

        return builder.ToString().TrimEnd();
    }

    IEnumerable<IModuleHealthReporter> ResolveHealthReporters()
    {
        if (HealthReporterSourceOverride != null)
            return HealthReporterSourceOverride;

        try
        {
            return ChatActivity.ModuleService.Resolve<IEnumerable<IModuleHealthReporter>>();
        }
        catch
        {
            return [];
        }
    }

    static ModuleHealth GetHealthSafely(IModuleHealthReporter reporter)
    {
        try
        {
            return reporter.GetHealth();
        }
        catch (Exception ex)
        {
            return new ModuleHealth(
                reporter.GetType().Name,
                ModuleHealthStatus.Unavailable,
                $"health check failed: {ex.Message}");
        }
    }
}
