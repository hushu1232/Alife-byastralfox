using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;

namespace Alife.Function.DataAgent;

public sealed record DataAgentToolRouteContext(
    bool Present,
    string ToolName,
    bool AllowsTool,
    bool AllowsQuery,
    string RouteId,
    string Intent,
    string ReasonCode,
    string RouteSessionId)
{
    public const string MissingRouteReasonCode = "tool_route_required";
    public const string ToolNotAllowedReasonCode = "tool_not_allowed_in_current_route";
    public const string SessionNotAllowedReasonCode = "tool_session_not_allowed_in_current_route";

    public static DataAgentToolRouteContext Missing(string toolName)
    {
        return new DataAgentToolRouteContext(
            false,
            toolName,
            false,
            false,
            string.Empty,
            string.Empty,
            MissingRouteReasonCode,
            string.Empty);
    }
}

public interface IDataAgentToolRouteContextAccessor
{
    DataAgentToolRouteContext Get(string toolName, string? sessionId);
}

public sealed class MissingDataAgentToolRouteContextAccessor : IDataAgentToolRouteContextAccessor
{
    public static MissingDataAgentToolRouteContextAccessor Instance { get; } = new();

    MissingDataAgentToolRouteContextAccessor()
    {
    }

    public DataAgentToolRouteContext Get(string toolName, string? sessionId)
    {
        return DataAgentToolRouteContext.Missing(toolName);
    }
}

public sealed class XmlPolicyDataAgentToolRouteContextAccessor(XmlFunctionExecutionPolicy executionPolicy)
    : IDataAgentToolRouteContextAccessor
{
    public DataAgentToolRouteContext Get(string toolName, string? sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        ToolRouteDecision? route = executionPolicy.CurrentRoute;
        if (route is null)
            return DataAgentToolRouteContext.Missing(toolName);

        bool routeAllowsTool = route.Allows(toolName);
        bool sessionAllowed = IsSessionAllowed(toolName, sessionId, route.State.ActiveDataAgentSessionId);
        bool allowed = routeAllowsTool && sessionAllowed;
        string reasonCode = allowed
            ? route.ReasonCode
            : routeAllowsTool
                ? DataAgentToolRouteContext.SessionNotAllowedReasonCode
                : DataAgentToolRouteContext.ToolNotAllowedReasonCode;

        return new DataAgentToolRouteContext(
            true,
            toolName,
            allowed,
            allowed,
            route.RouteId,
            route.Intent,
            reasonCode,
            route.State.ActiveDataAgentSessionId);
    }

    static bool IsSessionAllowed(string toolName, string? requestedSessionId, string routeSessionId)
    {
        if (IsSessionScopedDataAgentTool(toolName) == false)
            return true;

        if (string.IsNullOrWhiteSpace(routeSessionId))
            return false;

        if (string.IsNullOrWhiteSpace(requestedSessionId))
            return false;

        return string.Equals(requestedSessionId, routeSessionId, StringComparison.Ordinal);
    }

    static bool IsSessionScopedDataAgentTool(string toolName)
    {
        return string.Equals(toolName, "dataagent_analysis_continue", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "dataagent_analysis_summarize", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "dataagent_analysis_end", StringComparison.OrdinalIgnoreCase);
    }
}
