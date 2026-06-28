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
