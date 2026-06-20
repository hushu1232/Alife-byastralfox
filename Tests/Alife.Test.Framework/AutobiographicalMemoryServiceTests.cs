using Alife.Framework;
using Alife.Function.Memory;

namespace Alife.Test.Framework;

public class AutobiographicalMemoryServiceTests
{
    [Test]
    public void MemoryServiceExposesAutobiographicalMemoryController()
    {
        Assert.That(typeof(IAutobiographicalMemoryController).IsAssignableFrom(typeof(MemoryService)), Is.True);
    }

    [Test]
    public async Task RememberRecentLife_NoEventsDoesNotWriteMemory()
    {
        FakeLifeEventStream stream = new();
        FakeMemorySink sink = new();
        AutobiographicalMemoryService service = new(stream, sink);

        string? result = await service.RememberRecentLifeAsync();

        Assert.That(result, Is.Null);
        Assert.That(sink.Writes, Is.Empty);
    }

    [Test]
    public async Task RememberRecentLife_BodyOnlyEventDoesNotWriteMemory()
    {
        FakeLifeEventStream stream = new();
        stream.Publish(Event(LifeEventKind.Body, "DeskPet", "Your desk-pet body played expression: smile."));
        FakeMemorySink sink = new();
        AutobiographicalMemoryService service = new(stream, sink);

        string? result = await service.RememberRecentLifeAsync();

        Assert.That(result, Is.Null);
        Assert.That(sink.Writes, Is.Empty);
    }

    [Test]
    public async Task RememberRecentLife_MeaningfulEventsWriteOneAutobiographicalMemory()
    {
        FakeLifeEventStream stream = new();
        stream.Publish(Event(LifeEventKind.Browser, "Browser", "You opened a browser page: https://example.com", minute: 1));
        stream.Publish(Event(LifeEventKind.Communication, "QChat", "You sent a QQ group message to 123456.", minute: 2));
        FakeMemorySink sink = new();
        AutobiographicalMemoryService service = new(stream, sink);

        string? result = await service.RememberRecentLifeAsync();

        Assert.That(result, Is.EqualTo("memory-1"));
        Assert.That(sink.Writes, Has.Count.EqualTo(1));
        Assert.That(sink.Writes[0].Summary, Does.Contain("Autobiographical memory"));
        Assert.That(sink.Writes[0].Summary, Does.Contain("You opened a browser page"));
        Assert.That(sink.Writes[0].Content, Does.Contain("[Browser/Browser] You opened a browser page: https://example.com"));
        Assert.That(sink.Writes[0].Content, Does.Contain("[Communication/QChat] You sent a QQ group message to 123456."));
        Assert.That(sink.Writes[0].StartTime, Is.EqualTo(new DateTime(2026, 6, 14, 10, 1, 0)));
        Assert.That(sink.Writes[0].EndTime, Is.EqualTo(new DateTime(2026, 6, 14, 10, 2, 0)));
    }

