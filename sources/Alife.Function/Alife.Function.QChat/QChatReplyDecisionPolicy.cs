using System;
using System.Linq;
using System.Text;

namespace Alife.Function.QChat;

public enum QChatReplyAction
{
    Ignore,
    ReactOnly,
    ReplyShort,
    ReplyNormally,
    SharpPushback,
    ToolRoute
}

public sealed record QChatReplyDecision(QChatReplyAction Action, int Score, string Reason);

public static class QChatReplyDecisionPolicy
{
    public static QChatReplyDecision DecidePassiveGroupMessage(
        string? rawMessage,
        QChatSenderRole senderRole,
        bool isMentionedOrWoken,
        bool suppressLowInformation,
        bool mediaOnlyReplyChanceAllowed)
    {
        if (senderRole == QChatSenderRole.Owner)
            return new QChatReplyDecision(QChatReplyAction.ReplyNormally, 100, "owner-priority");

        if (isMentionedOrWoken)
            return new QChatReplyDecision(QChatReplyAction.ReplyNormally, 90, "mention-or-wake");

        if (IsHostile(rawMessage))
            return new QChatReplyDecision(QChatReplyAction.SharpPushback, 75, "hostile");

        if (suppressLowInformation == false)
            return new QChatReplyDecision(QChatReplyAction.ReplyNormally, 50, "normal");

        if (IsMediaOnly(rawMessage))
        {
            return mediaOnlyReplyChanceAllowed
                ? new QChatReplyDecision(QChatReplyAction.ReplyNormally, 45, "media-only-chance")
                : new QChatReplyDecision(QChatReplyAction.Ignore, 5, "low-information");
        }

        if (IsLowInformation(rawMessage))
            return new QChatReplyDecision(QChatReplyAction.Ignore, 10, "low-information");

        return new QChatReplyDecision(QChatReplyAction.ReplyNormally, 50, "normal");
    }

    public static bool IsLowInformation(string? rawMessage)
    {
        string raw = rawMessage ?? "";
        string plain = OneBotSegment.GetPlainText(raw).Trim();
        if (string.IsNullOrWhiteSpace(plain))
            return ContainsLowInformationCqSegment(raw);

        string compact = CompactPassiveText(plain);
        if (compact.Length == 0)
            return ContainsLowInformationCqSegment(raw);

        return compact is "ok" or "k" or "6" or "hhh" or "www"
               or "\u54c8" or "\u54c8\u54c8" or "\u55ef" or "\u554a"
               or "\u8349" or "\u597d" or "\u884c";
    }

    public static bool IsMediaOnly(string? rawMessage)
    {
        string raw = rawMessage ?? "";
        if (ContainsLowInformationCqSegment(raw) == false)
            return false;

        return string.IsNullOrWhiteSpace(OneBotSegment.GetPlainText(raw));
    }

    static bool IsHostile(string? rawMessage)
    {
        string text = OneBotSegment.GetPlainText(rawMessage ?? "");
        string compact = CompactPassiveText(text);
        if (compact.Length == 0)
            return false;

        return compact.Contains("stupid", StringComparison.OrdinalIgnoreCase)
               || compact.Contains("idiot", StringComparison.OrdinalIgnoreCase)
               || compact.Contains("\u50bb", StringComparison.Ordinal)
               || compact.Contains("\u6eda", StringComparison.Ordinal)
               || compact.Contains("\u5783\u573e", StringComparison.Ordinal)
               || compact.Contains("\u5e9f\u7269", StringComparison.Ordinal);
    }

    static bool ContainsLowInformationCqSegment(string raw)
    {
        return raw.Contains("[CQ:image", StringComparison.OrdinalIgnoreCase)
               || raw.Contains("[CQ:face", StringComparison.OrdinalIgnoreCase)
               || raw.Contains("[CQ:mface", StringComparison.OrdinalIgnoreCase);
    }

    static string CompactPassiveText(string text)
    {
        StringBuilder builder = new(text.Length);
        foreach (char ch in text)
        {
            if (char.IsWhiteSpace(ch) || char.IsPunctuation(ch) || char.IsSymbol(ch))
                continue;
            builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }
}
