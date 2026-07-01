using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatSemanticStateEstimatorTests
{
    [Test]
    public void EstimateWaitsWhenLatestMessageLooksLikeContinuation()
    {
        DateTimeOffset start = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
        QChatSemanticWindowSnapshot snapshot = new(
            [
                new QChatSemanticWindowMessage(1, 100, "我想问一下", false, start),
                new QChatSemanticWindowMessage(2, 100, "关于 DataAgent:", false, start.AddSeconds(1))
            ],
            start,
            start.AddSeconds(1));

        QChatSemanticStateEstimate estimate = QChatSemanticStateEstimator.Estimate(
            snapshot,
            start.AddSeconds(5),
            new QChatSemanticSettleOptions());

        Assert.Multiple(() =>
        {
            Assert.That(estimate.ShouldWait, Is.True);
            Assert.That(estimate.ShouldAnswer, Is.False);
            Assert.That(estimate.SemanticCompletion, Is.LessThan(0.70));
            Assert.That(estimate.ReasonCode, Is.EqualTo("semantic_continuation_likely"));
        });
    }

    [Test]
    public void EstimateAnswersAfterStableQuestionAndSettleDelay()
    {
        DateTimeOffset start = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
        QChatSemanticWindowSnapshot snapshot = new(
            [
                new QChatSemanticWindowMessage(1, 100, "DataAgent 的状态机现在怎么维护？", false, start)
            ],
            start,
            start);

        QChatSemanticStateEstimate estimate = QChatSemanticStateEstimator.Estimate(
            snapshot,
            start.AddSeconds(6),
            new QChatSemanticSettleOptions());

        Assert.Multiple(() =>
        {
            Assert.That(estimate.ShouldWait, Is.False);
            Assert.That(estimate.ShouldAnswer, Is.True);
            Assert.That(estimate.SemanticCompletion, Is.GreaterThanOrEqualTo(0.75));
            Assert.That(estimate.ReasonCode, Is.EqualTo("semantic_completion_stable"));
        });
    }

    [Test]
    public void EstimateSummarizesWhenSummaryIntentIsStable()
    {
        DateTimeOffset start = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
        QChatSemanticWindowSnapshot snapshot = new(
            [
                new QChatSemanticWindowMessage(1, 100, "总结一下刚才 DataAgent V2.4 到 V2.5 的方案", false, start)
            ],
            start,
            start);

        QChatSemanticStateEstimate estimate = QChatSemanticStateEstimator.Estimate(
            snapshot,
            start.AddSeconds(8),
            new QChatSemanticSettleOptions());

        Assert.Multiple(() =>
        {
            Assert.That(estimate.ShouldSummarize, Is.True);
            Assert.That(estimate.SemanticCompletion, Is.GreaterThanOrEqualTo(0.80));
            Assert.That(estimate.ReasonCode, Is.EqualTo("summary_intent_stable"));
        });
    }
}
