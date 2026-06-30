namespace Alife.Function.DataAgent;

public interface IDataAgentAnalysisOrchestrator
{
    DataAgentOrchestrationResult Start(DataAgentOrchestrationRequest request);

    DataAgentOrchestrationResult Continue(DataAgentOrchestrationRequest request);

    DataAgentOrchestrationResult Summarize(string sessionId, DataAgentToolRouteContext? routeContext = null);

    DataAgentOrchestrationResult End(string sessionId, DataAgentToolRouteContext? routeContext = null);
}
