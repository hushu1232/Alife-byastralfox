using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Alife.Function.DataAgent;

public static class DataAgentScenarioDiagnosticsFormatter
{
    const string Redacted = "redacted";
    const int MaxItems = 16;
    const int MaxFieldLength = 120;

    static readonly Regex SqlLikePattern = new(
        @"\b(select|insert|update|delete|merge|drop|alter|create|truncate|with|from|join|where|having|limit|pragma|attach|detach)\b|\border\s+by\b|\bgroup\s+by\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    static readonly Regex UnsafeContextPattern = new(
        @"tool_route_context|data_agent_evidence_pack|hidden_context|hidden\s+context|hidden\s+prompt|ignore\s+previous\s+instructions|tool\s+broker|allowed\s+xml\s+tool",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string Format(DataAgentScenarioContext? context)
    {
        if (context is null)
        {
            return string.Join(Environment.NewLine,
                "DataAgent scenario diagnostics",
                "state=unavailable",
                $"reason={DataAgentScenarioContext.ReasonPackUnavailable}");
        }

        return string.Join(Environment.NewLine,
            "DataAgent scenario diagnostics",
            $"scenario={SafeText(context.Scenario)}",
            $"reason={SafeText(context.ReasonCode)}",
            $"datasets={FormatList(context.CandidateDatasets)}",
            $"fields={FormatList(context.CandidateFields)}",
            $"terms={FormatTerms(context.Terms)}",
            $"metrics={FormatMetrics(context.Metrics)}");
    }

    static string FormatList(IReadOnlyList<string> values)
    {
        string[] safeValues = values
            .Take(MaxItems)
            .Select(SafeText)
            .Where(value => value.Length > 0)
            .ToArray();

        return safeValues.Length == 0 ? "none" : string.Join(',', safeValues);
    }

    static string FormatTerms(IReadOnlyList<DataAgentScenarioTermMatch> terms)
    {
        string[] safeTerms = terms
            .Take(MaxItems)
            .Select(term => $"{SafeText(term.Term)}:{SafeText(term.Dataset)}")
            .Where(value => value != ":")
            .ToArray();

        return safeTerms.Length == 0 ? "none" : string.Join(';', safeTerms);
    }

    static string FormatMetrics(IReadOnlyList<DataAgentScenarioMetricMatch> metrics)
    {
        string[] safeMetrics = metrics
            .Take(MaxItems)
            .Select(metric => $"{SafeText(metric.Name)}:{SafeText(metric.Field)}{SafeOperator(metric.Operator)}{SafeValue(metric.Value)}")
            .Where(value => value != ":")
            .ToArray();

        return safeMetrics.Length == 0 ? "none" : string.Join(';', safeMetrics);
    }

    static string SafeValue(object? value)
    {
        if (value is null)
            return "null";

        if (value is bool boolValue)
            return boolValue ? "true" : "false";

        string text = value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : value.ToString() ?? string.Empty;

        return SafeText(text);
    }

    static string SafeOperator(string value)
    {
        return value switch
        {
            "=" or "!=" or "<>" or ">" or ">=" or "<" or "<=" => value,
            _ => SafeText(value)
        };
    }

    static string SafeText(string? value)
    {
        value ??= string.Empty;
        if (IsUnsafe(value))
            return Redacted;

        string sanitized = DataAgentContextFieldSanitizer.Sanitize(value, MaxFieldLength);
        sanitized = sanitized
            .Replace(';', ' ')
            .Replace(',', ' ')
            .Replace(':', ' ')
            .Replace('=', ':');
        sanitized = CollapseWhitespace(sanitized);

        return sanitized.Length == 0 ? "empty" : sanitized;
    }

    static bool IsUnsafe(string value)
    {
        return SqlLikePattern.IsMatch(value) ||
            UnsafeContextPattern.IsMatch(value);
    }

    static string CollapseWhitespace(string value)
    {
        StringBuilder builder = new(value.Length);
        bool previousWasWhiteSpace = false;

        foreach (char current in value)
        {
            if (char.IsWhiteSpace(current))
            {
                if (previousWasWhiteSpace == false)
                    builder.Append(' ');

                previousWasWhiteSpace = true;
                continue;
            }

            builder.Append(current);
            previousWasWhiteSpace = false;
        }

        return builder.ToString().Trim();
    }
}
