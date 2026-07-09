using System.Text;

namespace Alife.Function.DataAgent;

public sealed record DataAgentGraphHandshakeManualAuditBundle(
    string Path,
    string ReplayReportArtifactPath,
    string ArtifactIndexPath,
    string ReplayId,
    int ComparisonCount,
    int EvidenceItemCount,
    bool DefaultResultChanged,
    bool ManualOnly,
    bool StartsRuntime,
    bool InstallsDependencies,
    bool StoresSecrets,
    bool StoresSql,
    bool StoresHiddenContext);

public static class DataAgentGraphHandshakeManualAuditBundleWriter
{
    const int EvidenceItemCount = 5;

    public static DataAgentGraphHandshakeManualAuditBundle Write(
        DataAgentGraphHandshakeReplayReport report,
        DataAgentGraphHandshakeReplayReportArtifact artifact,
        DataAgentGraphHandshakeReplayReportArtifactIndex index,
        string bundlePath)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(index);
        if (string.IsNullOrWhiteSpace(bundlePath))
            throw new ArgumentException("Bundle path is required.", nameof(bundlePath));

        string fullBundlePath = Path.GetFullPath(bundlePath);
        if (Directory.Exists(fullBundlePath))
            throw new IOException("Bundle path must be a file path, not a directory.");

        string? directory = Path.GetDirectoryName(fullBundlePath);
        if (string.IsNullOrWhiteSpace(directory) == false)
            Directory.CreateDirectory(directory);

        string artifactPath = Path.GetFullPath(artifact.Path);
        string indexPath = Path.GetFullPath(index.Path);
        string text = FormatBundle(report, artifactPath, indexPath);
        File.WriteAllText(fullBundlePath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return new DataAgentGraphHandshakeManualAuditBundle(
            fullBundlePath,
            artifactPath,
            indexPath,
            report.ReplayId,
            report.ComparisonCount,
            EvidenceItemCount,
            report.DefaultResultChanged,
            ManualOnly: true,
            StartsRuntime: false,
            InstallsDependencies: false,
            StoresSecrets: false,
            StoresSql: false,
            StoresHiddenContext: false);
    }

    static string FormatBundle(
        DataAgentGraphHandshakeReplayReport report,
        string artifactPath,
        string indexPath)
    {
        List<string> lines =
        [
            "# DataAgent Manual Audit Bundle",
            "",
            "manual_audit_bundle=true",
            "bundle_writer=true",
            "source_versions=v3.18-v3.22",
            "manual_only=true",
            "starts_runtime=false",
            "installs_dependencies=false",
            "stores_secrets=false",
            "stores_sql=false",
            "stores_hidden_context=false",
            $"default_result_changed={LowerBool(report.DefaultResultChanged)}",
            $"replay_id={SafeToken(report.ReplayId)}",
            $"comparison_count={report.ComparisonCount}",
            $"evidence_item_count={EvidenceItemCount}",
            "",
            "## Evidence",
            "includes_smoke_result_artifact=true",
            "includes_replay_fixture_pack=true",
            "includes_shadow_replay_report=true",
            "includes_manual_replay_report_artifact=true",
            "includes_manual_artifact_index=true",
            $"replay_report_artifact_path={SafePathToken(artifactPath)}",
            $"artifact_index_path={SafePathToken(indexPath)}",
            "",
            "## Categories"
        ];

        foreach ((string status, int count) in report.StatusCounts.OrderBy(item => item.Key, StringComparer.Ordinal))
            lines.Add($"{SafeToken(status)}={count}");

        lines.Add(string.Empty);
        return string.Join(Environment.NewLine, lines);
    }

    static string LowerBool(bool value) => value ? "true" : "false";

    static string SafePathToken(string value)
    {
        string fileName = Path.GetFileName(value);
        return SafeToken(fileName);
    }

    static string SafeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "redacted";

        string trimmed = value.Trim();
        if (trimmed.Length > DataAgentGraphHandshakeLimits.MaxReasonCodeLength ||
            DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText(trimmed))
        {
            return "redacted";
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

            return "redacted";
        }

        return trimmed;
    }
}
