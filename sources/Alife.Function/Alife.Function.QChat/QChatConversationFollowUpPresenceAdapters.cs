using System;

namespace Alife.Function.QChat;

public sealed class XiaYuFollowUpPresenceAdapter(
    XiaYuSelfState state,
    XiaYuReplyStrategy strategy) : IQChatFollowUpPresenceAdapter
{
    public QChatFollowUpPresence Evaluate(QChatFollowUpPresenceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.IsTimerState ||
            context.IsHighConversationPressure ||
            state.Vigilance >= 0.70 ||
            strategy.Stance == XiaYuReplyStance.Silent ||
            string.Equals(state.CurrentFocus, "owner_private", StringComparison.Ordinal) == false)
        {
            return QChatFollowUpPresence.DoNotInterrupt;
        }

        return string.Equals(state.Mood, "softened", StringComparison.Ordinal)
            ? new QChatFollowUpPresence(QChatFollowUpIntent.WarmCoda)
            : QChatFollowUpPresence.None;
    }
}

public sealed class MixuFollowUpPresenceAdapter : IQChatFollowUpPresenceAdapter
{
    public QChatFollowUpPresence Evaluate(QChatFollowUpPresenceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.Equals(context.AgentId, "mixu", StringComparison.OrdinalIgnoreCase) == false)
            return QChatFollowUpPresence.None;

        return context.SourceText.Contains("晚安", StringComparison.Ordinal)
            ? new QChatFollowUpPresence(QChatFollowUpIntent.EmotionalAfterthought)
            : new QChatFollowUpPresence(QChatFollowUpIntent.WarmCoda);
    }
}
