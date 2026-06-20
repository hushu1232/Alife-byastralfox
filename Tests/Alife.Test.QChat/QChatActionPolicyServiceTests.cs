using System;
using Alife.Function.Agent;
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public class QChatActionPolicyServiceTests
{
    const long OwnerUserId = 3045846738;
    const long BotAccountId = 2905391496;
    const string AgentId = "xiayu";

    [Test]
    public void CreateConfigIncludesOwnerWhenConfigured()
    {
        QChatActionPolicyService service = new(OwnerUserId);

        AgentPermissionConfig config = service.CreateConfig();

        Assert.Multiple(() =>
        {
            Assert.That(config.OwnerUserIds, Is.EquivalentTo(new[] { OwnerUserId }));
            Assert.That(config.AllowGroupLowRisk, Is.True);
            Assert.That(config.AllowGroupMediumRiskWhenMentioned, Is.True);
            Assert.That(config.RequireConfirmationForHighRisk, Is.True);
        });
    }

    [Test]
    public void CreateConfigLeavesOwnersEmptyWhenOwnerIsUnset()
    {
        QChatActionPolicyService service = new(0);

        AgentPermissionConfig config = service.CreateConfig();

        Assert.That(config.OwnerUserIds, Is.Empty);
    }

    [Test]
    public void CreateConfigReturnsFreshMutableConfigEachCall()
    {
        QChatActionPolicyService service = new(OwnerUserId);

        AgentPermissionConfig first = service.CreateConfig();
        first.OwnerUserIds.Add(123456);
        first.AllowGroupLowRisk = false;
        first.AllowGroupMediumRiskWhenMentioned = false;
        first.RequireConfirmationForHighRisk = false;

        AgentPermissionConfig second = service.CreateConfig();

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second.OwnerUserIds, Is.Not.SameAs(first.OwnerUserIds));
            Assert.That(second.OwnerUserIds, Is.EquivalentTo(new[] { OwnerUserId }));
            Assert.That(second.AllowGroupLowRisk, Is.True);
            Assert.That(second.AllowGroupMediumRiskWhenMentioned, Is.True);
            Assert.That(second.RequireConfirmationForHighRisk, Is.True);
        });
    }

    [Test]
    public void CreateRequestMapsPrivateRouteAndPreservesRequestDetails()
    {
        QChatActionPolicyService service = new(OwnerUserId);
        QChatAgentRoute route = PrivateRoute(senderId: OwnerUserId);

        AgentPermissionRequest request = service.CreateRequest(
            route,
            AgentRiskLevel.Medium,
            "memory.write",
            isMentioned: false,
            hasExplicitConfirmation: true);

        Assert.Multiple(() =>
        {
            Assert.That(request.ActorUserId, Is.EqualTo(OwnerUserId));
            Assert.That(request.Source, Is.EqualTo(AgentRequestSource.PrivateChat));
            Assert.That(request.RiskLevel, Is.EqualTo(AgentRiskLevel.Medium));
            Assert.That(request.Action, Is.EqualTo("memory.write"));
            Assert.That(request.IsMentioned, Is.False);
            Assert.That(request.HasExplicitConfirmation, Is.True);
        });
    }

    [Test]
    public void CreateRequestMapsGroupRouteAndPreservesSender()
    {
        QChatActionPolicyService service = new(OwnerUserId);
        QChatAgentRoute route = GroupRoute(senderId: 111111, groupId: 987654);

        AgentPermissionRequest request = service.CreateRequest(
            route,
            AgentRiskLevel.Low,
            "qq.message",
            isMentioned: true,
            hasExplicitConfirmation: false);

        Assert.Multiple(() =>
        {
            Assert.That(request.ActorUserId, Is.EqualTo(111111));
            Assert.That(request.Source, Is.EqualTo(AgentRequestSource.GroupChat));
            Assert.That(request.RiskLevel, Is.EqualTo(AgentRiskLevel.Low));
            Assert.That(request.Action, Is.EqualTo("qq.message"));
            Assert.That(request.IsMentioned, Is.True);
            Assert.That(request.HasExplicitConfirmation, Is.False);
        });
    }

    [Test]
    public void CreateRequestThrowsWhenRouteIsNull()
    {
        QChatActionPolicyService service = new(OwnerUserId);

        Assert.Throws<ArgumentNullException>(() => service.CreateRequest(
            null!,
            AgentRiskLevel.Low,
            "qq.message",
            isMentioned: false,
            hasExplicitConfirmation: false));
    }

    [Test]
    public void OwnerPrivateMediumRiskRequestEvaluatesAllowed()
    {
        QChatActionPolicyService service = new(OwnerUserId);
        AgentPermissionPolicy policy = new(service.CreateConfig());

        AgentPermissionDecision decision = policy.Evaluate(service.CreateRequest(
            PrivateRoute(senderId: OwnerUserId),
            AgentRiskLevel.Medium,
            "memory.write",
            isMentioned: false,
            hasExplicitConfirmation: false));

        Assert.That(decision.Allowed, Is.True);
    }

    [Test]
    public void NonOwnerGroupHighRiskDeleteRequestEvaluatesDeniedBeforeOwnerApproval()
    {
        QChatActionPolicyService service = new(OwnerUserId);
        AgentPermissionPolicy policy = new(service.CreateConfig());

        AgentPermissionDecision decision = policy.Evaluate(service.CreateRequest(
            GroupRoute(senderId: 111111, groupId: 987654),
            AgentRiskLevel.High,
            "file.delete",
            isMentioned: true,
            hasExplicitConfirmation: true));

        Assert.That(decision.Allowed, Is.False);
    }

    [Test]
    public void NonOwnerGroupMediumRiskRequestWithMentionEvaluatesAllowed()
    {
        QChatActionPolicyService service = new(OwnerUserId);
        AgentPermissionPolicy policy = new(service.CreateConfig());

        AgentPermissionDecision decision = policy.Evaluate(service.CreateRequest(
            GroupRoute(senderId: 111111, groupId: 987654),
            AgentRiskLevel.Medium,
            "memory.write",
            isMentioned: true,
            hasExplicitConfirmation: false));

        Assert.That(decision.Allowed, Is.True);
    }

    [Test]
    public void OwnerPrivateHighRiskRequestEvaluatesAllowed()
    {
        QChatActionPolicyService service = new(OwnerUserId);
        AgentPermissionPolicy policy = new(service.CreateConfig());

        AgentPermissionDecision decision = policy.Evaluate(service.CreateRequest(
            PrivateRoute(senderId: OwnerUserId),
            AgentRiskLevel.High,
            "workspace.delete",
            isMentioned: false,
            hasExplicitConfirmation: false));

        Assert.That(decision.Allowed, Is.True);
    }

    static QChatAgentRoute PrivateRoute(long senderId) => new(
        AgentId,
        BotAccountId,
        QChatConversationKind.Private,
        senderId,
        senderId,
        senderId == OwnerUserId,
        $"qq:{AgentId}:{BotAccountId}:private:{senderId}");

    static QChatAgentRoute GroupRoute(long senderId, long groupId) => new(
        AgentId,
        BotAccountId,
        QChatConversationKind.Group,
        groupId,
        senderId,
        senderId == OwnerUserId,
        $"qq:{AgentId}:{BotAccountId}:group:{groupId}");
}
