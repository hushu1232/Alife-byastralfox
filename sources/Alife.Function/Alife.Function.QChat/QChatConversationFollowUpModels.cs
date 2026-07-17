using System;

namespace Alife.Function.QChat;

public enum QChatFollowUpIntent
{
    None,
    WarmCoda,
    PracticalAddendum,
    EmotionalAfterthought,
    DoNotInterrupt
}

public sealed record QChatFollowUpPresence(QChatFollowUpIntent Intent)
{
    public static QChatFollowUpPresence None { get; } = new(QChatFollowUpIntent.None);
    public static QChatFollowUpPresence DoNotInterrupt { get; } = new(QChatFollowUpIntent.DoNotInterrupt);
}

public sealed record QChatFollowUpPresenceContext(
    string AgentId,
    OneBotMessageType MessageType,
    QChatSenderRole SenderRole,
    string SourceText,
    string ReplyText,
    bool IsRiskConversation,
    bool IsDeterministicTask,
    bool HasPendingMedia,
    bool IsQuiet,
    bool ModelReplyWasBlocked,
    bool IsTimerState,
    bool IsHighConversationPressure)
{
    public bool IsOwnerPrivate => MessageType == OneBotMessageType.Private && SenderRole == QChatSenderRole.Owner;
}

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
