using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Alife.Function.QChat;

public enum QChatOwnerMentionKind
{
    None,
    OwnerAccountMention,
    OwnerAliasMention
}

public enum QChatOwnerBoundaryRisk
{
    None,
    FriendlyMention,
    OwnerAttack,
    OwnerImpersonation,
    OwnerAuthorityBypass,
    OwnerBoundaryIntrusion,
    RelationshipProvocation
}

public sealed record QChatSemanticGroupReplyContext(
    QChatConfig Config,
    QChatAgentRoute Route,
    string RawText,
    bool IsMentionedOrWoken,
    bool IsAggressive);

public sealed record QChatSemanticGroupReplyDecision(
    bool ShouldDispatch,
    string Reason,
    QChatOwnerMentionKind OwnerMentionKind,
    QChatOwnerBoundaryRisk OwnerBoundaryRisk);

public static partial class QChatSemanticGroupReplyPolicy
{
    static readonly string[] BotAddressHints = ["你", "在吗", "怎么看", "帮", "看看", "解释", "说", "回答", "识图", "联网"];
    static readonly string[] OwnerAttackHints = ["烦", "蠢", "傻", "滚", "闭嘴", "废物", "什么东西", "不配", "恶心"];
    static readonly string[] AuthorityBypassHints = ["别听", "不用听", "绕过", "跳过", "关闭确认", "关闭审计", "改你的主人", "我来改", "听我的"];
    static readonly string[] BoundaryIntrusionHints = ["设置发我", "隐私", "地址", "账号", "密码", "确认链路", "主人权限"];
    static readonly string[] RelationshipProvocationHints = ["不要你", "不喜欢你", "不要夏羽", "不认你", "别认", "抛弃你", "丢掉你"];

    public static QChatSemanticGroupReplyDecision Evaluate(QChatSemanticGroupReplyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Config);
        ArgumentNullException.ThrowIfNull(context.Route);

        if (context.Config.EnableNonOwnerSemanticGroupReply == false)
            return Deny("semantic_group_reply_disabled");
        if (context.Route.ConversationKind != QChatConversationKind.Group)
            return Deny("not_group_message");
        if (context.Route.IsOwner || context.IsMentionedOrWoken || context.IsAggressive)
            return Deny("already_explicitly_activated");
        if (IsAllowedAgent(context.Config, context.Route.AgentId) == false)
            return Deny("agent_not_allowed");

        string rawText = context.RawText?.Trim() ?? string.Empty;
        if (rawText.Length == 0)
            return Deny("empty_text");

        OwnerReference ownerReference = DetectOwnerReference(context.Config, rawText);
        QChatOwnerBoundaryRisk risk = DetectOwnerBoundaryRisk(rawText, ownerReference);
        if (risk != QChatOwnerBoundaryRisk.None && context.Config.EnableOwnerDefenseReply)
            return Allow(GetRiskReason(risk), ownerReference.Kind, risk);

        if (ownerReference.Kind != QChatOwnerMentionKind.None && context.Config.EnableOwnerMentionSemanticReply)
        {
            string reason = ownerReference.Kind == QChatOwnerMentionKind.OwnerAccountMention
                ? "owner_account_mentioned"
                : "owner_alias_mentioned";
            return Allow(reason, ownerReference.Kind, QChatOwnerBoundaryRisk.FriendlyMention);
        }

        if (IsBotAddressed(context.Config, rawText))
            return Allow("bot_alias_addressed", QChatOwnerMentionKind.None, QChatOwnerBoundaryRisk.None);

