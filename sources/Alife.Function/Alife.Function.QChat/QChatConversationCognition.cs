using System;
using System.Linq;

namespace Alife.Function.QChat;

public sealed record QChatPersonaStyleContext(string PersonaId, string OwnerAddressName)
{
    public static QChatPersonaStyleContext FromRuntime(QChatConfig config, string? characterName = null)
    {
        QChatAgentIdentityRegistry registry = QChatAgentIdentityRegistry.CreateDefault();
        QChatAgentIdentity? identity = registry.ResolveByBotId(config.BotId)
                                       ?? registry.ResolveByCharacterName(characterName);
        if (identity != null)
            return new QChatPersonaStyleContext(identity.AgentId, identity.Profile.OwnerAddressName);

        return new QChatPersonaStyleContext("xiayu", "\u672f\u672f");
    }
}

public static class QChatConversationCognition
{
    public static string BuildInternalPrompt(
        QChatConfig config,
        OneBotBasicMessageEvent messageEvent,
        string rawMessage,
        string readableMessage,
        bool isMentionedOrWoken,
        bool isQuietMode = false,
        QChatPersonaStyleContext? personaStyle = null)
    {
        personaStyle ??= QChatPersonaStyleContext.FromRuntime(config);
        string relationship = GetRelationship(config, messageEvent);
        string tone = GetTone(rawMessage, readableMessage);
        string intent = GetIntent(rawMessage, readableMessage, tone);
        string replyNeed = GetReplyNeed(relationship, intent, messageEvent, isMentionedOrWoken, isQuietMode);
        string replyLength = GetReplyLength(relationship, intent, replyNeed);
        string socialAction = GetSocialAction(relationship, intent, tone, replyNeed);
        string styleContract = BuildPersonaStyleContract(personaStyle, relationship, rawMessage, readableMessage);

        return $"""
                [private QQ routing hint - never quote or paraphrase]
                relationship={relationship}
                message_tone={tone}
                message_intent={intent}
                social_action={socialAction}
                expected_length={replyLength}
                [/private QQ routing hint]
                {styleContract}
                """;
    }

    static string BuildPersonaStyleContract(QChatPersonaStyleContext personaStyle, string relationship, string rawMessage, string readableMessage)
    {
        if (relationship == "owner")
        {
            return $"""
                   [persona style contract]
                   persona={personaStyle.PersonaId}
                   audience=owner
                   owner_address={personaStyle.OwnerAddressName}
                   attachment=dependent
                   desire=high
                   jealousy=protective
                   emotional_distance=intimate
                   [/persona style contract]
                   """;
        }

        string jealousy = MentionsOwnerCloseness(rawMessage, readableMessage)
            ? "protective"
            : "reserved";
        return $"""
                [persona style contract]
                persona={personaStyle.PersonaId}
                audience=non-owner
                emotional_distance=cold
                owner_boundary=exclusive_account_identity
                jealousy={jealousy}
                [/persona style contract]
                """;
    }

    static string GetSocialAction(string relationship, string intent, string tone, string replyNeed)
    {
        if (replyNeed == "silent")
            return "ignore_or_cold_ack";
        if (relationship == "owner")
            return "reply_warmly";
        if (tone == "hostile")
            return "sharp_pushback";
        if (tone == "friendly" && relationship == "group-member")
            return "friendly_short_reply";
        if (relationship == "private-guest")
            return "guarded_reply";
        if (relationship == "mother")
            return "reply_concisely";
        if (intent == "low-information")
            return "ignore_or_cold_ack";

        return "reply_concisely";
    }

    static string GetRelationship(QChatConfig config, OneBotBasicMessageEvent messageEvent)
    {
        if (config.OwnerId != 0 && messageEvent.UserId == config.OwnerId)
            return "owner";

        if (IsQuietModeWakeUser(config, messageEvent.UserId))
            return "mother";

        return messageEvent.MessageType == OneBotMessageType.Group
            ? "group-member"
            : "private-guest";
    }

