namespace Alife.Function.DataAgent;

public interface IDataAgentAnalysisSessionStore
{
    DataAgentAnalysisSession Create(string callerId, string goal, DateTimeOffset now);

    DataAgentAnalysisSession? Get(string sessionId);

    DataAgentAnalysisSession Save(DataAgentAnalysisSession session);

    DataAgentAnalysisSession? Update(
        string sessionId,
        Func<DataAgentAnalysisSession, DataAgentAnalysisSession> update);

    bool End(string sessionId, DateTimeOffset now);
}
