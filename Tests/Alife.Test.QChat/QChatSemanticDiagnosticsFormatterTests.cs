using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatSemanticDiagnosticsFormatterTests
{
    [Test]
    public void FormatWithEstimateEmitsStableSemanticDiagnostics()
    {
        QChatSemanticStateEstimate estimate = new(
            SemanticCompletion: 0.7345,
            ContinuationLikelihood: 0.2214,
            TopicStability: 0.8,
            SummaryIntent: 0.05,
            ShouldWait: false,
            ShouldAnswer: true,
            ShouldSummarize: false,
            ReasonCode: "semantic_completion_stable");
        QChatSemanticDiagnosticsSnapshot snapshot = new(
            estimate,
            WindowMessageCount: 1,
            WindowAge: TimeSpan.FromSeconds(6),
            LastUpdateAge: TimeSpan.FromSeconds(3));

        string text = QChatSemanticDiagnosticsFormatter.Format(snapshot);

        string[] expectedLines =
        [
            "QChat semantic diagnostics",
            "semantic_completion=0.735",
            "continuation_likelihood=0.221",
            "topic_stability=0.8",
            "summary_intent=0.05",
            "should_wait=false",
            "should_answer=true",
            "should_summarize=false",
            "reason_code=semantic_completion_stable",
            "window_messages=1",
            "window_age_seconds=6",
            "last_update_age_seconds=3"
        ];
        Assert.That(text.Split(Environment.NewLine), Is.EqualTo(expectedLines));
    }

    [Test]
    public void FormatWithoutEstimateEmitsTruthfulUnavailableState()
    {
        QChatSemanticDiagnosticsSnapshot snapshot = new(
            Estimate: null,
            WindowMessageCount: 0,
            WindowAge: TimeSpan.Zero,
            LastUpdateAge: TimeSpan.Zero);

        string text = QChatSemanticDiagnosticsFormatter.Format(snapshot);

        string[] expectedLines =
        [
            "QChat semantic diagnostics",
            "state=unavailable",
            "reason=semantic_window_empty"
        ];
        Assert.That(text.Split(Environment.NewLine), Is.EqualTo(expectedLines));
    }

    [Test]
    public void FormatClampsInvalidNumericValues()
    {
        QChatSemanticStateEstimate estimate = new(
            SemanticCompletion: double.NaN,
            ContinuationLikelihood: double.PositiveInfinity,
            TopicStability: -5,
            SummaryIntent: 7,
            ShouldWait: true,
            ShouldAnswer: false,
            ShouldSummarize: false,
            ReasonCode: "semantic_continuation_likely");
        QChatSemanticDiagnosticsSnapshot snapshot = new(
            estimate,
            WindowMessageCount: -3,
            WindowAge: TimeSpan.FromSeconds(-2),
            LastUpdateAge: TimeSpan.FromSeconds(-1));

        string text = QChatSemanticDiagnosticsFormatter.Format(snapshot);

        string[] expectedLines =
        [
            "QChat semantic diagnostics",
            "semantic_completion=0",
            "continuation_likelihood=0",
            "topic_stability=0",
            "summary_intent=1",
            "should_wait=true",
            "should_answer=false",
            "should_summarize=false",
            "reason_code=semantic_continuation_likely",
            "window_messages=0",
            "window_age_seconds=0",
            "last_update_age_seconds=0"
        ];
        Assert.That(text.Split(Environment.NewLine), Is.EqualTo(expectedLines));
    }
}
