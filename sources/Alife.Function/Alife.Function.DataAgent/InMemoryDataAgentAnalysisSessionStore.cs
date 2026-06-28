using System.Collections.Concurrent;

namespace Alife.Function.DataAgent;

public sealed class InMemoryDataAgentAnalysisSessionStore : IDataAgentAnalysisSessionStore
{
    readonly ConcurrentDictionary<string, DataAgentAnalysisSession> sessions = new(StringComparer.Ordinal);

    public DataAgentAnalysisSession Create(string callerId, string goal, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(goal);

        string safeCallerId = DataAgentContextFieldSanitizer.Sanitize(callerId ?? string.Empty, 80);
        if (string.IsNullOrWhiteSpace(safeCallerId))
            safeCallerId = "local";

        string safeGoal = DataAgentContextFieldSanitizer.Sanitize(goal, 240);
        if (string.IsNullOrWhiteSpace(safeGoal))
            throw new ArgumentException("Goal cannot be empty after sanitization.", nameof(goal));

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

        DataAgentAnalysisSession snapshot = Snapshot(session);
        sessions[snapshot.SessionId] = snapshot;
        return Snapshot(snapshot);
    }

    public DataAgentAnalysisSession? Get(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return null;

        return sessions.TryGetValue(sessionId, out DataAgentAnalysisSession? session)
            ? Snapshot(session)
            : null;
    }

    public DataAgentAnalysisSession Save(DataAgentAnalysisSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.SessionId);

        DataAgentAnalysisSession incoming = Snapshot(session);
        DataAgentAnalysisSession saved = sessions.AddOrUpdate(
            incoming.SessionId,
            incoming,
            (_, current) =>
            {
                if (current.Status == DataAgentAnalysisSessionStatus.Ended &&
                    incoming.Status != DataAgentAnalysisSessionStatus.Ended)
                    return current;

                return incoming;
            });

        return Snapshot(saved);
    }

    public bool End(string sessionId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return false;

        while (sessions.TryGetValue(sessionId, out DataAgentAnalysisSession? current))
        {
            if (current.Status == DataAgentAnalysisSessionStatus.Ended)
                return true;

            DataAgentAnalysisSession ended = Snapshot(current with
            {
                Status = DataAgentAnalysisSessionStatus.Ended,
                UpdatedAt = now
            });

            if (sessions.TryUpdate(sessionId, ended, current))
                return true;
        }

        return false;
    }

    static DataAgentAnalysisSession Snapshot(DataAgentAnalysisSession session)
    {
        return session with { Turns = Array.AsReadOnly(session.Turns.ToArray()) };
    }
}
