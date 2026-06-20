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

public sealed record QChatFriendDeleteDecision(bool CanDelete, string Reason);

public static class QChatRiskActionPolicy
{
    public static QChatFriendDeleteDecision EvaluateFriendDelete(QChatFriendDeleteContext context)
    {
        if (context.EnableAutoFriendDelete == false)
            return new QChatFriendDeleteDecision(false, "auto_delete_disabled");
        if (context.UserId == context.OwnerId)
            return new QChatFriendDeleteDecision(false, "owner_protected");
        if (context.UserId == context.BotId)
            return new QChatFriendDeleteDecision(false, "bot_protected");
        if (ContainsToken(context.DeleteAllowedAgentIds, context.AgentId) == false)
            return new QChatFriendDeleteDecision(false, "agent_not_allowed");
        if (ContainsId(context.AllowedPrivateUserIds, context.UserId))
            return new QChatFriendDeleteDecision(false, "allowed_private_user");
        if (ContainsId(context.ProtectedUserIds, context.UserId))
            return new QChatFriendDeleteDecision(false, "protected_user");
        if (ContainsId(context.QuietModeWakeUserIds, context.UserId))
            return new QChatFriendDeleteDecision(false, "quiet_mode_wake_user");
        if (context.Score < context.Threshold)
            return new QChatFriendDeleteDecision(false, "below_threshold");
        if (context.EventCount < context.MinIndependentEvents)
            return new QChatFriendDeleteDecision(false, "insufficient_events");
        if (context.MinutesBetweenFirstAndLastRisk < context.MinObservationMinutes)
            return new QChatFriendDeleteDecision(false, "observation_window");
        if (context.CooldownActive)
            return new QChatFriendDeleteDecision(false, "cooldown_active");
        if (context.DailyDeleteLimit <= 0 || context.DailyDeleteCount >= context.DailyDeleteLimit)
            return new QChatFriendDeleteDecision(false, "daily_limit_reached");

        return new QChatFriendDeleteDecision(true, "eligible");
    }

    static bool ContainsId(string? csv, long id)
    {
        return (csv ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(item => long.TryParse(item, out long parsed) && parsed == id);
    }

    static bool ContainsToken(string? csv, string value)
    {
        string normalized = (value ?? "").Trim();
        if (normalized.Length == 0)
            return false;

        return (csv ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(item => string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase));
    }
}
