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

    public static DataAgentClarificationRequest Sanitize(DataAgentClarificationRequest clarification)
    {
        ArgumentNullException.ThrowIfNull(clarification);
        ArgumentNullException.ThrowIfNull(clarification.Options);

        return new DataAgentClarificationRequest(
            DataAgentContextFieldSanitizer.Sanitize(clarification.Question, MaxQuestionLength),
            clarification.Options.Select(option => DataAgentContextFieldSanitizer.Sanitize(option, MaxOptionLength)).ToArray(),
            DataAgentContextFieldSanitizer.Sanitize(clarification.Reason, MaxReasonLength));
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
