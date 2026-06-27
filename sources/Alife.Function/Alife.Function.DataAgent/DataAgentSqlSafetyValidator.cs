using System.Text.RegularExpressions;

namespace Alife.Function.DataAgent;

public sealed class DataAgentSqlSafetyValidator
{
    static readonly Regex LimitPattern = new(@"\bLIMIT\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Regex UnsafeKeywordPattern = new(@"\b(INSERT|UPDATE|DELETE|DROP|ALTER|CREATE|ATTACH|DETACH|PRAGMA|VACUUM|REINDEX)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public DataAgentSqlSafetyResult Validate(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return DataAgentSqlSafetyResult.Unsafe("empty_sql");

        string trimmed = sql.Trim();

        if (trimmed.Contains(';', StringComparison.Ordinal))
            return DataAgentSqlSafetyResult.Unsafe("multi_statement_sql_rejected");

        if (UnsafeKeywordPattern.IsMatch(trimmed))
            return DataAgentSqlSafetyResult.Unsafe("unsafe_keyword_rejected");

        if (StartsWithReadOnlyQuery(trimmed) == false)
            return DataAgentSqlSafetyResult.Unsafe("only_select_or_with_allowed");

        if (LimitPattern.IsMatch(trimmed) == false)
            return DataAgentSqlSafetyResult.Unsafe("limit_required");

        return DataAgentSqlSafetyResult.Safe();
    }

    static bool StartsWithReadOnlyQuery(string sql)
    {
        return sql.StartsWith("SELECT ", StringComparison.OrdinalIgnoreCase) ||
               sql.StartsWith("WITH ", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record DataAgentSqlSafetyResult(bool IsSafe, string Reason)
{
    public static DataAgentSqlSafetyResult Safe() => new(true, string.Empty);

    public static DataAgentSqlSafetyResult Unsafe(string reason) => new(false, reason);
}
