using System.Text;

namespace Alife.Function.DataAgent;

public static class DataAgentContextProvider
{
    public static string Build(
        string question,
        string dataset,
        string sql,
        int rowCount,
        string summary,
        DataAgentQueryResult result,
        DataAgentPlannerExplanation explanation)
    {
        StringBuilder builder = new();
        builder.AppendLine("[data_agent_context]");
        builder.AppendLine($"question={Sanitize(question)}");
        builder.AppendLine($"dataset={dataset}");
        builder.AppendLine("sql_status=validated");
        AppendPlannerMetadata(builder, explanation);
        builder.AppendLine($"row_count={rowCount}");
        builder.AppendLine($"sql={Sanitize(sql)}");
        builder.AppendLine($"summary={Sanitize(summary)}");

        string evidence = string.Join(
            ", ",
            result.Rows
                .Select(row => row.TryGetValue("evidence_path", out object? value) ? Convert.ToString(value) : null)
                .Where(value => string.IsNullOrWhiteSpace(value) == false)
                .Distinct(StringComparer.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(evidence) == false)
            builder.AppendLine($"evidence={Sanitize(evidence)}");

        builder.AppendLine("[/data_agent_context]");
        return builder.ToString().Trim();
    }

    public static string BuildRejected(
        string question,
        string dataset,
        string reason,
        DataAgentPlannerExplanation explanation)
    {
        StringBuilder builder = new();
        builder.AppendLine("[data_agent_context]");
        builder.AppendLine($"question={Sanitize(question)}");
        builder.AppendLine($"dataset={dataset}");
        builder.AppendLine("sql_status=rejected");
        AppendPlannerMetadata(builder, explanation);
        builder.AppendLine($"rejected_reason={Sanitize(reason)}");
        builder.AppendLine("[/data_agent_context]");
        return builder.ToString().Trim();
    }

    public static string BuildClarification(
        string question,
        DataAgentClarificationRequest clarification,
        DataAgentPlannerExplanation explanation)
    {
        ArgumentNullException.ThrowIfNull(clarification);

        StringBuilder builder = new();
        builder.AppendLine("[data_agent_context]");
        builder.AppendLine($"question={Sanitize(question)}");
        builder.AppendLine("dataset=");
        builder.AppendLine("sql_status=needs_clarification");
        AppendPlannerMetadata(builder, explanation);
        builder.AppendLine($"clarification_question={Sanitize(clarification.Question)}");
        builder.AppendLine($"clarification_options={Sanitize(string.Join(", ", clarification.Options))}");
        builder.AppendLine($"clarification_reason={Sanitize(clarification.Reason)}");
        builder.AppendLine("[/data_agent_context]");
        return builder.ToString().Trim();
    }

    static void AppendPlannerMetadata(StringBuilder builder, DataAgentPlannerExplanation explanation)
    {
        ArgumentNullException.ThrowIfNull(explanation);

        builder.AppendLine($"planner={Sanitize(explanation.PlannerName)}");
        builder.AppendLine($"planner_confidence={Sanitize(explanation.Confidence)}");
        builder.AppendLine($"planner_reason={Sanitize(explanation.Reason)}");
        builder.AppendLine($"planner_signals={Sanitize(string.Join(", ", explanation.Signals))}");
    }

    static string Sanitize(string value)
    {
        return value
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
    }
}
