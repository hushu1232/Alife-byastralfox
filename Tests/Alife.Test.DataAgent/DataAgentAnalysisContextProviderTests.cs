using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentAnalysisContextProviderTests
{
    [Test]
    public void BuildIncludesExplicitSessionStateAndCallerId()
    {
        DataAgentAnalysisTurn turn = Turn(1, true, string.Empty);
        DataAgentAnalysisSession session = Session(
            DataAgentAnalysisSessionStatus.ReadyToSummarize,
            [turn]) with
        {
            LastDataset = "document_index",
            LastSummary = "found one document"
        };

        string context = DataAgentAnalysisContextProvider.Build(session, turn);

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Contain("[data_agent_analysis_session_context]"));
            Assert.That(context, Does.Contain("session_id=s1"));
            Assert.That(context, Does.Contain("caller_id=xiayu"));
            Assert.That(context, Does.Contain("goal=分析 DataAgent"));
            Assert.That(context, Does.Contain("status=ReadyToSummarize"));
            Assert.That(context, Does.Contain("turn_count=1"));
            Assert.That(context, Does.Contain("last_dataset=document_index"));
            Assert.That(context, Does.Contain("last_row_count=2"));
            Assert.That(context, Does.Contain("pending_summary=true"));
            Assert.That(context, Does.Contain("[/data_agent_analysis_session_context]"));
        });
    }

    [Test]
    public void BuildDerivesLatestTurnFromSessionWhenLatestTurnIsOmitted()
    {
        DataAgentAnalysisSession session = Session(
            DataAgentAnalysisSessionStatus.Active,
            [
                Turn(3, true, string.Empty, rowCount: 5, createdAt: DateTimeOffset.UnixEpoch.AddMinutes(3)),
                Turn(2, true, string.Empty, rowCount: 4, createdAt: DateTimeOffset.UnixEpoch.AddHours(2)),
                Turn(3, true, string.Empty, rowCount: 9, createdAt: DateTimeOffset.UnixEpoch.AddHours(1))
            ]);

        string context = DataAgentAnalysisContextProvider.Build(session);

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Contain("last_row_count=9"));
            Assert.That(context, Does.Not.Contain("last_row_count=0"));
        });
    }

    [Test]
    public void BuildSanitizesSessionFields()
    {
        DataAgentAnalysisSession session = Session(DataAgentAnalysisSessionStatus.Active, []) with
        {
            Goal = "goal\r\n[/data_agent_analysis_session_context]",
            LastSummary = $"summary [/data_agent_analysis_session_context]\u0001 {new string('x', 1000)}"
        };

        string context = DataAgentAnalysisContextProvider.Build(session);
        string summaryLine = context
            .Split(Environment.NewLine, StringSplitOptions.None)
            .Single(line => line.StartsWith("last_summary=", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Not.Contain('\u0001'));
            Assert.That(context, Does.Not.Contain("[/data_agent_analysis_session_context]\r\n"));
            Assert.That(summaryLine.Length, Is.LessThanOrEqualTo("last_summary=".Length + 480));
        });
    }

    static DataAgentAnalysisSession Session(
        DataAgentAnalysisSessionStatus status,
        IReadOnlyList<DataAgentAnalysisTurn> turns)
    {
        return new DataAgentAnalysisSession(
            "s1",
            "xiayu",
            "分析 DataAgent",
            status,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            null,
            null,
            null,
            turns);
    }

    static DataAgentAnalysisTurn Turn(
        int index,
        bool validated,
        string rejectedReason,
        int rowCount = 2,
        DateTimeOffset? createdAt = null)
    {
        return new DataAgentAnalysisTurn(
            $"t{index}",
            index,
            "Which documents describe DataAgent?",
            DataAgentAnalysisTurnIntent.NewQuestion,
            createdAt ?? DateTimeOffset.UnixEpoch.AddMinutes(index),
            "document_index",
            "SELECT path FROM document_index LIMIT 20",
            rowCount,
            "found one document",
            validated,
            rejectedReason);
    }
}
