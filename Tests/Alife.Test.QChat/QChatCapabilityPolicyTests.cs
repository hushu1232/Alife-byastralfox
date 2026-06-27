using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatCapabilityPolicyTests
{
    [Test]
    public void NormalChatDoesNotRequireOwner()
    {
        QChatCapabilityDecision decision = QChatCapabilityPolicy.Evaluate(new QChatCapabilityContext(
            Capability: QChatCapability.NormalChat,
            SenderRole: QChatSenderRole.GroupMember,
            AgentId: "mixu"));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Allowed, Is.True);
            Assert.That(decision.Reason, Is.EqualTo("allowed"));
            Assert.That(decision.RiskLevel, Is.EqualTo(QChatCapabilityRiskLevel.Low));
            Assert.That(decision.RequiresOwnerEventOutbox, Is.False);
            Assert.That(decision.RequiresOwnerApproval, Is.False);
        });
    }

    [Test]
    public void OwnerDiagnosticsRequiresOwner()
    {
        QChatCapabilityDecision ownerDecision = QChatCapabilityPolicy.Evaluate(new QChatCapabilityContext(
            Capability: QChatCapability.OwnerDiagnostics,
            SenderRole: QChatSenderRole.Owner,
            AgentId: "xiayu"));
        QChatCapabilityDecision guestDecision = QChatCapabilityPolicy.Evaluate(new QChatCapabilityContext(
            Capability: QChatCapability.OwnerDiagnostics,
            SenderRole: QChatSenderRole.PrivateGuest,
            AgentId: "xiayu"));

        Assert.Multiple(() =>
        {
            Assert.That(ownerDecision.Allowed, Is.True);
            Assert.That(ownerDecision.RiskLevel, Is.EqualTo(QChatCapabilityRiskLevel.Low));
            Assert.That(guestDecision.Allowed, Is.False);
            Assert.That(guestDecision.Reason, Is.EqualTo("owner_required"));
        });
    }

    [Test]
    public void GroupFileUploadRequiresOwnerAndXiaYu()
    {
        QChatCapabilityDecision memberDecision = QChatCapabilityPolicy.Evaluate(new QChatCapabilityContext(
            Capability: QChatCapability.GroupFileUpload,
            SenderRole: QChatSenderRole.GroupMember,
            AgentId: "xiayu"));
        QChatCapabilityDecision ownerDecision = QChatCapabilityPolicy.Evaluate(new QChatCapabilityContext(
            Capability: QChatCapability.GroupFileUpload,
            SenderRole: QChatSenderRole.Owner,
            AgentId: "xiayu"));
        QChatCapabilityDecision mixuDecision = QChatCapabilityPolicy.Evaluate(new QChatCapabilityContext(
            Capability: QChatCapability.GroupFileUpload,
            SenderRole: QChatSenderRole.Owner,
            AgentId: "mixu"));

        Assert.Multiple(() =>
        {
            Assert.That(memberDecision.Allowed, Is.False);
            Assert.That(memberDecision.Reason, Is.EqualTo("owner_required"));
            Assert.That(memberDecision.RiskLevel, Is.EqualTo(QChatCapabilityRiskLevel.High));
            Assert.That(ownerDecision.Allowed, Is.True);
            Assert.That(mixuDecision.Allowed, Is.False);
            Assert.That(mixuDecision.Reason, Is.EqualTo("agent_not_allowed"));
        });
    }

    [Test]
    public void DesktopBusinessTaskRequiresOwnerAndXiaYu()
    {
        QChatCapabilityDecision allowed = QChatCapabilityPolicy.Evaluate(new QChatCapabilityContext(
            Capability: QChatCapability.DesktopBusinessTask,
            SenderRole: QChatSenderRole.Owner,
            AgentId: "xiayu"));
        QChatCapabilityDecision nonOwner = QChatCapabilityPolicy.Evaluate(new QChatCapabilityContext(
            Capability: QChatCapability.DesktopBusinessTask,
            SenderRole: QChatSenderRole.GroupMember,
            AgentId: "xiayu"));
        QChatCapabilityDecision mixu = QChatCapabilityPolicy.Evaluate(new QChatCapabilityContext(
            Capability: QChatCapability.DesktopBusinessTask,
            SenderRole: QChatSenderRole.Owner,
            AgentId: "mixu"));

        Assert.Multiple(() =>
        {
            Assert.That(allowed.Allowed, Is.True);
            Assert.That(allowed.RiskLevel, Is.EqualTo(QChatCapabilityRiskLevel.Critical));
            Assert.That(allowed.RequiresOwnerEventOutbox, Is.True);
            Assert.That(nonOwner.Allowed, Is.False);
            Assert.That(nonOwner.Reason, Is.EqualTo("owner_required"));
            Assert.That(mixu.Allowed, Is.False);
            Assert.That(mixu.Reason, Is.EqualTo("agent_not_allowed"));
        });
    }

    [Test]
    public void RiskFriendDeleteRequiresXiaYuAndProtectsOwnerAndProtectedUsers()
    {
        QChatCapabilityDecision allowed = QChatCapabilityPolicy.Evaluate(new QChatCapabilityContext(
            Capability: QChatCapability.RiskFriendDelete,
            SenderRole: QChatSenderRole.Owner,
            AgentId: "xiayu",
            UserId: 2001,
            BotId: 2905391496,
            OwnerId: 1001,
            ProtectedUserIds: ""));
        QChatCapabilityDecision owner = QChatCapabilityPolicy.Evaluate(new QChatCapabilityContext(
            Capability: QChatCapability.RiskFriendDelete,
            SenderRole: QChatSenderRole.Owner,
            AgentId: "xiayu",
            UserId: 1001,
            BotId: 2905391496,
            OwnerId: 1001,
            ProtectedUserIds: ""));
        QChatCapabilityDecision protectedUser = QChatCapabilityPolicy.Evaluate(new QChatCapabilityContext(
            Capability: QChatCapability.RiskFriendDelete,
            SenderRole: QChatSenderRole.Owner,
            AgentId: "xiayu",
            UserId: 2001,
            BotId: 2905391496,
            OwnerId: 1001,
            ProtectedUserIds: "2001"));
        QChatCapabilityDecision mixu = QChatCapabilityPolicy.Evaluate(new QChatCapabilityContext(
            Capability: QChatCapability.RiskFriendDelete,
            SenderRole: QChatSenderRole.Owner,
            AgentId: "mixu",
            UserId: 2001,
            BotId: 3340947887,
            OwnerId: 1001,
            ProtectedUserIds: ""));

        Assert.Multiple(() =>
        {
            Assert.That(allowed.Allowed, Is.True);
            Assert.That(allowed.RiskLevel, Is.EqualTo(QChatCapabilityRiskLevel.Critical));
            Assert.That(allowed.RequiresOwnerEventOutbox, Is.True);
            Assert.That(owner.Allowed, Is.False);
            Assert.That(owner.Reason, Is.EqualTo("owner_protected"));
            Assert.That(protectedUser.Allowed, Is.False);
            Assert.That(protectedUser.Reason, Is.EqualTo("protected_user"));
            Assert.That(mixu.Allowed, Is.False);
            Assert.That(mixu.Reason, Is.EqualTo("agent_not_allowed"));
        });
    }
}
