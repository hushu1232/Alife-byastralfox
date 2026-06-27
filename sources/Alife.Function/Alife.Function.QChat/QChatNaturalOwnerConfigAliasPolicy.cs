using System;
using System.Linq;

namespace Alife.Function.QChat;

public static class QChatNaturalOwnerConfigAliasPolicy
{
    public static bool TryMapCommand(string? text, out string command)
    {
        command = "";
        string normalized = text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (IsTimingStatusAlias(normalized))
        {
            command = "/qchat timing status";
            return true;
        }

        if (IsTimingOffAlias(normalized))
        {
            command = "/qchat timing off";
            return true;
        }

        if (IsTimingOnAlias(normalized))
        {
            command = "/qchat timing on";
            return true;
        }

        return false;
    }

    static bool IsTimingStatusAlias(string text)
    {
        return ContainsAny(text, "延时设置", "延迟设置", "回复节奏", "回复设置")
               && ContainsAny(text, "看看", "看一下", "检查", "状态", "怎么样", "如何");
    }

    static bool IsTimingOnAlias(string text)
    {
        return ContainsAny(text, "说慢一点", "说慢点", "回复慢一点", "回复慢点", "回慢一点", "回慢点")
               || ContainsAny(text, "先合并一下多段消息", "合并一下多段消息", "合并多段消息", "等我连发", "等我说完");
    }

    static bool IsTimingOffAlias(string text)
    {
        return ContainsAny(text, "回复快一点", "回复快点", "回快一点", "回快点", "不用等我连发了", "不用等我连发", "别等我连发", "不用合并多段消息");
    }

    static bool ContainsAny(string text, params string[] values)
    {
        return values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
    }
}
