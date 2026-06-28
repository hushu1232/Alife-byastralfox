using System.Text.Json;

namespace Alife.Function.DataAgent;

public sealed record DataAgentLlmPlannerResult(
    bool IsValid,
    DataAgentQueryPlanEnvelope? Envelope,
    string RawModelOutput,
    string RejectedReason)
{
    public static DataAgentLlmPlannerResult Valid(string rawModelOutput, DataAgentQueryPlanEnvelope envelope)
    {
        return new DataAgentLlmPlannerResult(true, envelope, rawModelOutput, string.Empty);
    }

    public static DataAgentLlmPlannerResult Invalid(string rawModelOutput, string reason)
    {
        return new DataAgentLlmPlannerResult(false, null, rawModelOutput, reason);
    }
}

public sealed class LlmDataAgentPlannerResponseParser(DataAgentCatalog catalog)
{
    static readonly HashSet<string> AllowedConfidence = new(StringComparer.OrdinalIgnoreCase)
    {
        "high",
        "medium",
        "low"
    };

    readonly DataAgentQueryPlanValidator validator = new(catalog);

    public DataAgentLlmPlannerResult Parse(string rawModelOutput)
    {
        if (string.IsNullOrWhiteSpace(rawModelOutput))
            return DataAgentLlmPlannerResult.Invalid(rawModelOutput, "empty_output");

        string trimmed = rawModelOutput.Trim();
        if (trimmed.StartsWith('{') == false || trimmed.EndsWith('}') == false)
            return DataAgentLlmPlannerResult.Invalid(rawModelOutput, "json_must_be_single_object");

        try
        {
            using JsonDocument document = JsonDocument.Parse(trimmed);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return DataAgentLlmPlannerResult.Invalid(rawModelOutput, "json_must_be_single_object");

            string type = RequiredString(root, "type");

            return type switch
            {
                "plan" => ParsePlan(rawModelOutput, root),
                "clarification" => ParseClarification(rawModelOutput, root),
                _ => DataAgentLlmPlannerResult.Invalid(rawModelOutput, $"unsupported_type:{type}")
            };
        }
        catch (JsonException exception)
        {
            if (exception.Message.Contains("after a single JSON value", StringComparison.OrdinalIgnoreCase) ||
                exception.Message.Contains("Expected end of data", StringComparison.OrdinalIgnoreCase))
                return DataAgentLlmPlannerResult.Invalid(rawModelOutput, "json_must_be_single_object");

            return DataAgentLlmPlannerResult.Invalid(rawModelOutput, $"invalid_json:{exception.Message}");
        }
        catch (ArgumentException exception)
        {
            return DataAgentLlmPlannerResult.Invalid(rawModelOutput, exception.Message);
        }
    }

    DataAgentLlmPlannerResult ParsePlan(string rawModelOutput, JsonElement root)
    {
        _ = RequiredString(root, "planner_name");
        string intent = RequiredString(root, "intent");
        string dataset = RequiredString(root, "dataset");
        string confidence = RequiredConfidence(root);
        IReadOnlyList<string> signals = RequiredStringArray(root, "signals", requireNonEmpty: true);
        string reason = RequiredString(root, "reason");

        DataAgentQueryPlan plan = new(
            dataset,
            intent,
            RequiredStringArray(root, "select_fields", requireNonEmpty: false),
            RequiredFilters(root),
            RequiredSorts(root),
            RequiredInt(root, "limit"));

        DataAgentValidationResult validation = validator.Validate(plan);
        if (validation.IsValid == false)
            return DataAgentLlmPlannerResult.Invalid(rawModelOutput, string.Join(";", validation.Errors));

        DataAgentQueryPlanEnvelope envelope = new(
            plan,
            new DataAgentPlannerExplanation(
                nameof(LlmDataAgentQueryPlanner),
                intent,
                dataset,
                confidence,
                signals,
                reason));

        return DataAgentLlmPlannerResult.Valid(rawModelOutput, envelope);
    }

