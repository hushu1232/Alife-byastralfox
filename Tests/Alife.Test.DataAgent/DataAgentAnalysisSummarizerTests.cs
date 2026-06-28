using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentAnalysisSummarizerTests
{
    [Test]
    public void SummarizeUsesOnlyStoredTurnSnapshots()
    {
        DataAgentAnalysisSession session = new(
            "s1",
            "local",
            "分析最近失败的测试",
            DataAgentAnalysisSessionStatus.Active,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            "test_run",
            "latest test run had 3 failures",
            null,
            [
                Turn(1, "test_run", true, string.Empty, "latest test run had 3 failures"),
                Turn(2, "test_run", false, "needs_clarification", "DataAgent needs clarification")
            ]);

        string summary = DataAgentAnalysisSummarizer.Summarize(session);

        Assert.Multiple(() =>
        {
            Assert.That(summary, Does.Contain("goal=分析最近失败的测试"));
            Assert.That(summary, Does.Contain("turns=2"));
            Assert.That(summary, Does.Contain("validated=1"));
            Assert.That(summary, Does.Contain("rejected_or_clarification=1"));
            Assert.That(summary, Does.Contain("datasets=test_run"));
            Assert.That(summary, Does.Contain("latest_summary=DataAgent needs clarification"));
        });
    }

    static DataAgentAnalysisTurn Turn(
        int index,
        string dataset,
        bool validated,
        string rejectedReason,
        string summary)
    {
        return new DataAgentAnalysisTurn(
            $"t{index}",
            index,
            $"question {index}",
            DataAgentAnalysisTurnIntent.NewQuestion,
            DateTimeOffset.UnixEpoch.AddMinutes(index),
            dataset,
            "SELECT 1 LIMIT 1",
            validated ? 1 : 0,
            summary,
            validated,
            rejectedReason);
    }
}
