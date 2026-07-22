using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

public sealed class QChatConversationContextCapabilityTests
{
    [Test]
    public void ReadReturnsOnlyBoundedEarlierConversationAsUntrustedData()
    {
        QChatRecentEventMemory memory = new();
        DateTimeOffset now = new(2026, 7, 22, 9, 0, 0, TimeSpan.FromHours(8));
        for (int index = 1; index <= 7; index++)
        {
            memory.Remember(new OneBotMessageEvent
            {
                SelfId = 999,
                MessageId = index,
                UserId = 1001,
                RawMessage = $"当前私聊 {index}"
            }, $"当前私聊 {index}", now.AddSeconds(index));
        }
        memory.Remember(new OneBotMessageEvent
        {
            SelfId = 999,
            MessageId = 8,
            UserId = 2002,
            RawMessage = "其他私聊"
        }, "其他私聊", now.AddSeconds(8));

        QChatConversationContextCapability capability = new(memory);
        QChatCapabilityFeedback feedback = capability.Read(new QChatConversationContextRequest(
            SelfId: 999,
            MessageType: OneBotMessageType.Private,
            TargetId: 1001,
            MaximumMessages: 12,
            MaximumCharacters: 3000), now.AddSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(feedback.Status, Is.EqualTo(QChatCapabilityFeedbackStatus.Succeeded));
            Assert.That(feedback.Capability, Is.EqualTo("current_conversation_context"));
            Assert.That(feedback.Untrusted, Is.True);
            Assert.That(feedback.Data, Does.Contain("当前私聊 1"));
            Assert.That(feedback.Data, Does.Not.Contain("当前私聊 7"));
            Assert.That(feedback.Data, Does.Not.Contain("其他私聊"));
            Assert.That(feedback.Data, Does.Not.Contain("message_id="));
        });
    }

    [Test]
    public void ReadRejectsInvalidConversationScopeWithoutData()
    {
        QChatConversationContextCapability capability = new(new QChatRecentEventMemory());

        QChatCapabilityFeedback feedback = capability.Read(new QChatConversationContextRequest(
            SelfId: 0,
            MessageType: OneBotMessageType.Private,
            TargetId: 1001), DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(feedback.Status, Is.EqualTo(QChatCapabilityFeedbackStatus.Denied));
            Assert.That(feedback.Data, Is.Empty);
            Assert.That(feedback.UserSafeHint, Is.Not.Empty);
        });
    }

    [Test]
    public void ReadReportsNoRelevantDataForAnEmptyAuthorizedConversation()
    {
        QChatConversationContextCapability capability = new(new QChatRecentEventMemory());

        QChatCapabilityFeedback feedback = capability.Read(new QChatConversationContextRequest(
            SelfId: 999,
            MessageType: OneBotMessageType.Private,
            TargetId: 1001), DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(feedback.Status, Is.EqualTo(QChatCapabilityFeedbackStatus.NoRelevantData));
            Assert.That(feedback.Data, Is.Empty);
            Assert.That(feedback.Untrusted, Is.True);
        });
    }
}
