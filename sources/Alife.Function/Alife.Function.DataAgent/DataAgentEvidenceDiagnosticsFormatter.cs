using System.Globalization;
using System.Text;

namespace Alife.Function.DataAgent;

public static class DataAgentEvidenceDiagnosticsFormatter
{
    public static string Format(DataAgentEvidencePack? pack)
    {
        if (pack is null)
            return string.Join(Environment.NewLine,
                "DataAgent evidence diagnostics",
                "state=unavailable",
                "reason=evidence_pack_unavailable");

        return string.Join(Environment.NewLine,
            "DataAgent evidence diagnostics",
            $"analysis_confidence={Score(pack.AnalysisConfidence)}",
            $"answer_stability={Score(pack.AnswerStability)}",
            $"clarification_need={Score(pack.ClarificationNeed)}",
            $"risk_level={Score(pack.RiskLevel)}",
            $"state_estimate_reason_code={Sanitize(pack.StateEstimateReasonCode)}",
            $"route_allowed={Bool(pack.RouteAllowed)}",
            $"route_allows_query={Bool(pack.RouteAllowsQuery)}",
            $"executed_sql={Bool(pack.ExecutedSql)}",
            $"terminal={Bool(pack.Terminal)}",
            $"tool_broker_audit_allowed={Bool(pack.ToolBrokerAuditAllowed)}");
    }

    static string Score(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return "0";

        return Math.Clamp(value, 0.0, 1.0).ToString("0.###", CultureInfo.InvariantCulture);
    }

    static string Bool(bool value)
    {
        return value ? "true" : "false";
    }

    static string Sanitize(string value)
    {
        string sanitized = DataAgentContextFieldSanitizer.Sanitize(value ?? string.Empty)
            .Replace("[data_agent_evidence_pack]", "data_agent_evidence_pack", StringComparison.OrdinalIgnoreCase)
            .Replace("[/data_agent_evidence_pack]", "data_agent_evidence_pack", StringComparison.OrdinalIgnoreCase)
            .Replace("(data_agent_evidence_pack)", "data_agent_evidence_pack", StringComparison.OrdinalIgnoreCase)
            .Replace("(/data_agent_evidence_pack)", "data_agent_evidence_pack", StringComparison.OrdinalIgnoreCase);

        return CollapseWhitespace(sanitized);
    }

    static string CollapseWhitespace(string value)
    {
        StringBuilder builder = new(value.Length);
        bool previousWasWhiteSpace = false;

        foreach (char current in value)
        {
            if (char.IsWhiteSpace(current))
            {
                if (previousWasWhiteSpace == false)
                    builder.Append(' ');

                previousWasWhiteSpace = true;
                continue;
            }

            builder.Append(current);
            previousWasWhiteSpace = false;
        }

        return builder.ToString().Trim();
    }
}
