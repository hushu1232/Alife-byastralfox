using System.Text;
using System.Text.Json;

namespace Alife.Tools.DataAgentReplay;

public static class DataAgentReplayReportFormatter
{
    public static string FormatMarkdown(DataAgentReplayResult result)
    {
        StringBuilder builder = new();
        builder.AppendLine($"# DataAgent Replay: {result.Fixture.Name}");
        builder.AppendLine();
        builder.AppendLine("## Fixture");
        builder.AppendLine($"- version: {result.Fixture.Version}");
        builder.AppendLine($"- caller: {result.Fixture.CallerId}");
        builder.AppendLine($"- utterance: {result.Fixture.Utterance}");
        builder.AppendLine();
        builder.AppendLine("## Route Decision");
        builder.AppendLine($"- domain: {result.Route.Domain}");
        builder.AppendLine($"- intent: {result.Route.Intent}");
        builder.AppendLine($"- reason_code: {result.Route.ReasonCode}");
        builder.AppendLine($"- allowed_tools: {string.Join(", ", result.Route.AllowedTools)}");
        builder.AppendLine();
        builder.AppendLine("## XML Policy");
        builder.AppendLine($"- allowed: {LowerBool(result.XmlPolicy.Allowed)}");
        builder.AppendLine($"- reason: {result.XmlPolicy.Reason}");
        builder.AppendLine();
        builder.AppendLine("## Route Context");
        builder.AppendLine($"- present: {LowerBool(result.RouteContext.Present)}");
        builder.AppendLine($"- tool: {result.RouteContext.ToolName}");
        builder.AppendLine($"- allows_query: {LowerBool(result.RouteContext.AllowsQuery)}");
        builder.AppendLine($"- reason_code: {result.RouteContext.ReasonCode}");
        builder.AppendLine($"- route_session_id: {result.RouteContext.RouteSessionId}");
        builder.AppendLine();
        builder.AppendLine("## Orchestration");
        builder.AppendLine($"- trace: {result.Orchestration.Trace}");
        builder.AppendLine($"- accepted: {LowerBool(result.Orchestration.Accepted)}");
        builder.AppendLine($"- row_count: {result.Orchestration.RowCount}");
        builder.AppendLine();
        builder.AppendLine("## Session");
        builder.AppendLine($"- session_id: {result.Session.SessionId}");
        builder.AppendLine($"- status: {result.Session.Status}");
        builder.AppendLine($"- active_route_session: {LowerBool(result.Session.HasActiveRouteSession)}");
        builder.AppendLine();
        builder.AppendLine("## Diagnostics");
        builder.AppendLine($"- evidence: {FirstLine(result.Diagnostics.Evidence)}");
        builder.AppendLine($"- trace: {FirstLine(result.Diagnostics.Trace)}");
        builder.AppendLine($"- progress: {FirstLine(result.Diagnostics.Progress)}");
        builder.AppendLine($"- graph: {FirstLine(result.Diagnostics.Graph)}");
        builder.AppendLine($"- qchat_evidence: {FirstLine(result.Diagnostics.QChatEvidence)}");
        builder.AppendLine($"- qchat_trace: {FirstLine(result.Diagnostics.QChatTrace)}");
        builder.AppendLine($"- qchat_progress: {FirstLine(result.Diagnostics.QChatProgress)}");
        builder.AppendLine($"- qchat_graph: {FirstLine(result.Diagnostics.QChatGraph)}");
        builder.AppendLine();
        builder.AppendLine("## Expected Markers");
        foreach (DataAgentReplayExpectedMarker marker in result.ExpectedMarkers)
            builder.AppendLine($"- {(marker.Passed ? "PASS" : "MISSING")} {marker.Marker}");
        builder.AppendLine();
        builder.AppendLine("## Offline Boundary");
        builder.AppendLine($"- sidecar_authority={LowerBool(result.OfflineBoundary.SidecarAuthority)}");
        builder.AppendLine($"- default_tests_live_runtime={LowerBool(result.OfflineBoundary.DefaultTestsLiveRuntime)}");
        builder.AppendLine($"- passed={LowerBool(result.Passed)}");
        return builder.ToString();
    }

    public static string FormatJson(DataAgentReplayResult result)
    {
        return JsonSerializer.Serialize(result, DataAgentReplayFixture.ReportJsonOptions);
    }

    static string FirstLine(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unavailable";

        return value.ReplaceLineEndings("\n").Split('\n')[0].Trim();
    }

    static string LowerBool(bool value) => value ? "true" : "false";
}
