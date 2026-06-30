using System.Globalization;
using System.Text;

namespace Alife.Function.DataAgent;

public static class DataAgentEvidencePackFormatter
{
    public static string Format(DataAgentEvidencePack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);

        StringBuilder builder = new();
        builder.AppendLine("[data_agent_evidence_pack]");
        Append(builder, "session_id", pack.SessionId);
        Append(builder, "status", pack.SessionStatus.ToString());
        Append(builder, "turn_count", pack.TurnCount.ToString(CultureInfo.InvariantCulture));
        Append(builder, "route_present", Bool(pack.RoutePresent));
        Append(builder, "route_tool", pack.RouteTool);
        Append(builder, "route_allowed", Bool(pack.RouteAllowed));
        Append(builder, "route_allows_query", Bool(pack.RouteAllowsQuery));
        Append(builder, "route_reason_code", pack.RouteReasonCode);
        Append(builder, "trace", pack.Trace);
        Append(builder, "executed_sql", Bool(pack.ExecutedSql));
        Append(builder, "terminal", Bool(pack.Terminal));
        Append(builder, "can_continue", Bool(pack.CanContinue));
        Append(builder, "can_summarize", Bool(pack.CanSummarize));
        Append(builder, "audit_validated", Bool(pack.AuditValidated));
        Append(builder, "audit_dataset", pack.AuditDataset);
        Append(builder, "audit_row_count", pack.AuditRowCount.ToString(CultureInfo.InvariantCulture));
        Append(builder, "audit_rejected_reason", pack.AuditRejectedReason);
        Append(builder, "tool_broker_audit_allowed", Bool(pack.ToolBrokerAuditAllowed));
        Append(builder, "tool_broker_audit_reason_code", pack.ToolBrokerAuditReasonCode);
        Append(builder, "safety_summary", pack.SafetySummary, SanitizeTokenList);
        Append(builder, "interview_summary", pack.InterviewSummary);
        builder.Append("[/data_agent_evidence_pack]");
        return builder.ToString();
    }

    static void Append(StringBuilder builder, string key, string value)
    {
        Append(builder, key, value, SanitizeField);
    }

    static void Append(StringBuilder builder, string key, string value, Func<string, string> sanitize)
    {
        builder.Append(key);
        builder.Append('=');
        builder.AppendLine(sanitize(value));
    }

    static string Bool(bool value)
    {
        return value ? "true" : "false";
    }

    static string SanitizeField(string value)
    {
        return NeutralizeEvidencePackCloseTag(DataAgentContextFieldSanitizer.Sanitize(value));
    }

    static string SanitizeTokenList(string value)
    {
        return CollapseWhitespace(SanitizeField(value).Replace(';', ' '));
    }

    static string NeutralizeEvidencePackCloseTag(string value)
    {
        return value.Replace("(/data_agent_evidence_pack)", "data_agent_evidence_pack", StringComparison.Ordinal);
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
