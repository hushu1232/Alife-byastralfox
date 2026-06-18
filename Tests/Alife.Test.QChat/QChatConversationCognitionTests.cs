using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

public class QChatConversationCognitionTests
{
    [Test]
    public void BuildInternalPrompt_DescribesOwnerQuestionAsPrivateWarmReplyHint()
    {
        QChatConfig config = new()
        {
            OwnerId = 10001,
            QuietModeWakeUserIds = "20002",
        };
        OneBotMessageEvent messageEvent = new()
        {
            UserId = 10001,
            RawMessage = "how should we improve memory?"
        };

        string prompt = QChatConversationCognition.BuildInternalPrompt(
            config,
            messageEvent,
            messageEvent.RawMessage,
            "how should we improve memory?",
            isMentionedOrWoken: false);

        Assert.That(prompt, Does.Contain("[private QQ routing hint - never quote or paraphrase]"));
        Assert.That(prompt, Does.Contain("relationship=owner"));
        Assert.That(prompt, Does.Contain("message_intent=question"));
        Assert.That(prompt, Does.Contain("social_action=reply_warmly"));
        Assert.That(prompt, Does.Contain("expected_length=medium"));
        Assert.That(prompt, Does.Contain("[/private QQ routing hint]"));
        Assert.That(prompt, Does.Not.Contain("[QQ cognition]"));
        Assert.That(prompt, Does.Not.Contain("reply_need="));
    }

    [Test]
    public void BuildInternalPrompt_DescribesQuietWakeUserAsMotherWithoutOwnerPriority()
    {
        QChatConfig config = new()
        {
            OwnerId = 10001,
            QuietModeWakeUserIds = "20002",
        };
        OneBotMessageEvent messageEvent = new()
        {
            UserId = 20002,
            RawMessage = "wake up"
        };

        string prompt = QChatConversationCognition.BuildInternalPrompt(
            config,
            messageEvent,
            messageEvent.RawMessage,
            "wake up",
            isMentionedOrWoken: false);

        Assert.That(prompt, Does.Contain("relationship=mother"));
        Assert.That(prompt, Does.Contain("message_intent=command"));
        Assert.That(prompt, Does.Contain("social_action=reply_concisely"));
        Assert.That(prompt, Does.Contain("expected_length=short"));
        Assert.That(prompt, Does.Not.Contain("priority=owner"));
    }

    [Test]
    public void BuildInternalPrompt_DescribesOrdinaryGroupMemberAsLowNeedShortReply()
    {
        QChatConfig config = new()
        {
            OwnerId = 10001,
            QuietModeWakeUserIds = "20002",
        };
        OneBotMessageEvent messageEvent = new()
        {
            UserId = 30003,
            GroupId = 40004,
            RawMessage = "I think this part is confusing"
        };

        string prompt = QChatConversationCognition.BuildInternalPrompt(
            config,
            messageEvent,
            messageEvent.RawMessage,
            "I think this part is confusing",
            isMentionedOrWoken: false);

        Assert.That(prompt, Does.Contain("relationship=group-member"));
        Assert.That(prompt, Does.Contain("message_intent=reaction"));
        Assert.That(prompt, Does.Contain("social_action=reply_concisely"));
        Assert.That(prompt, Does.Contain("expected_length=short"));
    }

    [Test]
    public void BuildInternalPrompt_DescribesImageOnlyMessageAsImageReaction()
    {
        QChatConfig config = new()
        {
            OwnerId = 10001,
        };
        OneBotMessageEvent messageEvent = new()
        {
            UserId = 30003,
            GroupId = 40004,
            RawMessage = "[CQ:image,file=abc.jpg]"
        };

        string prompt = QChatConversationCognition.BuildInternalPrompt(
            config,
            messageEvent,
            messageEvent.RawMessage,
            "",
            isMentionedOrWoken: false);

        Assert.That(prompt, Does.Contain("message_intent=image-reaction"));
        Assert.That(prompt, Does.Contain("social_action=reply_concisely"));
        Assert.That(prompt, Does.Contain("expected_length=short"));
    }

    [Test]
    public void BuildInternalPrompt_DescribesLowInformationPassiveGroupMessageAsSilent()
    {
        QChatConfig config = new()
        {
            OwnerId = 10001,
        };
        OneBotMessageEvent messageEvent = new()
        {
            UserId = 30003,
            GroupId = 40004,
            RawMessage = "ok"
        };

        string prompt = QChatConversationCognition.BuildInternalPrompt(
            config,
            messageEvent,
            messageEvent.RawMessage,
            "ok",
            isMentionedOrWoken: false);

        Assert.That(prompt, Does.Contain("relationship=group-member"));
        Assert.That(prompt, Does.Contain("message_intent=low-information"));
        Assert.That(prompt, Does.Contain("social_action=ignore_or_cold_ack"));
        Assert.That(prompt, Does.Contain("expected_length=short"));
    }
}
