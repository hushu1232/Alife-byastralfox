using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Alife.Function.QChat;

public sealed record QChatRecentMessageSnapshot(
    long MessageId,
    long SelfId,
    OneBotMessageType MessageType,
    long UserId,
    long GroupId,
    string RawMessage,
    string ReadableMessage,
    DateTimeOffset ReceivedAt,
    bool IsRecalled = false);

public sealed record QChatRecallSnapshot(
    long SelfId,
    string NoticeType,
    long MessageId,
    long UserId,
    long GroupId,
    long OperatorId,
    QChatRecentMessageSnapshot? Message);

public sealed record QChatRecentRecallEventSnapshot(
    long SelfId,
    OneBotMessageType MessageType,
    long TargetId,
    long MessageId,
    long UserId,
    long OperatorId,
    DateTimeOffset RecalledAt,
    bool MatchedMessage);

public sealed class QChatRecentEventMemory(int maxMessages = 500, TimeSpan? retention = null)
{
    readonly int maxMessages = Math.Max(1, maxMessages);
    readonly TimeSpan retention = retention ?? TimeSpan.FromMinutes(60);
    readonly LinkedList<QChatRecentMessageSnapshot> messages = new();
    readonly LinkedList<QChatRecentRecallEventSnapshot> recalls = new();
    readonly Dictionary<(long SelfId, long MessageId), LinkedListNode<QChatRecentMessageSnapshot>> byMessageId = new();

    public void Remember(OneBotMessageEvent messageEvent, string readable, DateTimeOffset now)
    {
        if (messageEvent.MessageId <= 0)
            return;

        Prune(now);
        (long SelfId, long MessageId) key = (messageEvent.SelfId, messageEvent.MessageId);
        if (byMessageId.TryGetValue(key, out LinkedListNode<QChatRecentMessageSnapshot>? existing))
        {
            messages.Remove(existing);
            byMessageId.Remove(key);
        }

        QChatRecentMessageSnapshot snapshot = new(
            messageEvent.MessageId,
            messageEvent.SelfId,
            messageEvent.MessageType,
            messageEvent.UserId,
            messageEvent.GroupId,
            messageEvent.RawMessage,
            readable,
            now);
        LinkedListNode<QChatRecentMessageSnapshot> node = messages.AddLast(snapshot);
        byMessageId[key] = node;
        TrimToMaxMessages();
    }

    public QChatRecallSnapshot BuildRecall(OneBotNoticeEvent noticeEvent)
    {
        byMessageId.TryGetValue((noticeEvent.SelfId, noticeEvent.MessageId), out LinkedListNode<QChatRecentMessageSnapshot>? messageNode);
        return new QChatRecallSnapshot(
            noticeEvent.SelfId,
            noticeEvent.NoticeType ?? "",
            noticeEvent.MessageId,
            noticeEvent.UserId,
            noticeEvent.GroupId,
            noticeEvent.OperatorId,
            messageNode?.Value);
    }

    public QChatRecallSnapshot RememberRecall(OneBotNoticeEvent noticeEvent, DateTimeOffset now)
    {
        Prune(now);
        (long SelfId, long MessageId) key = (noticeEvent.SelfId, noticeEvent.MessageId);
        if (byMessageId.TryGetValue(key, out LinkedListNode<QChatRecentMessageSnapshot>? messageNode))
            messageNode.Value = messageNode.Value with { IsRecalled = true };

        QChatRecallSnapshot recall = BuildRecall(noticeEvent);
        QChatRecentMessageSnapshot? message = recall.Message;
        OneBotMessageType messageType = message?.MessageType ?? noticeEvent.MessageType;
        long targetId = message == null ? GetTargetId(noticeEvent) : GetTargetId(message);
        recalls.AddLast(new QChatRecentRecallEventSnapshot(
            recall.SelfId,
            messageType,
            targetId,
            recall.MessageId,
            recall.UserId,
            recall.OperatorId,
            now,
            message != null));
        TrimToMaxMessages();
        return recall;
    }

    public IReadOnlyList<QChatRecentMessageSnapshot> GetRecentConversation(
        long selfId,
        OneBotMessageType type,
        long targetId,
        int limit,
        DateTimeOffset now)
    {
        if (limit <= 0)
            return [];

        Prune(now);
        return messages
            .Reverse()
            .Where(message => message.SelfId == selfId &&
                              message.MessageType == type &&
                              GetTargetId(message) == targetId)
            .Take(limit)
            .Reverse()
            .ToArray();
    }