    [Test]
    public async Task RememberRecentLife_DoesNotWriteSameEventsTwice()
    {
        FakeLifeEventStream stream = new();
        stream.Publish(Event(LifeEventKind.Browser, "Browser", "You observed a page.", minute: 1));
        FakeMemorySink sink = new();
        AutobiographicalMemoryService service = new(stream, sink);

        await service.RememberRecentLifeAsync();
        string? second = await service.RememberRecentLifeAsync();

        Assert.That(second, Is.Null);
        Assert.That(sink.Writes, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task RememberRecentLife_WritesNewEventsAfterPreviousMemory()
    {
        FakeLifeEventStream stream = new();
        stream.Publish(Event(LifeEventKind.Browser, "Browser", "You observed a page.", minute: 1));
        FakeMemorySink sink = new();
        AutobiographicalMemoryService service = new(stream, sink);

        await service.RememberRecentLifeAsync();
        stream.Publish(Event(LifeEventKind.Communication, "QChat", "You sent a QQ private message.", minute: 3));
        string? second = await service.RememberRecentLifeAsync();

        Assert.That(second, Is.EqualTo("memory-2"));
        Assert.That(sink.Writes, Has.Count.EqualTo(2));
        Assert.That(sink.Writes[1].Content, Does.Contain("You sent a QQ private message."));
        Assert.That(sink.Writes[1].Content, Does.Not.Contain("You observed a page."));
    }

    [Test]
    public async Task RememberRecentLife_ConvertsQChatRuntimeEventsIntoLivedExperience()
    {
        FakeLifeEventStream stream = new();
        stream.Publish(Event(
            LifeEventKind.Communication,
            "QChat",
            "group-decision decision=suppressed reason=social-attention group=867165927 user=2002 raw=路过说一句",
            minute: 1));
        stream.Publish(Event(
            LifeEventKind.Communication,
            "QChat",
            "group-decision decision=accepted reason=mention-or-wake group=867165927 user=2001 raw=[CQ:at,qq=3340947887] 你在吗",
            minute: 2));
        stream.Publish(Event(
            LifeEventKind.Communication,
            "QChat",
            "qchat-quiet-mode-enabled reason=owner-sleep-command",
            minute: 3));
        stream.Publish(Event(
            LifeEventKind.Communication,
            "QChat",
            "qchat-quiet-mode-disabled reason=trusted-wake-user-command",
            minute: 4));
        FakeMemorySink sink = new();
        AutobiographicalMemoryService service = new(stream, sink);

        string? result = await service.RememberRecentLifeAsync();

        Assert.That(result, Is.EqualTo("memory-1"));
        Assert.That(sink.Writes, Has.Count.EqualTo(1));
        Assert.That(sink.Writes[0].Summary, Does.Contain("群 867165927 有人在说话，但我判断那不是必须插话的时机"));
        Assert.That(sink.Writes[0].Summary, Does.Contain("有人把我的注意力叫回群 867165927"));
        Assert.That(sink.Writes[0].Content, Does.Contain("[Communication/QChat] 群 867165927 有人在说话，但我判断那不是必须插话的时机。"));
        Assert.That(sink.Writes[0].Content, Does.Contain("[Communication/QChat] 有人把我的注意力叫回群 867165927，我短暂进入了回应状态。"));
        Assert.That(sink.Writes[0].Content, Does.Contain("[Communication/QChat] 主人让我安静，我把 QQ 参与欲望降下来了。"));
        Assert.That(sink.Writes[0].Content, Does.Contain("[Communication/QChat] 安静状态被唤醒，我可以重新关注 QQ 对话。"));
        Assert.That(sink.Writes[0].Content, Does.Not.Contain("group-decision"));
        Assert.That(sink.Writes[0].Content, Does.Not.Contain("qchat-quiet-mode-enabled"));
        Assert.That(sink.Writes[0].Content, Does.Not.Contain("qchat-quiet-mode-disabled"));
    }

    [Test]
    public async Task RememberRecentLife_SkipsRawQChatToolAndSystemNoise()
    {
        FakeLifeEventStream stream = new();
        stream.Publish(Event(LifeEventKind.Browser, "Browser", "You opened a useful page.", minute: 1));
        stream.Publish(Event(LifeEventKind.Communication, "QChat", "<qchat type=\"Group\" targetId=\"123\">bad xml</qchat>", minute: 2));
        stream.Publish(Event(LifeEventKind.Communication, "System", "[XmlFunctionCaller] qchat tag error: invalid child closing tag", minute: 3));
        stream.Publish(Event(LifeEventKind.Communication, "System", "[系统报点] timer fired; do not tell the owner this was automatic", minute: 4));
        stream.Publish(Event(LifeEventKind.Browser, "Browser", "You finished reading the useful page.", minute: 5));
        FakeMemorySink sink = new();
        AutobiographicalMemoryService service = new(stream, sink);

        string? result = await service.RememberRecentLifeAsync();

        Assert.That(result, Is.EqualTo("memory-1"));
        Assert.That(sink.Writes, Has.Count.EqualTo(1));
        Assert.That(sink.Writes[0].Content, Does.Contain("You opened a useful page."));
        Assert.That(sink.Writes[0].Content, Does.Contain("You finished reading the useful page."));
        Assert.That(sink.Writes[0].Content, Does.Not.Contain("<qchat"));
        Assert.That(sink.Writes[0].Content, Does.Not.Contain("XmlFunctionCaller"));
        Assert.That(sink.Writes[0].Content, Does.Not.Contain("qchat tag error"));
        Assert.That(sink.Writes[0].Content, Does.Not.Contain("系统报点"));
        Assert.That(sink.Writes[0].Content, Does.Not.Contain("do not tell the owner"));
    }

    [Test]
    public async Task RememberRecentLife_IgnoresOlderUnpersistedEventsAfterPreviousMemory()
    {
        FakeLifeEventStream stream = new();
        stream.Publish(Event(LifeEventKind.Browser, "Browser", "You observed a page.", minute: 2));
        FakeMemorySink sink = new();
        AutobiographicalMemoryService service = new(stream, sink);

        await service.RememberRecentLifeAsync();
        stream.Publish(Event(LifeEventKind.Communication, "QChat", "A delayed old QQ event arrived.", minute: 1));
        string? second = await service.RememberRecentLifeAsync();

        Assert.That(second, Is.Null);
        Assert.That(sink.Writes, Has.Count.EqualTo(1));
    }

    static LifeEvent Event(LifeEventKind kind, string source, string summary, int minute = 0)
    {
        return new LifeEvent(
            new DateTimeOffset(2026, 6, 14, 10, minute, 0, TimeSpan.Zero),
            kind,
            source,
            summary);
    }

    sealed class FakeLifeEventStream : ILifeEventStream
    {
        readonly List<LifeEvent> events = new();

        public void Publish(LifeEvent lifeEvent)
        {
            events.Add(lifeEvent);
        }

        public IReadOnlyList<LifeEvent> GetRecentEvents(int maxCount)
        {
            return events
                .OrderBy(lifeEvent => lifeEvent.Timestamp)
                .TakeLast(maxCount)
                .ToArray();
        }
    }

    sealed class FakeMemorySink : IAutobiographicalMemorySink
    {
        public List<Write> Writes { get; } = new();

        public Task<string> InsertAutobiographicalMemoryAsync(
            string summary,
            string content,
            DateTime startTime,
            DateTime endTime,
            CancellationToken cancellationToken = default)
        {
            Writes.Add(new Write(summary, content, startTime, endTime));
            return Task.FromResult($"memory-{Writes.Count}");
        }
    }

    sealed record Write(string Summary, string Content, DateTime StartTime, DateTime EndTime);
}
