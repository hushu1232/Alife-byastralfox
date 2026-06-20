namespace Alife.Function.QChat;

public enum QChatContinuationAction
{
    ReplyNow,
    WaitForMoreContext,
    TaskFeedbackOnly,
    StopAfterTaskFeedback,
    NoReply
}

public sealed record QChatContinuationContext(
    bool DeterministicTaskHandled,
    bool SentTaskFeedback,
    bool HasModelReply,
    string IncomingText);

public sealed record QChatContinuationDecision(
    QChatContinuationAction Action,
    bool ShouldDispatchModel,
    string Reason);

public static class QChatContinuationPolicy
{
    public static QChatContinuationDecision Decide(QChatContinuationContext context)
    {
        if (context.DeterministicTaskHandled)
        {
            return new QChatContinuationDecision(
                context.SentTaskFeedback
                    ? QChatContinuationAction.StopAfterTaskFeedback
                    : QChatContinuationAction.TaskFeedbackOnly,
                ShouldDispatchModel: false,
                "deterministic-task-handled");
        }

        return new QChatContinuationDecision(
            QChatContinuationAction.ReplyNow,
            ShouldDispatchModel: true,
            "normal-conversation");
    }
}
