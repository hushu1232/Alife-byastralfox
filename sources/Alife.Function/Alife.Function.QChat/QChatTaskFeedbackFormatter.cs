using System;

namespace Alife.Function.QChat;

public enum QChatTaskFeedbackKind
{
    Progress,
    Succeeded,
    Failed,
    Uncertain,
    Canceled
}

public sealed record QChatTaskFeedbackContext(
    QChatTaskFeedbackKind Kind,
    string TaskType,
    string? FileName,
    long? TargetId,
    string? Detail);

public static class QChatTaskFeedbackFormatter
{
    public static string Format(QChatTaskFeedbackContext context)
    {
        string fileName = string.IsNullOrWhiteSpace(context.FileName)
            ? "\u6587\u4ef6"
            : context.FileName.Trim();
        string target = FormatTarget(context);
        string detail = NormalizeDetail(context.Detail);

        return context.Kind switch
        {
            QChatTaskFeedbackKind.Progress => $"{fileName} \u5728\u4f20\u5230 {target}\uff0c\u7b49\u63a5\u53e3\u8fd4\u56de\u3002",
            QChatTaskFeedbackKind.Succeeded => $"{fileName} \u5df2\u4e0a\u4f20\u5230 {target}",
            QChatTaskFeedbackKind.Failed => string.IsNullOrWhiteSpace(detail)
                ? $"{fileName} \u6ca1\u4f20\u6210\uff0c\u76ee\u6807\u662f {target}\u3002"
                : $"{fileName} \u6ca1\u4f20\u6210\uff0c\u76ee\u6807\u662f {target}\u3002{detail}",
            QChatTaskFeedbackKind.Uncertain => string.IsNullOrWhiteSpace(detail)
                ? $"{fileName} \u63a5\u53e3\u6ca1\u786e\u8ba4\uff0c\u53ef\u80fd\u8fd8\u5728\u5904\u7406\u3002"
                : $"{fileName} \u63a5\u53e3\u6ca1\u786e\u8ba4\uff0c\u53ef\u80fd\u8fd8\u5728\u5904\u7406\u3002{detail}",
            QChatTaskFeedbackKind.Canceled => $"{fileName} \u5df2\u53d6\u6d88\u3002",
            _ => $"{fileName} \u72b6\u6001\u4e0d\u660e\u3002"
        };
    }

    static string FormatTarget(QChatTaskFeedbackContext context)
    {
        if (context.TargetId.HasValue == false)
            return "\u76ee\u6807\u4f4d\u7f6e";

        return context.TaskType.Contains("private", StringComparison.OrdinalIgnoreCase)
            ? $"{context.TargetId.Value} \u79c1\u804a\u6587\u4ef6"
            : $"{context.TargetId.Value} \u7fa4\u6587\u4ef6";
    }

    static string NormalizeDetail(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return "";

        string normalized = detail.Trim();
        return normalized.Length <= 180 ? normalized : normalized[..180];
    }
}
