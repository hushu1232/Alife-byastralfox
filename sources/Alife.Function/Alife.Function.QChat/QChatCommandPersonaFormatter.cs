using System;

namespace Alife.Function.QChat;

public static class QChatCommandPersonaFormatter
{
    public static string Format(string? agentId, QChatSenderRole senderRole, string? text)
    {
        string body = text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(body))
            return "";

        if (IsDenial(body))
            return body;

        string normalizedAgent = string.IsNullOrWhiteSpace(agentId) ? "" : agentId.Trim().ToLowerInvariant();
        string lead = normalizedAgent switch
        {
            "xiayu" when senderRole == QChatSenderRole.Owner => "术术，我看过了。",
            "mixu" when senderRole == QChatSenderRole.Owner => "主人，状态在这里。",
            _ => "状态如下。"
        };

        return $"{lead}{Environment.NewLine}{body}";
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
