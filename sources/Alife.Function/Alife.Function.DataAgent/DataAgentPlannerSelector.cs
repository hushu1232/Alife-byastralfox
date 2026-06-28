namespace Alife.Function.DataAgent;

public enum LlmDataAgentPlannerMode
{
    Disabled,
    Harness,
    Live
}

public sealed class LlmDataAgentPlannerOptions
{
    public LlmDataAgentPlannerMode Mode { get; init; } = LlmDataAgentPlannerMode.Disabled;
}

public static class DataAgentPlannerSelector
{
    public static IDataAgentQueryPlanner Create(
        LlmDataAgentPlannerOptions options,
        string databasePath,
        ILlmDataAgentPlannerClient? llmClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        return options.Mode switch
        {
            LlmDataAgentPlannerMode.Disabled => new DeterministicDataAgentQueryPlanner(),
            LlmDataAgentPlannerMode.Harness => CreateLlmPlanner(
                databasePath,
                llmClient,
                "Harness mode requires an LLM planner client."),
            LlmDataAgentPlannerMode.Live => CreateLlmPlanner(
                databasePath,
                llmClient,
                "Live mode requires an LLM planner client."),
            _ => throw new InvalidOperationException($"Unsupported DataAgent LLM planner mode '{options.Mode}'.")
        };
    }

    static LlmDataAgentQueryPlanner CreateLlmPlanner(
        string databasePath,
        ILlmDataAgentPlannerClient? llmClient,
        string missingClientMessage)
    {
        if (llmClient is null)
            throw new InvalidOperationException(missingClientMessage);

        return new LlmDataAgentQueryPlanner(
            databasePath,
            llmClient,
            new DeterministicDataAgentQueryPlanner());
    }
}
