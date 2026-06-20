using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;
using Autofac;

namespace Alife.Function.MessageFilter;

[Module(
    "System Health",
    "Aggregates selected module health reports so the activity can diagnose unavailable or degraded abilities.",
    defaultCategory: "Alife Official/Living Environment",
    LaunchOrder = -75)]
public class SystemHealthService(XmlFunctionCaller? functionCaller = null) : InteractiveModule<SystemHealthService>
{
    public IEnumerable<IModuleHealthReporter>? HealthReporterSourceOverride { get; set; }

    [XmlFunction(FunctionMode.OneShot, name: "system_health")]
    [Description("Show the current health of selected Alife modules and embodied capabilities.")]
    public void SystemHealth()
    {
        Poke(FormatHealthSnapshot(GetHealthSnapshot()));
    }

    public IReadOnlyList<ModuleHealth> GetHealthSnapshot()
    {
        return ResolveHealthReporters()
            .Where(reporter => ReferenceEquals(reporter, this) == false)
            .Select(GetHealthSafely)
            .OrderBy(health => health.Status)
            .ThenBy(health => health.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string FormatHealthSnapshot(IEnumerable<ModuleHealth> healthSnapshot)
    {
        ModuleHealth[] health = healthSnapshot.ToArray();
        if (health.Length == 0)
            return "No module health reporters are available.";

        StringBuilder builder = new();
        builder.AppendLine("Module health:");
        foreach (ModuleHealth moduleHealth in health)
            builder.AppendLine($"- [{moduleHealth.Status}] {moduleHealth.Name}: {moduleHealth.Summary}");
        return builder.ToString().TrimEnd();
    }

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);
        functionCaller?.RegisterHandler(this);
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
