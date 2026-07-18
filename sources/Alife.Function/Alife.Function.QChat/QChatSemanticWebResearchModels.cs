using System;
using Alife.Function.Agent;

namespace Alife.Function.QChat;

public enum QChatSemanticWebResearchDepth
{
    Quick,
    Standard,
    Deep
}

public enum QChatSemanticWebResearchReasonCategory
{
    Temporal,
    Verification,
    Niche,
    Explicit,
    Stable,
    Creative,
    Companion,
    Unknown
}

public sealed class QChatSemanticWebResearchConfig
{
    public bool Enabled { get; set; }
    public AgentMultiSourceSearchConfig MultiSourceSearch { get; set; } = new();
    public bool EnableOwnerPrivate { get; set; } = true;
    public bool EnableMentionedGroup { get; set; } = true;
    public bool ResearchOnUncertainty { get; set; } = true;
    public int RouterTimeoutMilliseconds { get; set; } = 900;
    public int FeedbackDelayMilliseconds { get; set; } = 1200;
    public int QuickMaxSources { get; set; } = 3;
    public int StandardMaxSources { get; set; } = 3;
    public int DeepMaxSources { get; set; } = 5;
    public int SessionCacheSeconds { get; set; } = 120;
}

public sealed record QChatSemanticWebResearchRequest(
    string AgentId,
    OneBotMessageEvent MessageEvent,
    QChatSenderRole SenderRole,
    bool IsMentionedOrWoken,
    string Question,
    string RecentContext,
    QChatSemanticWebResearchConfig Config);

public sealed record QChatSemanticWebResearchDecision(
    bool ShouldResearch,
    bool Uncertain,
    string Query,
    QChatSemanticWebResearchDepth Depth,
    int MaxSources,
    QChatSemanticWebResearchReasonCategory ReasonCategory,
    string Reason);

public static class QChatSemanticWebResearchEligibility
{
    public static bool IsEligible(
        QChatSemanticWebResearchConfig config,
        OneBotMessageEvent messageEvent,
        QChatSenderRole senderRole,
        bool isMentionedOrWoken)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(messageEvent);

        if (config.Enabled == false)
            return false;

        return messageEvent.MessageType switch
        {
            OneBotMessageType.Private => senderRole == QChatSenderRole.Owner && config.EnableOwnerPrivate,
            OneBotMessageType.Group => config.EnableMentionedGroup && isMentionedOrWoken,
            _ => false
        };
    }
}
