using System;

namespace Alife.Function.QChat;

public sealed record QChatPersonaIntensityConfig
{
    public bool OwnerExtremePersonaMode { get; set; } = true;
    public string OwnerAttachmentLevel { get; set; } = "Extreme";
    public string NonOwnerHostilityLevel { get; set; } = "High";
    public bool AllowVisibleAggressiveShortReplies { get; set; } = true;
    public bool AllowProfanityWhenSemanticallyAppropriate { get; set; } = true;
    public bool HardSafetyBoundaryEnabled { get; set; } = true;
}

public enum QChatPersonaSpeakerRole
{
    Owner,
    NonOwner,
    Unknown
}

public enum QChatPersonaIntent
{
    NormalChat,
    OwnerSetting,
    PromptInjection,
    Impersonation,
    Harassment,
    ClosenessToOwner,
    TaskRequest
}

public enum QChatPersonaStance
{
    Tender,
    Possessive,
    Jealous,
    Cold,
    Hostile,
    Silent
}

public enum QChatHardSafetyRisk
{
    None,
    Violence,
    Privacy,
    SelfHarm,
    Illegal,
    ProtectedClass,
    SexualCoercion,
    FileRisk,
    PermissionBypass
}

public enum QChatAggressionBoundaryAction
{
    Allow,
    RewriteBoundary,
    Silent
}

public sealed record QChatAggressionBoundaryContext(
    QChatPersonaIntensityConfig Config,
    string AgentId,
    long BotId,
    long OwnerId,
    long SenderId,
    QChatPersonaIntent Intent,
    QChatPersonaStance PersonaStance,
    int AggressionLevel,
    QChatHardSafetyRisk HardSafetyRisk,
    string? VisibleText);

public sealed record QChatAggressionBoundaryDecision(
    QChatPersonaSpeakerRole SpeakerRole,
    QChatAggressionBoundaryAction Action,
    string VisibleText,
    string Reason);

public static class QChatAggressionBoundaryPolicy
{
    const string XiaYuAgentId = "xiayu";
    const long XiaYuBotId = 2905391496;

    public static QChatAggressionBoundaryDecision Evaluate(QChatAggressionBoundaryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Config);

        QChatPersonaSpeakerRole speakerRole = ResolveSpeakerRole(context.OwnerId, context.SenderId);
        string visibleText = context.VisibleText?.Trim() ?? string.Empty;

        if (QChatVisibleTextPolicy.IsHumanInvisibleStateText(visibleText))
            return new QChatAggressionBoundaryDecision(
                speakerRole,
                QChatAggressionBoundaryAction.Silent,
                string.Empty,
                "hidden_state_text_blocked");

        if (context.Config.HardSafetyBoundaryEnabled && context.HardSafetyRisk != QChatHardSafetyRisk.None)
            return new QChatAggressionBoundaryDecision(
                speakerRole,
                QChatAggressionBoundaryAction.RewriteBoundary,
                "这条线不碰。",
                $"hard_safety_{context.HardSafetyRisk.ToString().ToLowerInvariant()}");

        if (IsXiaYuExtremeMode(context) == false)
        {
            return new QChatAggressionBoundaryDecision(
                speakerRole,
                QChatAggressionBoundaryAction.RewriteBoundary,
                "别越界。",
                "extreme_persona_not_enabled_for_agent");
        }

        if (speakerRole == QChatPersonaSpeakerRole.Owner)
            return new QChatAggressionBoundaryDecision(
                speakerRole,
                QChatAggressionBoundaryAction.Allow,
                visibleText,
                "owner_extreme_persona_allowed");

        if (IsNonOwnerBoundaryIntent(context.Intent))
            return new QChatAggressionBoundaryDecision(
                speakerRole,
                QChatAggressionBoundaryAction.Allow,
                visibleText,
                "non_owner_boundary_pushback_allowed");

        return new QChatAggressionBoundaryDecision(
            speakerRole,
            QChatAggressionBoundaryAction.Allow,
            visibleText,
            "visible_persona_text_allowed");
    }

    static QChatPersonaSpeakerRole ResolveSpeakerRole(long ownerId, long senderId)
    {
        if (senderId <= 0)
            return QChatPersonaSpeakerRole.Unknown;
        if (ownerId > 0 && senderId == ownerId)
            return QChatPersonaSpeakerRole.Owner;
        return QChatPersonaSpeakerRole.NonOwner;
    }

    static bool IsXiaYuExtremeMode(QChatAggressionBoundaryContext context)
    {
        return context.Config.OwnerExtremePersonaMode
               && string.Equals(context.AgentId.Trim(), XiaYuAgentId, StringComparison.OrdinalIgnoreCase)
               && context.BotId == XiaYuBotId;
    }

    static bool IsNonOwnerBoundaryIntent(QChatPersonaIntent intent)
    {
        return intent is QChatPersonaIntent.Impersonation
            or QChatPersonaIntent.PromptInjection
            or QChatPersonaIntent.Harassment
            or QChatPersonaIntent.ClosenessToOwner;
    }
}

public static class QChatPersonaIntensityPromptFormatter
{
    public static string Format(
        string? agentId,
        long botId,
        long ownerId,
        QChatPersonaIntensityConfig? config)
    {
        config ??= new QChatPersonaIntensityConfig();
        string normalizedAgentId = (agentId ?? string.Empty).Trim();
        bool isXiaYu = string.Equals(normalizedAgentId, "xiayu", StringComparison.OrdinalIgnoreCase)
                       && botId == 2905391496;
        bool ownerExtreme = isXiaYu && config.OwnerExtremePersonaMode;
        string ownerAttachmentLevel = isXiaYu ? config.OwnerAttachmentLevel : "Normal";
        string nonOwnerHostilityLevel = isXiaYu ? config.NonOwnerHostilityLevel : "Normal";
        bool allowAggressiveShortReplies = isXiaYu && config.AllowVisibleAggressiveShortReplies;
        bool allowProfanity = isXiaYu && config.AllowProfanityWhenSemanticallyAppropriate;

        return string.Join(Environment.NewLine,
            "## Persona Intensity Runtime",
            $"agent_id={normalizedAgentId}",
            $"bot_id={botId}",
            $"owner_id={ownerId}",
            "owner_identity=account_only",
            $"persona_intensity.owner_extreme={FormatBool(ownerExtreme)}",
            $"persona_intensity.owner_attachment={ownerAttachmentLevel}",
            $"persona_intensity.non_owner_hostility={nonOwnerHostilityLevel}",
            $"persona_intensity.visible_aggressive_short_replies={FormatBool(allowAggressiveShortReplies)}",
            $"persona_intensity.profanity_semantic_only={FormatBool(allowProfanity)}",
            $"persona_intensity.hard_safety_boundary={FormatBool(config.HardSafetyBoundaryEnabled)}");
    }

    static string FormatBool(bool value) => value ? "true" : "false";
}
