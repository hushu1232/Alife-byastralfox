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

public sealed class QChatRecentEventMemory(int maxMessages = 500, TimeSpan? retention = null)
{
    readonly int maxMessages = Math.Max(1, maxMessages);
    readonly TimeSpan retention = retention ?? TimeSpan.FromMinutes(60);
    readonly LinkedList<QChatRecentMessageSnapshot> messages = new();
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

        return BuildRecall(noticeEvent);
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
        bool includeRecalledMessages = true)
    {
        IReadOnlyList<QChatRecentMessageSnapshot> recent = GetRecentConversation(selfId, type, targetId, limit, now)
            .Where(message => includeRecalledMessages || message.IsRecalled == false)
            .ToArray();
        if (recent.Count == 0)
            return "";

        StringBuilder builder = new();
        builder.AppendLine("[Recent QQ context]");
        foreach (QChatRecentMessageSnapshot message in recent)
        {
            string recalled = message.IsRecalled ? " recalled" : "";
            string readable = Truncate(CollapseWhitespace(message.ReadableMessage), 180);
            builder
                .Append("- ")
                .Append(message.ReceivedAt.ToString("HH:mm"))
                .Append(" user ")
                .Append(message.UserId)
                .Append(recalled)
                .Append(": ")
                .AppendLine(readable);
        }

        builder.Append("[/Recent QQ context]");
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
    }

    static long GetTargetId(QChatRecentMessageSnapshot message)
    {
        return message.MessageType == OneBotMessageType.Group
            ? message.GroupId
            : message.UserId;
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
    }
}
