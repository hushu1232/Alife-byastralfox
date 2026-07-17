using System;

namespace Alife.Function.QChat;

public readonly record struct QChatFollowUpSessionKey(string Value)
{
    public static QChatFollowUpSessionKey Create(string agentId, long botId, long peerUserId)
    {
        string normalizedAgentId = string.IsNullOrWhiteSpace(agentId)
            ? "default"
            : agentId.Trim().ToLowerInvariant();
        return new QChatFollowUpSessionKey($"qq:{normalizedAgentId}:{botId}:private:{peerUserId}");
    }
}

public sealed record QChatFollowUpSettings(
    bool Enabled,
    bool OwnerPrivateOnly,
    bool AllowGroups,
    TimeSpan DelayMin,
    TimeSpan DelayMax,
    int MaxFollowUpsPerTurn,
    TimeSpan SessionCooldown,
    int DailyLimitPerSession)
{
    public bool CanSchedule => Enabled && MaxFollowUpsPerTurn > 0 && DailyLimitPerSession > 0;

    public static QChatFollowUpSettings From(QChatConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        int minimumDelaySeconds = Math.Max(1, config.FollowUpDelayMinSeconds);
        int maximumDelaySeconds = Math.Max(minimumDelaySeconds, config.FollowUpDelayMaxSeconds);
        return new QChatFollowUpSettings(
            config.EnableConversationFollowUp,
            config.ConversationFollowUpOwnerPrivateOnly,
            config.AllowConversationFollowUpInGroups,
            TimeSpan.FromSeconds(minimumDelaySeconds),
            TimeSpan.FromSeconds(maximumDelaySeconds),
            Math.Max(0, config.MaxFollowUpsPerTurn),
            TimeSpan.FromMinutes(Math.Max(0, config.FollowUpSessionCooldownMinutes)),
            Math.Max(0, config.FollowUpDailyLimitPerSession));
    }
}
