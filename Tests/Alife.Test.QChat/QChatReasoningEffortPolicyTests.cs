using System.Reflection;
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatReasoningEffortPolicyTests
{
    [Test]
    public void OwnerAnalysisAndRootCauseRequestsSelectBoundedReasoningEffort()
    {
        Type? policyType = typeof(QChatService).Assembly.GetType(
            "Alife.Function.QChat.QChatReasoningEffortPolicy");
        Assert.That(policyType, Is.Not.Null, "QChat needs a scoped reasoning-effort policy.");
        MethodInfo? decide = policyType!.GetMethod(
            "Decide",
            [typeof(QChatSenderRole), typeof(string)]);
        Assert.That(decide, Is.Not.Null);

        string normal = (string)decide!.Invoke(null, [QChatSenderRole.Owner, "今天怎么样"] )!;
        string analysis = (string)decide.Invoke(null, [QChatSenderRole.Owner, "帮我分析一下这个调试问题"] )!;
        string rootCause = (string)decide.Invoke(null, [QChatSenderRole.Owner, "深度排查这个系统设计的根因"] )!;
        string guest = (string)decide.Invoke(null, [QChatSenderRole.PrivateGuest, "深度排查这个系统设计的根因"] )!;

        Assert.Multiple(() =>
        {
            Assert.That(normal, Is.EqualTo("low"));
            Assert.That(analysis, Is.EqualTo("medium"));
            Assert.That(rootCause, Is.EqualTo("high"));
            Assert.That(guest, Is.EqualTo("low"));
        });
    }
}