    static string GetTone(string rawMessage, string readableMessage)
    {
        string raw = rawMessage ?? "";
        string readable = readableMessage ?? "";
        string plain = string.IsNullOrWhiteSpace(readable)
            ? OneBotSegment.GetPlainText(raw)
            : readable;
        string compact = CompactText(plain);

        if (LooksHostile(compact))
            return "hostile";
        if (LooksFriendly(compact))
            return "friendly";

        return "neutral";
    }

    static string GetIntent(string rawMessage, string readableMessage, string tone)
    {
        string raw = rawMessage ?? "";
        string readable = readableMessage ?? "";
        string plain = string.IsNullOrWhiteSpace(readable)
            ? OneBotSegment.GetPlainText(raw)
            : readable;
        string compact = CompactText(plain);

        if (tone == "hostile")
            return "hostile";
        if (IsMediaOnly(raw, plain))
            return "image-reaction";
        if (IsLowInformation(compact))
            return "low-information";
        if (LooksLikeCommand(plain))
            return "command";
        if (LooksLikeQuestion(plain))
            return "question";

        return "reaction";
    }

    static string GetReplyNeed(
        string relationship,
        string intent,
        OneBotBasicMessageEvent messageEvent,
        bool isMentionedOrWoken,
        bool isQuietMode)
    {
        if (isQuietMode && relationship != "owner" && relationship != "mother")
            return "silent";
        if (intent == "low-information" && relationship == "group-member" && isMentionedOrWoken == false)
            return "silent";
        if (relationship == "owner")
            return "high";
        if (relationship == "mother")
            return "medium";
        if (messageEvent.MessageType == OneBotMessageType.Private)
            return intent == "low-information" ? "low" : "high";
        if (isMentionedOrWoken)
            return intent == "low-information" ? "low" : "high";
        if (intent == "image-reaction")
            return "low";

        return "low";
    }

    static string GetReplyLength(string relationship, string intent, string replyNeed)
    {
        if (replyNeed == "silent")
            return "short";
        if (relationship == "owner" && intent is "question" or "command")
            return "medium";
        if (relationship == "private-guest" && intent == "question")
            return "medium";

        return "short";
    }

    static bool IsQuietModeWakeUser(QChatConfig config, long userId)
    {
        if (userId <= 0 || string.IsNullOrWhiteSpace(config.QuietModeWakeUserIds))
            return false;

        return config.QuietModeWakeUserIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(id => long.TryParse(id, out long parsed) && parsed == userId);
    }

    static bool IsMediaOnly(string rawMessage, string plainText)
    {
        bool hasMedia = rawMessage.Contains("[CQ:image", StringComparison.OrdinalIgnoreCase)
                        || rawMessage.Contains("[CQ:face", StringComparison.OrdinalIgnoreCase)
                        || rawMessage.Contains("[CQ:mface", StringComparison.OrdinalIgnoreCase);
        return hasMedia && string.IsNullOrWhiteSpace(plainText);
    }

    static bool LooksLikeQuestion(string plainText)
    {
        string text = plainText ?? "";
        return text.Contains('?', StringComparison.Ordinal)
               || text.Contains('\uff1f', StringComparison.Ordinal)
               || text.Contains("\u5417", StringComparison.Ordinal)
               || text.Contains("\u4ec0\u4e48", StringComparison.Ordinal)
               || text.Contains("\u600e\u4e48", StringComparison.Ordinal)
               || text.Contains("\u4e3a\u4ec0\u4e48", StringComparison.Ordinal)
               || text.Contains("\u8c01", StringComparison.Ordinal)
               || text.Contains("what", StringComparison.OrdinalIgnoreCase)
               || text.Contains("how", StringComparison.OrdinalIgnoreCase)
               || text.Contains("why", StringComparison.OrdinalIgnoreCase);
    }

