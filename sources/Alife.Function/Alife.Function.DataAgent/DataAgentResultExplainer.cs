namespace Alife.Function.DataAgent;

public static class DataAgentResultExplainer
{
    public static string ExplainAccepted(
        string question,
        string dataset,
        int rowCount,
        string summary,
        DataAgentPlannerExplanation explanation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataset);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentNullException.ThrowIfNull(explanation);

        string rowWord = rowCount == 1 ? "row" : "rows";
        string signals = DataAgentContextFieldSanitizer.Sanitize(string.Join(", ", explanation.Signals), 240);
        return Sanitize(
            $"This query matched {dataset} and returned {rowCount} {rowWord}. " +
            $"The planner selected this dataset because it observed these signals: {signals}. " +
            "Results come from the local SQLite store and do not include live external data.");
    }

    public static string ExplainClarification(DataAgentClarificationRequest clarification)
    {
        ArgumentNullException.ThrowIfNull(clarification);
        return Sanitize($"DataAgent needs clarification before it can run a SQL query: {clarification.Question}");
    }

    static string Sanitize(string value)
    {
        return DataAgentContextFieldSanitizer.Sanitize(value);
    }
}
