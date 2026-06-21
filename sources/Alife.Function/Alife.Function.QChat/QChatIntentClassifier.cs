using System;
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
        bool meta = ContainsAny(text, "是不是", "会不会", "能不能", "为什么", "怎么", "失败", "不会撤回");
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
        string text = Merge(input.PlainText, input.ReadableText);
        bool candidate = ContainsAny(text, "白名单", "allowlist", "qchat_allowlist_update");
        if (candidate == false)
            return None(QChatIntentKind.AllowlistUpdate, "no allowlist keyword");

        string action = ContainsAny(text, "移除", "删除", "remove") ? "remove" : "add";
        long id = ExtractFirstId(text);
        if (id == 0 && ContainsAny(text, "这个群", "本群", "当前群"))
            id = currentGroupId;
        bool confirmed = id > 0 && ContainsAny(text, "群", "group", "target=\"group\"");

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

    static long ExtractFirstId(string text)
    {
        Match match = Regex.Match(text, @"(?<!\d)([1-9]\d{5,12})(?!\d)");
        return match.Success && long.TryParse(match.Groups[1].Value, out long value) ? value : 0;
    }

    static string? ExtractWindowsPath(string text)
    {
        Match match = Regex.Match(text, @"[A-Za-z]:[\\/][^\r\n""<>|?*]+");
        return match.Success ? match.Value.Trim() : null;
    }
}
