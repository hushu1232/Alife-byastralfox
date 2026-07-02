using Alife.Function.DataAgent;
using NUnit.Framework;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentTraceRecorderTests
{
    [Test]
    public void GetLatestReturnsNewestTimelineForSession()
    {
        DataAgentTraceRecorder recorder = new(maxTimelinesPerSession: 4, ttl: TimeSpan.FromMinutes(30));
        DateTimeOffset start = DateTimeOffset.Parse("2026-07-02T00:00:00Z");

        recorder.Record(Timeline("session-a", 1, start, "old"));
        recorder.Record(Timeline("session-a", 2, start.AddSeconds(5), "new"));

        DataAgentTraceTimeline? latest = recorder.GetLatest("session-a", start.AddSeconds(6));

        Assert.Multiple(() =>
        {
            Assert.That(latest, Is.Not.Null);
            Assert.That(latest!.TurnCount, Is.EqualTo(2));
            Assert.That(latest.Events.Single().ReasonCode, Is.EqualTo("new"));
        });
    }

    [Test]
    public void GetLatestIsolatesSessions()
    {
        DataAgentTraceRecorder recorder = new(maxTimelinesPerSession: 4, ttl: TimeSpan.FromMinutes(30));
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:00:00Z");

        recorder.Record(Timeline("session-a", 1, now, "a"));
        recorder.Record(Timeline("session-b", 1, now, "b"));

        Assert.Multiple(() =>
        {
            Assert.That(recorder.GetLatest("session-a", now)!.SessionId, Is.EqualTo("session-a"));
            Assert.That(recorder.GetLatest("session-b", now)!.SessionId, Is.EqualTo("session-b"));
            Assert.That(recorder.GetLatest("session-c", now), Is.Null);
        });
    }

    [Test]
    public void RecordEvictsOldestTimelineWithinSessionCapacity()
    {
        DataAgentTraceRecorder recorder = new(maxTimelinesPerSession: 2, ttl: TimeSpan.FromMinutes(30));
        DateTimeOffset start = DateTimeOffset.Parse("2026-07-02T00:00:00Z");

        recorder.Record(Timeline("session-a", 1, start, "first"));
        recorder.Record(Timeline("session-a", 2, start.AddSeconds(1), "second"));
        recorder.Record(Timeline("session-a", 3, start.AddSeconds(2), "third"));

        IReadOnlyList<DataAgentTraceTimeline> recent = recorder.GetRecent("session-a", start.AddSeconds(3));

        Assert.Multiple(() =>
        {
            Assert.That(recent.Select(timeline => timeline.TurnCount), Is.EqualTo(new[] { 2, 3 }));
            Assert.That(recent.Select(timeline => timeline.Events.Single().ReasonCode), Is.EqualTo(new[] { "second", "third" }));
        });
    }

    [Test]
    public void ReadsFilterExpiredTimelinesWithoutRemovingThem()
    {
        DataAgentTraceRecorder recorder = new(maxTimelinesPerSession: 4, ttl: TimeSpan.FromSeconds(30));
        DateTimeOffset start = DateTimeOffset.Parse("2026-07-02T00:00:00Z");

        recorder.Record(Timeline("session-a", 1, start, "before-expiry"));

        Assert.Multiple(() =>
        {
            Assert.That(recorder.GetRecent("session-a", start.AddSeconds(45)), Is.Empty);
            Assert.That(recorder.GetLatest("session-a", start.AddSeconds(45)), Is.Null);
            Assert.That(recorder.GetLatest("session-a", start.AddSeconds(20)), Is.Not.Null);
            Assert.That(recorder.GetRecent("session-a", start.AddSeconds(20)).Single().TurnCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void RecordIgnoresEmptySessionAndTimelineWithoutEvents()
    {
        DataAgentTraceRecorder recorder = new(maxTimelinesPerSession: 4, ttl: TimeSpan.FromMinutes(30));
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:00:00Z");

        recorder.Record(new DataAgentTraceTimeline(
            "",
            DataAgentAnalysisSessionStatus.Active,
            1,
            now,
            now,
            Terminal: false,
            []));
        recorder.Record(new DataAgentTraceTimeline(
            "session-a",
            DataAgentAnalysisSessionStatus.Active,
            1,
            now,
            now,
            Terminal: false,
            []));

        Assert.That(recorder.GetRecent("session-a", now), Is.Empty);
    }

    static DataAgentTraceTimeline Timeline(string sessionId, int turn, DateTimeOffset startedAt, string reason)
    {
        return new DataAgentTraceTimeline(
            sessionId,
            DataAgentAnalysisSessionStatus.Active,
            turn,
            startedAt,
            startedAt.AddMilliseconds(10),
            Terminal: false,
            [
                new DataAgentTraceEvent(
                    DataAgentTraceEventKind.RouteGate,
                    DataAgentTraceEventStatus.Succeeded,
                    reason,
                    ExecutedSql: false,
                    QueryAllowed: true,
                    Terminal: false,
                    new Dictionary<string, string> { ["route_allowed"] = "true" })
            ]);
    }
}
