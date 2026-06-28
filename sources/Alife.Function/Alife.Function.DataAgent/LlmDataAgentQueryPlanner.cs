namespace Alife.Function.DataAgent;

public sealed class LlmDataAgentQueryPlanner : IDataAgentQueryPlanner
{
    readonly string databasePath;
    readonly ILlmDataAgentPlannerClient client;
    readonly IDataAgentQueryPlanner fallback;
    readonly DataAgentCatalog catalog;
    readonly LlmDataAgentPlannerPromptFormatter formatter;

    public LlmDataAgentQueryPlanner(
        string databasePath,
        ILlmDataAgentPlannerClient client,
        IDataAgentQueryPlanner fallback)
        : this(databasePath, client, fallback, DataAgentCatalog.CreateDefault(), new LlmDataAgentPlannerPromptFormatter())
    {
    }

    public LlmDataAgentQueryPlanner(
        string databasePath,
        ILlmDataAgentPlannerClient client,
        IDataAgentQueryPlanner fallback,
        DataAgentCatalog catalog,
        LlmDataAgentPlannerPromptFormatter formatter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(fallback);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(formatter);

        this.databasePath = databasePath;
        this.client = client;
        this.fallback = fallback;
        this.catalog = catalog;
        this.formatter = formatter;
    }

    public DataAgentQueryPlanEnvelope Plan(DataAgentQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        DataAgentSchemaSnapshot schemaSnapshot = new DataAgentSchemaIntrospector(catalog, databasePath).Inspect();
        DataAgentLlmPlannerPrompt prompt = formatter.Format(request, catalog, schemaSnapshot);
        string rawOutput = client.Complete(prompt);
        DataAgentLlmPlannerResult result = new LlmDataAgentPlannerResponseParser(catalog).Parse(rawOutput);

        if (result.IsValid && result.Envelope is not null)
            return result.Envelope;

        return BuildFallbackEnvelope(request, result.RejectedReason);
    }

    DataAgentQueryPlanEnvelope BuildFallbackEnvelope(DataAgentQueryRequest request, string rejectedReason)
    {
        DataAgentQueryPlanEnvelope fallbackEnvelope = fallback.Plan(request);
        if (fallbackEnvelope.Plan is null || fallbackEnvelope.Clarification is not null)
            throw new InvalidOperationException("Fallback planner must return a query plan.");

        DataAgentQueryPlan plan = fallbackEnvelope.Plan;
        string[] signals = fallbackEnvelope.Explanation.Signals
            .Concat(["llm_invalid_output_fallback"])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new DataAgentQueryPlanEnvelope(
            plan,
            new DataAgentPlannerExplanation(
                nameof(LlmDataAgentQueryPlanner),
                plan.Intent,
                plan.Dataset,
                "low",
                signals,
                BuildFallbackReason(rejectedReason)));
    }

    static string BuildFallbackReason(string? rejectedReason)
    {
        string reason = SanitizeFallbackReason(rejectedReason);
        string fallbackReason = $"LLM planner output was invalid; deterministic fallback used: {reason}";

        return fallbackReason.Length <= 120
            ? fallbackReason
            : fallbackReason[..120];
    }

    static string SanitizeFallbackReason(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "invalid_model_output";

        string sanitized = value
            .Replace('\r', ' ')
            .Replace('\n', ' ');

        string[] dangerousKeywords =
        [
            "DROP",
            "DELETE",
            "INSERT",
            "UPDATE",
            "ALTER",
            "ATTACH",
            "PRAGMA",
            "TABLE"
        ];

        foreach (string keyword in dangerousKeywords)
            sanitized = ReplaceKeyword(sanitized, keyword);

        return string.Join(' ', sanitized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    static string ReplaceKeyword(string value, string keyword)
    {
        int start = 0;
        while (start < value.Length)
        {
            int index = value.IndexOf(keyword, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return value;

            int end = index + keyword.Length;
            if (IsBoundary(value, index - 1) && IsBoundary(value, end))
            {
                value = value[..index] + "[redacted]" + value[end..];
                start = index + "[redacted]".Length;
            }
            else
            {
                start = end;
            }
        }

        return value;
    }

    static bool IsBoundary(string value, int index)
    {
        return index < 0 ||
               index >= value.Length ||
               char.IsLetterOrDigit(value[index]) == false && value[index] != '_';
    }
}
