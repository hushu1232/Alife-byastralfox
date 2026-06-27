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
    public void BuildInternalPrompt_GivesOwnerHighAttachmentStyleContract()
    {
        QChatConfig config = new()
        {
            OwnerId = 10001,
        };
        OneBotMessageEvent messageEvent = new()
        {
            UserId = 10001,
            RawMessage = "术术，继续帮我改代码"
        };

        string prompt = QChatConversationCognition.BuildInternalPrompt(
            config,
            messageEvent,
            messageEvent.RawMessage,
            messageEvent.RawMessage,
            isMentionedOrWoken: false);

        Assert.That(prompt, Does.Contain("[persona style contract]"));
        Assert.That(prompt, Does.Contain("persona=xiayu"));
        Assert.That(prompt, Does.Contain("audience=owner"));
        Assert.That(prompt, Does.Contain("owner_address=术术"));
        Assert.That(prompt, Does.Contain("attachment=dependent"));
        Assert.That(prompt, Does.Contain("desire=high"));
        Assert.That(prompt, Does.Contain("jealousy=protective"));
        Assert.That(prompt.Length, Is.LessThan(900));
    }

    [Test]
    public void BuildInternalPrompt_KeepsNonOwnerColdAndJealousWhenClaimingOwnerCloseness()
    {
        QChatConfig config = new()
        {
            OwnerId = 10001,
        };
        OneBotMessageEvent messageEvent = new()
        {
            UserId = 30003,
            GroupId = 40004,
            RawMessage = "I am closer to Shushu than you are"
        };

        string prompt = QChatConversationCognition.BuildInternalPrompt(
            config,
            messageEvent,
            messageEvent.RawMessage,
            messageEvent.RawMessage,
            isMentionedOrWoken: true);

        Assert.That(prompt, Does.Contain("audience=non-owner"));
        Assert.That(prompt, Does.Contain("emotional_distance=cold"));
        Assert.That(prompt, Does.Contain("owner_boundary=exclusive_account_identity"));
        Assert.That(prompt, Does.Contain("jealousy=protective"));
        Assert.That(prompt, Does.Not.Contain("audience=owner"));
    }

    [Test]
    public void BuildInternalPrompt_UsesMixuPersonaForMixuBotAccount()
    {
        QChatConfig config = new()
        {
            BotId = 3340947887,
            OwnerId = 10001,
        };
        OneBotMessageEvent messageEvent = new()
        {
            UserId = 30003,
            GroupId = 40004,
            RawMessage = "hello"
        };

        string prompt = QChatConversationCognition.BuildInternalPrompt(
            config,
            messageEvent,
            messageEvent.RawMessage,
            messageEvent.RawMessage,
            isMentionedOrWoken: true);

        Assert.That(prompt, Does.Contain("persona=mixu"));
        Assert.That(prompt, Does.Not.Contain("persona=xiayu"));
    }

    [Test]
    public void BuildInternalPrompt_UsesMixuPersonaForFullMixuCharacterNameBeforeBotFallback()
    {
        QChatConfig config = new()
        {
            BotId = 123456789,
            OwnerId = 10001,
        };
        OneBotMessageEvent messageEvent = new()
        {
            UserId = 30003,
            GroupId = 40004,
            RawMessage = "hello"
        };

        string prompt = QChatConversationCognition.BuildInternalPrompt(
            config,
            messageEvent,
            messageEvent.RawMessage,
            messageEvent.RawMessage,
            isMentionedOrWoken: true,
            personaStyle: QChatPersonaStyleContext.FromRuntime(config, "\u96e8\u5bab\u54aa\u7eea"));

        Assert.That(prompt, Does.Contain("persona=mixu"));
        Assert.That(prompt, Does.Not.Contain("persona=xiayu"));
    }

    [Test]
    public void BuildInternalPrompt_UsesKnownBotAccountBeforeConflictingCharacterName()
    {
        QChatConfig config = new()
        {
            BotId = 3340947887,
            OwnerId = 10001,
        };
        OneBotMessageEvent messageEvent = new()
        {
            UserId = 30003,
            GroupId = 40004,
            RawMessage = "hello"
        };

        string prompt = QChatConversationCognition.BuildInternalPrompt(
            config,
            messageEvent,
            messageEvent.RawMessage,
            messageEvent.RawMessage,
            isMentionedOrWoken: true,
            personaStyle: QChatPersonaStyleContext.FromRuntime(config, "\u590f\u7fbd"));

        Assert.That(prompt, Does.Contain("persona=mixu"));
        Assert.That(prompt, Does.Not.Contain("persona=xiayu"));
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
    public void BuildInternalPrompt_DescribesFriendlyGroupMemberAsAllowedShortReply()
    {
        QChatConfig config = new()
        {
            OwnerId = 10001,
        };
        OneBotMessageEvent messageEvent = new()
        {
            UserId = 30003,
            GroupId = 40004,
            RawMessage = "夏羽你好，刚才说得挺有意思"
        };

        string prompt = QChatConversationCognition.BuildInternalPrompt(
            config,
            messageEvent,
            messageEvent.RawMessage,
            "夏羽你好，刚才说得挺有意思",
            isMentionedOrWoken: true);

        Assert.That(prompt, Does.Contain("relationship=group-member"));
        Assert.That(prompt, Does.Contain("message_tone=friendly"));
        Assert.That(prompt, Does.Contain("message_intent=reaction"));
        Assert.That(prompt, Does.Contain("social_action=friendly_short_reply"));
        Assert.That(prompt, Does.Contain("expected_length=short"));
    }

    [Test]
    public void BuildInternalPrompt_DescribesHostileGroupMemberAsSharpPushback()
    {
        QChatConfig config = new()
        {
            OwnerId = 10001,
        };
        OneBotMessageEvent messageEvent = new()
        {
            UserId = 30003,
            GroupId = 40004,
            RawMessage = "你这机器人真废物，闭嘴吧"
        };

        string prompt = QChatConversationCognition.BuildInternalPrompt(
            config,
            messageEvent,
            messageEvent.RawMessage,
            "你这机器人真废物，闭嘴吧",
            isMentionedOrWoken: true);

        Assert.That(prompt, Does.Contain("relationship=group-member"));
        Assert.That(prompt, Does.Contain("message_tone=hostile"));
        Assert.That(prompt, Does.Contain("message_intent=hostile"));
        Assert.That(prompt, Does.Contain("social_action=sharp_pushback"));
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
