using System;

namespace Alife.Function.QChat;

public sealed record QChatPersonaFeedbackContext(
    string? AgentId,
    QChatSenderRole SenderRole,
    string? PreferredAddress = null,
    string? RelationshipLabel = null);

public static class QChatPersonaFeedback
{
    public static string BuildLead(QChatPersonaFeedbackContext context)
    {
        string agentId = Normalize(context.AgentId);
        return agentId switch
        {
            "xiayu" when context.SenderRole == QChatSenderRole.Owner => "\u672f\u672f\uff0c\u6211\u770b\u8fc7\u4e86\u3002",
            "xiayu" => "\u72b6\u6001\u5982\u4e0b\u3002",
            "mixu" when UsesAddress(context, "\u5988\u5988") => "\u5988\u5988\uff0c\u6211\u8ba4\u771f\u770b\u8fc7\u4e86\u3002",
            "mixu" when UsesAddress(context, "\u4e3b\u4eba") => "\u4e3b\u4eba\uff0c\u72b6\u6001\u5728\u8fd9\u91cc\u3002",
            "mixu" when context.SenderRole == QChatSenderRole.Owner => "\u4e3b\u4eba\uff0c\u72b6\u6001\u5728\u8fd9\u91cc\u3002",
            "mixu" when UsesAddress(context, "\u524d\u8f88") => "\u524d\u8f88\uff0c\u6211\u628a\u60c5\u51b5\u6574\u7406\u597d\u4e86\u3002",
            "mixu" => "\u6211\u5df2\u7ecf\u6574\u7406\u597d\u4e86\u3002",
            _ => "\u72b6\u6001\u5982\u4e0b\u3002"
        };
    }

    public static string Prefix(QChatPersonaFeedbackContext context, string? body)
    {
        string text = body?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : $"{BuildLead(context)}{Environment.NewLine}{text}";
    }

    static bool UsesAddress(QChatPersonaFeedbackContext context, string expected)
    {
        return string.Equals(context.PreferredAddress?.Trim(), expected, StringComparison.Ordinal);
    }

    static string Normalize(string? agentId)
    {
        return string.IsNullOrWhiteSpace(agentId) ? string.Empty : agentId.Trim().ToLowerInvariant();
    }
}
