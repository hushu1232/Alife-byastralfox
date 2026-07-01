using System;
using System.Collections.Generic;
using System.Linq;

namespace Alife.Function.QChat;

public sealed class QChatSemanticSettleOptions
{
    public TimeSpan SettleDelay { get; set; } = TimeSpan.FromSeconds(3);
    public TimeSpan MaxWindowDuration { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxMessages { get; set; } = 12;
}

public sealed record QChatSemanticWindowMessage(
    long MessageId,
    long SenderId,
    string Text,
    bool HasImage,
    DateTimeOffset Timestamp);

public sealed record QChatSemanticWindowSnapshot(
    IReadOnlyList<QChatSemanticWindowMessage> Messages,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUpdatedAt);

public sealed class QChatSemanticSettleWindow(QChatSemanticSettleOptions? options = null, DateTimeOffset? createdAt = null)
{
    readonly QChatSemanticSettleOptions options = options ?? new QChatSemanticSettleOptions();
    readonly List<QChatSemanticWindowMessage> messages = [];
    readonly DateTimeOffset createdAt = createdAt ?? DateTimeOffset.UtcNow;
    DateTimeOffset lastUpdatedAt = createdAt ?? DateTimeOffset.UtcNow;

    public void AddMessage(QChatSemanticWindowMessage message)
    {
        messages.Add(message);
        lastUpdatedAt = message.Timestamp;
    }

    public void RemoveMessage(long messageId)
    {
        messages.RemoveAll(message => message.MessageId == messageId);
    }

    public bool ShouldSettle(DateTimeOffset now)
    {
        if (messages.Count == 0)
            return false;

        if (messages.Count >= Math.Max(1, options.MaxMessages))
            return true;

        if (now - createdAt >= options.MaxWindowDuration)
            return true;

        if (now - lastUpdatedAt < options.SettleDelay)
            return false;

        if (LooksIncomplete(messages[^1].Text))
            return false;

        QChatSemanticStateEstimate estimate = QChatSemanticStateEstimator.Estimate(Snapshot(), now, options);
        return estimate.ShouldAnswer || estimate.ShouldSummarize;
    }

    public QChatSemanticWindowSnapshot Snapshot()
    {
        return new QChatSemanticWindowSnapshot(messages.ToArray(), createdAt, lastUpdatedAt);
    }

    static bool LooksIncomplete(string? text)
    {
        string value = (text ?? "").Trim();
        if (value.Length == 0)
            return true;

        return value.EndsWith(",", StringComparison.Ordinal) ||
               value.EndsWith(";", StringComparison.Ordinal) ||
               value.EndsWith(":", StringComparison.Ordinal) ||
               value.EndsWith("...", StringComparison.Ordinal) ||
               value.EndsWith("and", StringComparison.OrdinalIgnoreCase) ||
               value.EndsWith("or", StringComparison.OrdinalIgnoreCase);
    }
}
