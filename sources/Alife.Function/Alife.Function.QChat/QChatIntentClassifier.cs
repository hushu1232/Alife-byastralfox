using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Alife.Function.QChat;

public enum QChatIntentKind
{
    None,
    RecallMessage,
    GroupFileUpload,
    PrivateFileUpload,
    AllowlistUpdate,
    Poke,
    QuietMode,
    GroupWake
}

public enum QChatIntentTargetKind
{
    None,
    CurrentSession,
    RepliedMessage,
    RecentBotMessage,
    TextMatch,
    ExplicitGroup,
    ExplicitUser,
    ExplicitFile
}

public sealed record QChatIntentInput(
    string PlainText,
    string ReadableText,
    string RawMessage,
    bool HasReply,
    long? ReplyMessageId)
{
    public static QChatIntentInput FromText(string text)
    {
        string value = text ?? string.Empty;
        return new QChatIntentInput(value, value, value, false, null);
    }
}

public sealed record QChatIntentDecision(
    QChatIntentKind Kind,
    bool IsCandidate,
    bool IsConfirmed,
    double Confidence,
    QChatIntentTargetKind TargetKind,
    string? TargetText,
    long? TargetId,
    string? FilePath,
    bool HasNegation,
    bool IsMetaDiscussion,
    string Reason);

public static class QChatIntentClassifier
{
    public static QChatIntentDecision ClassifyRecall(QChatIntentInput input)
    {
        string text = Merge(input.PlainText, input.ReadableText);
        bool candidate = ContainsAny(text, "撤", "撤回", "收回", "删掉", "删除");
        if (candidate == false)
            return None(QChatIntentKind.RecallMessage, "no recall keyword");

        bool negation = ContainsAny(text, "不要撤", "别撤", "不用撤", "不要删除", "别删除");
        bool meta = ContainsAny(text, "是不是", "会不会", "能不能", "为什么", "怎么", "失败", "不会撤回", "测试", "验证", "演示", "试试");
        bool command = ContainsAny(text, "撤了", "撤回", "收回", "删掉", "删除", "撤你", "撤刚才", "撤上一", "把那条撤", "把这条撤");
        bool confirmed = command && negation == false && meta == false;
        QChatIntentTargetKind target = input.HasReply
            ? QChatIntentTargetKind.RepliedMessage
            : QChatIntentTargetKind.RecentBotMessage;

        return new QChatIntentDecision(
            QChatIntentKind.RecallMessage,
            true,
            confirmed,
            confirmed ? 0.9 : 0.35,
            confirmed ? target : QChatIntentTargetKind.None,
            null,
            input.ReplyMessageId,
            null,
            negation,
            meta,
            confirmed ? "confirmed recall command" : "recall keyword is not an execution command");
    }

    public static QChatIntentDecision ClassifyFileUpload(QChatIntentInput input)
    {
        string commandText = input.PlainText;
        string allText = Merge(input.PlainText, input.ReadableText, input.RawMessage);
        bool candidate = ContainsAny(allText, "发", "发送", "传", "上传", "send", "upload", "file", "文件", "群");
        if (candidate == false)
            return None(QChatIntentKind.GroupFileUpload, "no file-upload keyword");

        bool metadataOnly = string.IsNullOrWhiteSpace(commandText) ||
                            (ContainsAny(input.RawMessage, "[CQ:forward", "[CQ:image") &&
                             ContainsAny(input.ReadableText, "fileid=", "转发消息内容", "图片:"));
        bool confirmed = metadataOnly == false &&
                         ContainsAny(commandText, "发", "发送", "传", "上传", "send", "upload") &&
                         ContainsAny(commandText, "文件", ".c", "file", "hello_world") &&
                         ContainsAny(commandText, "群", "群文件", "这里", "当前群", "group");

        return new QChatIntentDecision(
            QChatIntentKind.GroupFileUpload,
            true,
            confirmed,
            confirmed ? 0.86 : 0.2,
            confirmed ? QChatIntentTargetKind.CurrentSession : QChatIntentTargetKind.None,
            null,
            null,
            ExtractWindowsPath(commandText),
            false,
            false,
            confirmed ? "confirmed explicit file upload request" : "file-upload keywords came from metadata or incomplete command");
    }

    public static QChatIntentDecision ClassifyAllowlist(QChatIntentInput input, long currentGroupId)
    {
        string candidateText = Merge(input.PlainText, input.ReadableText);
        bool candidate = ContainsAny(candidateText, "白名单", "allowlist", "qchat_allowlist_update");
        if (candidate == false)
            return None(QChatIntentKind.AllowlistUpdate, "no allowlist keyword");

        string commandText = input.PlainText ?? string.Empty;
        bool forwardedOrReadableOnly = string.IsNullOrWhiteSpace(commandText) ||
                                       ContainsAny(input.RawMessage, "[CQ:forward") ||
                                       ContainsAny(input.ReadableText, "转发消息内容");
        bool commandContainsAllowlist = ContainsAny(commandText, "白名单", "allowlist", "qchat_allowlist_update");
        if (commandContainsAllowlist == false)
        {
            return new QChatIntentDecision(
                QChatIntentKind.AllowlistUpdate,
                true,
                false,
                forwardedOrReadableOnly ? 0.18 : 0.3,
                QChatIntentTargetKind.None,
                null,
                null,
                null,
                false,
                false,
                forwardedOrReadableOnly
                    ? "allowlist keyword came from forward/readable content"
                    : "allowlist keyword is not in the current command text");
        }

        string action = ContainsAny(commandText, "移除", "删除", "remove") ? "remove" : "add";
        long id = ExtractFirstId(commandText);
        if (id == 0 && ContainsAny(commandText, "这个群", "本群", "当前群"))
            id = currentGroupId;
        bool confirmed = id > 0 && ContainsAny(commandText, "群", "group", "target=\"group\"");

        return new QChatIntentDecision(
            QChatIntentKind.AllowlistUpdate,
            true,
            confirmed,
            confirmed ? 0.88 : 0.3,
            confirmed ? QChatIntentTargetKind.ExplicitGroup : QChatIntentTargetKind.None,
            confirmed ? $"group:{action}" : null,
            confirmed ? id : null,
            null,
            false,
            false,
            confirmed ? "confirmed group allowlist update" : "allowlist target is missing");
    }

