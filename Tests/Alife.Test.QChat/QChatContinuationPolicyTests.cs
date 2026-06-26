using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public class QChatContinuationPolicyTests
{
    [Test]
    public void DeterministicTaskFeedbackStopsModelContinuation()
    {
        QChatContinuationDecision decision = QChatContinuationPolicy.Decide(new QChatContinuationContext(
            DeterministicTaskHandled: true,
            SentTaskFeedback: true,
            HasModelReply: false,
            IncomingText: "\u628a\u8fd9\u4e2a\u6587\u4ef6\u53d1\u5230 925402131 \u7fa4"));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Action, Is.EqualTo(QChatContinuationAction.StopAfterTaskFeedback));
            Assert.That(decision.ShouldDispatchModel, Is.False);
        });
    }

    [Test]
    public void NormalConversationAllowsModelDispatch()
    {
        QChatContinuationDecision decision = QChatContinuationPolicy.Decide(new QChatContinuationContext(
            DeterministicTaskHandled: false,
            SentTaskFeedback: false,
            HasModelReply: false,
            IncomingText: "\u4eca\u5929\u72b6\u6001\u600e\u4e48\u6837"));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Action, Is.EqualTo(QChatContinuationAction.ReplyNow));
            Assert.That(decision.ShouldDispatchModel, Is.True);
        });
    }

    [Test]
    public void DeterministicTaskWithoutFeedbackStillBlocksModelDispatch()
    {
        QChatContinuationDecision decision = QChatContinuationPolicy.Decide(new QChatContinuationContext(
            DeterministicTaskHandled: true,
            SentTaskFeedback: false,
            HasModelReply: false,
            IncomingText: "check file upload status"));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Action, Is.EqualTo(QChatContinuationAction.TaskFeedbackOnly));
            Assert.That(decision.ShouldDispatchModel, Is.False);
            Assert.That(decision.Reason, Is.EqualTo("deterministic-task-handled"));
        });
    }

    [Test]
    public void DeterministicTaskFeedbackStopsDispatchEvenWhenModelReplyExists()
    {
        QChatContinuationDecision decision = QChatContinuationPolicy.Decide(new QChatContinuationContext(
            DeterministicTaskHandled: true,
            SentTaskFeedback: true,
            HasModelReply: true,
            IncomingText: "task already completed"));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Action, Is.EqualTo(QChatContinuationAction.StopAfterTaskFeedback));
            Assert.That(decision.ShouldDispatchModel, Is.False);
            Assert.That(decision.Reason, Is.EqualTo("deterministic-task-handled"));
        });
    }

    [Test]
    public void FeedbackFlagAloneDoesNotSuppressNormalConversation()
    {
        QChatContinuationDecision decision = QChatContinuationPolicy.Decide(new QChatContinuationContext(
            DeterministicTaskHandled: false,
            SentTaskFeedback: true,
            HasModelReply: false,
            IncomingText: "continue the previous topic"));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Action, Is.EqualTo(QChatContinuationAction.ReplyNow));
            Assert.That(decision.ShouldDispatchModel, Is.True);
            Assert.That(decision.Reason, Is.EqualTo("normal-conversation"));
        });
    }
}
