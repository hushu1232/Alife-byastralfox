namespace Alife.Function.DataAgent;

public interface ILlmDataAgentPlannerClient
{
    string Complete(DataAgentLlmPlannerPrompt prompt);
}
