namespace Alife.Function.DataAgent;

public sealed class DataAgentScenarioContextBuilder
{
    public DataAgentScenarioContext Build(
        DataAgentCatalog catalog,
        DataAgentScenarioKnowledgePack? pack,
        string? utterance)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        if (pack is null)
        {
            return CreateEmpty(
                "unavailable",
                "und",
                DataAgentScenarioContext.ReasonPackUnavailable);
        }

        if (string.IsNullOrWhiteSpace(utterance))
        {
            return CreateEmpty(
                pack.Scenario,
                pack.Culture,
                DataAgentScenarioContext.ReasonNoMatch);
        }

        List<(DataAgentScenarioTerm Term, string MatchedText)> termTextMatches = [];
        foreach (DataAgentScenarioTerm term in pack.Terms ?? [])
        {
            if (TryGetMatchedText(term, utterance, out string? matchedText))
            {
                termTextMatches.Add((term, matchedText!));
            }
        }

        List<DataAgentScenarioMetric> metricTextMatches = [];
        foreach (DataAgentScenarioMetric metric in pack.Metrics ?? [])
        {
            if (!string.IsNullOrWhiteSpace(metric.Name) &&
                utterance.Contains(metric.Name, StringComparison.OrdinalIgnoreCase))
            {
                metricTextMatches.Add(metric);
            }
        }

        if (termTextMatches.Count == 0 && metricTextMatches.Count == 0)
        {
            return CreateEmpty(
                pack.Scenario,
                pack.Culture,
                DataAgentScenarioContext.ReasonNoMatch);
        }

        List<DataAgentScenarioTermMatch> termMatches = [];
        List<string> candidateDatasets = [];
        List<string> candidateFields = [];
        HashSet<string> seenDatasets = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> seenFields = new(StringComparer.OrdinalIgnoreCase);

        foreach ((DataAgentScenarioTerm term, string matchedText) in termTextMatches)
        {
            if (string.IsNullOrWhiteSpace(term.Dataset) ||
                catalog.HasDataset(term.Dataset) == false)
            {
                continue;
            }

            string[] validFields = (term.Fields ?? [])
                .Where(field => !string.IsNullOrWhiteSpace(field) &&
                                catalog.HasField(term.Dataset, field))
                .ToArray();
            if (validFields.Length == 0)
            {
                continue;
            }

            termMatches.Add(new DataAgentScenarioTermMatch(
                term.Term,
                term.Dataset,
                validFields,
                matchedText));

            if (seenDatasets.Add(term.Dataset))
            {
                candidateDatasets.Add(term.Dataset);
            }

            foreach (string field in validFields)
            {
                if (seenFields.Add(field))
                {
                    candidateFields.Add(field);
                }
            }
        }

        List<DataAgentScenarioMetricMatch> metricMatches = [];
        foreach (DataAgentScenarioMetric metric in metricTextMatches)
        {
            if (IsMetricValid(catalog, candidateDatasets, metric) == false)
            {
                continue;
            }

            metricMatches.Add(new DataAgentScenarioMetricMatch(
                metric.Name,
                metric.Field,
                metric.Operator,
                metric.Value));
        }

        if (termMatches.Count == 0 && metricMatches.Count == 0)
        {
            return CreateEmpty(
                pack.Scenario,
                pack.Culture,
                DataAgentScenarioContext.ReasonCatalogMismatch);
        }

        return new DataAgentScenarioContext(
            pack.Scenario,
            pack.Culture,
            termMatches,
            metricMatches,
            candidateDatasets,
            candidateFields,
            DataAgentScenarioContext.ReasonMatched);
    }

    static bool TryGetMatchedText(
        DataAgentScenarioTerm term,
        string utterance,
        out string? matchedText)
    {
        matchedText = null;

        if (!string.IsNullOrWhiteSpace(term.Term) &&
            utterance.Contains(term.Term, StringComparison.OrdinalIgnoreCase))
        {
            matchedText = term.Term;
        }

        foreach (string alias in term.Aliases ?? [])
        {
            if (!string.IsNullOrWhiteSpace(alias) &&
                utterance.Contains(alias, StringComparison.OrdinalIgnoreCase) &&
                (matchedText is null || alias.Length > matchedText.Length))
            {
                matchedText = alias;
            }
        }

        return matchedText is not null;
    }

    static bool IsMetricValid(
        DataAgentCatalog catalog,
        IReadOnlyList<string> candidateDatasets,
        DataAgentScenarioMetric metric)
    {
        if (string.IsNullOrWhiteSpace(metric.Field))
        {
            return false;
        }

        if (candidateDatasets.Count > 0)
        {
            return candidateDatasets.Any(dataset => catalog.HasField(dataset, metric.Field));
        }

        return catalog.Datasets.Any(dataset => catalog.HasField(dataset.Name, metric.Field));
    }

    static DataAgentScenarioContext CreateEmpty(
        string scenario,
        string culture,
        string reasonCode)
    {
        return new DataAgentScenarioContext(
            scenario,
            culture,
            [],
            [],
            [],
            [],
            reasonCode);
    }
}
