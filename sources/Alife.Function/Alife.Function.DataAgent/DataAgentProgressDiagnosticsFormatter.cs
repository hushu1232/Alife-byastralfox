using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Alife.Function.DataAgent;

public static class DataAgentProgressDiagnosticsFormatter
{
    const int MaxEvents = 16;
    const int MaxFieldLength = 160;
    const int DefaultMaxChars = 1800;
    const string Redacted = "redacted";
    const string TruncationSuffix = "...";

    static readonly Regex SqlLikePattern = new(
        @"\b(select|insert|update|delete|merge|drop|alter|create|truncate|with|from|join|where|having|limit)\b|\border\s+by\b|\bgroup\s+by\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    static readonly Regex BearerPattern = new(
        @"\bbearer\s+\S+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    static readonly Regex SecretPattern = new(
        @"(api[_-]?key|secret|token|password|pwd)\s*[:=]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    static readonly Regex ApiKeyPattern = new(
        @"\bsk-[A-Za-z0-9]{4,}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    static readonly Regex ConnectionStringPattern = new(
        @"\b(connection[_\s-]?string|server|data source|host|username|user id|uid|pwd|password)\s*=",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    static readonly Regex UnsafeContextPattern = new(
        @"tool_route_context|data_agent_evidence_pack|hidden_context|hidden\s+context|allowed\s+xml\s+tool",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string Format(
        IReadOnlyList<DataAgentProgressEvent>? events,
        string sessionId,
        DateTimeOffset now,
        int maxChars = DefaultMaxChars)
    {
        if (events is null || events.Count == 0)
        {
            return Bound(string.Join(Environment.NewLine,
                "DataAgent progress diagnostics",
                "state=unavailable",
                "reason=progress_unavailable"), maxChars);
        }

        StringBuilder builder = new();
        builder.AppendLine("DataAgent progress diagnostics");
        builder.AppendLine($"session={SafeValue(sessionId, shouldRedact: false)}");
        builder.AppendLine($"events={events.Count.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"now={now:O}");

        foreach (DataAgentProgressEvent progressEvent in events
                     .OrderBy(item => item.CreatedAt)
                     .TakeLast(MaxEvents))
        {
            builder.Append(progressEvent.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
            builder.Append(' ');
            builder.Append(progressEvent.Kind);
            builder.Append(':');
            builder.Append(progressEvent.Phase);
            builder.Append(':');
            builder.Append(progressEvent.Status);
            builder.Append(" reason=");
            builder.Append(SafeValue(progressEvent.ReasonCode, shouldRedact: false));
            builder.Append(" sql=");
            builder.Append(progressEvent.ExecutedSql ? Redacted : "not_executed");
            builder.Append(" query_allowed=");
            builder.Append(Bool(progressEvent.QueryAllowed));
            builder.Append(" terminal=");
            builder.Append(Bool(progressEvent.Terminal));
            AppendFacts(builder, progressEvent.Facts);
            builder.AppendLine();
        }

        return Bound(builder.ToString().TrimEnd(), maxChars);
    }

    static void AppendFacts(StringBuilder builder, IReadOnlyDictionary<string, string> facts)
    {
        foreach (KeyValuePair<string, string> fact in facts.OrderBy(fact => fact.Key, StringComparer.Ordinal).Take(8))
        {
            string key = SafeKey(fact.Key);
            if (ShouldSkipFactKey(key))
                continue;

            builder.Append(' ');
            builder.Append(key);
            builder.Append('=');
            builder.Append(SafeValue(fact.Value, ShouldRedactFactKey(key)));
        }
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

    static bool ShouldSkipFactKey(string key)
    {
        return key.Contains("hidden", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("tool_route", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("evidence_pack", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("authorization", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("pwd", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("api", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("key", StringComparison.OrdinalIgnoreCase);
    }

    static bool ShouldRedactFactKey(string key)
    {
        return key.Contains("sql", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("table", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("dataset", StringComparison.OrdinalIgnoreCase);
    }

    static string SafeValue(string value, bool shouldRedact)
    {
        value ??= string.Empty;
        if (shouldRedact || IsUnsafe(value))
            return Redacted;

        string sanitized = DataAgentContextFieldSanitizer.Sanitize(value, MaxFieldLength);
        sanitized = CollapseWhitespace(sanitized).Replace('=', ':');

        return sanitized.Length == 0 ? "empty" : sanitized;
    }

    static bool IsUnsafe(string value)
    {
        return SqlLikePattern.IsMatch(value) ||
            BearerPattern.IsMatch(value) ||
            SecretPattern.IsMatch(value) ||
            ApiKeyPattern.IsMatch(value) ||
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
        if (maxChars <= 0)
            return string.Empty;

        if (value.Length <= maxChars)
            return value;

        if (maxChars <= TruncationSuffix.Length)
            return TruncationSuffix[..maxChars];

        return value[..(maxChars - TruncationSuffix.Length)].TrimEnd() + TruncationSuffix;
    }

    static string Bool(bool value)
    {
        return value ? "true" : "false";
    }
}
