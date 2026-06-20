using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatBlocklistPolicyTests
{
    [Test]
    public void ExplicitBlockedUserIsBlocked()
    {
        QChatBlockDecision decision = QChatBlocklistPolicy.Evaluate(new QChatBlockContext(
            UserId: 2001,
            BotId: 999,
            OwnerId: 1001,
            GroupId: null,
            BlockedPrivateUserIds: "2001",
            BlockedGroupIds: "",
            IsLocallyBlocked: false));

        Assert.That(decision.IsBlocked, Is.True);
        Assert.That(decision.Reason, Is.EqualTo("blocked_private_user"));
    }

    [Test]
    public void OwnerIsNeverBlocked()
    {
        QChatBlockDecision decision = QChatBlocklistPolicy.Evaluate(new QChatBlockContext(
            UserId: 1001,
            BotId: 999,
            OwnerId: 1001,
            GroupId: null,
            BlockedPrivateUserIds: "1001",
            BlockedGroupIds: "",
            IsLocallyBlocked: true));

        Assert.That(decision.IsBlocked, Is.False);
    }

    [Test]
    public void RiskLocalBlockBlocksNonOwner()
    {
        QChatBlockDecision decision = QChatBlocklistPolicy.Evaluate(new QChatBlockContext(
            UserId: 2001,
            BotId: 999,
            OwnerId: 1001,
            GroupId: null,
            BlockedPrivateUserIds: "",
            BlockedGroupIds: "",
            IsLocallyBlocked: true));

        Assert.That(decision.IsBlocked, Is.True);
        Assert.That(decision.Reason, Is.EqualTo("risk_local_block"));
    }
}
