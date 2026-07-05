using System.Globalization;
using System.Text;

namespace Alife.Function.DataAgent;

public sealed record DataAgentLlmPlannerPrompt(
    string System,
    string Schema,
    string Contract,
    string User);

public sealed class LlmDataAgentPlannerPromptFormatter
{
    static readonly string[] AllowedOperators = ["=", "!=", "<>", ">", ">=", "<", "<=", "contains"];
    static readonly string[] DangerousScenarioKeywords =
    [
        "SELECT",
        "DELETE",
        "DROP",
        "INSERT",
        "UPDATE",
        "ALTER",
        "ATTACH",
        "PRAGMA",
        "TABLE",
        "FROM",
        "WHERE"
    ];
    const int MaxScenarioItems = 8;
    const int MaxScenarioValueLength = 120;

    public DataAgentLlmPlannerPrompt Format(
        DataAgentQueryRequest request,
        DataAgentCatalog catalog,
        DataAgentSchemaSnapshot schemaSnapshot)
    {
        return Format(request, catalog, schemaSnapshot, null);
    }

    public DataAgentLlmPlannerPrompt Format(
        DataAgentQueryRequest request,
        DataAgentCatalog catalog,
        DataAgentSchemaSnapshot schemaSnapshot,
        DataAgentScenarioContext? scenarioContext)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(schemaSnapshot);

        if (schemaSnapshot.CatalogMatchesDatabase == false)
            throw new InvalidOperationException("DataAgent LLM planner requires catalog and SQLite schema to match.");

