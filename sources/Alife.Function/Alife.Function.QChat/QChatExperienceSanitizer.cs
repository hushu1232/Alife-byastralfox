using System;
using System.Text.RegularExpressions;

namespace Alife.Function.QChat;

internal static class QChatExperienceSanitizer
{
    public static string SanitizeOutgoing(QChatConfig? config, OneBotMessageType type, long targetId, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "";

        string sanitized = message.Trim();
        if (IsXiayu(config) == false)
            return sanitized;

        if (IsHumanFacingNoReplyState(sanitized))
            return "";

        sanitized = RemoveXiayuPersonaBleed(sanitized);
        sanitized = RewriteXiayuMachineIdentity(sanitized);
        sanitized = RemoveRoutingLabels(sanitized, type);
        return sanitized.Trim();
    }

    public static bool IsHumanFacingNoReplyState(string message)
    {
        return QChatVisibleTextPolicy.IsHumanInvisibleStateText(message);
    }

    static bool IsXiayu(QChatConfig? config) => config?.BotId == 2905391496;

    static string RemoveXiayuPersonaBleed(string value)
    {
        return value
            .Replace("主人喵", "术术", StringComparison.Ordinal)
            .Replace("猫娘", "", StringComparison.Ordinal)
            .Replace("小鱼干", "零食", StringComparison.Ordinal)
            .Replace("喵", "", StringComparison.Ordinal)
            .Replace("嘛！", "。", StringComparison.Ordinal);
    }

    static string RewriteXiayuMachineIdentity(string value)
    {
        string result = value
            .Replace("你的高智商恋人型陪伴智能体", "夏羽", StringComparison.Ordinal)
            .Replace("你的陪伴智能体", "夏羽", StringComparison.Ordinal)
            .Replace("作为夏羽，", "", StringComparison.Ordinal)
            .Replace("作为夏羽", "", StringComparison.Ordinal)
            .Replace("根据设定，", "", StringComparison.Ordinal)
            .Replace("根据设定", "", StringComparison.Ordinal);

        result = RewriteXiayuSelfIdentity(result);

        return result
            .Replace("我是夏羽，你的夏羽", "我是夏羽", StringComparison.Ordinal)
            .Replace("我是夏羽夏羽", "我是夏羽", StringComparison.Ordinal)
            .Replace("你的夏羽", "夏羽", StringComparison.Ordinal);
    }

    static string RewriteXiayuSelfIdentity(string value)
    {
        string identity = @"(?:AI助手|AI模型|AI智能体|人工智能助手|人工智能模型|人工智能|大语言模型|语言模型|模型|机器人|智能体|助手|bot)";
        string article = @"(?:一个|一名|一款|1个)?";

        string result = Regex.Replace(
            value,
            $@"作为{article}{identity}[，,、\s]*我",
            "我",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        result = Regex.Replace(
            result,
            $@"(?<![A-Za-z0-9_])我(?:是|只是|属于|本质上是|算是){article}{identity}(?![A-Za-z0-9_])",
            "我是夏羽",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return result;
    }

    static string RemoveRoutingLabels(string value, OneBotMessageType type)
    {
        return type == OneBotMessageType.Private
            ? value.Replace("私聊主人：", "", StringComparison.Ordinal)
                .Replace("私聊回复：", "", StringComparison.Ordinal)
                .Replace("私聊回应：", "", StringComparison.Ordinal)
                .Replace("主人私聊：", "", StringComparison.Ordinal)
            : value.Replace("群里回复：", "", StringComparison.Ordinal)
                .Replace("群里回应：", "", StringComparison.Ordinal)
                .Replace("群聊回复：", "", StringComparison.Ordinal)
                .Replace("群聊回应：", "", StringComparison.Ordinal)
                .Replace("群内回复：", "", StringComparison.Ordinal)
                .Replace("群回复：", "", StringComparison.Ordinal);
    }

}
