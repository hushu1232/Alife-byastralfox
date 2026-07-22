using System;
using System.Collections.Generic;
using System.Linq;

namespace Alife.Function.QChat;

public sealed record QChatConversationContextRequest(
    long SelfId,
    OneBotMessageType MessageType,
    long TargetId,
    int MaximumMessages = 12,
    int MaximumCharacters = 3000,
    int RecentWindowMessages = 6);

public sealed class QChatConversationContextCapability(QChatRecentEventMemory recentEventMemory)
{
    const string CapabilityName = "current_conversation_context";
    readonly QChatRecentEventMemory recentEventMemory = recentEventMemory ?? throw new ArgumentNullException(nameof(recentEventMemory));

    public QChatCapabilityFeedback Read(QChatConversationContextRequest request, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SelfId <= 0 || request.TargetId <= 0)
            return QChatCapabilityFeedback.Denied(CapabilityName);

        int maximumMessages = Math.Clamp(request.MaximumMessages, 1, 12);
        int maximumCharacters = Math.Clamp(request.MaximumCharacters, 80, 3000);
        IReadOnlyList<QChatRecentMessageSnapshot> messages = GetReplayableMessages(
            request,
            maximumMessages,
            observedAt);
        string data = BuildBoundedData(messages, maximumCharacters);
        return string.IsNullOrEmpty(data)
            ? QChatCapabilityFeedback.NoRelevantData(CapabilityName, observedAt)
            : QChatCapabilityFeedback.Succeeded(CapabilityName, data, observedAt);
    }

    public bool HasReplayableConversation(QChatConversationContextRequest request, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SelfId <= 0 || request.TargetId <= 0)
            return false;

        return GetReplayableMessages(request, maximumMessages: 1, observedAt).Count > 0;
    }

    IReadOnlyList<QChatRecentMessageSnapshot> GetReplayableMessages(
        QChatConversationContextRequest request,
        int maximumMessages,
        DateTimeOffset observedAt)
    {
        int recentWindowMessages = Math.Clamp(request.RecentWindowMessages, 1, 6);
        IReadOnlyList<QChatRecentMessageSnapshot> currentAndEarlier = recentEventMemory
            .GetRecentConversation(
                request.SelfId,
                request.MessageType,
                request.TargetId,
                maximumMessages + recentWindowMessages,
                observedAt)
            .Where(message => message.IsRecalled == false)
            .ToArray();
        int replayableCount = Math.Max(0, currentAndEarlier.Count - recentWindowMessages);
        return currentAndEarlier.Take(replayableCount).ToArray();
    }

    static string BuildBoundedData(IReadOnlyList<QChatRecentMessageSnapshot> messages, int maximumCharacters)
    {
        List<string> selected = [];
        int remaining = maximumCharacters;
        foreach (QChatRecentMessageSnapshot message in messages.Reverse())
        {
            string speaker = message.Speaker == QChatConversationSpeaker.Self ? "self" : "peer";
            string text = CollapseWhitespace(message.ReadableMessage);
            if (string.IsNullOrEmpty(text))
                continue;

            string line = $"- {message.ReceivedAt:HH:mm} {speaker}: {text}";
            if (line.Length > remaining && selected.Count > 0)
                break;
            if (line.Length > remaining)
                line = line[..Math.Max(0, remaining)].TrimEnd();
            if (line.Length == 0)
                break;

            selected.Insert(0, line);
            remaining -= line.Length + Environment.NewLine.Length;
            if (remaining <= 0)
                break;
        }

        return string.Join(Environment.NewLine, selected);
    }

    static string CollapseWhitespace(string value) =>
        string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
