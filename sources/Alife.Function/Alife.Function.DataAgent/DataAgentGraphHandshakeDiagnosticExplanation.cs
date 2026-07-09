using System.Text;

namespace Alife.Function.DataAgent;

public sealed record DataAgentGraphHandshakeDiagnosticExplanationResult(
    bool Accepted,
    string ReasonCode,
    string Text,
    string SourceReasonCode,
    bool CSharpWriteAuthority,
    bool SidecarWriteAuthority,
    bool RequestsVisibleText,
    bool DefaultResultChanged);

public static class DataAgentGraphHandshakeDiagnosticExplanationValidator
{
    public const int MaxExplanationChars = 320;

    static readonly string[] VisibleTextMarkers =
    [
        "qchat",
        "qq",
        "visible text",
        "send this",
        "publish this",
        "reply to user"
    ];

    public static DataAgentGraphHandshakeDiagnosticExplanationResult Validate(
        string? explanation,
        string? sourceReasonCode)
    {
        string safeSourceReasonCode = SafeToken(sourceReasonCode);

        if (string.IsNullOrWhiteSpace(explanation))
            return Rejected("diagnostic_explanation_empty", "diagnostic_explanation_unavailable", safeSourceReasonCode);

        string collapsed = CollapseWhitespace(explanation);
        if (IsUnsafe(collapsed))
            return Rejected("diagnostic_explanation_unsafe", "diagnostic_explanation_rejected", safeSourceReasonCode);

        string bounded = Bound(collapsed, MaxExplanationChars);
        return new DataAgentGraphHandshakeDiagnosticExplanationResult(
            Accepted: true,
            ReasonCode: "diagnostic_explanation_accepted",
            Text: bounded,
            SourceReasonCode: safeSourceReasonCode,
            CSharpWriteAuthority: true,
            SidecarWriteAuthority: false,
            RequestsVisibleText: false,
            DefaultResultChanged: false);
    }

    static DataAgentGraphHandshakeDiagnosticExplanationResult Rejected(
        string reasonCode,
        string text,
        string sourceReasonCode)
    {
        return new DataAgentGraphHandshakeDiagnosticExplanationResult(
            Accepted: false,
            ReasonCode: reasonCode,
            Text: text,
            SourceReasonCode: sourceReasonCode,
            CSharpWriteAuthority: true,
            SidecarWriteAuthority: false,
            RequestsVisibleText: false,
            DefaultResultChanged: false);
    }

    static bool IsUnsafe(string value)
    {
        return DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText(value) ||
            VisibleTextMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    static string Bound(string value, int maxChars)
    {
        if (value.Length <= maxChars)
            return value;

        return value[..Math.Max(0, maxChars - 3)] + "...";
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

            builder.Append(current is >= ' ' and <= '~' ? current : '_');
            previousWasWhiteSpace = false;
        }

        return builder.ToString().Trim();
    }

    static string SafeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "reason_missing";

        string trimmed = value.Trim();
        if (trimmed.Length > DataAgentGraphHandshakeLimits.MaxReasonCodeLength ||
            DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText(trimmed))
        {
            return "reason_redacted";
        }

        foreach (char current in trimmed)
        {
            if (current is >= 'A' and <= 'Z'
                or >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '_'
                or '-'
                or '.')
            {
                continue;
            }

            return "reason_redacted";
        }

        return trimmed;
    }
}

public static class DataAgentGraphHandshakeDiagnosticExplanationFormatter
{
    public static string Format(DataAgentGraphHandshakeDiagnosticExplanationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return string.Join(
            Environment.NewLine,
            "DataAgent graph diagnostic explanation",
            $"accepted={LowerBool(result.Accepted)}",
            $"reason={result.ReasonCode}",
            $"source_reason={result.SourceReasonCode}",
            $"text={result.Text}",
            $"sidecar_write_authority={LowerBool(result.SidecarWriteAuthority)}",
            $"csharp_write_authority={LowerBool(result.CSharpWriteAuthority)}",
            $"requests_visible_text={LowerBool(result.RequestsVisibleText)}",
            $"default_result_changed={LowerBool(result.DefaultResultChanged)}");
    }

    static string LowerBool(bool value) => value ? "true" : "false";
}