    static DataAgentLlmPlannerResult ParseClarification(string rawModelOutput, JsonElement root)
    {
        _ = RequiredString(root, "planner_name");
        string intent = RequiredString(root, "intent");
        string dataset = OptionalString(root, "dataset");
        string confidence = RequiredConfidence(root);
        IReadOnlyList<string> signals = RequiredStringArray(root, "signals", requireNonEmpty: true);
        string reason = RequiredString(root, "reason");
        string question = RequiredString(root, "clarification_question");
        IReadOnlyList<string> options = RequiredStringArray(root, "clarification_options", requireNonEmpty: true);

        if (options.Count is < 2 or > 4)
            return DataAgentLlmPlannerResult.Invalid(rawModelOutput, "invalid_clarification_option_count");

        DataAgentQueryPlanEnvelope envelope = new(
            null,
            new DataAgentPlannerExplanation(
                nameof(LlmDataAgentQueryPlanner),
                intent,
                dataset,
                confidence,
                signals,
                reason),
            new DataAgentClarificationRequest(question, options, reason));

        return DataAgentLlmPlannerResult.Valid(rawModelOutput, envelope);
    }

    static IReadOnlyList<DataAgentFilter> RequiredFilters(JsonElement root)
    {
        JsonElement filters = RequiredArray(root, "filters");
        List<DataAgentFilter> parsed = [];

        foreach (JsonElement filter in filters.EnumerateArray())
        {
            if (filter.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("invalid_array_item:filters");

            parsed.Add(new DataAgentFilter(
                RequiredString(filter, "field"),
                RequiredString(filter, "operator"),
                RequiredScalarValue(filter, "value")));
        }

        return parsed;
    }

    static IReadOnlyList<DataAgentOrderBy> RequiredSorts(JsonElement root)
    {
        JsonElement sorts = RequiredArray(root, "sorts");
        List<DataAgentOrderBy> parsed = [];

        foreach (JsonElement sort in sorts.EnumerateArray())
        {
            if (sort.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("invalid_array_item:sorts");

            parsed.Add(new DataAgentOrderBy(
                RequiredString(sort, "field"),
                RequiredString(sort, "direction")));
        }

        return parsed;
    }

    static string RequiredString(JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out JsonElement value) == false ||
            value.ValueKind != JsonValueKind.String)
            throw new ArgumentException($"missing_or_empty:{propertyName}");

        string? text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException($"missing_or_empty:{propertyName}");

        return text;
    }

    static string OptionalString(JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out JsonElement value) == false ||
            value.ValueKind == JsonValueKind.Null)
            return string.Empty;

        if (value.ValueKind != JsonValueKind.String)
            throw new ArgumentException($"invalid_string:{propertyName}");

        return value.GetString() ?? string.Empty;
    }

    static int RequiredInt(JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out JsonElement value) == false ||
            value.ValueKind != JsonValueKind.Number ||
            value.TryGetInt32(out int parsed) == false)
            throw new ArgumentException($"missing_or_invalid:{propertyName}");

        return parsed;
    }

    static string RequiredConfidence(JsonElement root)
    {
        string confidence = RequiredString(root, "confidence");
        if (AllowedConfidence.Contains(confidence) == false)
            throw new ArgumentException($"invalid_confidence:{confidence}");

        return confidence;
    }

    static IReadOnlyList<string> RequiredStringArray(JsonElement root, string propertyName, bool requireNonEmpty)
    {
        JsonElement array = RequiredArray(root, propertyName);
        List<string> values = [];

        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                throw new ArgumentException($"invalid_string_array:{propertyName}");

            values.Add(item.GetString()!);
        }

        if (requireNonEmpty && values.Count == 0)
            throw new ArgumentException($"empty_array:{propertyName}");

        return values;
    }

    static JsonElement RequiredArray(JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out JsonElement value) == false ||
            value.ValueKind != JsonValueKind.Array)
            throw new ArgumentException($"missing_or_invalid:{propertyName}");

        return value;
    }

    static object? RequiredScalarValue(JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out JsonElement value) == false)
            throw new ArgumentException($"missing_or_invalid:{propertyName}");

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt64(out long integer) => integer,
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => throw new ArgumentException("unsupported_scalar_value")
        };
    }
}
