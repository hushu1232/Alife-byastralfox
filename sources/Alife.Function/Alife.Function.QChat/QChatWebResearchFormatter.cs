using System;
using Alife.Function.Agent;

namespace Alife.Function.QChat;

public sealed record QChatWebResearchFormatContext(QChatSenderRole SenderRole, OneBotMessageType MessageType);

public static class QChatWebResearchFormatter
{
    const int GroupMemberMaxLength = 420;
    const int OwnerMaxLength = 760;

    public static string Format(AgentWebResearchResult result)
    {
        return Format(result, new QChatWebResearchFormatContext(QChatSenderRole.PrivateGuest, OneBotMessageType.Private));
    }

    public static string Format(AgentWebResearchResult result, QChatWebResearchFormatContext context)
    {
        if (result.Success == false)
            return FormatFailure(result);

        int maxLength = GetMaxLength(context);
        int maxItems = context.SenderRole == QChatSenderRole.Owner ? 3 : 2;
        int conclusionBudget = Math.Max(48, maxLength / 3);
        string conclusion = Compact(result.Answer, conclusionBudget);
        if (string.IsNullOrWhiteSpace(conclusion))
            conclusion = Compact(result.Query, conclusionBudget);

        string formatted = "\u7ed3\u8bba\uff1a" + conclusion;
        int itemIndex = 0;
        foreach (AgentWebResearchEvidence evidence in result.Evidence)
        {
            if (itemIndex >= maxItems)
                break;

            int remainingItems = maxItems - itemIndex;
            int remainingLength = maxLength - formatted.Length - 1;
            if (remainingLength <= 8)
                break;

            int itemBudget = Math.Max(32, remainingLength / remainingItems);
            string item = FormatEvidence(evidence, itemBudget);
            if (string.IsNullOrWhiteSpace(item))
                continue;

            if (formatted.Length + 1 + item.Length > maxLength)
                item = TrimToLength(item, maxLength - formatted.Length - 1);
            if (string.IsNullOrWhiteSpace(item))
                break;

            formatted += "\n" + item;
            itemIndex++;
        }

        return TrimToLength(formatted, maxLength);
    }

    static string FormatFailure(AgentWebResearchResult result)
    {
        return result.Reason switch
        {
            "web_research_cooldown" => "\u641c\u592a\u5feb\u4e86\uff0c\u7b49\u4e00\u4e0b\u3002",
            "web_research_busy" => "\u73b0\u5728\u641c\u7d22\u961f\u5217\u6709\u70b9\u6ee1\uff0c\u7a0d\u540e\u518d\u8bd5\u3002",
            "empty_query" => "\u4f60\u8981\u6211\u641c\u4ec0\u4e48\uff1f",
            "no_results" => "\u6ca1\u67e5\u5230\u53ef\u9760\u6765\u6e90\u3002",
            "public_search_not_configured" => "\u641c\u7d22\u73b0\u5728\u4e0d\u53ef\u7528\u3002",
            _ when string.IsNullOrWhiteSpace(result.Answer) == false => result.Answer.Trim(),
            _ => "\u641c\u7d22\u5931\u8d25\uff0c\u5148\u4e0d\u4e71\u8bf4\u3002",
        };
    }

    static int GetMaxLength(QChatWebResearchFormatContext context)
    {
        return context.SenderRole == QChatSenderRole.Owner
            ? OwnerMaxLength
            : GroupMemberMaxLength;
    }

    static string FormatEvidence(AgentWebResearchEvidence evidence, int budget)
    {
        string title = Compact(evidence.Title, Math.Max(12, budget / 3));
        string summary = Compact(evidence.Summary, Math.Max(16, budget / 2));
        string url = Compact(evidence.Url, Math.Max(12, budget / 4));

        string line = "- ";
        if (string.IsNullOrWhiteSpace(title) == false)
            line += title;
        if (string.IsNullOrWhiteSpace(summary) == false)
            line += string.IsNullOrWhiteSpace(title) ? summary : "\uff1a" + summary;
        if (string.IsNullOrWhiteSpace(url) == false)
            line += " " + url;

        return line == "- " ? "" : TrimToLength(line, budget);
    }

    static string Compact(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        string compact = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return TrimToLength(compact, maxLength);
    }

    static string TrimToLength(string text, int maxLength)
    {
        if (maxLength <= 0)
            return "";
        if (text.Length <= maxLength)
            return text;
        if (maxLength <= 1)
            return "\u2026";

        return text[..(maxLength - 1)].TrimEnd() + "\u2026";
    }
}