        return new DataAgentLlmPlannerPrompt(
            BuildSystem(),
            BuildSchema(catalog, schemaSnapshot, scenarioContext),
            BuildContract(),
            BuildUser(request));
    }

    static string BuildSystem()
    {
        StringBuilder builder = new();
        builder.AppendLine("You are the DataAgent LLM planner.");
        builder.AppendLine("Return JSON only.");
        builder.AppendLine("Do not output SQL.");
        builder.AppendLine("Use only approved datasets, fields, and operators from the schema section.");
        builder.Append("Allowed operators: ");
        builder.AppendJoin(", ", AllowedOperators);
        builder.Append('.');
        return builder.ToString();
    }

    static string BuildSchema(
        DataAgentCatalog catalog,
        DataAgentSchemaSnapshot schemaSnapshot,
        DataAgentScenarioContext? scenarioContext)
    {
        StringBuilder builder = new();
        builder.AppendLine("Approved schema:");

        foreach (DataAgentDatasetSchema datasetSchema in schemaSnapshot.Datasets
            .Where(dataset => catalog.HasDataset(dataset.Name) && dataset.ExistsInDatabase && dataset.FieldsMatch)
            .OrderBy(dataset => dataset.Name, StringComparer.OrdinalIgnoreCase))
        {
            string[] fields = datasetSchema.DatabaseFields
                .Where(field => catalog.HasField(datasetSchema.Name, field))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (fields.Length == 0)
                continue;

            builder.Append("- ");
            builder.Append(datasetSchema.Name);
            builder.Append(": ");
            builder.AppendJoin(", ", fields);
            builder.AppendLine();
        }

        if (scenarioContext?.HasMatches == true)
        {
            builder.AppendLine();
            AppendScenarioContext(builder, scenarioContext);
        }

        return builder.ToString();
    }

    static void AppendScenarioContext(StringBuilder builder, DataAgentScenarioContext scenarioContext)
    {
        builder.AppendLine("Scenario context:");
        builder.Append("scenario: ");
        builder.AppendLine(SanitizeScenarioText(scenarioContext.Scenario));
        builder.Append("reason_code: ");
        builder.AppendLine(SanitizeScenarioText(scenarioContext.ReasonCode));
        builder.Append("candidate_datasets: ");
        builder.AppendLine(FormatScenarioList(scenarioContext.CandidateDatasets));
        builder.Append("candidate_fields: ");
        builder.AppendLine(FormatScenarioList(scenarioContext.CandidateFields));

        foreach (DataAgentScenarioTermMatch term in scenarioContext.Terms.Take(MaxScenarioItems))
        {
            builder.Append(SanitizeScenarioText(term.Term));
            builder.Append(" -> ");
            builder.Append(SanitizeScenarioText(term.Dataset));
            builder.Append('(');
            builder.Append(string.Join(',', term.Fields.Take(MaxScenarioItems).Select(SanitizeScenarioText)));
            builder.AppendLine(")");
        }

        foreach (DataAgentScenarioMetricMatch metric in scenarioContext.Metrics.Take(MaxScenarioItems))
        {
            builder.Append(SanitizeScenarioText(metric.Name));
            builder.Append(": ");
            builder.Append(SanitizeScenarioText(metric.Field));
            builder.Append(' ');
            builder.Append(SanitizeScenarioText(metric.Operator));
            builder.Append(' ');
            builder.Append(FormatScenarioValue(metric.Value));
            builder.AppendLine();
        }

        builder.AppendLine("Scenario context is a hint only; use only approved schema fields and operators.");
        builder.AppendLine("Do not output SQL.");
    }

    static string FormatScenarioList(IReadOnlyList<string> values)
    {
        string[] safeValues = values
            .Take(MaxScenarioItems)
            .Select(SanitizeScenarioText)
            .Where(value => value.Length > 0)
            .ToArray();

        return safeValues.Length == 0
            ? "none"
            : string.Join(", ", safeValues);
    }

    static string FormatScenarioValue(object? value)
    {
        if (value is null)
            return "null";

        if (value is bool boolValue)
            return boolValue ? "true" : "false";

        string text = value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : value.ToString() ?? string.Empty;

        return SanitizeScenarioText(text);
    }

    static string SanitizeScenarioText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string sanitized = value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace(';', ' ');

        foreach (string keyword in DangerousScenarioKeywords)
            sanitized = ReplaceScenarioKeyword(sanitized, keyword);

        sanitized = string.Join(' ', sanitized.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        return sanitized.Length <= MaxScenarioValueLength
            ? sanitized
            : sanitized[..MaxScenarioValueLength];
    }

    static string ReplaceScenarioKeyword(string value, string keyword)
    {
        int start = 0;
        while (start < value.Length)
        {
            int index = value.IndexOf(keyword, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return value;

            int end = index + keyword.Length;
            if (IsScenarioKeywordBoundary(value, index - 1) && IsScenarioKeywordBoundary(value, end))
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

    static bool IsScenarioKeywordBoundary(string value, int index)
    {
        return index < 0 ||
               index >= value.Length ||
               char.IsLetterOrDigit(value[index]) == false && value[index] != '_';
    }

    static string BuildContract()
    {
        StringBuilder builder = new();
        builder.AppendLine("Return exactly one JSON object matching one of these examples.");
        builder.AppendLine("""{"type":"plan","planner_name":"LlmDataAgentQueryPlanner","dataset":"document_index","intent":"find_documents","confidence":"medium","signals":["documents"],"reason":"The question asks for DataAgent documents.","select_fields":["path","summary"],"filters":[{"field":"summary","operator":"contains","value":"DataAgent"}],"sorts":[{"field":"updated_at","direction":"desc"}],"limit":20}""");
        builder.Append("""{"type":"clarification","planner_name":"LlmDataAgentQueryPlanner","intent":"clarify_ambiguous_query","dataset":"","confidence":"low","signals":["ambiguous_dataset"],"reason":"The question is ambiguous.","clarification_question":"Which dataset should I use?","clarification_options":["document_index","test_run"]}""");
        return builder.ToString();
    }

    static string BuildUser(DataAgentQueryRequest request)
    {
        StringBuilder builder = new();
        builder.AppendLine("Question: " + request.Question);
        builder.AppendLine("Role: " + request.Role);
        builder.AppendLine("Locale: " + request.Locale);
        builder.Append("AllowLiveSources: ");
        builder.Append(request.AllowLiveSources);
        return builder.ToString();
    }
}
