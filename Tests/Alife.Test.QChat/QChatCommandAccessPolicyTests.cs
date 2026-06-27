using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatCommandAccessPolicyTests
{
    [TestCase("/qchat")]
    [TestCase("/qchat status")]
    [TestCase("  /QCHAT identity  ")]
    public void OwnerQChatCommandIsAllowed(string text)
    {
        QChatCommandAccessDecision decision = QChatCommandAccessPolicy.Evaluate(
            new QChatCommandAccessContext(text, QChatSenderRole.Owner));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Action, Is.EqualTo(QChatCommandAccessAction.AllowOwnerCommand));
            Assert.That(decision.Reason, Is.EqualTo("owner_qchat_command"));
        });
    }

    [TestCase(QChatSenderRole.PrivateGuest)]
    [TestCase(QChatSenderRole.GroupMember)]
    [TestCase((QChatSenderRole)(-1))]
    public void NonOwnerQChatCommandIsDroppedSilently(QChatSenderRole role)
    {
        QChatCommandAccessDecision decision = QChatCommandAccessPolicy.Evaluate(
            new QChatCommandAccessContext("/qchat status", role));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Action, Is.EqualTo(QChatCommandAccessAction.DropSilently));
            Assert.That(decision.Reason, Is.EqualTo("non_owner_qchat_command"));
        });
    }

    [TestCase("夏羽，/qchat status 是什么")]
    [TestCase("请看这个报错")]
    [TestCase("/qchatstatus")]
    [TestCase("")]
    [TestCase(null)]
    public void NonCommandTextPassesThrough(string? text)
    {
        QChatCommandAccessDecision decision = QChatCommandAccessPolicy.Evaluate(
            new QChatCommandAccessContext(text, QChatSenderRole.GroupMember));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Action, Is.EqualTo(QChatCommandAccessAction.NotCommand));
            Assert.That(decision.Reason, Is.EqualTo("not_qchat_command"));
        });
    }
}
