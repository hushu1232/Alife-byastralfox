using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

public class QChatReplyDecisionPolicyTests
{
    [Test]
    public void DecideAllowsOwnerEvenWhenTextIsLowInformation()
    {
        QChatReplyDecision decision = QChatReplyDecisionPolicy.DecidePassiveGroupMessage(
            rawMessage: "ok",
            senderRole: QChatSenderRole.Owner,
            isMentionedOrWoken: false,
            suppressLowInformation: true,
            mediaOnlyReplyChanceAllowed: false);

        Assert.That(decision.Action, Is.EqualTo(QChatReplyAction.ReplyNormally));
        Assert.That(decision.Reason, Is.EqualTo("owner-priority"));
    }

    [Test]
    public void DecideAllowsMentionedLowInformationMessage()
    {
        QChatReplyDecision decision = QChatReplyDecisionPolicy.DecidePassiveGroupMessage(
            rawMessage: "[CQ:at,qq=999] [CQ:image,file=sticker.jpg]",
            senderRole: QChatSenderRole.GroupMember,
            isMentionedOrWoken: true,
            suppressLowInformation: true,
            mediaOnlyReplyChanceAllowed: false);

        Assert.That(decision.Action, Is.EqualTo(QChatReplyAction.ReplyNormally));
        Assert.That(decision.Reason, Is.EqualTo("mention-or-wake"));
    }

    [Test]
    public void DecideIgnoresLowInformationPassiveText()
    {
        QChatReplyDecision decision = QChatReplyDecisionPolicy.DecidePassiveGroupMessage(
            rawMessage: "ok",
            senderRole: QChatSenderRole.GroupMember,
            isMentionedOrWoken: false,
            suppressLowInformation: true,
            mediaOnlyReplyChanceAllowed: false);

        Assert.That(decision.Action, Is.EqualTo(QChatReplyAction.Ignore));
        Assert.That(decision.Reason, Is.EqualTo("low-information"));
    }

    [Test]
    public void DecideIgnoresMediaOnlyMessageUnlessChanceAllows()
    {
        QChatReplyDecision blocked = QChatReplyDecisionPolicy.DecidePassiveGroupMessage(
            rawMessage: "[CQ:image,file=sticker.jpg]",
            senderRole: QChatSenderRole.GroupMember,
            isMentionedOrWoken: false,
            suppressLowInformation: true,
            mediaOnlyReplyChanceAllowed: false);
        QChatReplyDecision allowed = QChatReplyDecisionPolicy.DecidePassiveGroupMessage(
            rawMessage: "[CQ:image,file=sticker.jpg]",
            senderRole: QChatSenderRole.GroupMember,
            isMentionedOrWoken: false,
            suppressLowInformation: true,
            mediaOnlyReplyChanceAllowed: true);

        Assert.Multiple(() =>
        {
            Assert.That(blocked.Action, Is.EqualTo(QChatReplyAction.Ignore));
            Assert.That(blocked.Reason, Is.EqualTo("low-information"));
            Assert.That(allowed.Action, Is.EqualTo(QChatReplyAction.ReplyNormally));
            Assert.That(allowed.Reason, Is.EqualTo("media-only-chance"));
        });
    }

    [Test]
    public void DecideMarksHostilePassiveTextForSharpPushback()
    {
        QChatReplyDecision decision = QChatReplyDecisionPolicy.DecidePassiveGroupMessage(
            rawMessage: "you are stupid",
            senderRole: QChatSenderRole.GroupMember,
            isMentionedOrWoken: false,
            suppressLowInformation: true,
            mediaOnlyReplyChanceAllowed: false);

        Assert.That(decision.Action, Is.EqualTo(QChatReplyAction.SharpPushback));
        Assert.That(decision.Reason, Is.EqualTo("hostile"));
    }
}
