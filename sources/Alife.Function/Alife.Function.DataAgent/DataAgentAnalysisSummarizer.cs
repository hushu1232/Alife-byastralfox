using System.Text;

namespace Alife.Function.DataAgent;

public static class DataAgentAnalysisSummarizer
{
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
                .Distinct(StringComparer.OrdinalIgnoreCase));
        string latestSummary = session.Turns.LastOrDefault()?.Summary ?? string.Empty;

        StringBuilder builder = new();
        builder.Append($"goal={session.Goal}; ");
        builder.Append($"turns={session.Turns.Count}; ");
        builder.Append($"validated={validated}; ");
        builder.Append($"rejected_or_clarification={rejected}; ");
        builder.Append($"datasets={datasets}; ");
        builder.Append($"latest_summary={latestSummary}");

        if (string.IsNullOrWhiteSpace(session.PendingClarificationQuestion) == false)
            builder.Append($"; pending_clarification={session.PendingClarificationQuestion}");

        return DataAgentContextFieldSanitizer.Sanitize(builder.ToString(), 720);
    }
}
