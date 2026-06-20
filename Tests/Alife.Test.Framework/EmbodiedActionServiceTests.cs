using Alife.Framework;
using Alife.Function.MessageFilter;

namespace Alife.Test.Framework;

public class EmbodiedActionServiceTests
{
    [Test]
    public async Task Act_SpeakAppliesBodyThenVoice()
    {
        FakeBodySink body = new();
        FakeVoiceSink voice = new();
        EmbodiedActionService service = new([body], [voice], []);

        await service.ExecuteActionAsync("speak", "hello", "smile", "wave", null, null, false, CancellationToken.None);

        Assert.That(body.Calls, Is.EqualTo(new[] { "expression:smile", "motion:wave" }));
        Assert.That(voice.Calls, Is.EqualTo(new[] { "voice:hello" }));
    }

    [Test]
    public async Task Act_BubbleRoutesToBodyBubble()
    {
        FakeBodySink body = new();
        EmbodiedActionService service = new([body], [], []);

        await service.ExecuteActionAsync("bubble", "thinking", null, null, null, null, false, CancellationToken.None);

        Assert.That(body.Calls, Is.EqualTo(new[] { "bubble:thinking" }));
    }

    [Test]
    public async Task Act_QChatRoutesTargetAndVoiceFlag()
    {
        FakeChatSink chat = new();
        EmbodiedActionService service = new([], [], [chat]);

        await service.ExecuteActionAsync("qchat", "message", null, null, "group", 123, true, CancellationToken.None);

        Assert.That(chat.Calls, Is.EqualTo(new[] { "chat:group:123:True:message" }));
    }

    [Test]
    public async Task Act_PublishesLifeEventAfterRoutingAction()
    {
        FakeVoiceSink voice = new();
        FakeLifeEventPublisher publisher = new();
        EmbodiedActionService service = new([], [voice], [], lifeEventPublisher: publisher);

        await service.ExecuteActionAsync("speak", "hello", null, null, null, null, false, CancellationToken.None);

        Assert.That(publisher.Events, Has.Count.EqualTo(1));
        Assert.That(publisher.Events[0].Kind, Is.EqualTo(LifeEventKind.Action));
        Assert.That(publisher.Events[0].Source, Is.EqualTo("EmbodiedAction"));
        Assert.That(publisher.Events[0].Summary, Does.Contain("You performed a speak action"));
    }

    [Test]
    public async Task Act_MissingBubbleSinkReportsInsteadOfThrowing()
    {
        EmbodiedActionService service = new([], [], []);

        await service.ExecuteActionAsync("bubble", "hello", null, null, null, null, false, CancellationToken.None);

        Assert.That(service.LastActionNotice, Does.Contain("No body sink is available"));
    }

    [Test]
    public void Act_QChatRequiresTargetId()
    {
        EmbodiedActionService service = new([], [], [new FakeChatSink()]);

        InvalidOperationException ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.ExecuteActionAsync("qchat", "message", null, null, "group", null, false, CancellationToken.None))!;

        Assert.That(ex.Message, Does.Contain("targetId"));
    }

    sealed class FakeBodySink : IBodyExpressionSink
    {
        public List<string> Calls { get; } = new();
        public void PlayExpression(string option) => Calls.Add("expression:" + option);
        public void PlayMotion(string option) => Calls.Add("motion:" + option);
        public Task ShowBubbleAsync(string text, CancellationToken cancellationToken = default)
        {
            Calls.Add("bubble:" + text);
            return Task.CompletedTask;
        }
    }

    sealed class FakeVoiceSink : IVoiceOutputSink
    {
        public List<string> Calls { get; } = new();
        public Task SpeakAsync(string text, CancellationToken cancellationToken = default)
        {
            Calls.Add("voice:" + text);
            return Task.CompletedTask;
        }
    }

    sealed class FakeChatSink : IChatOutputSink
    {
        public List<string> Calls { get; } = new();
        public Task SendChatAsync(string targetType, long targetId, string text, bool voice = false)
        {
            Calls.Add($"chat:{targetType}:{targetId}:{voice}:{text}");
            return Task.CompletedTask;
        }
    }

    sealed class FakeLifeEventPublisher : ILifeEventPublisher
    {
        public List<LifeEvent> Events { get; } = new();
        public void Publish(LifeEvent lifeEvent) => Events.Add(lifeEvent);
    }
}
