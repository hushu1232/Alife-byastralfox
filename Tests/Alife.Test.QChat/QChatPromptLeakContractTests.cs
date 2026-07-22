using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatPromptLeakContractTests
{
    [TestCase("\u5FC3\u7406\u72B6\u6001\uFF1A\u4FDD\u6301\u5B89\u9759\u89C2\u5BDF\u3002")]
    [TestCase("\u5185\u5FC3\uFF1A\u8FD9\u6BB5\u4E0D\u80FD\u53D1\u51FA\u53BB\u3002")]
    [TestCase("\u72B6\u6001\uFF1A\u5F85\u673A\u3002")]
    [TestCase("\uFF08\u4E0D\u56DE\u590D\uFF0C\u4FDD\u6301\u5B89\u9759\uFF09")]
    public void InternalStateTextDoesNotBecomePrivateVisibleReply(string text)
    {
        QChatVisibleReplyPolicy policy = new();

        QChatVisibleReplyResult result = policy.Normalize(
            text,
            QChatConversationKind.Private,
            shouldReply: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.ShouldSend, Is.False);
            Assert.That(result.Text, Is.Empty);
        });
    }

    [Test]
    public void GroupNoReplyStaysSilentInsteadOfLeakingInternalState()
    {
        QChatVisibleReplyPolicy policy = new();

        QChatVisibleReplyResult result = policy.Normalize(
            "心理状态：沉默旁观。",
            QChatConversationKind.Group,
            shouldReply: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.ShouldSend, Is.False);
            Assert.That(result.Text, Is.Empty);
            Assert.That(result.Text, Does.Not.Contain("心理状态"));
            Assert.That(result.Reason, Does.Contain("no-reply"));
        });
    }

    [Test]
    public void VisibleTechnicalStatusTextIsNotBlockedAsInternalPromptState()
    {
        QChatVisibleReplyPolicy policy = new();

        QChatVisibleReplyResult result = policy.Normalize(
            "\u72B6\u6001\u7801 500 \u8868\u793A\u670D\u52A1\u7AEF\u9519\u8BEF\u3002",
            QChatConversationKind.Private,
            shouldReply: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.ShouldSend, Is.True);
            Assert.That(result.Text, Is.EqualTo("\u72B6\u6001\u7801 500 \u8868\u793A\u670D\u52A1\u7AEF\u9519\u8BEF\u3002"));
        });
    }
}
