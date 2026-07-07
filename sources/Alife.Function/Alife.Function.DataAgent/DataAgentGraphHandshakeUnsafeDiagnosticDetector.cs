using System.Text.RegularExpressions;

namespace Alife.Function.DataAgent;

internal static class DataAgentGraphHandshakeUnsafeDiagnosticDetector
{
    static readonly Regex SqlCommandPattern = new(
        @"```sql|\bsql\b|\b(select|insert|update|delete|drop|alter|create|truncate)\b|\bwith\s+(?:recursive\s+)?[A-Za-z_][A-Za-z0-9_]*\s+as\s*\(|\bexecute\b(?:\s+[A-Za-z_][A-Za-z0-9_.]*)?|\bcall\b\s*(?:[A-Za-z_][A-Za-z0-9_.]*\s*)?\(|\bmerge\s+into\b|\bgrant\s+[A-Za-z]+\b|\brevoke\s+[A-Za-z]+\b|\bpragma\s+[A-Za-z_][A-Za-z0-9_]*\b|\bbegin(?:\s+(?:transaction|work))?\b|\bcommit\b|\brollback\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    static readonly Regex SqlFragmentPattern = new(
        @"\bfrom\s+[A-Za-z_][A-Za-z0-9_.]*\b|\bjoin\s+[A-Za-z_][A-Za-z0-9_.]*\b|\bwhere\s+[A-Za-z_][A-Za-z0-9_.]*\s*(?:=|<>|!=|<=|>=|<|>|\bis\b|\bin\b|\blike\b|\bbetween\b|\bnot\b)|\bhaving\s+[A-Za-z_][A-Za-z0-9_.]*\b|\blimit\s+\d+\b|\border\s+by\s+[A-Za-z_][A-Za-z0-9_.]*\b|\bgroup\s+by\s+[A-Za-z_][A-Za-z0-9_.]*\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    static readonly Regex UnsafeMarkerPattern = new(
        @"\[?(?:tool_route_context|data_agent_context|data_agent_evidence_pack)\]?|\ballowed\s+xml\s+tools?\b|\bhidden_context\b|\bconnection[_\s-]?string\b|\bapi[_-]?key\b|\bbearer\b|\bpassword\b|\bsk-[A-Za-z0-9_-]*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool ContainsUnsafeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) == false &&
               (SqlCommandPattern.IsMatch(value) ||
                SqlFragmentPattern.IsMatch(value) ||
                UnsafeMarkerPattern.IsMatch(value));
    }
}
