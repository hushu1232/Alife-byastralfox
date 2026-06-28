using System.Collections.Concurrent;

namespace Alife.Function.DataAgent;

public sealed class InMemoryDataAgentAnalysisSessionStore : IDataAgentAnalysisSessionStore
{
    readonly ConcurrentDictionary<string, DataAgentAnalysisSession> sessions = new(StringComparer.Ordinal);

    public DataAgentAnalysisSession Create(string callerId, string goal, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(goal);

        string safeCallerId = string.IsNullOrWhiteSpace(callerId)
            ? "local"
            : DataAgentContextFieldSanitizer.Sanitize(callerId, 80);
        string safeGoal = DataAgentContextFieldSanitizer.Sanitize(goal, 240);

        DataAgentAnalysisSession session = new(
            Guid.NewGuid().ToString("N"),
            safeCallerId,
            safeGoal,
            DataAgentAnalysisSessionStatus.Active,
            now,
            now,
            null,
            null,
            null,
            []);

        sessions[session.SessionId] = session;
        return session;
    }

    public DataAgentAnalysisSession? Get(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return null;

        return sessions.TryGetValue(sessionId, out DataAgentAnalysisSession? session)
            ? session
            : null;
    }

    public DataAgentAnalysisSession Save(DataAgentAnalysisSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.SessionId);

        sessions[session.SessionId] = session;
        return session;
    }

    public bool End(string sessionId, DateTimeOffset now)
    {
        if (Get(sessionId) is not DataAgentAnalysisSession session)
            return false;

        Save(session with
        {
            Status = DataAgentAnalysisSessionStatus.Ended,
            UpdatedAt = now
        });
        return true;
    }
}
