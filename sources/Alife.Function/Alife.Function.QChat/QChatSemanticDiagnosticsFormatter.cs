using System;
using System.Globalization;

namespace Alife.Function.QChat;

public sealed record QChatSemanticDiagnosticsSnapshot(
    QChatSemanticStateEstimate? Estimate,
    int WindowMessageCount,
    TimeSpan WindowAge,
    TimeSpan LastUpdateAge);

public static class QChatSemanticDiagnosticsFormatter
{
    public static string Format(QChatSemanticDiagnosticsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        QChatSemanticStateEstimate? estimate = snapshot.Estimate;
        if (estimate is null)
        {
            return string.Join(Environment.NewLine,
                "QChat semantic diagnostics",
                "state=unavailable",
                "reason=semantic_window_empty");
        }

        return string.Join(Environment.NewLine,
            "QChat semantic diagnostics",
            "semantic_completion=" + FormatScore(estimate.SemanticCompletion),
            "continuation_likelihood=" + FormatScore(estimate.ContinuationLikelihood),
            "topic_stability=" + FormatScore(estimate.TopicStability),
            "summary_intent=" + FormatScore(estimate.SummaryIntent),
            "should_wait=" + FormatBool(estimate.ShouldWait),
            "should_answer=" + FormatBool(estimate.ShouldAnswer),
            "should_summarize=" + FormatBool(estimate.ShouldSummarize),
            "reason_code=" + NormalizeReasonCode(estimate.ReasonCode),
            "window_messages=" + Math.Max(0, snapshot.WindowMessageCount).ToString(CultureInfo.InvariantCulture),
            "window_age_seconds=" + FormatSeconds(snapshot.WindowAge),
            "last_update_age_seconds=" + FormatSeconds(snapshot.LastUpdateAge));
    }

    static string FormatScore(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return "0";

        return Math.Clamp(value, 0.0, 1.0).ToString("0.###", CultureInfo.InvariantCulture);
    }

    static string FormatSeconds(TimeSpan value)
    {
        double seconds = Math.Max(0.0, value.TotalSeconds);
        return seconds.ToString("0.###", CultureInfo.InvariantCulture);
    }

    static string FormatBool(bool value)
    {
        return value ? "true" : "false";
    }

    static string NormalizeReasonCode(string? reasonCode)
    {
        if (string.IsNullOrWhiteSpace(reasonCode))
            return "";

        return string.Join(' ', reasonCode.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
