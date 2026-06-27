using Alife.Framework;

namespace Alife.Test.Framework;

public class AgentEventPipelineTests
{
    [Test]
    public async Task DispatchAsync_RunsHigherPriorityMatcherFirst()
    {
        List<string> handled = [];
        AgentEventPipeline pipeline = new();
        pipeline.Register(new AgentEventMatcher(
            Name: "low",
            Priority: 10,
            Rule: _ => true,
            Permission: _ => true,
            Handler: _ =>
            {
                handled.Add("low");
                return Task.CompletedTask;
            }));
        pipeline.Register(new AgentEventMatcher(
            Name: "high",
            Priority: 100,
            Rule: _ => true,
            Permission: _ => true,
            Handler: _ =>
            {
                handled.Add("high");
                return Task.CompletedTask;
            }));

        AgentEventDispatchResult result = await pipeline.DispatchAsync(CreateEvent());

        Assert.That(handled, Is.EqualTo(new[] { "high", "low" }));
        Assert.That(result.HandledMatchers, Is.EqualTo(new[] { "high", "low" }));
        Assert.That(result.Blocked, Is.False);
    }

    [Test]
    public async Task DispatchAsync_BlockingMatcherStopsLowerPriorityMatchers()
    {
        List<string> handled = [];
        AgentEventPipeline pipeline = new();
        pipeline.Register(new AgentEventMatcher(
            Name: "owner",
            Priority: 100,
            Rule: _ => true,
            Permission: _ => true,
            Handler: _ =>
            {
                handled.Add("owner");
                return Task.CompletedTask;
            },
            Block: true));
        pipeline.Register(new AgentEventMatcher(
            Name: "group",
            Priority: 10,
            Rule: _ => true,
            Permission: _ => true,
            Handler: _ =>
            {
                handled.Add("group");
                return Task.CompletedTask;
            }));

        AgentEventDispatchResult result = await pipeline.DispatchAsync(CreateEvent());

        Assert.That(handled, Is.EqualTo(new[] { "owner" }));
        Assert.That(result.HandledMatchers, Is.EqualTo(new[] { "owner" }));
        Assert.That(result.Blocked, Is.True);
    }

    [Test]
    public async Task DispatchAsync_SkipsMatcherWhenPermissionFails()
    {
        bool handlerRan = false;
        AgentEventPipeline pipeline = new();
        pipeline.Register(new AgentEventMatcher(
            Name: "denied",
            Priority: 100,
            Rule: _ => true,
            Permission: _ => false,
            Handler: _ =>
            {
                handlerRan = true;
                return Task.CompletedTask;
            }));

        AgentEventDispatchResult result = await pipeline.DispatchAsync(CreateEvent());

        Assert.That(handlerRan, Is.False);
        Assert.That(result.HandledMatchers, Is.Empty);
        Assert.That(result.Blocked, Is.False);
    }

    [Test]
    public void GetSnapshotReportsRegisteredMatchersForControlCenterVisibility()
    {
        AgentEventPipeline pipeline = new();
        pipeline.Register(new AgentEventMatcher(
            Name: "group-passive",
            Priority: 10,
            Rule: _ => true,
            Permission: _ => true,
            Handler: _ => Task.CompletedTask));
        pipeline.Register(new AgentEventMatcher(
            Name: "owner-command",
            Priority: 100,
            Rule: _ => true,
            Permission: _ => true,
            Handler: _ => Task.CompletedTask,
            Block: true));

        AgentEventPipelineSnapshot snapshot = pipeline.GetSnapshot();

        Assert.That(snapshot.MatcherCount, Is.EqualTo(2));
        Assert.That(snapshot.Matchers.Select(matcher => matcher.Name), Is.EqualTo(new[] { "owner-command", "group-passive" }));
        Assert.That(snapshot.Matchers[0].Priority, Is.EqualTo(100));
        Assert.That(snapshot.Matchers[0].Block, Is.True);
    }

    static AgentEvent CreateEvent() => new(
        Type: "qq.message",
        Source: "qq",
        SessionId: "group:1000",
        ActorId: "qq:2000",
        Text: "hello");
}
