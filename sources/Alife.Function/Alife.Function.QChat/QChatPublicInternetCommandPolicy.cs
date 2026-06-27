using System;
using System.Text.RegularExpressions;
using Alife.Function.Agent;

namespace Alife.Function.QChat;

public enum QChatPublicInternetCommandKind
{
    None,
    Search,
    RagQuery
}

public sealed record QChatPublicInternetCommand(
    QChatPublicInternetCommandKind Kind,
    string Query)
{
    public static QChatPublicInternetCommand None { get; } = new(QChatPublicInternetCommandKind.None, "");
}

public sealed record QChatPublicInternetCommandContext(
    QChatSenderRole SenderRole,
    QChatPublicInternetCommandKind Kind,
    string Query,
    int MaxQueryChars,
    bool EnablePublicSearch,
    bool EnablePublicRagQuery,
    bool AllowGroupMemberPublicSearch = true,
    bool AllowGroupMemberExternalRagQuery = true);

public sealed record QChatPublicInternetCommandDecision(bool Allowed, string Reason);

public static class QChatPublicInternetCommandPolicy
{
    public static QChatPublicInternetCommand Parse(string? text)
    {
        string normalized = text?.Trim() ?? string.Empty;
        if (normalized.StartsWith("/qchat", StringComparison.OrdinalIgnoreCase))
            return QChatPublicInternetCommand.None;

        if (TryParsePrefix(normalized, "/search ", out string search))
            return new QChatPublicInternetCommand(QChatPublicInternetCommandKind.Search, search);

        if (TryParsePrefix(normalized, "/rag ", out string rag))
            return new QChatPublicInternetCommand(QChatPublicInternetCommandKind.RagQuery, rag);

        return QChatPublicInternetCommand.None;
    }

    public static QChatPublicInternetCommand ParseMessage(
        OneBotMessageType messageType,
        long botId,
        string? rawMessage,
        string? readableText)
    {
        QChatPublicInternetCommand explicitCommand = Parse(readableText);
        if (explicitCommand.Kind != QChatPublicInternetCommandKind.None)
            return explicitCommand;

        if (messageType != OneBotMessageType.Group && messageType != OneBotMessageType.Private)
            return QChatPublicInternetCommand.None;

        string raw = rawMessage ?? string.Empty;
        if (messageType == OneBotMessageType.Group && (botId <= 0 || IsMentioned(raw, botId) == false))
            return QChatPublicInternetCommand.None;

        string plain = OneBotSegment.GetPlainText(raw);
        if (plain.Contains("\u6d4f\u89c8\u5668", StringComparison.OrdinalIgnoreCase))
            return QChatPublicInternetCommand.None;

        string query = ExtractSearchQuery(plain);
        return query.Length > 0
            ? new QChatPublicInternetCommand(QChatPublicInternetCommandKind.Search, query)
            : QChatPublicInternetCommand.None;
    }

    public static QChatPublicInternetCommandDecision Evaluate(QChatPublicInternetCommandContext context)
    {
        if (context.Kind == QChatPublicInternetCommandKind.None)
            return new QChatPublicInternetCommandDecision(false, "not_public_internet_command");

        if (context.SenderRole is not (QChatSenderRole.Owner or QChatSenderRole.GroupMember))
            return new QChatPublicInternetCommandDecision(false, "public_internet_sender_not_allowed");

        AgentWebAccessCapability capability = context.Kind == QChatPublicInternetCommandKind.Search
            ? AgentWebAccessCapability.PublicSearch
            : AgentWebAccessCapability.ExternalRagQuery;
        AgentWebAccessDecision decision = AgentWebAccessRouter.Evaluate(new AgentWebAccessRequest(
            MapActorRole(context.SenderRole),
            capability,
            context.Query,
            new AgentWebAccessConfig
            {
                EnablePublicSearch = context.EnablePublicSearch,
                EnableExternalRagQuery = context.EnablePublicRagQuery,
                AllowGroupMemberPublicSearch = context.AllowGroupMemberPublicSearch,
                AllowGroupMemberExternalRagQuery = context.AllowGroupMemberExternalRagQuery,
                MaxQueryChars = context.MaxQueryChars
            }));

        string reason = decision.Reason == "query_too_long"
            ? "public_query_too_long"
            : decision.Reason;
        if (reason == "external_rag_query_disabled")
            reason = "public_rag_disabled";
        return new QChatPublicInternetCommandDecision(decision.Allowed, reason);
    }

    static AgentWebAccessActorRole MapActorRole(QChatSenderRole senderRole)
    {
        return senderRole switch
        {
            QChatSenderRole.Owner => AgentWebAccessActorRole.Owner,
            QChatSenderRole.GroupMember => AgentWebAccessActorRole.GroupMember,
            QChatSenderRole.PrivateGuest => AgentWebAccessActorRole.PrivateGuest,
            _ => AgentWebAccessActorRole.Unknown
        };
    }

    static bool TryParsePrefix(string text, string prefix, out string value)
    {
        value = "";
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == false)
            return false;

        value = text[prefix.Length..].Trim();
        return value.Length > 0;
    }

    static bool IsMentioned(string rawMessage, long botId)
    {
        return Regex.IsMatch(
            rawMessage,
            $@"\[CQ:at,[^\]]*qq={Regex.Escape(botId.ToString())}(?:,|\])",
            RegexOptions.CultureInvariant);
    }

    static string ExtractSearchQuery(string text)
    {
        string normalized = Regex.Replace(text.Trim(), @"\s+", " ");
        if (normalized.Length == 0)
            return "";

        return ExtractSimpleSearchQuery(normalized);
    }

    static string ExtractSimpleSearchQuery(string normalized)
    {
        string[] triggers =
        [
            "\u5e2e\u6211\u641c\u4e00\u4e0b",
            "\u5e2e\u6211\u641c\u7d22\u4e00\u4e0b",
            "\u641c\u7d22\u4e00\u4e0b",
            "\u641c\u4e00\u4e0b",
            "\u67e5\u4e00\u4e0b",
            "\u5e2e\u6211\u67e5",
            "\u5e2e\u6211\u627e",
            "\u8054\u7f51\u67e5",
            "\u67e5\u6700\u65b0",
            "\u627e\u8d44\u6599",
            "\u6709\u6ca1\u6709\u516c\u5f00\u4fe1\u606f"
        ];

        foreach (string trigger in triggers)
        {
            if (normalized.StartsWith(trigger, StringComparison.OrdinalIgnoreCase) == false)
                continue;

            return CleanQuery(normalized[trigger.Length..]);
        }

        if (TryParseEnglishSearchTrigger(normalized, "search", out string searchQuery))
            return searchQuery;

        return TryParseEnglishSearchTrigger(normalized, "look up", out string lookUpQuery)
            ? lookUpQuery
            : "";
    }

    static bool TryParseEnglishSearchTrigger(string normalized, string trigger, out string query)
    {
        query = "";
        if (normalized.StartsWith(trigger, StringComparison.OrdinalIgnoreCase) == false)
            return false;

        if (normalized.Length > trigger.Length && char.IsWhiteSpace(normalized[trigger.Length]) == false)
            return false;

        query = CleanQuery(normalized[trigger.Length..]);
        return query.Length > 0;
    }

    static string CleanQuery(string query)
    {
        string cleaned = Regex.Replace(query.Trim(), @"^(?:\u4e00\u4e0b|\u770b\u770b|\u770b\u4e00\u4e0b)\s*", "");
        cleaned = Regex.Replace(cleaned, @"\s+", " ");
        return cleaned.Trim(' ', '：', ':', ',', '，', '。', '?', '？', '!', '！');
    }
}
