using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Alife.Function.DataAgent;

public static class DataAgentScenarioDiagnosticsFormatter
{
    const string Redacted = "redacted";
    const int MaxItems = 16;
    const int MaxFieldLength = 120;

    static readonly Regex ClearSqlFragmentPattern = new(
        @"\b(select|insert|update|delete|merge|drop|alter|create|truncate|with|pragma|attach|detach)\b|\bunion(?:\s+all)?\b|\btable\b(?:\s+\S+)?|\bfrom\s+\S+|\bjoin\s+\S+|\bwhere\s+\S+|\bhaving\s+\S+|\blimit\s+\d+\b|\border\s+by\b|\bgroup\s+by\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    static readonly Regex UnsafeMarkerPattern = new(
        @"tool_route_context|data_agent_evidence_pack|hidden_context|hidden\s+context|hidden\s+prompt|ignore\s+previous\s+instructions|tool[_\s-]?broker|allowed\s+xml\s+tools?",
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
            $"scenario={SafeIdentifier(context.Scenario)}",
            $"reason={SafeIdentifier(context.ReasonCode)}",
            $"datasets={FormatList(context.CandidateDatasets)}",
            $"fields={FormatList(context.CandidateFields)}",
            $"terms={FormatTerms(context.Terms)}",
            $"metrics={FormatMetrics(context.Metrics)}");
    }

    static string FormatList(IReadOnlyList<string> values)
    {
        string[] safeValues = values
            .Take(MaxItems)
            .Select(SafeIdentifier)
            .Where(value => value.Length > 0)
            .ToArray();

        return safeValues.Length == 0 ? "none" : string.Join(',', safeValues);
    }

    static string FormatTerms(IReadOnlyList<DataAgentScenarioTermMatch> terms)
    {
        string[] safeTerms = terms
            .Take(MaxItems)
            .Select(term => $"{SafeLabel(term.Term)}:{SafeIdentifier(term.Dataset)}")
            .ToArray();

        return safeTerms.Length == 0 ? "none" : string.Join(';', safeTerms);
    }

    static string FormatMetrics(IReadOnlyList<DataAgentScenarioMetricMatch> metrics)
    {
        string[] safeMetrics = metrics
            .Take(MaxItems)
            .Select(metric => $"{SafeLabel(metric.Name)}:{SafeIdentifier(metric.Field)}{SafeOperator(metric.Operator)}{SafeValue(metric.Value)}")
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

        return SafeFreeText(text);
    }

    static string SafeOperator(string value)
    {
        return SafeIdentifier(value);
    }

    static string SafeIdentifier(string? value)
    {
        value ??= string.Empty;
        string sanitized = NormalizeDelimitedText(value, replaceEquals: false);

        if (ContainsUnsafeMarker(value, sanitized) ||
            ContainsClearSqlIdentifierFragment(value, sanitized))
        {
            return Redacted;
        }

        return EmptyIfBlank(sanitized);
    }

    static string SafeLabel(string? value)
    {
        return SafeFreeText(value);
    }

    static string SafeFreeText(string? value)
    {
        value ??= string.Empty;
        string sanitized = NormalizeDelimitedText(value, replaceEquals: true);

        if (ContainsUnsafeMarker(value, sanitized) ||
            ContainsClearSqlFragment(value, sanitized))
        {
            return Redacted;
        }

        return EmptyIfBlank(sanitized);
    }

    static bool ContainsUnsafeMarker(string value, string sanitized)
    {
        return UnsafeMarkerPattern.IsMatch(value) ||
            UnsafeMarkerPattern.IsMatch(sanitized);
    }

    static bool ContainsClearSqlIdentifierFragment(string value, string sanitized)
    {
        if (IsSingleIdentifierToken(sanitized))
            return false;

        return ContainsClearSqlFragment(value, sanitized);
    }

    static bool ContainsClearSqlFragment(string value, string sanitized)
    {
        return ClearSqlFragmentPattern.IsMatch(value) ||
            ClearSqlFragmentPattern.IsMatch(sanitized);
    }

    static bool IsSingleIdentifierToken(string value)
    {
        return value.Length > 0 &&
            value.All(current => char.IsAsciiLetterOrDigit(current) || current is '_' or '-' or '.');
    }

    static string NormalizeDelimitedText(string value, bool replaceEquals)
    {
        string sanitized = DataAgentContextFieldSanitizer.Sanitize(value, MaxFieldLength)
            .Replace(';', ' ')
            .Replace(',', ' ')
            .Replace(':', ' ');

        if (replaceEquals)
            sanitized = sanitized.Replace('=', ':');

        return CollapseWhitespace(sanitized);
    }

    static string EmptyIfBlank(string value)
    {
        return value.Length == 0 ? "empty" : value;
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
