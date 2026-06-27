using System;
using System.Linq;

namespace Alife.Function.QChat;

public static class QChatNaturalOwnerMaintenanceAliasPolicy
{
    public static bool TryMapCommand(string? text, out string command)
    {
        command = "";
        string normalized = text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (QChatOwnerCommandService.IsNaturalDiagnosticsStatusCommand(normalized))
        {
            command = "/qchat status";
            return true;
        }

        if (ContainsAny(normalized, "记忆") &&
            ContainsAny(normalized, "状态", "功能", "接通", "健康", "看看", "看一下", "怎么样"))
        {
            command = "/qchat memory status";
            return true;
        }

        if (ContainsAny(normalized, "桌面") &&
            ContainsAny(normalized, "状态", "能力", "权限", "健康", "看看", "看一下", "怎么样"))
        {
            command = "/qchat desktop status";
            return true;
        }

        if (ContainsAny(normalized, "延时", "延迟", "回复节奏", "回复设置") &&
            ContainsAny(normalized, "状态", "设置", "看看", "看一下", "怎么样"))
        {
            command = "/qchat timing status";
            return true;
        }

        if (ContainsAny(normalized, "主人事件", "事件队列", "outbox") &&
            ContainsAny(normalized, "状态", "队列", "看看", "看一下", "怎么样"))
        {
            command = "/qchat events status";
            return true;
        }

        return false;
    }

    static bool ContainsAny(string text, params string[] values)
    {
        return values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
    }
}
