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

    [Test]
    public void RecordSnapshotsOriginalEventList()
    {
        DataAgentTraceRecorder recorder = new(maxTimelinesPerSession: 4, ttl: TimeSpan.FromMinutes(30));
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:00:00Z");
        List<DataAgentTraceEvent> events =
        [
            Event("original")
        ];

        recorder.Record(new DataAgentTraceTimeline(
            "session-a",
            DataAgentAnalysisSessionStatus.Active,
            1,
            now,
            now,
            Terminal: false,
            events));
        events.Add(Event("mutated"));

        DataAgentTraceTimeline latest = recorder.GetLatest("session-a", now)!;

        Assert.Multiple(() =>
        {
            Assert.That(latest.Events, Has.Count.EqualTo(1));
            Assert.That(latest.Events.Single().ReasonCode, Is.EqualTo("original"));
        });
    }

    [Test]
    public void RecordSnapshotsOriginalFactsDictionary()
    {
        DataAgentTraceRecorder recorder = new(maxTimelinesPerSession: 4, ttl: TimeSpan.FromMinutes(30));
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:00:00Z");
        Dictionary<string, string> facts = new() { ["route_allowed"] = "true" };

        recorder.Record(new DataAgentTraceTimeline(
            "session-a",
            DataAgentAnalysisSessionStatus.Active,
            1,
            now,
            now,
            Terminal: false,
            [
                new DataAgentTraceEvent(
                    DataAgentTraceEventKind.RouteGate,
                    DataAgentTraceEventStatus.Succeeded,
                    "original",
                    ExecutedSql: false,
                    QueryAllowed: true,
                    Terminal: false,
                    facts)
            ]));
        facts["route_allowed"] = "false";
        facts["late_fact"] = "mutated";

        IReadOnlyDictionary<string, string> storedFacts = recorder.GetLatest("session-a", now)!.Events.Single().Facts;

        Assert.Multiple(() =>
        {
            Assert.That(storedFacts["route_allowed"], Is.EqualTo("true"));
            Assert.That(storedFacts.ContainsKey("late_fact"), Is.False);
        });
    }

    [Test]
    public void ReadsReturnSnapshotsThatDoNotMutateStoredTimeline()
    {
        DataAgentTraceRecorder recorder = new(maxTimelinesPerSession: 4, ttl: TimeSpan.FromMinutes(30));
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:00:00Z");
        recorder.Record(Timeline("session-a", 1, now, "original"));

        DataAgentTraceTimeline firstRead = recorder.GetLatest("session-a", now)!;
        TryMutateEvents(firstRead.Events);
        TryMutateFacts(firstRead.Events.Single().Facts);

        DataAgentTraceTimeline secondRead = recorder.GetLatest("session-a", now)!;

        Assert.Multiple(() =>
        {
            Assert.That(secondRead.Events, Has.Count.EqualTo(1));
            Assert.That(secondRead.Events.Single().ReasonCode, Is.EqualTo("original"));
            Assert.That(secondRead.Events.Single().Facts["route_allowed"], Is.EqualTo("true"));
            Assert.That(secondRead.Events.Single().Facts.ContainsKey("late_fact"), Is.False);
        });
    }

    [Test]
    public void RecordEvictsOldestTimelineWithinGlobalCapacity()
    {
        DataAgentTraceRecorder recorder = new(maxTimelinesPerSession: 4, ttl: TimeSpan.FromMinutes(30), maxTimelinesTotal: 2);
        DateTimeOffset start = DateTimeOffset.Parse("2026-07-02T00:00:00Z");

        recorder.Record(Timeline("session-a", 1, start, "first"));
        recorder.Record(Timeline("session-b", 1, start.AddSeconds(1), "second"));
        recorder.Record(Timeline("session-c", 1, start.AddSeconds(2), "third"));

        Assert.Multiple(() =>
        {
            Assert.That(recorder.GetLatest("session-a", start.AddSeconds(3)), Is.Null);
            Assert.That(recorder.GetLatest("session-b", start.AddSeconds(3))!.Events.Single().ReasonCode, Is.EqualTo("second"));
            Assert.That(recorder.GetLatest("session-c", start.AddSeconds(3))!.Events.Single().ReasonCode, Is.EqualTo("third"));
        });
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

    static DataAgentTraceEvent Event(string reason)
    {
        return new DataAgentTraceEvent(
            DataAgentTraceEventKind.RouteGate,
            DataAgentTraceEventStatus.Succeeded,
            reason,
            ExecutedSql: false,
            QueryAllowed: true,
            Terminal: false,
            new Dictionary<string, string> { ["route_allowed"] = "true" });
    }

    static void TryMutateEvents(IReadOnlyList<DataAgentTraceEvent> events)
    {
        if (events is IList<DataAgentTraceEvent> list)
        {
            try
            {
                if (list.Count > 0)
                    list[0] = Event("mutated");

                list.Add(Event("mutated"));
            }
            catch (NotSupportedException)
            {
            }
        }
    }

    static void TryMutateFacts(IReadOnlyDictionary<string, string> facts)
    {
        if (facts is IDictionary<string, string> dictionary)
        {
            try
            {
                dictionary["route_allowed"] = "false";
                dictionary["late_fact"] = "mutated";
            }
            catch (NotSupportedException)
            {
            }
        }
    }
}