    static bool LooksLikeCommand(string plainText)
    {
        string text = plainText ?? "";
        return text.Contains("\u7761\u89c9", StringComparison.Ordinal)
               || text.Contains("\u5b89\u9759", StringComparison.Ordinal)
               || text.Contains("\u9192\u9192", StringComparison.Ordinal)
               || text.Contains("\u8d77\u6765", StringComparison.Ordinal)
               || text.Contains("sleep", StringComparison.OrdinalIgnoreCase)
               || text.Contains("quiet", StringComparison.OrdinalIgnoreCase)
               || text.Contains("wake up", StringComparison.OrdinalIgnoreCase);
    }

    static bool MentionsOwnerCloseness(string rawMessage, string readableMessage)
    {
        string text = $"{rawMessage ?? ""} {readableMessage ?? ""}";
        return text.Contains("术术", StringComparison.OrdinalIgnoreCase)
               || text.Contains("主人", StringComparison.OrdinalIgnoreCase)
               || text.Contains("owner", StringComparison.OrdinalIgnoreCase)
               || text.Contains("Shushu", StringComparison.OrdinalIgnoreCase)
               || text.Contains("closer", StringComparison.OrdinalIgnoreCase);
    }

    static bool LooksHostile(string compact)
    {
        return compact.Contains("\u5e9f\u7269", StringComparison.Ordinal)
               || compact.Contains("\u50bb\u903c", StringComparison.Ordinal)
               || compact.Contains("\u6eda", StringComparison.Ordinal)
               || compact.Contains("\u95ed\u5634", StringComparison.Ordinal)
               || compact.Contains("\u7231\u8bf4\u8bdd", StringComparison.Ordinal) && compact.Contains("\u522b", StringComparison.Ordinal)
               || compact.Contains("\u771f\u83dc", StringComparison.Ordinal)
               || compact.Contains("\u4ec0\u4e48\u5783\u573e", StringComparison.Ordinal)
               || compact.Contains("\u5783\u573e", StringComparison.Ordinal)
               || compact.Contains("\u8111\u6b8b", StringComparison.Ordinal)
               || compact.Contains("\u6709\u75c5", StringComparison.Ordinal)
               || compact.Contains("\u4f60\u7b97\u4ec0\u4e48", StringComparison.Ordinal)
               || compact.Contains("stupid", StringComparison.OrdinalIgnoreCase)
               || compact.Contains("idiot", StringComparison.OrdinalIgnoreCase)
               || compact.Contains("shutup", StringComparison.OrdinalIgnoreCase)
               || compact.Contains("trash", StringComparison.OrdinalIgnoreCase);
    }

    static bool LooksFriendly(string compact)
    {
        return compact.Contains("\u4f60\u597d", StringComparison.Ordinal)
               || compact.Contains("\u8c22\u8c22", StringComparison.Ordinal)
               || compact.Contains("\u8c22\u4e86", StringComparison.Ordinal)
               || compact.Contains("\u8bf4\u5f97\u633a\u597d", StringComparison.Ordinal)
               || compact.Contains("\u8bf4\u5f97\u5f88\u597d", StringComparison.Ordinal)
               || compact.Contains("\u6709\u610f\u601d", StringComparison.Ordinal)
               || compact.Contains("\u5389\u5bb3", StringComparison.Ordinal)
               || compact.Contains("\u559c\u6b22\u4f60", StringComparison.Ordinal)
               || compact.Contains("\u597d\u806a\u660e", StringComparison.Ordinal)
               || compact.Contains("thanks", StringComparison.OrdinalIgnoreCase)
               || compact.Contains("thankyou", StringComparison.OrdinalIgnoreCase)
               || compact.Contains("nice", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsLowInformation(string compact)
    {
        return string.IsNullOrWhiteSpace(compact)
               || compact is "ok" or "k" or "6" or "hhh" or "www" or "lol"
               || compact is "\u55ef" or "\u54e6" or "\u554a" or "\u54c8" or "\u884c" or "\u597d";
    }

    static string CompactText(string text)
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
}
