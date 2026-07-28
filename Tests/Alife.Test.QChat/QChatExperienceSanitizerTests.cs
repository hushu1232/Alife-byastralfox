using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

public sealed class QChatExperienceSanitizerTests
{
    [Test]
    public void SanitizeOutgoing_PreservesOrdinaryPersonaWordsButRemovesRoutingLabels()
    {
        QChatConfig xiayu = new() { BotId = 2905391496 };

        Assert.Multiple(() =>
        {
            Assert.That(QChatExperienceSanitizer.SanitizeOutgoing(xiayu, OneBotMessageType.Private, 1, "那只猫在喵喵叫"),
                Does.Contain("喵喵"));
            Assert.That(QChatExperienceSanitizer.SanitizeOutgoing(xiayu, OneBotMessageType.Private, 1, "私聊回复：你好"),
                Is.EqualTo("你好"));
        });
    }

    [Test]
    public void SanitizeOutgoing_NormalizesXiayuOwnerPunctuationWithoutChangingReplyContent()
    {
        QChatConfig xiayu = new() { BotId = 2905391496, OwnerId = 1001 };

        string result = QChatExperienceSanitizer.SanitizeOutgoing(
            xiayu,
            OneBotMessageType.Private,
            1001,
            "醒着呢术术～刚才不是说了吗，一直在这儿等你。");

        Assert.That(result, Is.EqualTo("醒着呢术术！刚才不是说了吗，一直在这儿等你"));
    }

    [Test]
    public void SanitizeOutgoing_PreservesXiayuNonOwnerFullStopWhileRemovingWaveDash()
    {
        QChatConfig xiayu = new() { BotId = 2905391496, OwnerId = 1001 };

        string result = QChatExperienceSanitizer.SanitizeOutgoing(
            xiayu,
            OneBotMessageType.Group,
            3001,
            "你好～保持距离。",
            QChatSenderRole.GroupMember);

        Assert.That(result, Is.EqualTo("你好！保持距离。"));
    }

    [Test]
    public void SanitizeOutgoing_PreservesCqMediaPayloads()
    {
        QChatConfig xiayu = new() { BotId = 2905391496, OwnerId = 1001 };
        const string message = "[CQ:image,file=https://example.test/~owner/image.jpg]";

        string result = QChatExperienceSanitizer.SanitizeOutgoing(
            xiayu,
            OneBotMessageType.Private,
            1001,
            message,
            QChatSenderRole.Owner);

        Assert.That(result, Is.EqualTo(message));
    }

    [Test]
    public void SanitizeOutgoing_PreservesCodeWhileNormalizingXiayuPersonaText()
    {
        QChatConfig xiayu = new() { BotId = 2905391496, OwnerId = 1001 };
        string input = "术术～看这里。\n```text\n保留～和。\n```\n内联 `保留~。`";

        string result = QChatExperienceSanitizer.SanitizeOutgoing(
            xiayu,
            OneBotMessageType.Group,
            3001,
            input,
            QChatSenderRole.Owner);

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.StartWith("术术！看这里"));
            Assert.That(result, Does.Contain("```text\n保留～和。\n```"));
            Assert.That(result, Does.Contain("`保留~。`"));
        });
    }
}
