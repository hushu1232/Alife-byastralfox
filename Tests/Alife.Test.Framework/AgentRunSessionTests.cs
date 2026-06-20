using Alife.Framework;

namespace Alife.Test.Framework;

public class AgentRunSessionTests
{
    [Test]
    public void Snapshot_ReportsSourceEventAndStreamingMode()
    {
        AgentEvent source = CreateEvent();
        AgentRunSession session = AgentRunSession.Start(
            source,
            StreamingOutputPolicy.QqGroupText,
            startedAt: new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero));

        AgentRunSnapshot snapshot = session.Snapshot();

        Assert.That(snapshot.Status, Is.EqualTo(AgentRunStatus.Running));
        Assert.That(snapshot.SourceEventType, Is.EqualTo("qq.message.group"));
        Assert.That(snapshot.SourceSessionId, Is.EqualTo("qq:group:1000"));
        Assert.That(snapshot.StreamingMode, Is.EqualTo(StreamingOutputMode.Sentence));
        Assert.That(snapshot.StartedAt, Is.EqualTo(new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero)));
    }

    [Test]
    public void MarkFirstContent_RecordsFirstContentLatencyOnlyOnce()
    {
        AgentRunSession session = AgentRunSession.Start(
            CreateEvent(),
            StreamingOutputPolicy.Token,
            startedAt: new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero));

        session.MarkFirstContent(new DateTimeOffset(2026, 6, 15, 10, 0, 2, TimeSpan.Zero));
        session.MarkFirstContent(new DateTimeOffset(2026, 6, 15, 10, 0, 5, TimeSpan.Zero));

        AgentRunSnapshot snapshot = session.Snapshot();

        Assert.That(snapshot.FirstContentAt, Is.EqualTo(new DateTimeOffset(2026, 6, 15, 10, 0, 2, TimeSpan.Zero)));
        Assert.That(snapshot.FirstContentLatency, Is.EqualTo(TimeSpan.FromSeconds(2)));
    }

    [Test]
    public void RecordToolStep_AppendsToolObservation()
    {
        AgentRunSession session = AgentRunSession.Start(
            CreateEvent(),
            StreamingOutputPolicy.Token,
            startedAt: new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero));

        session.RecordToolStep("qchat_group_members_refresh", "ok", new DateTimeOffset(2026, 6, 15, 10, 0, 3, TimeSpan.Zero));

        AgentRunSnapshot snapshot = session.Snapshot();

        Assert.That(snapshot.ToolSteps, Has.Count.EqualTo(1));
        Assert.That(snapshot.ToolSteps[0].ToolName, Is.EqualTo("qchat_group_members_refresh"));
        Assert.That(snapshot.ToolSteps[0].ResultSummary, Is.EqualTo("ok"));
    }

    [Test]
    public void Fail_CompletesRunWithErrorAndDuration()
    {
        AgentRunSession session = AgentRunSession.Start(
            CreateEvent(),
            StreamingOutputPolicy.Token,
            startedAt: new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero));

        session.Fail("model unavailable", new DateTimeOffset(2026, 6, 15, 10, 0, 4, TimeSpan.Zero));

        AgentRunSnapshot snapshot = session.Snapshot();

        Assert.That(snapshot.Status, Is.EqualTo(AgentRunStatus.Failed));
        Assert.That(snapshot.Error, Is.EqualTo("model unavailable"));
        Assert.That(snapshot.EndedAt, Is.EqualTo(new DateTimeOffset(2026, 6, 15, 10, 0, 4, TimeSpan.Zero)));
        Assert.That(snapshot.Duration, Is.EqualTo(TimeSpan.FromSeconds(4)));
    }

    static AgentEvent CreateEvent() => new(
        Type: "qq.message.group",
        Source: "qq",
        SessionId: "qq:group:1000",
        ActorId: "qq:2000",
        Text: "hello");
}
