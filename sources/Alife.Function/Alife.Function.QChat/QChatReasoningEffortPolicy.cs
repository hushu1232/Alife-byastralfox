using System;

namespace Alife.Function.QChat;

public static class QChatReasoningEffortPolicy
{
    public static string Decide(QChatSenderRole senderRole, string? message)
    {
        if (senderRole != QChatSenderRole.Owner)
            return "low";

        string text = message?.Trim() ?? string.Empty;
        if (ContainsAny(text, "根因", "深度排查", "深度设计", "系统设计", "架构设计", "完整方案"))
            return "high";
        if (ContainsAny(text, "分析", "调试", "排查", "计划", "方案", "设计", "怎么实现", "如何实现"))
            return "medium";

        return "low";
    }

    static bool ContainsAny(string text, params string[] values)
    {
        foreach (string value in values)
        {
            if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
