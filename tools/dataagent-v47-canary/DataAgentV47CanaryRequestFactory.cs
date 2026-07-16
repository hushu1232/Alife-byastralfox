using Alife.Function.DataAgent;

namespace Alife.Tools.DataAgentV47Canary;

public static class DataAgentV47CanaryRequestFactory
{
    public static DataAgentOrchestrationResult Create(int sequence)
    {
        if (sequence < 1)
            throw new ArgumentOutOfRangeException(nameof(sequence));
        string sessionId = $"v47-canary-{sequence}-{Guid.NewGuid():N}";
        DataAgentAnalysisResponse response = new(
            sessionId, DataAgentAnalysisSessionStatus.Active,
            DataAgentAnalysisTurnIntent.NewQuestion, null,
            "canary", "canary", true, string.Empty);
        return new DataAgentOrchestrationResult(
            sessionId,
            DataAgentAnalysisSessionStatus.Active,
            [new(DataAgentOrchestrationNodeKind.RouteGate,
                DataAgentOrchestrationStepStatus.Succeeded, "route_allowed", false)],
            new(sessionId, DataAgentAnalysisSessionStatus.Active, "none", sequence,
                true, true, false),
            response,
            new(true, "dataagent_analysis_start", true, true,
                "v47-canary", "analysis_start", "route_allowed", string.Empty));
    }
}
