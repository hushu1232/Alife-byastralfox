namespace Alife.Function.DataAgent;

public enum DataAgentAnalysisSessionStatus
{
    Active = 0,
    AwaitingClarification = 1,
    ReadyToSummarize = 2,
    Summarized = 3,
    Ended = 4,
    Rejected = 5
}

public enum DataAgentAnalysisTurnIntent
{
    NewQuestion,
    Continue,
    RefinePrevious,
    AnswerClarification,
    Summarize,
    End
}

internal static class DataAgentAnalysisTurnIntentExtensions
{
    public static bool ProducesQuery(this DataAgentAnalysisTurnIntent intent)
    {
        return intent is DataAgentAnalysisTurnIntent.NewQuestion
            or DataAgentAnalysisTurnIntent.Continue
            or DataAgentAnalysisTurnIntent.RefinePrevious
            or DataAgentAnalysisTurnIntent.AnswerClarification;
    }
}

public sealed record DataAgentAnalysisTurn(
    string TurnId,
    int Index,
    string Question,
    DataAgentAnalysisTurnIntent Intent,
    DateTimeOffset CreatedAt,
    string Dataset,
    string Sql,
    int RowCount,
    string Summary,
    bool Validated,
    string RejectedReason);

public sealed record DataAgentAnalysisSession(
    string SessionId,
    string CallerId,
    string Goal,
    DataAgentAnalysisSessionStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? LastDataset,
    string? LastSummary,
    string? PendingClarificationQuestion,
    IReadOnlyList<DataAgentAnalysisTurn> Turns);

public sealed record DataAgentAnalysisResponse(
    string SessionId,
    DataAgentAnalysisSessionStatus Status,
    DataAgentAnalysisTurnIntent Intent,
    DataAgentAnswer? Answer,
    string Summary,
    string Context,
    bool Accepted,
    string RejectedReason);
