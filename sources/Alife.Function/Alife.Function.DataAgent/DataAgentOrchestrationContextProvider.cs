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
