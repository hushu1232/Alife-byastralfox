using System;
using System.Linq;

namespace Alife.Function.QChat;

public sealed record QChatFriendDeleteContext(
    bool EnableAutoFriendDelete,
    string AgentId,
    long UserId,
    long BotId,
    long OwnerId,
    string AllowedPrivateUserIds,
    string ProtectedUserIds,
    string QuietModeWakeUserIds,
    int Score,
    int EventCount,
    int MinutesBetweenFirstAndLastRisk,
    int DailyDeleteCount,
    int DailyDeleteLimit,
    bool CooldownActive,
    int Threshold,
    int MinIndependentEvents = 2,
    int MinObservationMinutes = 10,
    string DeleteAllowedAgentIds = "xiayu");

public sealed record QChatFriendDeleteDecision(
    bool CanDelete,
    string Reason,
    QChatCapabilityRiskLevel RiskLevel = QChatCapabilityRiskLevel.Critical,
    bool RequiresOwnerEventOutbox = true);

public static class QChatRiskActionPolicy
{
    public static QChatFriendDeleteDecision EvaluateFriendDelete(QChatFriendDeleteContext context)
    {
        QChatCapabilityDecision capabilityDecision = QChatCapabilityPolicy.Evaluate(new QChatCapabilityContext(
            Capability: QChatCapability.RiskFriendDelete,
            SenderRole: QChatSenderRole.Owner,
            AgentId: context.AgentId,
            UserId: context.UserId,
            BotId: context.BotId,
            OwnerId: context.OwnerId,
            ProtectedUserIds: context.ProtectedUserIds,
            AllowedAgentIds: context.DeleteAllowedAgentIds));

        QChatFriendDeleteDecision FromCapability(bool canDelete, string reason)
        {
            return new QChatFriendDeleteDecision(
                canDelete,
                reason,
                capabilityDecision.RiskLevel,
                capabilityDecision.RequiresOwnerEventOutbox);
        }

        if (context.EnableAutoFriendDelete == false)
            return FromCapability(false, "auto_delete_disabled");
        if (capabilityDecision.Allowed == false &&
            capabilityDecision.Reason is "owner_protected" or "bot_protected" or "agent_not_allowed")
        {
            return FromCapability(false, capabilityDecision.Reason);
        }
        if (ContainsId(context.AllowedPrivateUserIds, context.UserId))
            return FromCapability(false, "allowed_private_user");
        if (capabilityDecision.Allowed == false)
            return FromCapability(false, capabilityDecision.Reason);
        if (ContainsId(context.QuietModeWakeUserIds, context.UserId))
            return FromCapability(false, "quiet_mode_wake_user");
        if (context.Score < context.Threshold)
            return FromCapability(false, "below_threshold");
        if (context.EventCount < context.MinIndependentEvents)
            return FromCapability(false, "insufficient_events");
        if (context.MinutesBetweenFirstAndLastRisk < context.MinObservationMinutes)
            return FromCapability(false, "observation_window");
        if (context.CooldownActive)
            return FromCapability(false, "cooldown_active");
        if (context.DailyDeleteLimit <= 0 || context.DailyDeleteCount >= context.DailyDeleteLimit)
            return FromCapability(false, "daily_limit_reached");

        return FromCapability(true, "eligible");
    }

    static bool ContainsId(string? csv, long id)
    {
        return (csv ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(item => long.TryParse(item, out long parsed) && parsed == id);
    }

}
