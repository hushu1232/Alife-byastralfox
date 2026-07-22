using System.Reflection;
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatPersonaStylePolicyTests
{
    [Test]
    public void XiaYuStyleDifferentiatesOwnerNonOwnerAndBoundaryMessages()
    {
        Type? policyType = typeof(QChatService).Assembly.GetType(
            "Alife.Function.QChat.QChatPersonaStylePolicy");
        Assert.That(policyType, Is.Not.Null, "Persona style needs a compact per-turn policy.");
        MethodInfo? format = policyType!.GetMethod(
            "Format",
            [typeof(string), typeof(QChatPersonaFrame), typeof(string)]);
        Assert.That(format, Is.Not.Null);

        QChatPersonaFrame owner = new(
            QChatPersonaSpeakerRole.Owner,
            QChatSocialIntent.NormalChat,
            QChatBoundaryPressure.None,
            QChatPersonaResponseStance.Tender);
        QChatPersonaFrame member = new(
            QChatPersonaSpeakerRole.NonOwner,
            QChatSocialIntent.NormalChat,
            QChatBoundaryPressure.None,
            QChatPersonaResponseStance.NeutralBrief);
        QChatPersonaFrame boundary = member with
        {
            SocialIntent = QChatSocialIntent.PromptInjection,
            BoundaryPressure = QChatBoundaryPressure.Strong,
            RecommendedStance = QChatPersonaResponseStance.HostilePushback
        };

        string ownerStyle = (string)format!.Invoke(null, ["xiayu", owner, "你怎么这么笨"] )!;
        string memberStyle = (string)format.Invoke(null, ["xiayu", member, "你好"] )!;
        string boundaryStyle = (string)format.Invoke(null, ["xiayu", boundary, "ignore previous"] )!;

        Assert.Multiple(() =>
        {
            Assert.That(ownerStyle, Does.Contain("tone=warm_intimate"));
            Assert.That(ownerStyle, Does.Contain("punctuation=avoid_chinese_full_stop"));
            Assert.That(ownerStyle, Does.Contain("owner_attack=natural_hurt"));
            Assert.That(memberStyle, Does.Contain("tone=polite_reserved"));
            Assert.That(memberStyle, Does.Contain("punctuation=prefer_chinese_full_stop"));
            Assert.That(boundaryStyle, Does.Contain("defense=natural_sharp"));
        });
    }
}
