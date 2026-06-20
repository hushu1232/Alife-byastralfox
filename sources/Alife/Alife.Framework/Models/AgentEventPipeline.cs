using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Alife.Framework;

public sealed record AgentEvent(
    string Type,
    string Source,
    string SessionId,
    string? ActorId,
    string Text)
{
    public Dictionary<string, object?> State { get; } = [];
}

public sealed record AgentEventMatcher(
    string Name,
    int Priority,
    Func<AgentEvent, bool> Rule,
    Func<AgentEvent, bool> Permission,
    Func<AgentEvent, Task> Handler,
    bool Block = false);

public sealed record AgentEventDispatchResult(
    IReadOnlyList<string> HandledMatchers,
    bool Blocked);

public sealed record AgentEventMatcherSnapshot(
    string Name,
    int Priority,
    bool Block);

public sealed record AgentEventPipelineSnapshot(
    int MatcherCount,
    IReadOnlyList<AgentEventMatcherSnapshot> Matchers);

public sealed class AgentEventPipeline
{
    readonly List<AgentEventMatcher> matchers = [];

    public void Register(AgentEventMatcher matcher)
    {
        matchers.Add(matcher);
    }

    public AgentEventPipelineSnapshot GetSnapshot()
    {
        AgentEventMatcherSnapshot[] matcherSnapshots = matchers
            .OrderByDescending(matcher => matcher.Priority)
            .ThenBy(matcher => matcher.Name, StringComparer.OrdinalIgnoreCase)
            .Select(matcher => new AgentEventMatcherSnapshot(matcher.Name, matcher.Priority, matcher.Block))
            .ToArray();

        return new AgentEventPipelineSnapshot(matcherSnapshots.Length, matcherSnapshots);
    }

    public async Task<AgentEventDispatchResult> DispatchAsync(AgentEvent agentEvent)
    {
        List<string> handled = [];
        bool blocked = false;

        foreach (AgentEventMatcher matcher in matchers
                     .OrderByDescending(matcher => matcher.Priority)
                     .ThenBy(matcher => matcher.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (matcher.Rule(agentEvent) == false)
                continue;
            if (matcher.Permission(agentEvent) == false)
                continue;

            await matcher.Handler(agentEvent);
            handled.Add(matcher.Name);

            if (matcher.Block)
            {
                blocked = true;
                break;
            }
        }

        return new AgentEventDispatchResult(handled, blocked);
    }
}
