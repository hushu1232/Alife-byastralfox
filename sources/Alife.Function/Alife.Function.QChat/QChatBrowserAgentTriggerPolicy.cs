using System;
using System.Text.RegularExpressions;

namespace Alife.Function.QChat;

public enum QChatBrowserAgentTriggerKind
{
    None,
    RunBrowserTask,
    Denied
}

public sealed record QChatBrowserAgentTrigger(
    QChatBrowserAgentTriggerKind Kind,
    string Task = "",
    string Reason = "");

public static class QChatBrowserAgentTriggerPolicy
{
    static readonly Regex BrowserIntent = new(
        @"\b(open|browse|inspect|browser|website|web page|readme|official site|docs|documentation)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Regex SearchOnly = new(
        @"^\s*(search|look up|find|google|bing)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static QChatBrowserAgentTrigger Parse(
        OneBotMessageType messageType,
        QChatSenderRole senderRole,
        string? rawText)
    {
        string text = OneBotSegment.GetPlainText(rawText ?? "").Trim();
        if (text.Length == 0)
            return new QChatBrowserAgentTrigger(QChatBrowserAgentTriggerKind.None);
        if (text.StartsWith("/qchat", StringComparison.OrdinalIgnoreCase))
            return new QChatBrowserAgentTrigger(QChatBrowserAgentTriggerKind.None);
        if (BrowserIntent.IsMatch(text) == false)
            return new QChatBrowserAgentTrigger(QChatBrowserAgentTriggerKind.None);
        if (SearchOnly.IsMatch(text))
            return new QChatBrowserAgentTrigger(QChatBrowserAgentTriggerKind.None);
        if (messageType != OneBotMessageType.Private)
            return new QChatBrowserAgentTrigger(QChatBrowserAgentTriggerKind.None);
        if (senderRole != QChatSenderRole.Owner)
            return new QChatBrowserAgentTrigger(QChatBrowserAgentTriggerKind.Denied, Reason: "browser_agent_owner_required");

        return new QChatBrowserAgentTrigger(QChatBrowserAgentTriggerKind.RunBrowserTask, text, "owner_private_browser_request");
    }
}
