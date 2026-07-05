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
        "WITH",
        "JOIN",
        "UNION",
        "CREATE",
        "TRUNCATE",
        "MERGE",
        "INSERT",
        "UPDATE",
        "ALTER",
        "ATTACH",
        "PRAGMA"
    ];
    static readonly string[] DangerousScenarioPhrases =
    [
        "ignore previous instructions"
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
        ApprovedSchemaView approvedSchema = BuildApprovedSchemaView(catalog, schemaSnapshot);
        StringBuilder builder = new();
        builder.AppendLine("Approved schema:");

        foreach (ApprovedDatasetSchema datasetSchema in approvedSchema.Datasets)
        {
            builder.Append("- ");
            builder.Append(datasetSchema.Name);
            builder.Append(": ");
            builder.AppendJoin(", ", datasetSchema.Fields);
            builder.AppendLine();
        }

        if (scenarioContext?.HasMatches == true)
        {
            FilteredScenarioContext? filteredScenarioContext = FilterScenarioContext(scenarioContext, approvedSchema);
            if (filteredScenarioContext is not null)
            {
                builder.AppendLine();
                AppendScenarioContext(builder, scenarioContext, filteredScenarioContext);
            }
        }

        return builder.ToString();
    }

    static ApprovedSchemaView BuildApprovedSchemaView(
        DataAgentCatalog catalog,
        DataAgentSchemaSnapshot schemaSnapshot)
    {
        ApprovedDatasetSchema[] datasets = schemaSnapshot.Datasets
            .Where(dataset => catalog.HasDataset(dataset.Name) && dataset.ExistsInDatabase && dataset.FieldsMatch)
            .OrderBy(dataset => dataset.Name, StringComparer.OrdinalIgnoreCase)
            .Select(dataset =>
            {
                string[] fields = dataset.DatabaseFields
                    .Where(field => catalog.HasField(dataset.Name, field))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return fields.Length == 0
                    ? null
                    : new ApprovedDatasetSchema(dataset.Name, fields);
            })
            .Where(dataset => dataset is not null)
            .Cast<ApprovedDatasetSchema>()
            .ToArray();

        return new ApprovedSchemaView(datasets);
    }

    static FilteredScenarioContext? FilterScenarioContext(
        DataAgentScenarioContext scenarioContext,
        ApprovedSchemaView approvedSchema)
    {
        string[] candidateDatasets = FilterCandidateDatasets(scenarioContext.CandidateDatasets, approvedSchema);
        string[] candidateFields = FilterCandidateFields(scenarioContext.CandidateFields, approvedSchema);
        FilteredScenarioTerm[] terms = FilterScenarioTerms(scenarioContext.Terms, approvedSchema);
        FilteredScenarioMetric[] metrics = FilterScenarioMetrics(scenarioContext.Metrics, candidateDatasets, approvedSchema);

        return terms.Length == 0 && metrics.Length == 0
            ? null
            : new FilteredScenarioContext(candidateDatasets, candidateFields, terms, metrics);
    }

    static string[] FilterCandidateDatasets(
        IReadOnlyList<string> candidateDatasets,
        ApprovedSchemaView approvedSchema)
    {
        List<string> values = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (string candidateDataset in candidateDatasets)
        {
            if (approvedSchema.TryGetDataset(candidateDataset, out ApprovedDatasetSchema dataset) &&
                seen.Add(dataset.Name))
            {
                values.Add(dataset.Name);
            }
        }

        return values.ToArray();
    }

    static string[] FilterCandidateFields(
        IReadOnlyList<string> candidateFields,
        ApprovedSchemaView approvedSchema)
    {
        List<string> values = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (string candidateField in candidateFields)
        {
            if (approvedSchema.HasFieldAny(candidateField) &&
                seen.Add(candidateField))
            {
                values.Add(candidateField);
            }
        }

        return values.ToArray();
    }

    static FilteredScenarioTerm[] FilterScenarioTerms(
        IReadOnlyList<DataAgentScenarioTermMatch> terms,
        ApprovedSchemaView approvedSchema)
    {
        List<FilteredScenarioTerm> values = [];

        foreach (DataAgentScenarioTermMatch term in terms)
        {
            if (approvedSchema.TryGetDataset(term.Dataset, out ApprovedDatasetSchema dataset) == false)
                continue;

            string[] fields = FilterFieldsForDataset(term.Fields, dataset);
            if (fields.Length == 0)
                continue;

            values.Add(new FilteredScenarioTerm(term.Term, dataset.Name, fields));
        }

        return values.ToArray();
    }

    static string[] FilterFieldsForDataset(
        IReadOnlyList<string> fields,
        ApprovedDatasetSchema dataset)
    {
        List<string> values = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (string field in fields)
        {
            if (dataset.HasField(field) && seen.Add(field))
                values.Add(field);
        }

        return values.ToArray();
    }

    static FilteredScenarioMetric[] FilterScenarioMetrics(
        IReadOnlyList<DataAgentScenarioMetricMatch> metrics,
        IReadOnlyList<string> candidateDatasets,
        ApprovedSchemaView approvedSchema)
    {
        List<FilteredScenarioMetric> values = [];

        foreach (DataAgentScenarioMetricMatch metric in metrics)
        {
            if (TryGetAllowedOperator(metric.Operator, out string? allowedOperator) == false)
                continue;

            if (IsMetricFieldApproved(metric.Field, candidateDatasets, approvedSchema) == false)
                continue;

            values.Add(new FilteredScenarioMetric(metric.Name, metric.Field, allowedOperator!, metric.Value));
        }

        return values.ToArray();
    }

    static bool IsMetricFieldApproved(
        string field,
        IReadOnlyList<string> candidateDatasets,
        ApprovedSchemaView approvedSchema)
    {
        if (candidateDatasets.Count > 0)
            return candidateDatasets.Any(dataset => approvedSchema.HasField(dataset, field));

        return approvedSchema.HasFieldAny(field);
    }

    static bool TryGetAllowedOperator(string value, out string? allowedOperator)
    {
        allowedOperator = AllowedOperators.FirstOrDefault(
            current => string.Equals(current, value, StringComparison.OrdinalIgnoreCase));
        return allowedOperator is not null;
    }

    static void AppendScenarioContext(
        StringBuilder builder,
        DataAgentScenarioContext scenarioContext,
        FilteredScenarioContext filteredScenarioContext)
    {
        builder.AppendLine("Scenario context:");
        builder.Append("scenario: ");
        builder.AppendLine(SanitizeScenarioText(scenarioContext.Scenario));
        builder.Append("reason_code: ");
        builder.AppendLine(SanitizeScenarioText(scenarioContext.ReasonCode));
        builder.Append("candidate_datasets: ");
        builder.AppendLine(FormatIdentifierList(filteredScenarioContext.CandidateDatasets));
        builder.Append("candidate_fields: ");
        builder.AppendLine(FormatIdentifierList(filteredScenarioContext.CandidateFields));

        if (filteredScenarioContext.Terms.Count > 0)
        {
            builder.AppendLine("matched_terms:");
            foreach (FilteredScenarioTerm term in filteredScenarioContext.Terms.Take(MaxScenarioItems))
            {
                builder.Append(SanitizeScenarioText(term.Term));
                builder.Append(" -> ");
                builder.Append(SanitizeApprovedIdentifier(term.Dataset));
                builder.Append('(');
                builder.Append(string.Join(',', term.Fields.Take(MaxScenarioItems).Select(SanitizeApprovedIdentifier)));
                builder.AppendLine(")");
            }
        }

        if (filteredScenarioContext.Metrics.Count > 0)
        {
            builder.AppendLine("matched_metrics:");
            foreach (FilteredScenarioMetric metric in filteredScenarioContext.Metrics.Take(MaxScenarioItems))
            {
                builder.Append(SanitizeScenarioText(metric.Name));
                builder.Append(": ");
                builder.Append(SanitizeApprovedIdentifier(metric.Field));
                builder.Append(' ');
                builder.Append(SanitizeApprovedIdentifier(metric.Operator));
                builder.Append(' ');
                builder.Append(FormatScenarioValue(metric.Value));
                builder.AppendLine();
            }
        }

        builder.AppendLine("Scenario context is a hint only; use only approved schema fields and operators.");
        builder.AppendLine("Do not output SQL.");
    }

    static string FormatIdentifierList(IReadOnlyList<string> values)
    {
        string[] safeValues = values
            .Take(MaxScenarioItems)
            .Select(SanitizeApprovedIdentifier)
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

    static string SanitizeApprovedIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string sanitized = value
            .Replace('\r', ' ')
            .Replace('\n', ' ');

        sanitized = string.Join(' ', sanitized.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        return sanitized.Length <= MaxScenarioValueLength
            ? sanitized
            : sanitized[..MaxScenarioValueLength];
    }

    static string SanitizeScenarioText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string sanitized = value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace(';', ' ');

        foreach (string phrase in DangerousScenarioPhrases)
            sanitized = ReplaceScenarioKeyword(sanitized, phrase);

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

    sealed class ApprovedSchemaView(IEnumerable<ApprovedDatasetSchema> datasets)
    {
        readonly Dictionary<string, ApprovedDatasetSchema> datasetsByName =
            datasets.ToDictionary(dataset => dataset.Name, StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<ApprovedDatasetSchema> Datasets { get; } = datasets.ToArray();

        public bool TryGetDataset(string? name, out ApprovedDatasetSchema dataset)
        {
            if (string.IsNullOrWhiteSpace(name) == false &&
                datasetsByName.TryGetValue(name, out ApprovedDatasetSchema? match))
            {
                dataset = match;
                return true;
            }

            dataset = null!;
            return false;
        }

        public bool HasField(string? datasetName, string? field)
        {
            return TryGetDataset(datasetName, out ApprovedDatasetSchema dataset) &&
                   dataset.HasField(field);
        }

        public bool HasFieldAny(string? field)
        {
            return string.IsNullOrWhiteSpace(field) == false &&
                   Datasets.Any(dataset => dataset.HasField(field));
        }
    }

    sealed class ApprovedDatasetSchema
    {
        readonly HashSet<string> fields;

        public ApprovedDatasetSchema(string name, IReadOnlyList<string> fields)
        {
            Name = name;
            Fields = fields;
            this.fields = fields.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        public string Name { get; }

        public IReadOnlyList<string> Fields { get; }

        public bool HasField(string? field)
        {
            return string.IsNullOrWhiteSpace(field) == false &&
                   fields.Contains(field);
        }
    }

    sealed record FilteredScenarioContext(
        IReadOnlyList<string> CandidateDatasets,
        IReadOnlyList<string> CandidateFields,
        IReadOnlyList<FilteredScenarioTerm> Terms,
        IReadOnlyList<FilteredScenarioMetric> Metrics);

    sealed record FilteredScenarioTerm(
        string Term,
        string Dataset,
        IReadOnlyList<string> Fields);

    sealed record FilteredScenarioMetric(
        string Name,
        string Field,
        string Operator,
        object? Value);

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
