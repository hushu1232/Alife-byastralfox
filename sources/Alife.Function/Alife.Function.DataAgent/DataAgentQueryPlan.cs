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

public sealed record DataAgentPlannerExplanation(
    string PlannerName,
    string Intent,
    string Dataset,
    string Confidence,
    IReadOnlyList<string> Signals,
    string Reason);

public sealed record DataAgentFilter(string Field, string Operator, object? Value);

public sealed record DataAgentOrderBy(string Field, string Direction);
