namespace Alife.Function.DataAgent;

public interface IDataAgentAnalysisSessionStore
{
    DataAgentAnalysisSession Create(string callerId, string goal, DateTimeOffset now);

    DataAgentAnalysisSession? Get(string sessionId);

    DataAgentAnalysisSession Save(DataAgentAnalysisSession session);

    bool End(string sessionId, DateTimeOffset now);
}
