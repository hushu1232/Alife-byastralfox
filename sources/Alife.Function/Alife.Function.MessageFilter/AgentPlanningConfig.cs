namespace Alife.Function.MessageFilter;

public sealed class AgentPlanningConfig
{
    public bool EnablePlanner { get; set; }
    public string PlannerModelId { get; set; } = "deepseek-v4-pro";
    public string ExecutorModelId { get; set; } = "deepseek-v4-flash";
    public int MaxPlannerTokens { get; set; } = 4096;
}
