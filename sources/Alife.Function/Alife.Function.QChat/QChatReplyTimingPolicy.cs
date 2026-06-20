using System;

namespace Alife.Function.QChat;

public sealed record QChatReplyTimingContext(
    OneBotMessageType MessageType,
    QChatSenderRole SenderRole,
    QChatReplyAction Action,
    bool IsToolConfirmation);

public sealed class QChatReplyTimingPolicy(Random? random = null)
{
    readonly Random random = random ?? Random.Shared;

    public TimeSpan SelectDelay(QChatReplyTimingContext context)
    {
        if (context.IsToolConfirmation)
            return Between(0, 500);

        if (context.Action == QChatReplyAction.ReactOnly || context.Action == QChatReplyAction.ReplyShort)
            return Between(300, 1200);

        if (context.MessageType == OneBotMessageType.Private && context.SenderRole == QChatSenderRole.Owner)
            return Between(300, 1800);

        if (context.MessageType == OneBotMessageType.Group && context.SenderRole == QChatSenderRole.Owner)
            return Between(600, 2600);

        if (context.MessageType == OneBotMessageType.Group)
            return Between(1200, 6500);

        return Between(600, 2600);
    }

    public bool CanStartProactiveTopic(
        DateTimeOffset now,
        DateTimeOffset? lastProactiveTopicAt,
        bool hasRecentContext,
        bool hasPendingToolOrApproval,
        TimeSpan cooldown)
    {
        if (hasRecentContext == false)
            return false;
        if (hasPendingToolOrApproval)
            return false;
        if (lastProactiveTopicAt.HasValue == false)
            return true;

        return now - lastProactiveTopicAt.Value >= cooldown;
    }

    TimeSpan Between(int minMilliseconds, int maxMilliseconds)
    {
        if (maxMilliseconds <= minMilliseconds)
            return TimeSpan.FromMilliseconds(minMilliseconds);

        double value = random.NextDouble();
        int delay = minMilliseconds + (int)Math.Round((maxMilliseconds - minMilliseconds) * value);
        return TimeSpan.FromMilliseconds(Math.Clamp(delay, minMilliseconds, maxMilliseconds));
    }
}
