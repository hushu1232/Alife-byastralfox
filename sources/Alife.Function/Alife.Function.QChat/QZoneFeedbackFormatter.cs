using System;

namespace Alife.Function.QChat;

public static class QZoneFeedbackFormatter
{
    public static string Format(string? personaId, bool succeeded, string? safeReason)
    {
        QChatPersonaFeedbackContext context = new(personaId, QChatSenderRole.PrivateGuest);
        return QChatPersonaFeedback.Prefix(context, FormatBody(succeeded, safeReason));
    }

    static string FormatBody(bool succeeded, string? safeReason)
    {
        string reason = safeReason?.Trim() ?? string.Empty;
        if (reason.StartsWith("published", StringComparison.OrdinalIgnoreCase))
            return "QQ\u7a7a\u95f4\u52a8\u6001\u5df2\u53d1\u5e03\u3002";
        if (reason.StartsWith("commented", StringComparison.OrdinalIgnoreCase))
            return "\u5df2\u8bc4\u8bbaQQ\u7a7a\u95f4\u52a8\u6001\u3002";
        if (reason.StartsWith("replied", StringComparison.OrdinalIgnoreCase))
            return "\u5df2\u56de\u590dQQ\u7a7a\u95f4\u8bc4\u8bba\u3002";
        if (reason.StartsWith("liked", StringComparison.OrdinalIgnoreCase))
            return "\u5df2\u70b9\u8d5eQQ\u7a7a\u95f4\u52a8\u6001\u3002";
        if (reason.StartsWith("deleted", StringComparison.OrdinalIgnoreCase))
            return "\u5df2\u5220\u9664QQ\u7a7a\u95f4\u5185\u5bb9\u3002";
        if (reason.StartsWith("qzone_http_", StringComparison.OrdinalIgnoreCase))
            return "QQ\u7a7a\u95f4\u63a5\u53e3\u6682\u65f6\u4e0d\u53ef\u7528\uff0c\u8bf7\u7a0d\u540e\u518d\u8bd5\u3002";
        if (reason.StartsWith("qzone_api_", StringComparison.OrdinalIgnoreCase))
            return "QQ\u7a7a\u95f4\u63a5\u53e3\u6ca1\u6709\u786e\u8ba4\u672c\u6b21\u64cd\u4f5c\u3002";
        if (reason.StartsWith("qzone_session_", StringComparison.OrdinalIgnoreCase))
            return "QQ\u7a7a\u95f4\u767b\u5f55\u72b6\u6001\u4e0d\u53ef\u7528\uff0c\u8bf7\u68c0\u67e5\u8fde\u63a5\u540e\u91cd\u8bd5\u3002";

        return succeeded
            ? "QQ\u7a7a\u95f4\u64cd\u4f5c\u5df2\u5b8c\u6210\u3002"
            : "QQ\u7a7a\u95f4\u64cd\u4f5c\u6ca1\u6709\u5b8c\u6210\u3002";
    }
}
