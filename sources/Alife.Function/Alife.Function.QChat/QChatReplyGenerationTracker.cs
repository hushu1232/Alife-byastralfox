using System;
using System.Collections.Generic;
using System.Threading;

namespace Alife.Function.QChat;

public sealed record QChatReplyGenerationLease(
    string ConversationKey,
    long Generation,
    CancellationTokenSource Cancellation)
{
    public CancellationToken CancellationToken => Cancellation.Token;
}

public sealed class QChatReplyGenerationTracker
{
    readonly object gate = new();
    readonly Dictionary<string, QChatReplyGenerationLease> current = new(StringComparer.Ordinal);
    long generation;

    public QChatReplyGenerationLease Begin(long selfId, OneBotMessageType messageType, long targetId)
    {
        string key = BuildConversationKey(selfId, messageType, targetId);
        lock (gate)
        {
            if (current.TryGetValue(key, out QChatReplyGenerationLease? previous))
                previous.Cancellation.Cancel();

            QChatReplyGenerationLease lease = new(
                key,
                Interlocked.Increment(ref generation),
                new CancellationTokenSource());
            current[key] = lease;
            return lease;
        }
    }

    public bool IsCurrent(QChatReplyGenerationLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        lock (gate)
        {
            return current.TryGetValue(lease.ConversationKey, out QChatReplyGenerationLease? active)
                   && ReferenceEquals(active, lease)
                   && lease.CancellationToken.IsCancellationRequested == false;
        }
    }

    public void Release(QChatReplyGenerationLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        lock (gate)
        {
            if (current.TryGetValue(lease.ConversationKey, out QChatReplyGenerationLease? active) &&
                ReferenceEquals(active, lease))
            {
                current.Remove(lease.ConversationKey);
            }
        }

        lease.Cancellation.Dispose();
    }

    static string BuildConversationKey(long selfId, OneBotMessageType messageType, long targetId) =>
        $"{selfId}:{messageType}:{targetId}";
}
