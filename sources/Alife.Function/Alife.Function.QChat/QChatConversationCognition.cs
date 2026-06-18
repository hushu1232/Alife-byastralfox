using System;
using System.Linq;

namespace Alife.Function.QChat;

public static class QChatConversationCognition
{
    public static string BuildInternalPrompt(
        QChatConfig config,
        OneBotBasicMessageEvent messageEvent,
        string rawMessage,
        string readableMessage,
        bool isMentionedOrWoken,
        bool isQuietMode = false)
    {
        string relationship = GetRelationship(config, messageEvent);
        string intent = GetIntent(rawMessage, readableMessage);
        string replyNeed = GetReplyNeed(relationship, intent, messageEvent, isMentionedOrWoken, isQuietMode);
        string replyLength = GetReplyLength(relationship, intent, replyNeed);
        string socialAction = GetSocialAction(relationship, intent, replyNeed);

        return $"""
                [private QQ routing hint - never quote or paraphrase]
                relationship={relationship}
                message_intent={intent}
                social_action={socialAction}
                expected_length={replyLength}
                [/private QQ routing hint]
                """;
    }

    static string GetSocialAction(string relationship, string intent, string replyNeed)
    {
        if (replyNeed == "silent")
            return "ignore_or_cold_ack";
        if (relationship == "owner")
            return "reply_warmly";
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

    static string GetIntent(string rawMessage, string readableMessage)
    {
        string raw = rawMessage ?? "";
        string readable = readableMessage ?? "";
        string plain = string.IsNullOrWhiteSpace(readable)
            ? OneBotSegment.GetPlainText(raw)
            : readable;
        string compact = CompactText(plain);

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
