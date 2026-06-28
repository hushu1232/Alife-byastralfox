using System.Text;

namespace Alife.Function.DataAgent;

public static class DataAgentAnalysisContextProvider
{
    const int MaxSummaryLength = 480;

    public static string Build(
        DataAgentAnalysisSession session,
        DataAgentAnalysisTurn? latestTurn = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        DataAgentAnalysisTurn? effectiveLatestTurn = latestTurn ?? GetLatestTurn(session.Turns);

        StringBuilder builder = new();
        builder.AppendLine("[data_agent_analysis_session_context]");
        builder.AppendLine($"session_id={Sanitize(session.SessionId)}");
        builder.AppendLine($"caller_id={Sanitize(session.CallerId)}");
        builder.AppendLine($"goal={Sanitize(session.Goal)}");
        builder.AppendLine($"status={session.Status}");
        builder.AppendLine($"turn_count={session.Turns.Count}");
        builder.AppendLine($"last_dataset={Sanitize(session.LastDataset ?? string.Empty)}");
        builder.AppendLine($"last_row_count={effectiveLatestTurn?.RowCount ?? 0}");
        builder.AppendLine($"last_summary={Sanitize(session.LastSummary ?? string.Empty, MaxSummaryLength)}");
        builder.AppendLine($"pending_clarification={ToLowerBool(string.IsNullOrWhiteSpace(session.PendingClarificationQuestion) == false)}");
        builder.AppendLine($"pending_summary={ToLowerBool(session.Status == DataAgentAnalysisSessionStatus.ReadyToSummarize)}");
        builder.AppendLine("[/data_agent_analysis_session_context]");
        return builder.ToString().Trim();
    }

    static DataAgentAnalysisTurn? GetLatestTurn(IEnumerable<DataAgentAnalysisTurn> turns)
    {
        return turns
            .OrderByDescending(turn => turn.Index)
            .ThenByDescending(turn => turn.CreatedAt)
            .FirstOrDefault();
    }

    static string Sanitize(string value)
    {
        return DataAgentContextFieldSanitizer.Sanitize(value);
    }

    static string Sanitize(string value, int maxLength)
    {
        return DataAgentContextFieldSanitizer.Sanitize(value, maxLength);
    }

    static string ToLowerBool(bool value)
    {
        return value ? "true" : "false";
    }
}
