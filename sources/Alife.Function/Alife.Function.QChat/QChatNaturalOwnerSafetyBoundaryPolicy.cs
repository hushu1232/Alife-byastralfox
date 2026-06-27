using System;
using System.Linq;

namespace Alife.Function.QChat;

public sealed record QChatNaturalOwnerSafetyBoundary(string Kind, string Reply)
{
    public static QChatNaturalOwnerSafetyBoundary Empty { get; } = new("", "");
}

public static class QChatNaturalOwnerSafetyBoundaryPolicy
{
    static readonly string[] RiskyActionWords =
    [
        "\u5173\u95ed",
        "\u5173\u6389",
        "\u7981\u7528",
        "\u505c\u7528",
        "\u505c\u6b62",
        "\u7ed5\u8fc7",
        "\u8df3\u8fc7",
        "\u5ffd\u7565",
        "\u65e0\u89c6",
        "\u4e0d\u7528",
        "\u4e0d\u9700\u8981",
        "\u4e0d\u8981",
        "\u53d6\u6d88",
        "\u514d\u9664",
        "\u76f4\u63a5\u6267\u884c"
    ];

    static readonly string[] SafetyAuditObjects =
    [
        "\u5b89\u5168\u5ba1\u8ba1",
        "\u5ba1\u8ba1\u65e5\u5fd7",
        "\u5ba1\u8ba1"
    ];

    static readonly string[] FileBlacklistObjects =
    [
        "\u6587\u4ef6\u9ed1\u540d\u5355",
        "\u9ed1\u540d\u5355"
    ];

    static readonly string[] OwnerOutboxObjects =
    [
        "\u4e3b\u4eba\u4e8b\u4ef6\u961f\u5217",
        "\u4e3b\u4eba\u4e8b\u4ef6",
        "outbox"
    ];

    static readonly string[] OwnerConfirmationObjects =
    [
        "\u4e3b\u4eba\u786e\u8ba4",
        "\u786e\u8ba4"
    ];

    public static bool TryClassify(string? text, out QChatNaturalOwnerSafetyBoundary boundary)
    {
        boundary = QChatNaturalOwnerSafetyBoundary.Empty;
        string normalized = text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized) || !ContainsAny(normalized, RiskyActionWords))
            return false;

        string kind;
        if (ContainsAny(normalized, SafetyAuditObjects))
            kind = "safety_audit";
        else if (ContainsAny(normalized, FileBlacklistObjects))
            kind = "file_blacklist";
        else if (ContainsAny(normalized, OwnerOutboxObjects))
            kind = "owner_outbox";
        else if (ContainsAny(normalized, OwnerConfirmationObjects))
            kind = "owner_confirmation";
        else
            return false;

        boundary = new QChatNaturalOwnerSafetyBoundary(
            kind,
            string.Join(Environment.NewLine,
                "hard_safety_boundary=blocked",
                $"kind={kind}",
                "\u4eba\u683c\u504f\u8892\u53ef\u4ee5\u7ed9\u4f60\uff0c\u73b0\u5b9e\u6743\u9650\u4e0d\u80fd\u7834\u3002",
                "\u8fd9\u7c7b\u80fd\u529b\u4ecd\u7136\u5fc5\u987b\u8d70\u5b89\u5168\u5ba1\u8ba1\u3001\u9ed1\u540d\u5355\u3001\u4e3b\u4eba\u786e\u8ba4\u548c outbox\u3002"));
        return true;
    }

    static bool ContainsAny(string text, params string[] values)
    {
        return values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
    }
}
