using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatInternetCapabilityPolicyTests
{
    [Test]
    public void InternetLookup_AllowsOwnerOnAllowedAgent()
    {
        QChatCapabilityDecision decision = QChatCapabilityPolicy.Evaluate(new QChatCapabilityContext(
            QChatCapability.InternetLookup,
            QChatSenderRole.Owner,
            AgentId: "xiayu",
            UserId: 3045846738,
            BotId: 2905391496,
            OwnerId: 3045846738));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Allowed, Is.True);
            Assert.That(decision.RiskLevel, Is.EqualTo(QChatCapabilityRiskLevel.Medium));
            Assert.That(decision.RequiresOwnerApproval, Is.False);
            Assert.That(decision.RequiresOwnerEventOutbox, Is.False);
        });
    }

    [Test]
    public void InternetLookup_DeniesNonOwner()
    {
        QChatCapabilityDecision decision = QChatCapabilityPolicy.Evaluate(new QChatCapabilityContext(
            QChatCapability.InternetLookup,
            QChatSenderRole.GroupMember,
            AgentId: "xiayu",
            UserId: 2002,
            BotId: 2905391496,
            OwnerId: 3045846738));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Allowed, Is.False);
            Assert.That(decision.Reason, Is.EqualTo("owner_required"));
        });
    }

    [Test]
    public void InternetLookup_DeniesNonAllowedAgent()
    {
        QChatCapabilityDecision decision = QChatCapabilityPolicy.Evaluate(new QChatCapabilityContext(
            QChatCapability.InternetLookup,
            QChatSenderRole.Owner,
            AgentId: "mixu",
            UserId: 3045846738,
            BotId: 3340947887,
            OwnerId: 3045846738,
            AllowedAgentIds: "xiayu"));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Allowed, Is.False);
            Assert.That(decision.Reason, Is.EqualTo("agent_not_allowed"));
        });
    }
}
