using System;

namespace Alife.Function.QChat;

public interface IQChatFollowUpPresenceAdapter
{
    QChatFollowUpPresence Evaluate(QChatFollowUpPresenceContext context);
}

public sealed class QChatFollowUpPresencePolicy
{
    static readonly string[] NaturalContinuationCues = ["晚安", "先忙", "先去忙", "回头", "再见", "下次", "…", "..."];

    public QChatFollowUpPresence Evaluate(
        QChatFollowUpPresenceContext context,
        IQChatFollowUpPresenceAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(adapter);

        if (context.IsOwnerPrivate == false ||
            context.IsRiskConversation ||
            context.IsDeterministicTask ||
            context.HasPendingMedia ||
            context.IsQuiet ||
            context.ModelReplyWasBlocked)
        {
            return QChatFollowUpPresence.DoNotInterrupt;
        }

        QChatFollowUpPresence presence = adapter.Evaluate(context);
        if (presence.Intent is QChatFollowUpIntent.None or QChatFollowUpIntent.DoNotInterrupt)
            return presence;

        return HasNaturalContinuationCue(context.SourceText) || HasNaturalContinuationCue(context.ReplyText)
            ? presence
            : QChatFollowUpPresence.None;
    }

    static bool HasNaturalContinuationCue(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        foreach (string cue in NaturalContinuationCues)
        {
            if (text.Contains(cue, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