    public static QChatIntentDecision ClassifyQuietMode(
        QChatIntentInput input,
        string? sleepCommand,
        string? wakeCommand)
    {
        string text = NormalizeQuietModeCommand(input.PlainText);
        string sleep = NormalizeQuietModeCommand(sleepCommand);
        string wake = NormalizeQuietModeCommand(wakeCommand);
        bool sleepMatched = text.Length > 0 && sleep.Length > 0 && text.Equals(sleep, StringComparison.Ordinal);
        bool wakeMatched = text.Length > 0 && wake.Length > 0 && text.Equals(wake, StringComparison.Ordinal);
        if (sleepMatched == false && wakeMatched == false)
            return None(QChatIntentKind.QuietMode, "message is not an exact quiet-mode command");
        if (sleepMatched && wakeMatched)
        {
            return new QChatIntentDecision(
                QChatIntentKind.QuietMode,
                true,
                false,
                0,
                QChatIntentTargetKind.None,
                null,
                null,
                null,
                false,
                false,
                "quiet-mode commands are ambiguous");
        }

        string action = wakeMatched ? "wake" : "sleep";

        return new QChatIntentDecision(
            QChatIntentKind.QuietMode,
            true,
            true,
            1,
            QChatIntentTargetKind.CurrentSession,
            action,
            null,
            null,
            false,
            false,
            $"confirmed exact quiet-mode {action} command");
    }

    static string NormalizeQuietModeCommand(string? text)
    {
        return new string((text ?? string.Empty)
            .Where(ch => char.IsWhiteSpace(ch) == false)
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    public static QChatIntentDecision ClassifyGroupWake(
        QChatIntentInput input,
        IEnumerable<string> wakingWords,
        bool isAtBot)
    {
        string text = Merge(input.PlainText, input.ReadableText);
        if (isAtBot)
        {
            return new QChatIntentDecision(
                QChatIntentKind.GroupWake,
                true,
                true,
                1,
                QChatIntentTargetKind.CurrentSession,
                "at",
                null,
                null,
                false,
                false,
                "bot was mentioned by at");
        }

        string[] words = wakingWords
            .Where(word => string.IsNullOrWhiteSpace(word) == false)
            .Select(word => word.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        bool hasWakeName = words.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
        if (hasWakeName == false)
            return None(QChatIntentKind.GroupWake, "no wake target");

        bool negation = ContainsAny(text, "不是在叫", "不叫", "别出来", "不用出来", "不用回");
        bool meta = ContainsAny(text, "讨论", "这个名字", "会不会被", "是不是会", "为什么", "怎么唤醒", "测试", "验证", "演示", "试试");
        bool callAction = ContainsAny(text, "出来", "在吗", "帮我", "帮忙", "看看", "回我", "理我", "醒醒", "过来", "听得到", "你怎么看", "能不能帮");
        string compactText = CompactForIntent(text);
        bool directNameCall = words.Any(word => compactText.Equals(CompactForIntent(word), StringComparison.OrdinalIgnoreCase));
        bool confirmed = (callAction || directNameCall) && negation == false && meta == false;

        return new QChatIntentDecision(
            QChatIntentKind.GroupWake,
            true,
            confirmed,
            confirmed ? 0.86 : 0.3,
            confirmed ? QChatIntentTargetKind.CurrentSession : QChatIntentTargetKind.None,
            confirmed ? "directed" : null,
            null,
            null,
            negation,
            meta,
            confirmed ? "confirmed directed group wake request" : "wake target is not a directed request");
    }

    static QChatIntentDecision None(QChatIntentKind kind, string reason)
    {
        return new QChatIntentDecision(kind, false, false, 0, QChatIntentTargetKind.None, null, null, null, false, false, reason);
    }

    static string Merge(params string[] values)
    {
        return string.Join('\n', values.Where(value => string.IsNullOrWhiteSpace(value) == false));
    }

    static bool ContainsAny(string text, params string[] values)
    {
        return values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    static string CompactForIntent(string text)
    {
        return new string(text.Where(ch => char.IsWhiteSpace(ch) == false && char.IsPunctuation(ch) == false && char.IsSymbol(ch) == false).ToArray());
    }

    static long ExtractFirstId(string text)
    {
        Match match = Regex.Match(text, @"(?<!\d)([1-9]\d{5,12})(?!\d)");
        return match.Success && long.TryParse(match.Groups[1].Value, out long value) ? value : 0;
    }

    internal static string? ExtractWindowsPath(string text)
    {
        Match quoted = Regex.Match(text, @"""([A-Za-z]:[\\/][^""<>|?*]+)""");
        if (quoted.Success)
            return quoted.Groups[1].Value.Trim();

        Match match = Regex.Match(text, @"[A-Za-z]:[\\/][^\s""<>|?*]+");
        return match.Success
            ? match.Value.Trim().TrimEnd('.', ',', ';', ':', '!', '。', '，', '；', '：', '！', '？')
            : null;
    }
}