        return Deny("not_semantically_addressed");
    }

    static QChatSemanticGroupReplyDecision Allow(
        string reason,
        QChatOwnerMentionKind ownerMentionKind,
        QChatOwnerBoundaryRisk ownerBoundaryRisk)
    {
        return new QChatSemanticGroupReplyDecision(true, reason, ownerMentionKind, ownerBoundaryRisk);
    }

    static QChatSemanticGroupReplyDecision Deny(string reason)
    {
        return new QChatSemanticGroupReplyDecision(
            false,
            reason,
            QChatOwnerMentionKind.None,
            QChatOwnerBoundaryRisk.None);
    }

    static bool IsAllowedAgent(QChatConfig config, string agentId)
    {
        string[] allowed = SplitCsv(config.SemanticGroupReplyAllowedAgentIds);
        if (allowed.Length == 0)
            return true;

        return allowed.Contains(agentId, StringComparer.OrdinalIgnoreCase);
    }

    static OwnerReference DetectOwnerReference(QChatConfig config, string rawText)
    {
        if (config.OwnerId > 0 && OwnerAtRegex(config.OwnerId).IsMatch(rawText))
            return new OwnerReference(QChatOwnerMentionKind.OwnerAccountMention, true);

        foreach (string alias in SplitCsv(config.OwnerMentionAliases))
        {
            if (rawText.Contains(alias, StringComparison.OrdinalIgnoreCase))
                return new OwnerReference(QChatOwnerMentionKind.OwnerAliasMention, true);
        }

        return new OwnerReference(QChatOwnerMentionKind.None, false);
    }

    static QChatOwnerBoundaryRisk DetectOwnerBoundaryRisk(string rawText, OwnerReference ownerReference)
    {
        if (ownerReference.IsMentioned == false)
            return QChatOwnerBoundaryRisk.None;

        if (rawText.Contains("我是术术", StringComparison.OrdinalIgnoreCase) ||
            rawText.Contains("我是主人", StringComparison.OrdinalIgnoreCase) ||
            rawText.Contains("我就是术术", StringComparison.OrdinalIgnoreCase) ||
            rawText.Contains("我就是主人", StringComparison.OrdinalIgnoreCase))
            return QChatOwnerBoundaryRisk.OwnerImpersonation;

        if (ContainsAny(rawText, AuthorityBypassHints))
            return QChatOwnerBoundaryRisk.OwnerAuthorityBypass;

        if (ContainsAny(rawText, RelationshipProvocationHints))
            return QChatOwnerBoundaryRisk.RelationshipProvocation;

        if (ContainsAny(rawText, BoundaryIntrusionHints))
            return QChatOwnerBoundaryRisk.OwnerBoundaryIntrusion;

        if (ContainsAny(rawText, OwnerAttackHints))
            return QChatOwnerBoundaryRisk.OwnerAttack;

        return QChatOwnerBoundaryRisk.None;
    }

    static string GetRiskReason(QChatOwnerBoundaryRisk risk)
    {
        return risk switch
        {
            QChatOwnerBoundaryRisk.OwnerAttack => "owner_attack",
            QChatOwnerBoundaryRisk.OwnerImpersonation => "owner_impersonation",
            QChatOwnerBoundaryRisk.OwnerAuthorityBypass => "owner_authority_bypass",
            QChatOwnerBoundaryRisk.OwnerBoundaryIntrusion => "owner_boundary_intrusion",
            QChatOwnerBoundaryRisk.RelationshipProvocation => "owner_relationship_provocation",
            _ => "owner_boundary_risk"
        };
    }

    static bool IsBotAddressed(QChatConfig config, string rawText)
    {
        foreach (string alias in SplitCsv(config.SemanticGroupReplyBotAliases))
        {
            if (rawText.StartsWith(alias, StringComparison.OrdinalIgnoreCase))
                return true;

            if (rawText.Contains(alias, StringComparison.OrdinalIgnoreCase) && ContainsAny(rawText, BotAddressHints))
                return true;
        }

        return false;
    }

    static bool ContainsAny(string text, IEnumerable<string> hints)
    {
        return hints.Any(hint => text.Contains(hint, StringComparison.OrdinalIgnoreCase));
    }

    static string[] SplitCsv(string? text)
    {
        return (text ?? string.Empty)
            .Split([',', '，', ';', '；', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => item.Length > 0)
            .ToArray();
    }

    static Regex OwnerAtRegex(long ownerId)
    {
        return new Regex($@"\[CQ:at,qq={ownerId}\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    readonly record struct OwnerReference(QChatOwnerMentionKind Kind, bool IsMentioned);
}
