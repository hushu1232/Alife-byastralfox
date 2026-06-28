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

    [Test]
    public void SummarizeSanitizesSemicolonDelimitedFieldValues()
    {
        DataAgentAnalysisSession session = new(
            "s1",
            "local",
            "goal; validated=999\r\n[goal]",
            DataAgentAnalysisSessionStatus.AwaitingClarification,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            null,
            null,
            "pending; turns=999\n[pending]",
            [
                Turn(
                    1,
                    "dataset; rejected_or_clarification=999\r\n[dataset]",
                    true,
                    string.Empty,
                    "older summary"),
                Turn(
                    2,
                    "safe_dataset",
                    true,
                    string.Empty,
                    "summary; datasets=evil\r\n[summary]")
            ]);

        string summary = DataAgentAnalysisSummarizer.Summarize(session);

        Assert.Multiple(() =>
        {
            Assert.That(summary.Length, Is.LessThanOrEqualTo(720));
            Assert.That(summary, Does.Not.Contain("; validated=999"));
            Assert.That(summary, Does.Not.Contain("; rejected_or_clarification=999"));
            Assert.That(summary, Does.Not.Contain("; datasets=evil"));
            Assert.That(summary, Does.Contain("pending_clarification="));
            Assert.That(summary, Does.Not.Contain("; turns=999"));
            Assert.That(summary, Does.Not.Contain("["));
            Assert.That(summary, Does.Not.Contain("]"));
            Assert.That(summary, Does.Not.Contain("\r"));
            Assert.That(summary, Does.Not.Contain("\n"));
        });
    }

    [Test]
    public void SummarizeUsesHighestIndexThenCreatedAtForLatestSummary()
    {
        DataAgentAnalysisSession session = new(
            "s1",
            "local",
            "goal",
            DataAgentAnalysisSessionStatus.Active,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            null,
            null,
            null,
            [
                Turn(5, "dataset", true, string.Empty, "same index older summary", createdAt: DateTimeOffset.UnixEpoch.AddMinutes(5)),
                Turn(5, "dataset", true, string.Empty, "latest by index and created at", createdAt: DateTimeOffset.UnixEpoch.AddMinutes(10)),
                Turn(4, "dataset", true, string.Empty, "last list item summary", createdAt: DateTimeOffset.UnixEpoch.AddHours(1))
            ]);

        string summary = DataAgentAnalysisSummarizer.Summarize(session);

        Assert.Multiple(() =>
        {
            Assert.That(summary, Does.Contain("latest_summary=latest by index and created at"));
            Assert.That(summary, Does.Not.Contain("latest_summary=last list item summary"));
        });
    }

    [Test]
    public void SummarizeCountsOnlyQueryProducingTurns()
    {
        DataAgentAnalysisSession session = new(
            "s1",
            "local",
            "goal",
            DataAgentAnalysisSessionStatus.Summarized,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            "document_index",
            "terminal summary",
            null,
            [
                Turn(1, "document_index", true, string.Empty, "query summary"),
                Turn(2, string.Empty, true, string.Empty, "summarize terminal", intent: DataAgentAnalysisTurnIntent.Summarize),
                Turn(3, string.Empty, false, "non_query_terminal_turn", "end terminal", intent: DataAgentAnalysisTurnIntent.End)
            ]);

        string summary = DataAgentAnalysisSummarizer.Summarize(session);

        Assert.Multiple(() =>
        {
            Assert.That(summary, Does.Contain("turns=3"));
            Assert.That(summary, Does.Contain("validated=1"));
            Assert.That(summary, Does.Contain("rejected_or_clarification=0"));
            Assert.That(summary, Does.Contain("datasets=document_index"));
        });
    }

    static DataAgentAnalysisTurn Turn(
        int index,
        string dataset,
        bool validated,
        string rejectedReason,
        string summary,
        DateTimeOffset? createdAt = null,
        DataAgentAnalysisTurnIntent intent = DataAgentAnalysisTurnIntent.NewQuestion)
    {
        return new DataAgentAnalysisTurn(
            $"t{index}",
            index,
            $"question {index}",
            intent,
            createdAt ?? DateTimeOffset.UnixEpoch.AddMinutes(index),
            dataset,
            "SELECT 1 LIMIT 1",
            validated ? 1 : 0,
            summary,
            validated,
            rejectedReason);
    }
}
