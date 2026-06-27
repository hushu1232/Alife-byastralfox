using System;
using Alife.Framework;

namespace Alife.Function.Agent;

public enum AgentHealthIssueKind
{
    ActionableFault,
    ExternalEnvironment,
    Waiting
}

public static class AgentHealthIssueClassifier
{
    public static AgentHealthIssueKind Classify(ModuleHealth health)
    {
        string name = health.Name ?? string.Empty;
        string summary = health.Summary ?? string.Empty;
        string combined = $"{name} {summary}";

        if (summary.Contains("did not initialize", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("health check failed", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("failed", StringComparison.OrdinalIgnoreCase))
        {
            return AgentHealthIssueKind.ActionableFault;
        }

        if (combined.Contains("OneBot", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("QQ", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("QZone", StringComparison.OrdinalIgnoreCase))
        {
            return AgentHealthIssueKind.ExternalEnvironment;
        }

        if (summary.Contains("not initialized yet", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("loading", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("disabled", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("dry-run", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("not configured", StringComparison.OrdinalIgnoreCase))
        {
            return AgentHealthIssueKind.Waiting;
        }

        return health.Status == ModuleHealthStatus.Unavailable
            ? AgentHealthIssueKind.ActionableFault
            : AgentHealthIssueKind.Waiting;
    }

    public static bool IsActionable(ModuleHealth health) => Classify(health) == AgentHealthIssueKind.ActionableFault;
}
