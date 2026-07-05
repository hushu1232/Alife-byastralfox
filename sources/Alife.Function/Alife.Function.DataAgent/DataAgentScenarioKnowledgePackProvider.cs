using System.Text.Json;

namespace Alife.Function.DataAgent;

public static class DataAgentScenarioKnowledgePackProvider
{
    static readonly string[] SupportedMetricOperators =
    [
        "=",
        "!=",
        ">",
        ">=",
        "<",
        "<=",
        "contains"
    ];

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
            return Array.AsReadOnly(Array.Empty<DataAgentScenarioTerm>());
        }

        DataAgentScenarioTerm[] matches = pack.Terms
            .Where(term => MatchesTerm(term, utterance))
            .ToArray();

        return Array.AsReadOnly(matches);
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
            .Select(SnapshotMetric)
            .ToArray();

        return new DataAgentScenarioKnowledgePack(
            pack.Scenario,
            pack.Culture,
            Array.AsReadOnly(termSnapshots),
            Array.AsReadOnly(metricSnapshots));
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

        string[] aliases = term.Aliases?.ToArray() ?? [];
        string[] fields = term.Fields.ToArray();

        return new DataAgentScenarioTerm(
            term.Term,
            Array.AsReadOnly(aliases),
            term.Dataset,
            Array.AsReadOnly(fields));
    }

    static DataAgentScenarioMetric SnapshotMetric(DataAgentScenarioMetric metric)
    {
        if (string.IsNullOrWhiteSpace(metric.Name))
        {
            throw new InvalidOperationException("Scenario metric name is required.");
        }

        if (string.IsNullOrWhiteSpace(metric.Field))
        {
            throw new InvalidOperationException($"Scenario metric '{metric.Name}' field is required.");
        }

        if (string.IsNullOrWhiteSpace(metric.Operator))
        {
            throw new InvalidOperationException($"Scenario metric '{metric.Name}' operator is required.");
        }

        if (!SupportedMetricOperators.Contains(metric.Operator, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Scenario metric '{metric.Name}' operator '{metric.Operator}' is not supported.");
        }

        return new DataAgentScenarioMetric(
            metric.Name,
            metric.Field,
            metric.Operator,
            NormalizeMetricValue(metric.Name, metric.Value));
    }

    static object? NormalizeMetricValue(string metricName, object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is JsonElement jsonElement)
        {
            return NormalizeJsonMetricValue(metricName, jsonElement);
        }

        if (value is string or bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
        {
            return value;
        }

        throw new InvalidOperationException($"Scenario metric '{metricName}' value must be a scalar.");
    }

    static object? NormalizeJsonMetricValue(string metricName, JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => NormalizeJsonMetricNumber(metricName, value),
            JsonValueKind.Null => null,
            _ => throw new InvalidOperationException($"Scenario metric '{metricName}' value must be a scalar.")
        };
    }

    static object NormalizeJsonMetricNumber(string metricName, JsonElement value)
    {
        if (value.TryGetInt32(out int intValue))
        {
            return intValue;
        }

        if (value.TryGetInt64(out long longValue))
        {
            return longValue;
        }

        if (value.TryGetDecimal(out decimal decimalValue))
        {
            return decimalValue;
        }

        if (value.TryGetDouble(out double doubleValue))
        {
            return doubleValue;
        }

        throw new InvalidOperationException($"Scenario metric '{metricName}' value must be a scalar number.");
    }
}
