using System.Text;

namespace Alife.Function.DataAgent;

public static class DataAgentOrchestrationContextProvider
{
    public static string Build(DataAgentOrchestrationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        StringBuilder builder = new();
        if (string.IsNullOrWhiteSpace(result.Response.Context) == false)
            builder.AppendLine(result.Response.Context.Trim());

        builder.AppendLine($"orchestration_trace={BuildTrace(result.Steps)}");
        builder.AppendLine($"checkpoint_session_id={Sanitize(result.Checkpoint.SessionId)}");
        builder.AppendLine($"checkpoint_status={result.Checkpoint.SessionStatus}");
        builder.AppendLine($"checkpoint_turn_count={result.Checkpoint.TurnCount}");
        builder.AppendLine($"checkpoint_can_continue={ToLowerBool(result.Checkpoint.CanContinue)}");
        builder.AppendLine($"checkpoint_can_summarize={ToLowerBool(result.Checkpoint.CanSummarize)}");
        builder.AppendLine($"checkpoint_terminal={ToLowerBool(result.Checkpoint.Terminal)}");
        if (result.RouteContext is not null)
        {
            builder.AppendLine($"route_present={ToLowerBool(result.RouteContext.Present)}");
            builder.AppendLine($"route_tool={Sanitize(result.RouteContext.ToolName)}");
            builder.AppendLine($"route_allows_tool={ToLowerBool(result.RouteContext.AllowsTool)}");
            builder.AppendLine($"route_allows_query={ToLowerBool(result.RouteContext.AllowsQuery)}");
            builder.AppendLine($"route_id={Sanitize(result.RouteContext.RouteId)}");
            builder.AppendLine($"route_intent={Sanitize(result.RouteContext.Intent)}");
            builder.AppendLine($"route_reason_code={Sanitize(result.RouteContext.ReasonCode)}");
            builder.AppendLine($"route_session_id={Sanitize(result.RouteContext.RouteSessionId)}");
        }

        return builder.ToString().Trim();
    }

    static string BuildTrace(IEnumerable<DataAgentOrchestrationStep> steps)
    {
        return string.Join(
            ">",
            steps.Select(step => $"{step.Node}:{step.Status}"));
    }

    static string Sanitize(string value)
    {
        return DataAgentContextFieldSanitizer.Sanitize(value);
    }

    static string ToLowerBool(bool value)
    {
        return value ? "true" : "false";
    }
}
