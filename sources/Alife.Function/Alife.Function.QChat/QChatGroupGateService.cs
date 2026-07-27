using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Alife.Function.QChat;

public sealed record QChatGroupGateDecision(
    QChatInboundDecisionKind Kind,
    string Reason,
    string PendingContextText,
    string ContextBeforeDispatch,
    bool RequiresRelevanceCheck = false);

public sealed class QChatGroupGateService
{
    const int MaxPendingItems = 12;

    readonly ConcurrentDictionary<string, PendingSessionContext> pendingBySession = new();
    readonly ConcurrentDictionary<string, DateTimeOffset> activeThreads = new();

    public QChatGroupGateDecision Evaluate(
        QChatAgentRoute route,
        string rawText,
        bool isMentionedOrWoken,
        bool isAggressive,
        bool isSemanticReply = false,
        bool isReplyToBot = false,
        bool isAddressedToOther = false,
        DateTimeOffset? observedAt = null,
        TimeSpan? activeWindow = null)
    {
        ArgumentNullException.ThrowIfNull(route);

        string text = rawText?.Trim() ?? string.Empty;
        DateTimeOffset now = observedAt ?? DateTimeOffset.UtcNow;

        if (route.ConversationKind == QChatConversationKind.Private)
        {
            return new QChatGroupGateDecision(
                QChatInboundDecisionKind.DispatchToModel,
                "private route bypasses group gate",
                string.Empty,
                string.Empty);
        }

        if (isMentionedOrWoken || isSemanticReply || isReplyToBot)
        {
            TouchActiveThread(route, now);
            return new QChatGroupGateDecision(
                QChatInboundDecisionKind.DispatchToModel,
                CreateDispatchReason(isMentionedOrWoken, isAggressive, isSemanticReply, isReplyToBot),
                string.Empty,
                DrainPending(GetThreadKey(route)));
        }

        if (isAddressedToOther)
            return ListenOnly(text, "group message addresses another participant");

        TimeSpan window = activeWindow ?? TimeSpan.FromSeconds(120);
        if (window > TimeSpan.Zero && IsActiveThread(route, now, window))
        {
            return new QChatGroupGateDecision(
                QChatInboundDecisionKind.ListenOnly,
                "active group thread requires relevance check",
                string.Empty,
                string.Empty,
                RequiresRelevanceCheck: true);
        }

        if (text.Length > 0)
            Remember(GetThreadKey(route), text);

        return ListenOnly(text, "group message is not activated");
    }

    public QChatGroupGateDecision AcceptContinuation(QChatAgentRoute route, DateTimeOffset? observedAt = null)
    {
        ArgumentNullException.ThrowIfNull(route);
        TouchActiveThread(route, observedAt ?? DateTimeOffset.UtcNow);
        return new QChatGroupGateDecision(
            QChatInboundDecisionKind.DispatchToModel,
            "active group thread continuation",
            string.Empty,
            DrainPending(GetThreadKey(route)));
    }

    public void CloseActiveThread(QChatAgentRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        activeThreads.TryRemove(GetThreadKey(route), out _);
    }

    static QChatGroupGateDecision ListenOnly(string text, string reason) => new(
        QChatInboundDecisionKind.ListenOnly,
        reason,
        text,
        string.Empty);

    static string CreateDispatchReason(bool isMentionedOrWoken, bool isAggressive, bool isSemanticReply, bool isReplyToBot)
    {
        if (isReplyToBot)
            return "reply to bot group message";

        if (isAggressive && (isMentionedOrWoken || isSemanticReply))
            return "addressed aggressive group message";

        if (isMentionedOrWoken)
            return "mentioned or woken group message";

        if (isSemanticReply)
            return "semantic group reply";

        return "group message dispatch";
    }

    bool IsActiveThread(QChatAgentRoute route, DateTimeOffset now, TimeSpan activeWindow)
    {
        string key = GetThreadKey(route);
        if (activeThreads.TryGetValue(key, out DateTimeOffset lastActivity) == false)
            return false;
        if (now - lastActivity <= activeWindow)
            return true;

        activeThreads.TryRemove(key, out _);
        return false;
    }

    void TouchActiveThread(QChatAgentRoute route, DateTimeOffset now)
    {
        activeThreads[GetThreadKey(route)] = now;
    }

    static string GetThreadKey(QChatAgentRoute route) => $"{route.SessionKey}:sender:{route.SenderId}";

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
