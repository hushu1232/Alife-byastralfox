using System;
using System.Collections.Generic;

namespace Alife.Framework;

public enum LifeEventKind
{
    System,
    Body,
    Voice,
    Communication,
    Browser,
    Memory,
    Sense,
    Action
}

public enum LifeEventPrivacy
{
    Public,
    Private,
    Sensitive
}

public sealed record LifeEvent(
    DateTimeOffset Timestamp,
    LifeEventKind Kind,
    string Source,
    string Summary)
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public int Importance { get; init; } = 1;
    public LifeEventPrivacy Privacy { get; init; } = LifeEventPrivacy.Private;
    public bool IsPersisted { get; init; }
}

public interface ILifeEventPublisher
{
    void Publish(LifeEvent lifeEvent);
}

public interface ILifeEventStream : ILifeEventPublisher
{
    IReadOnlyList<LifeEvent> GetRecentEvents(int maxCount);
    void MarkPersisted(IEnumerable<string> eventIds) {}
}
