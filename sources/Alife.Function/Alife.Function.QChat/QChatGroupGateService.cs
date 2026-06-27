using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Alife.Function.QChat;

public sealed record QChatGroupGateDecision(
    QChatInboundDecisionKind Kind,
    string Reason,
    string PendingContextText,
    string ContextBeforeDispatch);

public sealed class QChatGroupGateService
{
    const int MaxPendingItems = 12;

    readonly ConcurrentDictionary<string, PendingSessionContext> pendingBySession = new();

    public QChatGroupGateDecision Evaluate(
        QChatAgentRoute route,
        string rawText,
        bool isMentionedOrWoken,
        bool isAggressive,
        bool isSemanticReply = false)
    {
        ArgumentNullException.ThrowIfNull(route);

        string text = rawText?.Trim() ?? string.Empty;

        if (route.ConversationKind == QChatConversationKind.Private)
        {
            return new QChatGroupGateDecision(
                QChatInboundDecisionKind.DispatchToModel,
                "private route bypasses group gate",
                string.Empty,
                string.Empty);
        }

        if (route.IsOwner || isMentionedOrWoken || isAggressive || isSemanticReply)
        {
            return new QChatGroupGateDecision(
                QChatInboundDecisionKind.DispatchToModel,
                CreateDispatchReason(route, isMentionedOrWoken, isAggressive, isSemanticReply),
                string.Empty,
                DrainPending(route.SessionKey));
        }

        if (text.Length > 0)
            Remember(route.SessionKey, text);

        return new QChatGroupGateDecision(
            QChatInboundDecisionKind.ListenOnly,
            "group message is not activated",
            text,
            string.Empty);
    }

    static string CreateDispatchReason(QChatAgentRoute route, bool isMentionedOrWoken, bool isAggressive, bool isSemanticReply)
    {
        if (route.IsOwner)
            return "owner group message";

        if (isAggressive)
            return "aggressive group message";

        if (isMentionedOrWoken)
            return "mentioned or woken group message";

        if (isSemanticReply)
            return "semantic group reply";

        return "group message dispatch";
    }

    void Remember(string sessionKey, string text)
    {
        PendingSessionContext context = pendingBySession.GetOrAdd(sessionKey, _ => new PendingSessionContext(MaxPendingItems));
        context.Remember(text);
    }

    string DrainPending(string sessionKey)
    {
        if (pendingBySession.TryGetValue(sessionKey, out PendingSessionContext? context) == false)
            return string.Empty;

        return context.Drain();
    }

    sealed class PendingSessionContext(int maxItems)
    {
        readonly object syncRoot = new();
        readonly Queue<string> queue = new();

        public void Remember(string text)
        {
            lock (syncRoot)
            {
                queue.Enqueue(text);
                while (queue.Count > maxItems)
                    queue.Dequeue();
            }
        }

        public string Drain()
        {
            lock (syncRoot)
            {
                string context = string.Join('\n', queue.ToArray().Select(item => $"- {item}"));
                queue.Clear();
                return context;
            }
        }
    }
}
