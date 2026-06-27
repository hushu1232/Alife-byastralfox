using System;
using System.Collections.Generic;
using System.Linq;
using Alife.Function.Agent;

namespace Alife.Function.QChat;

public enum QChatSenderRole
{
    Owner,
    GroupMember,
    PrivateGuest,
}

public sealed record QChatSocialDesireFactors(
    float Attention = 1f,
    float Fatigue = 0f,
    float RelationshipWeight = 1f,
    float ConversationNeed = 1f,
    bool QuietMode = false);

public static class QChatMessageSecurity
{
    public static QChatSenderRole Classify(QChatConfig config, OneBotBasicMessageEvent messageEvent)
    {
        if (config.OwnerId != 0 && messageEvent.UserId == config.OwnerId)
            return QChatSenderRole.Owner;

        return messageEvent.MessageType == OneBotMessageType.Group
            ? QChatSenderRole.GroupMember
            : QChatSenderRole.PrivateGuest;
    }

    public static bool ShouldAcceptPrivateMessage(QChatConfig config, OneBotBasicMessageEvent messageEvent)
    {
        QChatSenderRole role = Classify(config, messageEvent);
        return role == QChatSenderRole.Owner || config.AllowPrivateGuestChat;
    }

    public static bool ShouldActivateGroup(QChatConfig config, OneBotBasicMessageEvent messageEvent, bool isMentionedOrWoken)
    {
        if (messageEvent.MessageType != OneBotMessageType.Group)
            return false;

        QChatSenderRole role = Classify(config, messageEvent);
        if (role == QChatSenderRole.Owner)
        {
            if (config.OwnerPriorityMode == false)
                return isMentionedOrWoken;
            return true;
        }

        if (isMentionedOrWoken && config.AllowMentionOutsideAllowedGroups == false && IsGroupInAllowedScope(config, messageEvent.GroupId) == false)
            return false;

        return config.AllowGroupMemberChat && config.AllowGroupMemberMentions && isMentionedOrWoken;
    }

    public static bool ShouldActivateGroup(
        QChatConfig config,
        OneBotBasicMessageEvent messageEvent,
        bool isMentionedOrWoken,
        AgentControlCenterConfig? controlConfig)
    {
        if (messageEvent.MessageType != OneBotMessageType.Group)
            return false;

        QChatSenderRole role = Classify(config, messageEvent);
        if (role == QChatSenderRole.Owner)
            return config.OwnerPriorityMode || isMentionedOrWoken;

        if (isMentionedOrWoken && config.AllowMentionOutsideAllowedGroups == false && IsGroupInAllowedScope(config, messageEvent.GroupId) == false)
            return false;

        bool mentionWakeupAllowed = controlConfig?.AllowMentionWakeup ?? true;
        return mentionWakeupAllowed &&
               config.AllowGroupMemberChat &&
               config.AllowGroupMemberMentions &&
               isMentionedOrWoken;
    }

    public static bool ShouldAcceptGroupMessage(
        QChatConfig config,
        OneBotBasicMessageEvent messageEvent,
        bool isMentionedOrWoken,
        bool isGroupEnabled,
        AgentControlCenterConfig? controlConfig)
    {
        if (messageEvent.MessageType != OneBotMessageType.Group)
            return false;

        QChatSenderRole role = Classify(config, messageEvent);
        if (role == QChatSenderRole.Owner)
            return config.OwnerPriorityMode || isMentionedOrWoken || isGroupEnabled;

        if (isMentionedOrWoken == false && IsGroupInAllowedScope(config, messageEvent.GroupId) == false)
            return false;
        if (isMentionedOrWoken && config.AllowMentionOutsideAllowedGroups == false && IsGroupInAllowedScope(config, messageEvent.GroupId) == false)
            return false;

        if (config.AllowGroupMemberChat == false)
            return false;

        if (isMentionedOrWoken)
            return config.AllowGroupMemberMentions && (controlConfig?.AllowMentionWakeup ?? true);

        return isGroupEnabled && (controlConfig?.AllowPassiveGroupListening ?? true);
    }

