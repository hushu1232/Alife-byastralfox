using System.Text;

namespace Alife.Function.DataAgent;

public static class DataAgentAnalysisSummarizer
{
    const int MaxSummaryLength = 720;

    public static string Summarize(DataAgentAnalysisSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        int validated = session.Turns.Count(turn => turn.Validated);
        int rejected = session.Turns.Count(turn => turn.Validated == false);
        string datasets = string.Join(
            ", ",
            session.Turns
                .Select(turn => turn.Dataset)
                .Where(dataset => string.IsNullOrWhiteSpace(dataset) == false)
                .Select(SanitizeValue)
                .Distinct(StringComparer.OrdinalIgnoreCase));
        string latestSummary = session.Turns
            .OrderByDescending(turn => turn.Index)
            .ThenByDescending(turn => turn.CreatedAt)
            .FirstOrDefault()
            ?.Summary ?? string.Empty;

        StringBuilder builder = new();
        builder.Append($"goal={SanitizeValue(session.Goal)}; ");
        builder.Append($"turns={session.Turns.Count}; ");
        builder.Append($"validated={validated}; ");
        builder.Append($"rejected_or_clarification={rejected}; ");
        builder.Append($"datasets={datasets}; ");
        builder.Append($"latest_summary={SanitizeValue(latestSummary)}");

        if (string.IsNullOrWhiteSpace(session.PendingClarificationQuestion) == false)
            builder.Append($"; pending_clarification={SanitizeValue(session.PendingClarificationQuestion)}");

        return DataAgentContextFieldSanitizer.Sanitize(builder.ToString(), MaxSummaryLength);
    }

    static string SanitizeValue(string value)
    {
        return DataAgentContextFieldSanitizer
            .Sanitize(value)
            .Replace(';', ',')
            .Replace('=', ':');
    }
}
