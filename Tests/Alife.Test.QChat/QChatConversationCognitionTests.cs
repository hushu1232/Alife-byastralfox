using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

public sealed class QChatConversationCognitionTests
{
    [Test]
    public void BuildInternalPrompt_ProvidesFactualOwnerQuestionRoutingWithoutForcedPersonaAction()
    {
        QChatConfig config = new() { OwnerId = 10001, QuietModeWakeUserIds = "20002" };
        OneBotMessageEvent messageEvent = new() { UserId = 10001, RawMessage = "how should we improve memory?" };

        string prompt = QChatConversationCognition.BuildInternalPrompt(
            config,
            messageEvent,
            messageEvent.RawMessage,
            "how should we improve memory?",
            isMentionedOrWoken: false);

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("relationship=owner"));
            Assert.That(prompt, Does.Contain("message_intent=question"));
            Assert.That(prompt, Does.Contain("mentioned_or_woken=false"));
            Assert.That(prompt, Does.Contain("reply_eligibility=high"));
            Assert.That(prompt, Does.Contain("expected_length=medium"));
            Assert.That(prompt, Does.Not.Contain("social_action="));
            Assert.That(prompt, Does.Not.Contain("attachment="));
            Assert.That(prompt, Does.Not.Contain("desire="));
            Assert.That(prompt, Does.Not.Contain("jealousy="));
            Assert.That(prompt, Does.Not.Contain("emotional_distance="));
            Assert.That(prompt, Does.Not.Contain("[persona style contract]"));
        });
    }

    [Test]
    public void BuildInternalPrompt_DescribesHostileGroupMessageWithoutForcingItsReplyWording()
    {
        QChatConfig config = new() { OwnerId = 10001 };
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
            messageEvent.RawMessage,
            isMentionedOrWoken: true);

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("relationship=group-member"));
            Assert.That(prompt, Does.Contain("message_tone=hostile"));
            Assert.That(prompt, Does.Contain("message_intent=hostile"));
            Assert.That(prompt, Does.Contain("mentioned_or_woken=true"));
            Assert.That(prompt, Does.Not.Contain("sharp_pushback"));
            Assert.That(prompt, Does.Not.Contain("cold_ack"));
            Assert.That(prompt, Does.Not.Contain("[persona style contract]"));
        });
    }

    [Test]
    public void PersonaStyleContext_ResolvesMixuByKnownBotAndNeverFallsBackToXiayu()
    {
        QChatPersonaStyleContext mixu = QChatPersonaStyleContext.FromRuntime(
            new QChatConfig { BotId = 3340947887 },
            "夏羽");
        QChatPersonaStyleContext unknown = QChatPersonaStyleContext.FromRuntime(
            new QChatConfig { BotId = 123456789 },
            "未知角色");

        Assert.Multiple(() =>
        {
            Assert.That(mixu.PersonaId, Is.EqualTo("mixu"));
            Assert.That(unknown.PersonaId, Is.EqualTo("default"));
            Assert.That(unknown.OwnerAddressName, Is.Empty);
        });
    }

    [Test]
    public void BuildInternalPrompt_DescribesQuietWakeUserAsMotherWithoutEmotionContract()
    {
        QChatConfig config = new() { OwnerId = 10001, QuietModeWakeUserIds = "20002" };
        OneBotMessageEvent messageEvent = new() { UserId = 20002, RawMessage = "wake up" };

        string prompt = QChatConversationCognition.BuildInternalPrompt(
            config,
            messageEvent,
            messageEvent.RawMessage,
            "wake up",
            isMentionedOrWoken: false);

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("relationship=mother"));
            Assert.That(prompt, Does.Contain("message_intent=command"));
            Assert.That(prompt, Does.Contain("reply_eligibility=medium"));
            Assert.That(prompt, Does.Not.Contain("social_action="));
            Assert.That(prompt, Does.Not.Contain("persona="));
        });
    }
}
