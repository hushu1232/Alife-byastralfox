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
        Append(builder, "turn_count", pack.TurnCount.ToString());
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
        Append(builder, "audit_row_count", pack.AuditRowCount.ToString());
        Append(builder, "audit_rejected_reason", pack.AuditRejectedReason);
        Append(builder, "tool_broker_audit_allowed", Bool(pack.ToolBrokerAuditAllowed));
        Append(builder, "tool_broker_audit_reason_code", pack.ToolBrokerAuditReasonCode);
        Append(builder, "safety_summary", pack.SafetySummary);
        Append(builder, "interview_summary", pack.InterviewSummary);
        builder.Append("[/data_agent_evidence_pack]");
        return builder.ToString();
    }

    static void Append(StringBuilder builder, string key, string value)
    {
        builder.Append(key);
        builder.Append('=');
        builder.AppendLine(Sanitize(value));
    }

    static string Bool(bool value)
    {
        return value ? "true" : "false";
    }

    static string Sanitize(string value)
    {
        string sanitized = DataAgentContextFieldSanitizer.Sanitize(value);
        StringBuilder builder = new(sanitized.Length);
        bool previousWasWhiteSpace = false;

        foreach (char current in sanitized)
        {
            char next = current switch
            {
                ';' or '/' or '(' or ')' => ' ',
                _ => current
            };

            if (char.IsWhiteSpace(next))
            {
                if (previousWasWhiteSpace == false)
                    builder.Append(' ');

                previousWasWhiteSpace = true;
                continue;
            }

            builder.Append(next);
            previousWasWhiteSpace = false;
        }

        return builder.ToString().Trim();
    }
}
