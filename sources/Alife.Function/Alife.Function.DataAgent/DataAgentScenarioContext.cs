namespace Alife.Function.DataAgent;

public sealed class DataAgentScenarioContext
{
    public const string ReasonMatched = "scenario_context_matched";
    public const string ReasonNoMatch = "scenario_context_no_match";
    public const string ReasonCatalogMismatch = "scenario_context_catalog_mismatch";
    public const string ReasonPackUnavailable = "scenario_context_pack_unavailable";

    public DataAgentScenarioContext(
        string? scenario,
        string? culture,
        IEnumerable<DataAgentScenarioTermMatch>? terms,
        IEnumerable<DataAgentScenarioMetricMatch>? metrics,
        IEnumerable<string>? candidateDatasets,
        IEnumerable<string>? candidateFields,
        string? reasonCode)
    {
        Scenario = NormalizeOrDefault(scenario, "unknown");
        Culture = NormalizeOrDefault(culture, "und");
        Terms = Snapshot(terms);
        Metrics = Snapshot(metrics);
        CandidateDatasets = Snapshot(candidateDatasets);
        CandidateFields = Snapshot(candidateFields);
        ReasonCode = NormalizeOrDefault(reasonCode, ReasonNoMatch);
    }

    public string Scenario { get; }

    public string Culture { get; }

    public IReadOnlyList<DataAgentScenarioTermMatch> Terms { get; }

    public IReadOnlyList<DataAgentScenarioMetricMatch> Metrics { get; }

    public IReadOnlyList<string> CandidateDatasets { get; }

    public IReadOnlyList<string> CandidateFields { get; }

    public string ReasonCode { get; }

    public bool HasMatches => Terms.Count > 0 || Metrics.Count > 0;

    static IReadOnlyList<T> Snapshot<T>(IEnumerable<T>? values)
    {
        if (values is null)
        {
            return Array.AsReadOnly(Array.Empty<T>());
        }

        return Array.AsReadOnly(values.ToArray());
    }

    static string NormalizeOrDefault(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}

public sealed class DataAgentScenarioTermMatch
{
    public DataAgentScenarioTermMatch(
        string term,
        string dataset,
        IEnumerable<string>? fields,
        string matchedText)
    {
        Term = term;
        Dataset = dataset;
        Fields = Snapshot(fields);
        MatchedText = matchedText;
    }

    public string Term { get; }

    public string Dataset { get; }

    public IReadOnlyList<string> Fields { get; }

    public string MatchedText { get; }

    static IReadOnlyList<string> Snapshot(IEnumerable<string>? values)
    {
        if (values is null)
        {
            return Array.AsReadOnly(Array.Empty<string>());
        }

        return Array.AsReadOnly(values.ToArray());
    }
}

public sealed record DataAgentScenarioMetricMatch(
    string Name,
    string Field,
    string Operator,
    object? Value);
