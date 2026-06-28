using System;
using System.Collections.Generic;
using System.Linq;

namespace Alife.Function.FunctionCaller;

public sealed record ToolRouteState(
    string ActiveDataAgentSessionId,
    string ActiveDataAgentStatus,
    bool IsOwner,
    bool IsPrivateChat,
    bool IsTrustedRuntime)
{
    public bool HasActiveDataAgentSession =>
        string.IsNullOrWhiteSpace(ActiveDataAgentSessionId) == false
        && string.Equals(ActiveDataAgentStatus, "Active", StringComparison.OrdinalIgnoreCase);

    public static ToolRouteState Empty { get; } = new(
        string.Empty,
        string.Empty,
        false,
        false,
        true);
}

public sealed record ToolRouteDeniedTool(string Name, string Reason);

public sealed record ToolRouteDecision(
    string RouteId,
    ToolCapabilityDomain Domain,
    string Intent,
    IReadOnlyList<string> AllowedTools,
    IReadOnlyList<ToolRouteDeniedTool> DeniedTools,
    ToolRouteState State,
    string Reason)
{
    public bool Allows(string toolName)
    {
        return AllowedTools.Any(name => string.Equals(name, toolName, StringComparison.OrdinalIgnoreCase));
    }
}
