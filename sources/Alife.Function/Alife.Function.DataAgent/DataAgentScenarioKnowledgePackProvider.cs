using System.Text.Json;

namespace Alife.Function.DataAgent;

public static class DataAgentScenarioKnowledgePackProvider
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static DataAgentScenarioKnowledgePack Load(string path)
    {
        string json = File.ReadAllText(path);
        DataAgentScenarioKnowledgePack pack =
            JsonSerializer.Deserialize<DataAgentScenarioKnowledgePack>(json, JsonOptions)
            ?? throw new InvalidOperationException("Scenario knowledge pack could not be deserialized.");

        return SnapshotAndValidate(pack);
    }

    public static IReadOnlyList<DataAgentScenarioTerm> ResolveTerms(
        DataAgentScenarioKnowledgePack pack,
        string utterance)
    {
        if (string.IsNullOrWhiteSpace(utterance))
        {
            return [];
        }

        return pack.Terms
            .Where(term => MatchesTerm(term, utterance))
            .ToArray();
    }

    static bool MatchesTerm(DataAgentScenarioTerm term, string utterance)
    {
        if (!string.IsNullOrWhiteSpace(term.Term) &&
            utterance.Contains(term.Term, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return term.Aliases.Any(alias =>
            !string.IsNullOrWhiteSpace(alias) &&
            utterance.Contains(alias, StringComparison.OrdinalIgnoreCase));
    }

    static DataAgentScenarioKnowledgePack SnapshotAndValidate(DataAgentScenarioKnowledgePack pack)
    {
        if (string.IsNullOrWhiteSpace(pack.Scenario))
        {
            throw new InvalidOperationException("Scenario knowledge pack scenario is required.");
        }

        if (string.IsNullOrWhiteSpace(pack.Culture))
        {
            throw new InvalidOperationException("Scenario knowledge pack culture is required.");
        }

        if (pack.Terms is null)
        {
            throw new InvalidOperationException("Scenario knowledge pack terms are required.");
        }

        if (pack.Metrics is null)
        {
            throw new InvalidOperationException("Scenario knowledge pack metrics are required.");
        }

        HashSet<string> terms = new(StringComparer.Ordinal);
        DataAgentScenarioTerm[] termSnapshots = pack.Terms
            .Select(term => SnapshotTerm(term, terms))
            .ToArray();
        DataAgentScenarioMetric[] metricSnapshots = pack.Metrics
            .Select(metric => new DataAgentScenarioMetric(
                metric.Name,
                metric.Field,
                metric.Operator,
                metric.Value))
            .ToArray();

        return new DataAgentScenarioKnowledgePack(
            pack.Scenario,
            pack.Culture,
            termSnapshots,
            metricSnapshots);
    }

    static DataAgentScenarioTerm SnapshotTerm(
        DataAgentScenarioTerm term,
        HashSet<string> terms)
    {
        if (string.IsNullOrWhiteSpace(term.Term))
        {
            throw new InvalidOperationException("Scenario term is required.");
        }

        if (!terms.Add(term.Term))
        {
            throw new InvalidOperationException($"Duplicate scenario term: {term.Term}");
        }

        if (string.IsNullOrWhiteSpace(term.Dataset))
        {
            throw new InvalidOperationException($"Scenario term '{term.Term}' dataset is required.");
        }

        if (term.Fields is null || term.Fields.Count == 0)
        {
            throw new InvalidOperationException($"Scenario term '{term.Term}' fields are required.");
        }

        return new DataAgentScenarioTerm(
            term.Term,
            term.Aliases?.ToArray() ?? [],
            term.Dataset,
            term.Fields.ToArray());
    }
}
