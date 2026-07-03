using System.Collections.ObjectModel;

namespace Alife.Function.DataAgent;

public sealed class DataAgentProgressRecorder : IDataAgentProgressSink
{
    readonly object gate = new();
    readonly int maxEventsPerSession;
    readonly int maxEventsTotal;
    readonly TimeSpan ttl;
    readonly List<DataAgentProgressRecord> events = [];
    long nextSequence;

    public DataAgentProgressRecorder(int maxEventsPerSession = 32, TimeSpan? ttl = null, int maxEventsTotal = 512)
    {
        this.maxEventsPerSession = Math.Max(1, maxEventsPerSession);
        this.maxEventsTotal = Math.Max(1, maxEventsTotal);
        this.ttl = ttl ?? TimeSpan.FromMinutes(30);
    }

    public void Publish(DataAgentProgressEvent? progressEvent)
    {
        if (progressEvent is null || string.IsNullOrWhiteSpace(progressEvent.SessionId))
            return;

        DataAgentProgressEvent normalized = Snapshot(progressEvent) with
        {
            SessionId = NormalizeToken(progressEvent.SessionId),
            ReasonCode = NormalizeToken(progressEvent.ReasonCode)
        };

        lock (gate)
        {
            PruneExpiredLocked(normalized.CreatedAt);
            events.Add(new DataAgentProgressRecord(normalized, nextSequence++));
            PruneCapacityLocked(normalized.SessionId);
            PruneGlobalCapacityLocked();
        }
    }

    public IReadOnlyList<DataAgentProgressEvent> GetRecent(string sessionId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return [];

        string normalizedSessionId = NormalizeToken(sessionId);
        lock (gate)
        {
            return events
                .Where(record => IsExpired(record, now) == false &&
                                 string.Equals(record.Event.SessionId, normalizedSessionId, StringComparison.Ordinal))
                .OrderBy(record => record.Event.CreatedAt)
                .ThenBy(record => record.Sequence)
                .Select(record => Snapshot(record.Event))
                .ToArray();
        }
    }

    public DataAgentProgressEvent? GetLatest(string sessionId, DateTimeOffset now)
    {
        return GetRecent(sessionId, now).LastOrDefault();
    }

    void PruneExpiredLocked(DateTimeOffset now)
    {
        events.RemoveAll(record => IsExpired(record, now));
    }

    bool IsExpired(DataAgentProgressRecord record, DateTimeOffset now)
    {
        return now - record.Event.CreatedAt > ttl;
    }

    void PruneCapacityLocked(string sessionId)
    {
        List<DataAgentProgressRecord> sessionEvents = events
            .Where(record => string.Equals(record.Event.SessionId, sessionId, StringComparison.Ordinal))
            .OrderBy(record => record.Event.CreatedAt)
            .ThenBy(record => record.Sequence)
            .ToList();

        int excess = sessionEvents.Count - maxEventsPerSession;
        if (excess <= 0)
            return;

        foreach (DataAgentProgressRecord record in sessionEvents.Take(excess))
            events.Remove(record);
    }

    void PruneGlobalCapacityLocked()
    {
        int excess = events.Count - maxEventsTotal;
        if (excess <= 0)
            return;

        foreach (DataAgentProgressRecord record in events
                     .OrderBy(item => item.Event.CreatedAt)
                     .ThenBy(item => item.Sequence)
                     .Take(excess)
                     .ToArray())
        {
            events.Remove(record);
        }
    }

    static DataAgentProgressEvent Snapshot(DataAgentProgressEvent progressEvent)
    {
        return progressEvent with
        {
            Facts = new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(progressEvent.Facts))
        };
    }

    static string NormalizeToken(string value)
    {
        return value.ReplaceLineEndings(" ").Replace(';', ',').Trim();
    }

    sealed class DataAgentProgressRecord(DataAgentProgressEvent progressEvent, long sequence)
    {
        public DataAgentProgressEvent Event { get; } = progressEvent;

        public long Sequence { get; } = sequence;
    }
}
