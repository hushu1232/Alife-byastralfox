namespace Alife.Function.DataAgent;

public interface IDataAgentTraceRecorder
{
    void Record(DataAgentTraceTimeline? timeline);

    DataAgentTraceTimeline? GetLatest(string sessionId, DateTimeOffset now);

    IReadOnlyList<DataAgentTraceTimeline> GetRecent(string sessionId, DateTimeOffset now);
}