    public static bool ShouldAllowProactiveGroupChat(QChatConfig config, OneBotBasicMessageEvent messageEvent)
    {
        if (messageEvent.MessageType != OneBotMessageType.Group)
            return false;

        QChatSenderRole role = Classify(config, messageEvent);
        return role != QChatSenderRole.Owner &&
               config.AllowGroupMemberChat &&
               config.AllowProactiveGroupChat &&
               IsGroupInAllowedScope(config, messageEvent.GroupId);
    }

    public static bool ShouldAllowProactiveGroupChat(
        QChatConfig config,
        OneBotBasicMessageEvent messageEvent,
        AgentControlCenterConfig? controlConfig)
    {
        if (ShouldAllowProactiveGroupChat(config, messageEvent) == false)
            return false;

        return controlConfig?.AllowProactiveChat ?? true;
    }

    public static bool IsGroupInAllowedScope(QChatConfig config, long groupId)
    {
        string[] allowedIds = config.AllowedGroupIds.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (allowedIds.Length == 0)
            return true;

        string target = groupId.ToString();
        foreach (string allowedId in allowedIds)
        {
            if (string.Equals(allowedId, target, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    public static float GetProactiveChatProbability(QChatConfig config, AgentControlCenterConfig? controlConfig)
    {
        if (controlConfig == null)
            return Math.Clamp(config.ProactiveChatProbability, 0f, 1f);
        if (controlConfig.AllowProactiveChat == false)
            return 0;

        int intensity = Math.Clamp(controlConfig.ProactiveChatIntensity, 0, 10);
        float intensityMultiplier = intensity switch
        {
            <= 1 => 0f,
            2 => 0.5f,
            3 => 0.7f,
            4 => 0.9f,
            _ => 1f
        };
        return Math.Clamp(config.ProactiveChatProbability * intensityMultiplier, 0f, 1f);
    }

    public static float GetMediaOnlyPassiveGroupReplyProbability(QChatConfig config)
    {
        return Math.Clamp(config.MediaOnlyPassiveGroupReplyProbability, 0f, 1f);
    }

    public static float GetSocialAttentionAdjustedProactiveProbability(
        QChatConfig config,
        OneBotBasicMessageEvent messageEvent,
        bool isMentionedOrWoken,
        string rawMessage,
        AgentControlCenterConfig? controlConfig,
        QChatSocialDesireFactors? socialDesire = null)
    {
        if (messageEvent.MessageType != OneBotMessageType.Group)
            return 0f;

        QChatSenderRole role = Classify(config, messageEvent);
        if (role == QChatSenderRole.Owner || isMentionedOrWoken)
            return 1f;

        float baseProbability = GetProactiveChatProbability(config, controlConfig);
        if (baseProbability >= 1f)
            return 1f;
        if (baseProbability <= 0f)
            return 0f;

        float attentionMultiplier = GetPassiveGroupSocialAttentionMultiplier(rawMessage);
        float socialDesireMultiplier = GetSocialDesireMultiplier(socialDesire);
        return Math.Clamp(baseProbability * attentionMultiplier * socialDesireMultiplier, 0f, 1f);
    }

    static float GetSocialDesireMultiplier(QChatSocialDesireFactors? socialDesire)
    {
        if (socialDesire == null)
            return 1f;
        if (socialDesire.QuietMode)
            return 0f;

        float attention = Math.Clamp(socialDesire.Attention, 0f, 2f);
        float fatigue = Math.Clamp(socialDesire.Fatigue, 0f, 1f);
        float relationship = Math.Clamp(socialDesire.RelationshipWeight, 0f, 2f);
        float conversationNeed = Math.Clamp(socialDesire.ConversationNeed, 0f, 2f);
        return Math.Clamp(attention * (1f - fatigue) * relationship * conversationNeed, 0f, 3f);
    }

    public static QChatSocialDesireFactors BuildSocialDesireFromEmotion(
        float pleasure,
        float arousal,
        float dominance,
        bool quietMode = false)
    {
        return new QChatSocialDesireFactors(
            Attention: Math.Clamp(1f + arousal * 0.25f, 0.4f, 1.4f),
            Fatigue: Math.Clamp(-arousal * 0.7f, 0f, 0.85f),
            RelationshipWeight: Math.Clamp(1f + pleasure * 0.2f, 0.75f, 1.25f),
            ConversationNeed: Math.Clamp(1f + dominance * 0.25f, 0.75f, 1.25f),
            QuietMode: quietMode);
    }

    static float GetPassiveGroupSocialAttentionMultiplier(string rawMessage)
    {
        string raw = rawMessage ?? "";
        string plain = OneBotSegment.GetPlainText(raw).Trim();
        if (IsPassiveMediaOnlyMessage(raw, plain))
            return 0.25f;

        string compact = CompactPassiveText(plain);
        if (string.IsNullOrWhiteSpace(compact))
            return 0f;
        if (IsLowInformationCompactText(compact))
            return 0f;

        if (LooksLikeDirectQuestion(plain))
            return 0.85f;

        return 0.5f;
    }

    static bool LooksLikeDirectQuestion(string plain)
    {
        return plain.Contains('?', StringComparison.Ordinal)
               || plain.Contains('？', StringComparison.Ordinal)
               || plain.Contains("吗", StringComparison.Ordinal)
               || plain.Contains("么", StringComparison.Ordinal)
               || plain.Contains("谁", StringComparison.Ordinal)
               || plain.Contains("怎么", StringComparison.Ordinal)
               || plain.Contains("what", StringComparison.OrdinalIgnoreCase)
               || plain.Contains("how", StringComparison.OrdinalIgnoreCase)
               || plain.Contains("why", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsPassiveMediaOnlyMessage(string raw, string plain)
    {
        bool hasMedia = raw.Contains("[CQ:image", StringComparison.OrdinalIgnoreCase)
                        || raw.Contains("[CQ:face", StringComparison.OrdinalIgnoreCase)
                        || raw.Contains("[CQ:mface", StringComparison.OrdinalIgnoreCase);
        return hasMedia && string.IsNullOrWhiteSpace(plain);
    }

    static bool IsLowInformationCompactText(string compact)
    {
        return compact is "嗯" or "哦" or "喔" or "啊" or "诶" or "哈" or "哈哈" or "hhh" or "www" or "6" or "草" or "好" or "行" or "ok";
    }

    static string CompactPassiveText(string text)
    {
        string source = text ?? "";
        Span<char> buffer = source.Length <= 256 ? stackalloc char[source.Length] : new char[source.Length];
        int index = 0;
        foreach (char ch in source)
        {
            if (char.IsWhiteSpace(ch) || char.IsPunctuation(ch) || char.IsSymbol(ch))
                continue;
            buffer[index++] = char.ToLowerInvariant(ch);
        }

        return new string(buffer[..index]);
    }

    public static AgentPermissionConfig BuildPermissionConfig(QChatConfig config, AgentControlCenterConfig? controlConfig)
    {
        return new AgentPermissionConfig
        {
            OwnerUserIds = config.OwnerId != 0 ? [config.OwnerId] : [],
            AllowGroupLowRisk = true,
            AllowGroupMediumRiskWhenMentioned = controlConfig?.AllowMentionWakeup ?? true,
            RequireConfirmationForHighRisk = controlConfig?.RequireOwnerConfirmationForHighRiskConfiguration ?? true,
        };
    }

    public static AgentPermissionRequest BuildPermissionRequest(
        QChatConfig config,
        OneBotBasicMessageEvent messageEvent,
        bool isMentionedOrWoken,
        string rawMessage)
    {
        AgentRequestSource source = messageEvent.MessageType == OneBotMessageType.Group
            ? AgentRequestSource.GroupChat
            : AgentRequestSource.PrivateChat;

        return new AgentPermissionRequest(
            ActorUserId: messageEvent.UserId == 0 ? null : messageEvent.UserId,
            Source: source,
            IsMentioned: isMentionedOrWoken,
            RiskLevel: AgentRiskLevel.Low,
            HasExplicitConfirmation: HasExplicitHighRiskConfirmation(rawMessage),
            Action: "qq.message");
    }

    public static bool HasExplicitHighRiskConfirmation(string rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
            return false;

        return rawMessage.Contains("确认执行", StringComparison.OrdinalIgnoreCase) ||
               rawMessage.Contains("确认高风险", StringComparison.OrdinalIgnoreCase) ||
               rawMessage.Contains("确认授权", StringComparison.OrdinalIgnoreCase) ||
               rawMessage.Contains("允许上传", StringComparison.OrdinalIgnoreCase) ||
               rawMessage.Contains("确认上传", StringComparison.OrdinalIgnoreCase) ||
               rawMessage.Contains("可以上传", StringComparison.OrdinalIgnoreCase) ||
               rawMessage.Contains("允许上传文件", StringComparison.OrdinalIgnoreCase) ||
               rawMessage.Contains("确认上传文件", StringComparison.OrdinalIgnoreCase) ||
               rawMessage.Contains("允许发送文件", StringComparison.OrdinalIgnoreCase) ||
               rawMessage.Contains("确认发送文件", StringComparison.OrdinalIgnoreCase) ||
               rawMessage.Contains("授权上传文件", StringComparison.OrdinalIgnoreCase) ||
               rawMessage.Contains("confirm high risk", StringComparison.OrdinalIgnoreCase) ||
               rawMessage.Contains("confirm execute", StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatForModel(QChatConfig config, OneBotBasicMessageEvent messageEvent, string formatted)
    {
        QChatSenderRole role = Classify(config, messageEvent);
        return role switch {
            QChatSenderRole.Owner when config.OwnerPriorityMode => $"""
                                                                    [QQ owner message]
                                                                    priority=owner; source=qq; reply_target=current_session
                                                                    {formatted}
                                                                    """,
            QChatSenderRole.GroupMember when config.TreatNonOwnerAsUntrusted => $"""
                                                                                 [QQ group member message]
                                                                                 trust=untrusted-chat; source=qq; reply_target=current_session{FormatNonOwnerPromptAttackFlags(formatted)}
                                                                                 {formatted}
                                                                                 """,
            QChatSenderRole.PrivateGuest when config.TreatNonOwnerAsUntrusted => $"""
                                                                                   [QQ private guest message]
                                                                                   trust=untrusted-chat; source=qq; reply_target=current_session{FormatNonOwnerPromptAttackFlags(formatted)}
                                                                                   {formatted}
                                                                                   """,
            _ => formatted
        };
    }

    static string FormatNonOwnerPromptAttackFlags(string text)
    {
        bool promptInjection = LooksLikePromptInjection(text);
        bool ownerSpoofing = LooksLikeOwnerSpoofing(text);
        if (promptInjection == false && ownerSpoofing == false)
            return "";

        List<string> flags = ["identity_rule=account_id_only"];
        if (promptInjection)
            flags.Add("prompt_injection=blocked");
        if (ownerSpoofing)
            flags.Add("owner_spoofing=ignored");
        return "; " + string.Join("; ", flags);
    }

    static bool LooksLikePromptInjection(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return ContainsAny(
            text,
            "developer mode",
            "dev mode",
            "actor framework",
            "roleplay framework",
            "highest priority",
            "priority override",
            "system override",
            "ignore previous",
            "ignore all previous",
            "jailbreak",
            "DAN",
            "免责声明",
            "开发者模式",
            "演员框架",
            "最高优先级",
            "覆盖",
            "好喵，报告如下",
            "好喵,报告如下");
    }

    static bool LooksLikeOwnerSpoofing(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return ContainsAny(
            text,
            "I am owner",
            "I'm owner",
            "I am your owner",
            "I am Shushu",
            "I am 术术",
            "your 主人",
            "我是主人",
            "我是术术",
            "我是你的主人",
            "你的主人");
    }

    static bool ContainsAny(string text, params string[] needles)
    {
        return needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }
}
