using System;
using System.Text.RegularExpressions;

namespace Alife.Function.QChat;

static class QChatDiagnosticTextSanitizer
{
    const string TruncationEllipsis = "...";

    static readonly Regex ApiKeyPattern = new(@"\bapi[-_]?key\s*[:=]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Regex AuthorizationBearerPattern = new(@"\b(?:authorization\s*:\s*)?bearer\s+\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Regex ConnectionSecretPattern = new(@"\b(connection[_\s-]?string|host|server|data\s+source|username|user\s*id|userid|uid|user|password|pwd)\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Regex SqlSelectPattern = new(@"\bselect\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Regex SqlStatementPattern = new(
        @"\b(insert\s+into|update\s+\w+|delete\s+from|drop\s+(table|database|schema)|create\s+(table|database|schema|index|view)|alter\s+(table|database|schema|index|view)|truncate\s+(table|database)|merge\s+into|exec(ute)?\s+\w+)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    public static bool ContainsUnsafeDiagnosticText(string text)
    {
        return text.Contains("[tool_route_context]", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("[/tool_route_context]", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("[data_agent_context]", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("[/data_agent_context]", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("[data_agent_evidence_pack]", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("[/data_agent_evidence_pack]", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Allowed XML tools", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("connection_string", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("sk-", StringComparison.OrdinalIgnoreCase) ||
               ApiKeyPattern.IsMatch(text) ||
               AuthorizationBearerPattern.IsMatch(text) ||
               ConnectionSecretPattern.IsMatch(text) ||
               SqlSelectPattern.IsMatch(text) ||
               SqlStatementPattern.IsMatch(text);
    }

    public static string SanitizeDiagnosticText(string? text, string title, int maxChars = 900)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        if (ContainsUnsafeDiagnosticText(text))
            return string.Join(Environment.NewLine,
                title,
                "state=redacted",
                "reason=hidden_context_redacted");

        return NormalizeDiagnosticText(text, maxChars);
    }

    public static string NormalizeDiagnosticText(string text, int maxChars)
    {
        string normalized = text.ReplaceLineEndings(Environment.NewLine).Trim();
        if (normalized.Length <= maxChars)
            return normalized;

        int prefixLength = Math.Max(0, maxChars - TruncationEllipsis.Length);
        return normalized[..prefixLength] + TruncationEllipsis;
    }

    public static string SanitizeToolRouteTrace(string? trace)
    {
        if (string.IsNullOrWhiteSpace(trace))
            return "none";

        if (ContainsUnsafeDiagnosticText(trace))
            return "redacted";

        return NormalizeToolRouteTrace(trace);
    }

    static string NormalizeToolRouteTrace(string trace)
    {
        string normalized = trace.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= 240
            ? normalized
            : normalized[..237] + TruncationEllipsis;
    }
}
