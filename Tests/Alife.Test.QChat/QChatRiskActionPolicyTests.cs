using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatRiskActionPolicyTests
{
    [Test]
    public void EligibleHighRiskUserCanBeAutoDeletedWhenEnabledForXiaYu()
    {
        QChatFriendDeleteDecision decision = QChatRiskActionPolicy.EvaluateFriendDelete(new QChatFriendDeleteContext(
            EnableAutoFriendDelete: true,
            AgentId: "xiayu",
            UserId: 2001,
            BotId: 999,
            OwnerId: 1001,
            AllowedPrivateUserIds: "",
            ProtectedUserIds: "",
            QuietModeWakeUserIds: "",
            Score: 170,
            EventCount: 3,
            MinutesBetweenFirstAndLastRisk: 15,
            DailyDeleteCount: 0,
            DailyDeleteLimit: 5,
            CooldownActive: false,
            Threshold: 160));

        Assert.That(decision.CanDelete, Is.True);
    }

    [Test]
    public void ProtectedUserCannotBeAutoDeleted()
    {
        QChatFriendDeleteDecision decision = QChatRiskActionPolicy.EvaluateFriendDelete(new QChatFriendDeleteContext(
            EnableAutoFriendDelete: true,
            AgentId: "xiayu",
            UserId: 2001,
            BotId: 999,
            OwnerId: 1001,
            AllowedPrivateUserIds: "",
            ProtectedUserIds: "2001",
            QuietModeWakeUserIds: "",
            Score: 220,
            EventCount: 5,
            MinutesBetweenFirstAndLastRisk: 30,
            DailyDeleteCount: 0,
            DailyDeleteLimit: 5,
            CooldownActive: false,
            Threshold: 160));

        Assert.That(decision.CanDelete, Is.False);
        Assert.That(decision.Reason, Is.EqualTo("protected_user"));
    }

    [Test]
    public void OwnerCannotBeAutoDeleted()
    {
        QChatFriendDeleteDecision decision = QChatRiskActionPolicy.EvaluateFriendDelete(new QChatFriendDeleteContext(
            EnableAutoFriendDelete: true,
            AgentId: "xiayu",
            UserId: 1001,
            BotId: 999,
            OwnerId: 1001,
            AllowedPrivateUserIds: "",
            ProtectedUserIds: "",
            QuietModeWakeUserIds: "",
            Score: 220,
            EventCount: 5,
            MinutesBetweenFirstAndLastRisk: 30,
            DailyDeleteCount: 0,
            DailyDeleteLimit: 5,
            CooldownActive: false,
            Threshold: 160));

        Assert.That(decision.CanDelete, Is.False);
        Assert.That(decision.Reason, Is.EqualTo("owner_protected"));
    }

    [Test]
    public void NonXiaYuAgentCannotAutoDelete()
    {
        QChatFriendDeleteDecision decision = QChatRiskActionPolicy.EvaluateFriendDelete(new QChatFriendDeleteContext(
            EnableAutoFriendDelete: true,
            AgentId: "mixu",
            UserId: 2001,
            BotId: 999,
            OwnerId: 1001,
            AllowedPrivateUserIds: "",
            ProtectedUserIds: "",
            QuietModeWakeUserIds: "",
            Score: 220,
            EventCount: 5,
            MinutesBetweenFirstAndLastRisk: 30,
            DailyDeleteCount: 0,
            DailyDeleteLimit: 5,
            CooldownActive: false,
            Threshold: 160));

        Assert.That(decision.CanDelete, Is.False);
        Assert.That(decision.Reason, Is.EqualTo("agent_not_allowed"));
    }

    [Test]
    public async Task NoopFriendActionGatewayDoesNotDelete()
    {
        QChatNoopFriendActionGateway gateway = new();

        QChatFriendDeleteResult result = await gateway.DeleteFriendAsync(2001);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Message, Is.EqualTo("friend_delete_gateway=not_enabled"));
    }
}
