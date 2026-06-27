using System.Reflection;
using Alife.Components.Services;
using Alife.Framework;

namespace Alife.Test.Framework;

public class ChatMessageServiceLifecycleTests
{
    [Test]
    public void DisposeUnsubscribesFromChatActivitySystemEvents()
    {
        StorageSystem storage = new();
        ChatActivitySystem chatActivitySystem = new(null!, null!, null!, storage);
        ChatMessageService service = new(chatActivitySystem, storage);

        service.Dispose();

        Assert.That(GetSubscriberCount(chatActivitySystem, "ActivatingCreated"), Is.Zero);
        Assert.That(GetSubscriberCount(chatActivitySystem, "Destroyed"), Is.Zero);
        Assert.That(GetSubscriberCount(chatActivitySystem, "ActivationFailed"), Is.Zero);
    }

    [Test]
    public async Task ChatMessagesAreTrimmedToConfiguredMaximum()
    {
        StorageSystem storage = new();
        ChatActivitySystem chatActivitySystem = new(null!, null!, null!, storage);
        ChatMessageService service = new(chatActivitySystem, storage);
        SetMaxMessageCount(service, 3);
        ChatBot chatBot = new(null!, null!);
        ChatActivity chatActivity = new(
            new Character { Name = "LifecycleTestCharacter" },
            null!,
            null!,
            chatBot,
            []);

        try
        {
            RaiseEvent(chatActivitySystem, "ActivatingCreated", chatActivity);

            RaiseEvent(chatBot, "ChatSent", "first");
            RaiseEvent(chatBot, "ChatOver");
            RaiseEvent(chatBot, "ChatSent", "second");
            RaiseEvent(chatBot, "ChatOver");

            List<ChatMessage> messages = service.GetMessages("LifecycleTestCharacter");

            Assert.That(messages, Has.Count.EqualTo(3));
            Assert.That(messages[0].Content, Is.Null);
            Assert.That(messages[0].IsUser, Is.False);
            Assert.That(messages[1].Content, Is.EqualTo("second"));
            Assert.That(messages[1].IsUser, Is.True);
            Assert.That(messages[2].IsInputting, Is.False);
        }
        finally
        {
            service.Dispose();
            await chatBot.DisposeAsync();
        }
    }

    static int GetSubscriberCount(object target, string eventName)
    {
        MulticastDelegate? eventDelegate = GetEventDelegate(target, eventName);
        return eventDelegate?.GetInvocationList().Length ?? 0;
    }

    static void RaiseEvent(object target, string eventName, params object?[] arguments)
    {
        MulticastDelegate? eventDelegate = GetEventDelegate(target, eventName);
        eventDelegate?.DynamicInvoke(arguments);
    }

    static MulticastDelegate? GetEventDelegate(object target, string eventName)
    {
        FieldInfo? field = target.GetType().GetField(
            eventName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Event backing field '{eventName}' should exist.");
        return field!.GetValue(target) as MulticastDelegate;
    }

    static void SetMaxMessageCount(ChatMessageService service, int maxMessageCount)
    {
        FieldInfo? field = typeof(ChatMessageService).GetField(
            "settings",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, "Settings field should exist.");
        ChatSettings settings = (ChatSettings)field!.GetValue(service)!;
        settings.MaxMessageCount = maxMessageCount;
    }
}
