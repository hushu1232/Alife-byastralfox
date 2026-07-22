using System;

namespace Alife.Function.QChat;

public enum QChatCapabilityCandidateKind
{
    None,
    ConversationContext,
    PersonaFact
}

public sealed record QChatCapabilityCandidate(
    QChatCapabilityCandidateKind Kind,
    QChatPersonaFactCategory? PersonaFactCategory = null)
{
    public static QChatCapabilityCandidate None { get; } = new(QChatCapabilityCandidateKind.None);
}

public sealed class QChatCapabilityCandidateSelector
{
    public QChatCapabilityCandidate Select(string? text, bool hasRecentConversation, bool hasApprovedPersona)
    {
        string normalized = text?.Trim() ?? string.Empty;
        if (hasApprovedPersona && ContainsAny(normalized, "说话风格", "口癖", "你是什么性格", "你的关系", "你的设定", "经历", "身份", "偏好"))
            return new QChatCapabilityCandidate(QChatCapabilityCandidateKind.PersonaFact, ResolvePersonaFactCategory(normalized));

        if (hasRecentConversation && ContainsAny(normalized, "刚才", "之前", "继续", "上面", "那个话题", "你说过"))
            return new QChatCapabilityCandidate(QChatCapabilityCandidateKind.ConversationContext);

        return QChatCapabilityCandidate.None;
    }

    static bool ContainsAny(string text, params string[] phrases)
    {
        foreach (string phrase in phrases)
        {
            if (text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    static QChatPersonaFactCategory ResolvePersonaFactCategory(string text)
    {
        if (ContainsAny(text, "关系", "主人", "妈妈", "前辈"))
            return QChatPersonaFactCategory.Relationship;
        if (ContainsAny(text, "经历", "故事", "身份", "起源"))
            return QChatPersonaFactCategory.Origin;
        if (ContainsAny(text, "偏好", "喜欢", "厌恶"))
            return QChatPersonaFactCategory.ConfirmedPreference;
        if (ContainsAny(text, "行为", "边界", "权限", "规则"))
            return QChatPersonaFactCategory.BehaviorBoundary;

        return QChatPersonaFactCategory.SpeechStyle;
    }
}
