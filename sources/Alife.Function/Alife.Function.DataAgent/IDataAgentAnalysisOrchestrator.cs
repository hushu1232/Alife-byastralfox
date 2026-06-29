namespace Alife.Function.DataAgent;

public interface IDataAgentAnalysisOrchestrator
{
    DataAgentOrchestrationResult Start(DataAgentOrchestrationRequest request);

    DataAgentOrchestrationResult Continue(DataAgentOrchestrationRequest request);
}
