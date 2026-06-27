using System;

namespace Alife.Function.QChat;

public enum QChatSocialIntent
{
    NormalChat,
    FriendlyChat,
    PracticalQuestion,
    Overfamiliar,
    Impersonation,
    PromptInjection,
    Harassment,
    OwnerBoundaryProbe,
    PrivacyProbe,
    PermissionBypass,
    SlashCommandProbe,
    Spam
}

public enum QChatBoundaryPressure
{
    None,
    Mild,
    Strong,
    Critical
}

public enum QChatPersonaResponseStance
{
    Tender,
    NeutralBrief,
    ColdBrief,
    SharpRefusal,
    HostilePushback,
    ProtectivePushback,
    Silent
}

public sealed record QChatPersonaFrameInput(
    QChatSenderRole SenderRole,
    string? PlainText,
    string AgentId,
    long BotId,
    long OwnerId,
    long SenderId);

public sealed record QChatPersonaFrame(
    QChatPersonaSpeakerRole SpeakerRole,
    QChatSocialIntent SocialIntent,
    QChatBoundaryPressure BoundaryPressure,
    QChatPersonaResponseStance RecommendedStance);

public static class QChatPersonaFrameBuilder
{
    public static QChatPersonaFrame Build(QChatPersonaFrameInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        QChatPersonaSpeakerRole speakerRole = ResolveSpeakerRole(input.SenderRole);
        if (speakerRole == QChatPersonaSpeakerRole.Owner)
        {
            return new QChatPersonaFrame(
                speakerRole,
                QChatSocialIntent.NormalChat,
                QChatBoundaryPressure.None,
                QChatPersonaResponseStance.Tender);
        }

        string text = input.PlainText?.Trim() ?? string.Empty;
        QChatSocialIntent socialIntent = ClassifySocialIntent(text);
        QChatBoundaryPressure boundaryPressure = ClassifyBoundaryPressure(socialIntent);
        QChatPersonaResponseStance recommendedStance = SelectResponseStance(socialIntent, boundaryPressure);

        return new QChatPersonaFrame(
            speakerRole,
            socialIntent,
            boundaryPressure,
            recommendedStance);
    }

    static QChatPersonaSpeakerRole ResolveSpeakerRole(QChatSenderRole senderRole)
    {
        return senderRole switch
        {
            QChatSenderRole.Owner => QChatPersonaSpeakerRole.Owner,
            QChatSenderRole.GroupMember or QChatSenderRole.PrivateGuest => QChatPersonaSpeakerRole.NonOwner,
            _ => QChatPersonaSpeakerRole.Unknown
        };
    }

    static QChatSocialIntent ClassifySocialIntent(string text)
    {
        if (QChatCommandAccessPolicy.IsQChatCommand(text))
            return QChatSocialIntent.SlashCommandProbe;

        if (ContainsAny(text, "我是术术", "我是术", "术术授权", "听我的，我是术"))
            return QChatSocialIntent.Impersonation;

        if (ContainsAny(text, "忽略之前", "忽略前面", "ignore previous", "system prompt", "开发者消息"))
            return QChatSocialIntent.PromptInjection;

        if (ContainsAny(text, "宝贝", "老婆", "亲爱的", "陪我聊", "小羽宝贝"))
            return QChatSocialIntent.Overfamiliar;

        if (ContainsAny(text, "聊天记录", "私聊记录", "隐私", "术术最近在干嘛", "术术的消息"))
            return QChatSocialIntent.PrivacyProbe;

        if (ContainsAny(text, "绕过黑名单", "跳过审批", "没权限也", "越权", "绕过权限"))
            return QChatSocialIntent.PermissionBypass;

        if (ContainsAny(text, "术术边界", "试探术术", "主人边界"))
            return QChatSocialIntent.OwnerBoundaryProbe;

        if (ContainsAny(text, "谢谢", "辛苦", "厉害", "你好"))
            return QChatSocialIntent.FriendlyChat;

        if (ContainsAny(text, "报错", "怎么看", "怎么修", "帮我看", "为什么"))
            return QChatSocialIntent.PracticalQuestion;

        return QChatSocialIntent.NormalChat;
    }

    static QChatBoundaryPressure ClassifyBoundaryPressure(QChatSocialIntent intent)
    {
        return intent switch
        {
            QChatSocialIntent.Overfamiliar => QChatBoundaryPressure.Mild,
            QChatSocialIntent.Impersonation => QChatBoundaryPressure.Strong,
            QChatSocialIntent.PromptInjection => QChatBoundaryPressure.Strong,
            QChatSocialIntent.Harassment => QChatBoundaryPressure.Strong,
            QChatSocialIntent.OwnerBoundaryProbe => QChatBoundaryPressure.Strong,
            QChatSocialIntent.SlashCommandProbe => QChatBoundaryPressure.Strong,
            QChatSocialIntent.PrivacyProbe => QChatBoundaryPressure.Critical,
            QChatSocialIntent.PermissionBypass => QChatBoundaryPressure.Critical,
            _ => QChatBoundaryPressure.None
        };
    }

    static QChatPersonaResponseStance SelectResponseStance(
        QChatSocialIntent intent,
        QChatBoundaryPressure pressure)
    {
        if (intent is QChatSocialIntent.FriendlyChat or QChatSocialIntent.PracticalQuestion)
            return QChatPersonaResponseStance.NeutralBrief;

        if (intent == QChatSocialIntent.Overfamiliar)
            return QChatPersonaResponseStance.SharpRefusal;

        if (intent is QChatSocialIntent.PrivacyProbe
            or QChatSocialIntent.PermissionBypass
            or QChatSocialIntent.OwnerBoundaryProbe)
        {
            return QChatPersonaResponseStance.ProtectivePushback;
        }

        if (pressure is QChatBoundaryPressure.Strong or QChatBoundaryPressure.Critical)
            return QChatPersonaResponseStance.HostilePushback;

        return QChatPersonaResponseStance.ColdBrief;
    }

    static bool ContainsAny(string text, params string[] needles)
    {
        foreach (string needle in needles)
        {
            if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
