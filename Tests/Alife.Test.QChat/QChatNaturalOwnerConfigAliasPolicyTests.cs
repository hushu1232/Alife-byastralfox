using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

public class QChatNaturalOwnerConfigAliasPolicyTests
{
    [TestCase("羽，说慢一点", "/qchat timing on")]
    [TestCase("羽，回复慢一点", "/qchat timing on")]
    [TestCase("羽，先合并一下多段消息", "/qchat timing on")]
    [TestCase("羽，回复快一点", "/qchat timing off")]
    [TestCase("羽，不用等我连发了", "/qchat timing off")]
    [TestCase("羽，看看延时设置", "/qchat timing status")]
    public void TryMapCommandMapsLowRiskTimingAliases(string text, string expectedCommand)
    {
        bool mapped = QChatNaturalOwnerConfigAliasPolicy.TryMapCommand(text, out string command);

        Assert.Multiple(() =>
        {
            Assert.That(mapped, Is.True);
            Assert.That(command, Is.EqualTo(expectedCommand));
        });
    }

    [TestCase("羽，关闭安全审计")]
    [TestCase("羽，绕过文件黑名单")]
    [TestCase("羽，关闭主人事件队列")]
    [TestCase("羽，删除这个文件")]
    public void TryMapCommandRejectsHardSafetyAndHighRiskAliases(string text)
    {
        bool mapped = QChatNaturalOwnerConfigAliasPolicy.TryMapCommand(text, out string command);

        Assert.Multiple(() =>
        {
            Assert.That(mapped, Is.False);
            Assert.That(command, Is.Empty);
        });
    }
}
