using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentFollowUpInterpreterTests
{
    [TestCase("继续", DataAgentAnalysisTurnIntent.Continue)]
    [TestCase("接着看", DataAgentAnalysisTurnIntent.Continue)]
    [TestCase("只看失败的", DataAgentAnalysisTurnIntent.RefinePrevious)]
    [TestCase("换成 DataAgent 相关", DataAgentAnalysisTurnIntent.RefinePrevious)]
    [TestCase("总结一下", DataAgentAnalysisTurnIntent.Summarize)]
    [TestCase("这次分析结论是什么", DataAgentAnalysisTurnIntent.Summarize)]
    [TestCase("结束", DataAgentAnalysisTurnIntent.End)]
    [TestCase("停止这次分析", DataAgentAnalysisTurnIntent.End)]
    [TestCase("最近的 readiness 状态是什么", DataAgentAnalysisTurnIntent.NewQuestion)]
    [TestCase("DataAgent \u76f8\u5173\u6587\u6863\u6709\u54ea\u4e9b\uff1f", DataAgentAnalysisTurnIntent.NewQuestion)]
    [TestCase("\u6362\u6210 DataAgent \u76f8\u5173", DataAgentAnalysisTurnIntent.RefinePrevious)]
    public void InterpretsCommonChineseFollowUpPhrases(string question, DataAgentAnalysisTurnIntent expected)
    {
        DataAgentFollowUpInterpreter interpreter = new();

        Assert.That(interpreter.Interpret(question), Is.EqualTo(expected));
    }

    [Test]
    public void AwaitingClarificationTreatsPlainAnswerAsClarificationAnswer()
    {
        DataAgentAnalysisSession session = Session(DataAgentAnalysisSessionStatus.AwaitingClarification);
        DataAgentFollowUpInterpreter interpreter = new();

        DataAgentAnalysisTurnIntent intent = interpreter.Interpret("last 7 days", session);

        Assert.That(intent, Is.EqualTo(DataAgentAnalysisTurnIntent.AnswerClarification));
    }

    [Test]
    public void SummarizeWinsWhenAwaitingClarificationAndMixedWithContinue()
    {
        DataAgentAnalysisSession session = Session(DataAgentAnalysisSessionStatus.AwaitingClarification);
        DataAgentFollowUpInterpreter interpreter = new();

        DataAgentAnalysisTurnIntent intent = interpreter.Interpret("\u7ee7\u7eed\u603b\u7ed3\u4e00\u4e0b", session);

        Assert.That(intent, Is.EqualTo(DataAgentAnalysisTurnIntent.Summarize));
    }

    [Test]
    public void EndWinsWhenAwaitingClarificationAndMixedWithContinue()
    {
        DataAgentAnalysisSession session = Session(DataAgentAnalysisSessionStatus.AwaitingClarification);
        DataAgentFollowUpInterpreter interpreter = new();

        DataAgentAnalysisTurnIntent intent = interpreter.Interpret("\u7ee7\u7eed\u7ed3\u675f", session);

        Assert.That(intent, Is.EqualTo(DataAgentAnalysisTurnIntent.End));
    }

    static DataAgentAnalysisSession Session(DataAgentAnalysisSessionStatus status)
    {
        return new DataAgentAnalysisSession(
            "s1",
            "local",
            "goal",
            status,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            null,
            null,
            status == DataAgentAnalysisSessionStatus.AwaitingClarification ? "Which range?" : null,
            []);
    }
}
