using Alife.Framework.Models.StateEstimation;

namespace Alife.Function.DataAgent;

public sealed record DataAgentAnalysisStateEstimate(
    double AnalysisConfidence,
    double AnswerStability,
    double ClarificationNeed,
    double RiskLevel,
    bool ShouldContinue,
    bool ShouldSummarize,
    bool ToolPermissionAllowed,
    string ReasonCode);

public static class DataAgentAnalysisStateEstimator
{
    public static DataAgentAnalysisStateEstimate Estimate(DataAgentEvidencePack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);

        bool routeDenied = pack.RouteAllowed == false ||
            pack.Trace.Contains("RouteGate:Rejected", StringComparison.Ordinal);
        bool acceptedQuery = pack.RouteAllowed &&
            pack.RouteAllowsQuery &&
            pack.ExecutedSql &&
            pack.AuditValidated;
        bool terminalNoQuery = pack.SafetySummary.Contains("terminal_no_query", StringComparison.Ordinal) ||
            pack.Trace.Contains("Summarize:Succeeded", StringComparison.Ordinal) ||
            pack.Trace.Contains("End:Succeeded", StringComparison.Ordinal);

        KalmanScalarFilter confidence = new(0.35, 0.60);
        KalmanScalarFilter stability = new(0.35, 0.60);
        KalmanScalarFilter clarification = new(0.45, 0.50);
        KalmanScalarFilter risk = new(0.40, 0.50);

        confidence = confidence.Predict(0.06).Update(acceptedQuery ? 0.92 : routeDenied ? 0.20 : 0.55, routeDenied ? 0.20 : 0.18);
        stability = stability.Predict(0.05).Update(pack.AuditValidated && pack.AuditRowCount > 0 ? 0.86 : terminalNoQuery ? 0.70 : 0.30, 0.20);
        clarification = clarification.Predict(0.05).Update(routeDenied ? 0.70 : acceptedQuery ? 0.18 : 0.55, 0.25);
        risk = risk.Predict(0.06).Update(routeDenied ? 0.92 : acceptedQuery ? 0.12 : 0.45, routeDenied ? 0.16 : 0.25);

        bool shouldSummarize = terminalNoQuery ||
            (pack.CanSummarize && stability.Value >= 0.70 && risk.Value <= 0.35);
        bool shouldContinue = pack.CanContinue &&
            routeDenied == false &&
            terminalNoQuery == false &&
            confidence.Value >= 0.60;
        string reasonCode = routeDenied
            ? "route_denied_no_query"
            : terminalNoQuery
                ? "terminal_no_query"
                : acceptedQuery
                    ? "analysis_evidence_stable"
                    : "analysis_needs_more_evidence";

        return new DataAgentAnalysisStateEstimate(
            confidence.Value,
            stability.Value,
            clarification.Value,
            risk.Value,
            shouldContinue,
            shouldSummarize,
            pack.RouteAllowed,
            reasonCode);
    }
}
