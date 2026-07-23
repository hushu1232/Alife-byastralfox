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
}
