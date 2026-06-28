namespace Alife.Function.DataAgent;

public sealed record DataAgentQueryPlan(
    string Dataset,
    string Intent,
    IReadOnlyList<string> Select,
    IReadOnlyList<DataAgentFilter> Filters,
    IReadOnlyList<DataAgentOrderBy> OrderBy,
    int Limit);

public sealed record DataAgentQueryPlanEnvelope(
    DataAgentQueryPlan? Plan,
    DataAgentPlannerExplanation Explanation,
    DataAgentClarificationRequest? Clarification = null);

public sealed record DataAgentClarificationRequest(
    string Question,
    IReadOnlyList<string> Options,
    string Reason);

public static class DataAgentClarificationSanitizer
{
    public const int MaxQuestionLength = 240;
    public const int MaxReasonLength = 240;
    public const int MaxOptionLength = 80;

    const string TruncationSuffix = "...";

    public static DataAgentClarificationRequest Sanitize(DataAgentClarificationRequest clarification)
    {
        ArgumentNullException.ThrowIfNull(clarification);
        ArgumentNullException.ThrowIfNull(clarification.Options);

        return new DataAgentClarificationRequest(
            SanitizeField(clarification.Question, MaxQuestionLength),
            clarification.Options.Select(option => SanitizeField(option, MaxOptionLength)).ToArray(),
            SanitizeField(clarification.Reason, MaxReasonLength));
    }

    static string SanitizeField(string value, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(value);

        char[] buffer = new char[value.Length];
        for (int i = 0; i < value.Length; i++)
        {
            char current = value[i];
            buffer[i] = current switch
            {
                '[' => '(',
                ']' => ')',
                _ when char.IsControl(current) => ' ',
                _ => current
            };
        }

        string sanitized = new string(buffer).Trim();
        if (sanitized.Length <= maxLength)
            return sanitized;

        return sanitized[..(maxLength - TruncationSuffix.Length)].TrimEnd() + TruncationSuffix;
    }
}

public sealed record DataAgentPlannerExplanation(
    string PlannerName,
    string Intent,
    string Dataset,
    string Confidence,
    IReadOnlyList<string> Signals,
    string Reason);

public sealed record DataAgentFilter(string Field, string Operator, object? Value);

public sealed record DataAgentOrderBy(string Field, string Direction);
