using Alife.Framework;

namespace Alife.Test.Framework;

public class AgentBackgroundTaskWakeEventTests
{
    [Test]
    public void ToWakeEvent_CompletedTaskCreatesSystemWakeEvent()
    {
        AgentBackgroundTaskResult result = AgentBackgroundTaskResult.Completed(
            taskId: "task-1",
            taskName: "qzone-reply-check",
            sourceSessionId: "qq:private:10001",
            resultText: "3 comments replied.",
            completedAt: new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero));

        AgentEvent agentEvent = result.ToWakeEvent();

        Assert.That(agentEvent.Type, Is.EqualTo("agent.background.completed"));
        Assert.That(agentEvent.Source, Is.EqualTo("system"));
        Assert.That(agentEvent.SessionId, Is.EqualTo("qq:private:10001"));
        Assert.That(agentEvent.ActorId, Is.Null);
        Assert.That(agentEvent.Text, Does.Contain("qzone-reply-check"));
        Assert.That(agentEvent.Text, Does.Contain("3 comments replied."));
        Assert.That(agentEvent.State[AgentBackgroundTaskResult.WakeResultKey], Is.SameAs(result));
    }

    [Test]
    public void ToWakeEvent_FailedTaskCreatesFailedWakeEvent()
    {
        AgentBackgroundTaskResult result = AgentBackgroundTaskResult.Failed(
            taskId: "task-2",
            taskName: "browser-observe",
            sourceSessionId: "qq:group:20002",
            error: "browser unavailable",
            completedAt: new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero));

        AgentEvent agentEvent = result.ToWakeEvent();

        Assert.That(agentEvent.Type, Is.EqualTo("agent.background.failed"));
        Assert.That(agentEvent.Text, Does.Contain("browser unavailable"));
        Assert.That(agentEvent.State[AgentBackgroundTaskResult.WakeResultKey], Is.SameAs(result));
    }
}
