using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Alife.Function.DataAgent;

public static class DataAgentTraceDiagnosticsFormatter
{
    const string TruncationSuffix = "...";
    const string Redacted = "redacted";
    const int MaxFieldLength = 160;

    static readonly Regex SqlLikePattern = new(
        @"\b(select|insert|update|delete|merge|drop|alter|create|truncate|with)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    static readonly Regex BearerPattern = new(
        @"\bbearer\s+\S+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    static readonly Regex SecretPattern = new(
        @"\b(api[_-]?key|secret|token|password|pwd)\s*=",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    static readonly Regex ConnectionStringPattern = new(
        @"\b(server|data source|host|uid|user id|pwd|password)\s*=",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    static readonly Regex UnsafeContextPattern = new(
        @"tool_route_context|data_agent_evidence_pack|allowed\s+xml\s+tools",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string Format(DataAgentTraceTimeline? timeline, int maxChars = 1800)
    {
        if (timeline is null)
            return string.Join(Environment.NewLine,
                "DataAgent trace diagnostics",
                "state=unavailable",
                "reason=trace_unavailable");

        StringBuilder builder = new();
        AppendLine(builder, "DataAgent trace diagnostics");
        AppendLine(builder, $"session={SafeValue(timeline.SessionId, keyIsSql: false)}");
        AppendLine(builder, $"turn={Math.Max(0, timeline.TurnCount).ToString(CultureInfo.InvariantCulture)}");
        AppendLine(builder, $"status={timeline.SessionStatus}");
        AppendLine(builder, $"terminal={Bool(timeline.Terminal)}");
        AppendLine(builder, $"events={timeline.Events.Count.ToString(CultureInfo.InvariantCulture)}");

        for (int i = 0; i < timeline.Events.Count; i++)
        {
            DataAgentTraceEvent traceEvent = timeline.Events[i];
            builder.Append((i + 1).ToString(CultureInfo.InvariantCulture));
            builder.Append(' ');
            builder.Append(traceEvent.Kind);
            builder.Append(' ');
            builder.Append(traceEvent.Status);
            builder.Append(" reason=");
            builder.Append(SafeValue(traceEvent.ReasonCode, keyIsSql: false));
            builder.Append(" query_allowed=");
            builder.Append(Bool(traceEvent.QueryAllowed));
            builder.Append(" executed_sql=");
            builder.Append(Bool(traceEvent.ExecutedSql));
            builder.Append(" terminal=");
            builder.Append(Bool(traceEvent.Terminal));

            foreach (KeyValuePair<string, string> fact in traceEvent.Facts.OrderBy(fact => fact.Key, StringComparer.Ordinal))
            {
                string key = SafeKey(fact.Key);
                builder.Append(' ');
                builder.Append(key);
                builder.Append('=');
                builder.Append(SafeValue(fact.Value, IsSqlKey(key)));
            }

            builder.AppendLine();
        }

        return Bound(builder.ToString().TrimEnd(), maxChars);
    }

    static void AppendLine(StringBuilder builder, string value)
    {
        builder.Append(value);
        builder.AppendLine();
    }

    static string SafeKey(string value)
    {
        string sanitized = DataAgentContextFieldSanitizer.Sanitize(value ?? string.Empty, MaxFieldLength);
        StringBuilder builder = new(sanitized.Length);

        foreach (char current in sanitized)
        {
            builder.Append(char.IsAsciiLetterOrDigit(current) || current is '_' or '-' or '.'
                ? current
                : '_');
        }

        string key = builder.ToString().Trim('_');
        return key.Length == 0 ? "fact" : key;
    }

    static string SafeValue(string value, bool keyIsSql)
    {
        value ??= string.Empty;
        if (keyIsSql || IsUnsafe(value))
            return Redacted;

        string sanitized = DataAgentContextFieldSanitizer.Sanitize(value, MaxFieldLength);
        sanitized = CollapseWhitespace(sanitized).Replace('=', ':');

        return sanitized.Length == 0 ? "empty" : sanitized;
    }

    static bool IsSqlKey(string key)
    {
        return key.Contains("sql", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsUnsafe(string value)
    {
        return SqlLikePattern.IsMatch(value) ||
            BearerPattern.IsMatch(value) ||
            SecretPattern.IsMatch(value) ||
            ConnectionStringPattern.IsMatch(value) ||
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

    static string Bound(string value, int maxChars)
    {
        if (maxChars <= 0 || value.Length <= maxChars)
            return maxChars <= 0 ? string.Empty : value;

        if (maxChars <= TruncationSuffix.Length)
            return TruncationSuffix[..maxChars];

        return value[..(maxChars - TruncationSuffix.Length)].TrimEnd() + TruncationSuffix;
    }

    static string Bool(bool value)
    {
        return value ? "true" : "false";
    }
}
