using Alife.Function.DataAgent;
using NUnit.Framework;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentProgressRecorderTests
{
    [Test]
    public void PublishStoresDefensiveSnapshotsPerSession()
    {
        Dictionary<string, string> facts = new()
        {
            ["rows"] = "3",
            ["sql"] = "redacted"
        };
        DataAgentProgressRecorder recorder = new(maxEventsPerSession: 4, ttl: TimeSpan.FromMinutes(10), maxEventsTotal: 8);
        DateTimeOffset now = new(2026, 7, 3, 10, 0, 0, TimeSpan.Zero);

        recorder.Publish(new DataAgentProgressEvent(
            "session-a",
            DataAgentProgressEventKind.Execute,
            DataAgentProgressEventPhase.Completed,
            DataAgentProgressEventStatus.Succeeded,
            "read_only_query_executed",
            TurnCount: 1,
            now,
            ExecutedSql: true,
            QueryAllowed: true,
            Terminal: false,
            facts));
        facts["rows"] = "999";

        IReadOnlyList<DataAgentProgressEvent> recent = recorder.GetRecent("session-a", now);

        Assert.That(recent, Has.Count.EqualTo(1));
        Assert.That(recent[0].Facts["rows"], Is.EqualTo("3"));
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<string, string>)recent[0].Facts)["rows"] = "mutated");
    }

    [Test]
    public void GetRecentFiltersBySessionTtlAndCapacity()
    {
        DataAgentProgressRecorder recorder = new(maxEventsPerSession: 2, ttl: TimeSpan.FromMinutes(5), maxEventsTotal: 10);
        DateTimeOffset now = new(2026, 7, 3, 10, 0, 0, TimeSpan.Zero);

        recorder.Publish(Event("session-a", DataAgentProgressEventKind.RouteGate, now.AddMinutes(-6)));
        recorder.Publish(Event("session-a", DataAgentProgressEventKind.Planner, now.AddMinutes(-2)));
        recorder.Publish(Event("session-a", DataAgentProgressEventKind.Execute, now.AddMinutes(-1)));
        recorder.Publish(Event("session-b", DataAgentProgressEventKind.RouteGate, now.AddMinutes(-1)));

        IReadOnlyList<DataAgentProgressEvent> recent = recorder.GetRecent("session-a", now);

        Assert.That(recent.Select(item => item.Kind), Is.EqualTo(new[]
        {
            DataAgentProgressEventKind.Planner,
            DataAgentProgressEventKind.Execute
        }));
    }

    static DataAgentProgressEvent Event(string sessionId, DataAgentProgressEventKind kind, DateTimeOffset at)
    {
        return new DataAgentProgressEvent(
            sessionId,
            kind,
            DataAgentProgressEventPhase.Completed,
            DataAgentProgressEventStatus.Succeeded,
            "ok",
            TurnCount: 1,
            at,
            ExecutedSql: kind == DataAgentProgressEventKind.Execute,
            QueryAllowed: true,
            Terminal: false,
            new Dictionary<string, string>());
    }
}
