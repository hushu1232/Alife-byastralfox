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
        && IsLiveDataAgentAnalysisStatus(ActiveDataAgentStatus);

    public static bool IsLiveDataAgentAnalysisStatus(string? status)
    {
        string normalized = status?.Trim() ?? string.Empty;
        return string.Equals(normalized, "Active", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "AwaitingClarification", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "ReadyToSummarize", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "Summarized", StringComparison.OrdinalIgnoreCase);
    }

    public static ToolRouteState Empty { get; } = new(
        string.Empty,
        string.Empty,
        false,
        false,
        false);
}

public sealed record ToolRouteDeniedTool(string Name, string Reason);

public sealed record ToolRouteDecision
{
    public ToolRouteDecision(
        string RouteId,
        ToolCapabilityDomain Domain,
        string Intent,
        IReadOnlyList<string>? AllowedTools,
        IReadOnlyList<ToolRouteDeniedTool>? DeniedTools,
        ToolRouteState State,
        string Reason)
    {
        this.RouteId = RouteId;
        this.Domain = Domain;
        this.Intent = Intent;
        this.AllowedTools = CopyOrEmpty(AllowedTools);
        this.DeniedTools = CopyOrEmpty(DeniedTools);
        this.State = State;
        this.Reason = Reason;
    }

    public string RouteId { get; }

    public ToolCapabilityDomain Domain { get; }

    public string Intent { get; }

    public IReadOnlyList<string> AllowedTools { get; }

    public IReadOnlyList<ToolRouteDeniedTool> DeniedTools { get; }

    public ToolRouteState State { get; }

    public string Reason { get; }

    public bool Allows(string? toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return false;
        }

        return AllowedTools.Any(name => string.Equals(name, toolName, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<T> CopyOrEmpty<T>(IReadOnlyList<T>? tools)
    {
        return tools is null || tools.Count == 0
            ? Array.Empty<T>()
            : Array.AsReadOnly(tools.ToArray());
    }
}