    public string BuildRecentContextBlock(
        long selfId,
        OneBotMessageType type,
        long targetId,
        int limit,
        DateTimeOffset now,
        bool includeRecalledMessages = true,
        int maxCharacters = 1200)
    {
        IReadOnlyList<QChatRecentMessageSnapshot> recent = GetRecentConversation(selfId, type, targetId, limit, now)
            .Where(message => includeRecalledMessages || message.IsRecalled == false)
            .ToArray();
        if (recent.Count == 0)
            return "";

        maxCharacters = Math.Max(80, maxCharacters);
        const string header = "[Recent QQ context]";
        const string footer = "[/Recent QQ context]";
        int remaining = maxCharacters - header.Length - footer.Length - Environment.NewLine.Length;
        if (remaining <= 0)
            return "";

        List<string> selectedLines = [];
        foreach (QChatRecentMessageSnapshot message in recent.Reverse())
        {
            string line = BuildRecentContextLine(message, Math.Min(180, remaining));
            int lineCost = line.Length + Environment.NewLine.Length;
            if (selectedLines.Count > 0 && lineCost > remaining)
                break;

            if (selectedLines.Count == 0 && lineCost > remaining)
            {
                line = Truncate(line, remaining);
                lineCost = line.Length + Environment.NewLine.Length;
            }

            selectedLines.Insert(0, line);
            remaining -= lineCost;
            if (remaining <= 0)
                break;
        }

        if (selectedLines.Count == 0)
            return "";

        StringBuilder builder = new();
        builder.AppendLine(header);
        foreach (string line in selectedLines)
            builder.AppendLine(line);

        builder.Append(footer);
        return builder.ToString();
    }

    public string BuildRecentRecallContextBlock(
        long selfId,
        OneBotMessageType type,
        long targetId,
        int limit,
        DateTimeOffset now)
    {
        if (limit <= 0)
            return "";

        Prune(now);
        QChatRecentRecallEventSnapshot[] recent = recalls
            .Reverse()
            .Where(recall => recall.SelfId == selfId &&
                             recall.MessageType == type &&
                             recall.TargetId == targetId)
            .Take(limit)
            .Reverse()
            .ToArray();
        if (recent.Length == 0)
            return "";

        string conversation = type == OneBotMessageType.Group ? "group" : "private";
        StringBuilder builder = new();
        builder.AppendLine("[Recent QQ events]");
        foreach (QChatRecentRecallEventSnapshot recall in recent)
        {
            builder
                .Append("- ")
                .Append(recall.RecalledAt.ToString("HH:mm"))
                .Append(" user ")
                .Append(recall.UserId)
                .Append(" recalled a recent ")
                .Append(conversation)
                .Append(" message")
                .Append(" message_id=")
                .Append(recall.MessageId);
            if (recall.OperatorId > 0 && recall.OperatorId != recall.UserId)
            {
                builder
                    .Append(" operator=")
                    .Append(recall.OperatorId);
            }
            if (recall.MatchedMessage == false)
                builder.Append(" unmatched");
            builder.AppendLine();
        }

        builder.Append("[/Recent QQ events]");
        return builder.ToString();
    }

    public void Prune(DateTimeOffset now)
    {
        while (messages.First is { } first &&
               now - first.Value.ReceivedAt > retention)
        {
            messages.RemoveFirst();
            byMessageId.Remove((first.Value.SelfId, first.Value.MessageId));
        }

        while (recalls.First is { } first &&
               now - first.Value.RecalledAt > retention)
        {
            recalls.RemoveFirst();
        }
    }

    static long GetTargetId(QChatRecentMessageSnapshot message)
    {
        return message.MessageType == OneBotMessageType.Group
            ? message.GroupId
            : message.UserId;
    }

    static long GetTargetId(OneBotBasicMessageEvent message)
    {
        return message.MessageType == OneBotMessageType.Group
            ? message.GroupId
            : message.UserId;
    }

    static string BuildRecentContextLine(QChatRecentMessageSnapshot message, int maxLength)
    {
        string recalled = message.IsRecalled ? " recalled" : "";
        string prefix = $"- {message.ReceivedAt:HH:mm} user {message.UserId}{recalled}: ";
        string readable = Truncate(CollapseWhitespace(message.ReadableMessage), Math.Max(0, maxLength - prefix.Length));
        return prefix + readable;
    }

    static string CollapseWhitespace(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        return string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength)
            return text;

        return text[..Math.Max(0, maxLength - 3)] + "...";
    }

    void TrimToMaxMessages()
    {
        while (messages.Count > maxMessages && messages.First is { } first)
        {
            messages.RemoveFirst();
            byMessageId.Remove((first.Value.SelfId, first.Value.MessageId));
        }

        while (recalls.Count > maxMessages)
        {
            recalls.RemoveFirst();
        }
    }
}
