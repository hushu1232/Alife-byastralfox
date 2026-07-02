namespace Alife.Function.DataAgent;

public sealed class DataAgentTraceRecorder : IDataAgentTraceRecorder
{
    readonly object gate = new();
    readonly int maxTimelinesPerSession;
    readonly TimeSpan ttl;
    readonly List<DataAgentTraceTimelineRecord> timelines = [];
    long nextSequence;

    public DataAgentTraceRecorder(int maxTimelinesPerSession = 4, TimeSpan? ttl = null)
    {
        this.maxTimelinesPerSession = Math.Max(1, maxTimelinesPerSession);
        this.ttl = ttl ?? TimeSpan.FromMinutes(30);
    }

    public void Record(DataAgentTraceTimeline? timeline)
    {
        if (timeline is null ||
            string.IsNullOrWhiteSpace(timeline.SessionId) ||
            timeline.Events.Count == 0)
        {
            return;
        }

        DataAgentTraceTimeline normalized = timeline with
        {
            SessionId = NormalizeSessionId(timeline.SessionId)
        };

        lock (gate)
        {
            PruneExpiredLocked(normalized.EndedAt);
            timelines.Add(new DataAgentTraceTimelineRecord(normalized, nextSequence++));
            PruneCapacityLocked(normalized.SessionId);
        }
    }

    public DataAgentTraceTimeline? GetLatest(string sessionId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return null;

        string normalizedSessionId = NormalizeSessionId(sessionId);
        lock (gate)
        {
            return timelines
                .Where(record => IsExpired(record, now) == false &&
                                 string.Equals(record.Timeline.SessionId, normalizedSessionId, StringComparison.Ordinal))
                .OrderByDescending(record => record.Timeline.EndedAt)
                .ThenByDescending(record => record.Sequence)
                .Select(record => record.Timeline)
                .FirstOrDefault();
        }
    }

    public IReadOnlyList<DataAgentTraceTimeline> GetRecent(string sessionId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return [];

        string normalizedSessionId = NormalizeSessionId(sessionId);
        lock (gate)
        {
            return timelines
                .Where(record => IsExpired(record, now) == false &&
                                 string.Equals(record.Timeline.SessionId, normalizedSessionId, StringComparison.Ordinal))
                .OrderBy(record => record.Timeline.EndedAt)
                .ThenBy(record => record.Sequence)
                .Select(record => record.Timeline)
                .ToArray();
        }
    }

    void PruneExpiredLocked(DateTimeOffset now)
    {
        timelines.RemoveAll(record => IsExpired(record, now));
    }

    bool IsExpired(DataAgentTraceTimelineRecord record, DateTimeOffset now)
    {
        return now - record.Timeline.EndedAt > ttl;
    }

    void PruneCapacityLocked(string sessionId)
    {
        List<DataAgentTraceTimelineRecord> sessionTimelines = timelines
            .Where(record => string.Equals(record.Timeline.SessionId, sessionId, StringComparison.Ordinal))
            .OrderBy(record => record.Timeline.EndedAt)
            .ThenBy(record => record.Sequence)
            .ToList();

        int excess = sessionTimelines.Count - maxTimelinesPerSession;
        if (excess <= 0)
            return;

        foreach (DataAgentTraceTimelineRecord record in sessionTimelines.Take(excess))
            timelines.Remove(record);
    }

    static string NormalizeSessionId(string value)
    {
        return value.ReplaceLineEndings(" ").Replace(';', ',').Trim();
    }

    sealed class DataAgentTraceTimelineRecord(DataAgentTraceTimeline timeline, long sequence)
    {
        public DataAgentTraceTimeline Timeline { get; } = timeline;

        public long Sequence { get; } = sequence;
    }
}
