namespace Alife.Function.DataAgent;

public sealed record DataAgentScenarioKnowledgePack(
    string Scenario,
    string Culture,
    IReadOnlyList<DataAgentScenarioTerm> Terms,
    IReadOnlyList<DataAgentScenarioMetric> Metrics);

public sealed record DataAgentScenarioTerm(
    string Term,
    IReadOnlyList<string> Aliases,
    string Dataset,
    IReadOnlyList<string> Fields);

public sealed record DataAgentScenarioMetric(
    string Name,
    string Field,
    string Operator,
    object? Value);
