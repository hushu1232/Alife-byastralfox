using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatOwnerEngineeringReplyFormatterTests
{
    [Test]
    public void FormatForXiayuOwnerRetainsFactsAndVerificationVerbatim()
    {
        QChatOwnerEngineeringReply reply = new(QChatOwnerEngineeringReplyStage.Hypothesis, "candidate=QZoneService.Report", "tests=not-run");
        string formatted = QChatOwnerEngineeringReplyFormatter.Format("xiayu", QChatSenderRole.Owner, reply);
        Assert.Multiple(() =>
        {
            Assert.That(formatted, Does.StartWith("术术，"));
            Assert.That(formatted, Does.Contain("candidate=QZoneService.Report"));
            Assert.That(formatted, Does.Contain("tests=not-run"));
        });
    }

    [Test]
    public void FormatBlockedReplyRetainsFailureWithoutCompleteLead()
    {
        QChatOwnerEngineeringReply reply = new(QChatOwnerEngineeringReplyStage.Blocked, "checked=qq-send-exit,file-runner", UncertaintyOrFailure: "missing_evidence=correlation-id");
        string formatted = QChatOwnerEngineeringReplyFormatter.Format("xiayu", QChatSenderRole.Owner, reply);
        Assert.Multiple(() =>
        {
            Assert.That(formatted, Does.Contain("missing_evidence=correlation-id"));
            Assert.That(formatted, Does.Not.Contain("已处理完"));
            Assert.That(formatted, Does.Not.Contain("完成了"));
        });
    }

    [Test]
    public void FormatCompleteReplyRetainsExactVerificationAndFailureText()
    {
        QChatOwnerEngineeringReply reply = new(QChatOwnerEngineeringReplyStage.Complete, "path=qchat-owner-event-dispatcher", "tests=5 passed, 0 failed", "live_validation=not-run");
        string formatted = QChatOwnerEngineeringReplyFormatter.Format("mixu", QChatSenderRole.Owner, reply);
        Assert.Multiple(() =>
        {
            Assert.That(formatted, Does.StartWith("主人，"));
            Assert.That(formatted, Does.Contain("tests=5 passed, 0 failed"));
            Assert.That(formatted, Does.Contain("live_validation=not-run"));
            Assert.That(formatted, Does.Not.Contain("术术"));
        });
    }

    [Test]
    public void FormatForNonOwnerUsesNeutralLead()
    {
        QChatOwnerEngineeringReply reply = new(QChatOwnerEngineeringReplyStage.Intake, "goal=remove-internal-label");
        string formatted = QChatOwnerEngineeringReplyFormatter.Format("xiayu", QChatSenderRole.PrivateGuest, reply);
        Assert.Multiple(() =>
        {
            Assert.That(formatted, Does.StartWith("工程状态如下。"));
            Assert.That(formatted, Does.Not.Contain("术术"));
            Assert.That(formatted, Does.Not.Contain("主人"));
        });
    }

    [Test]
    public void FormatReturnsEmptyWhenFactsAreBlank()
    {
        QChatOwnerEngineeringReply reply = new(QChatOwnerEngineeringReplyStage.Intake, "   ", "tests=not-run");
        Assert.That(QChatOwnerEngineeringReplyFormatter.Format("xiayu", QChatSenderRole.Owner, reply), Is.Empty);
    }
}
