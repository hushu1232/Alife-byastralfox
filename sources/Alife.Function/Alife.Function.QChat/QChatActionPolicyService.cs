using System;
using Alife.Function.Agent;

namespace Alife.Function.QChat;

public sealed class QChatActionPolicyService(long ownerUserId)
{
    public AgentPermissionConfig CreateConfig()
    {
        AgentPermissionConfig config = new()
        {
            AllowGroupLowRisk = true,
            AllowGroupMediumRiskWhenMentioned = true,
            RequireConfirmationForHighRisk = true
        };

        if (ownerUserId != 0)
            config.OwnerUserIds.Add(ownerUserId);

        return config;
    }

    public AgentPermissionRequest CreateRequest(
        QChatAgentRoute route,
        AgentRiskLevel riskLevel,
        string action,
        bool isMentioned,
        bool hasExplicitConfirmation)
    {
        ArgumentNullException.ThrowIfNull(route);

        AgentRequestSource source = route.ConversationKind == QChatConversationKind.Group
            ? AgentRequestSource.GroupChat
            : AgentRequestSource.PrivateChat;

        return new AgentPermissionRequest(
            ActorUserId: route.SenderId,
            Source: source,
            IsMentioned: isMentioned,
            RiskLevel: riskLevel,
            HasExplicitConfirmation: hasExplicitConfirmation,
            Action: action);
    }
}
