namespace Alife.Function.DataAgent;

public sealed record DataAgentQueryPlan(
    string Dataset,
    string Intent,
    IReadOnlyList<string> Select,
    IReadOnlyList<DataAgentFilter> Filters,
    IReadOnlyList<DataAgentOrderBy> OrderBy,
    int Limit);

public sealed record DataAgentFilter(string Field, string Operator, object? Value);

public sealed record DataAgentOrderBy(string Field, string Direction);
