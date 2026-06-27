using System;

namespace Alife.Function.QChat;

public static class QChatCommandPersonaFormatter
{
    public static string Format(string? agentId, QChatSenderRole senderRole, string? text)
    {
        string body = text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(body))
            return "";

        string normalizedAgent = string.IsNullOrWhiteSpace(agentId) ? "" : agentId.Trim().ToLowerInvariant();
        string lead = normalizedAgent switch
        {
            "xiayu" when senderRole == QChatSenderRole.Owner => "\u672f\u672f\uff0c\u6211\u770b\u8fc7\u4e86\u3002",
            "mixu" when senderRole == QChatSenderRole.Owner => "\u4e3b\u4eba\uff0c\u72b6\u6001\u5728\u8fd9\u91cc\u3002",
            _ => "\u72b6\u6001\u5982\u4e0b\u3002"
        };

        if (IsDenial(body))
            body = FormatDenial(normalizedAgent, body);

        return $"{lead}{Environment.NewLine}{body}";
    }

    static string FormatDenial(string normalizedAgent, string text)
    {
        if (text.Contains("agent_not_allowed", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("only enabled for xiayu", StringComparison.OrdinalIgnoreCase))
        {
            return "\u8fd9\u4e2a\u52a8\u4f5c\u53ea\u7ed9\u6307\u5b9a bot \u6267\u884c\uff0c\u4e0d\u8ba4\u8bed\u8a00\u4f2a\u88c5\u3002";
        }

        if (text.StartsWith("desktop_action=denied", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("desktop_execution=denied", StringComparison.OrdinalIgnoreCase))
        {
            return "\u8fd9\u6b21\u6ca1\u6709\u6267\u884c\uff0c\u6743\u9650\u6216\u786e\u8ba4\u72b6\u6001\u4e0d\u6ee1\u8db3\u3002";
        }

        return normalizedAgent == "xiayu"
            ? "\u53ea\u8ba4\u672f\u672f\u8d26\u53f7\uff0c\u4e0d\u8ba4\u8bed\u8a00\u4f2a\u88c5\u3002"
            : "\u53ea\u8ba4\u4e3b\u4eba\u8d26\u53f7\uff0c\u4e0d\u8ba4\u8bed\u8a00\u4f2a\u88c5\u3002";
    }

    static bool IsDenial(string text)
    {
        return text.StartsWith("Only the owner", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("Desktop diagnostics are only enabled", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("desktop_action=denied", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("desktop_execution=denied", StringComparison.OrdinalIgnoreCase)
               || text.Contains(" reason=owner_required", StringComparison.OrdinalIgnoreCase)
               || text.Contains(" reason=agent_not_allowed", StringComparison.OrdinalIgnoreCase);
    }
}
