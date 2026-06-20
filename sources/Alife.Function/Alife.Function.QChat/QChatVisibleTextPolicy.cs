using System;
using System.Linq;

namespace Alife.Function.QChat;

internal static class QChatVisibleTextPolicy
{
    static readonly string[] AlwaysHiddenMarkers =
    [
        "心理状态",
        "内心",
        "心想",
        "状态：",
        "状态:",
        "OS：",
        "OS:",
        "os：",
        "os:"
    ];

    static readonly string[] StageStateMarkers =
    [
        "安静",
        "待机",
        "等待",
        "观察",
        "旁观",
        "沉默",
        "不语",
        "不回",
        "不回复",
        "不回覆",
        "不回应",
        "不回應",
        "不作",
        "不做",
        "不插话",
        "不插話",
        "看着",
        "看著",
        "听着",
        "聽著",
        "待命"
    ];

    public static bool IsHumanInvisibleStateText(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        string trimmed = message.Trim();
        string compact = Compact(trimmed);
        if (AlwaysHiddenMarkers.Any(marker => compact.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            return true;

        UnwrappedText unwrapped = UnwrapDirective(trimmed);
        if (unwrapped.WasWrapped == false)
            return compact is "沉默" or "silent" or "stayquiet" or "noreply";

        string innerCompact = Compact(unwrapped.Text);
        return StageStateMarkers.Any(marker => innerCompact.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    static UnwrappedText UnwrapDirective(string value)
    {
        string current = value.Trim();
        bool unwrapped = false;
        while (current.Length >= 2 && IsWrappingPair(current[0], current[^1]))
        {
            current = current[1..^1].Trim();
            unwrapped = true;
        }

        return new UnwrappedText(current, unwrapped);
    }

    static string Compact(string value)
    {
        return value
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace("\t", "", StringComparison.Ordinal)
            .Replace("\r", "", StringComparison.Ordinal)
            .Replace("\n", "", StringComparison.Ordinal)
            .Replace("，", ",", StringComparison.Ordinal)
            .Replace("、", ",", StringComparison.Ordinal);
    }

    static bool IsWrappingPair(char start, char end)
    {
        return (start == '(' && end == ')')
               || (start == '（' && end == '）')
               || (start == '[' && end == ']')
               || (start == '【' && end == '】')
               || (start == '*' && end == '*');
    }

    readonly record struct UnwrappedText(string Text, bool WasWrapped);
}
