namespace Alife.Function.DataAgent;

public enum DataAgentAnalysisSessionStatus
{
    Active,
    AwaitingClarification,
    ReadyToSummarize,
    Summarized,
    Ended
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
