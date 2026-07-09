using System.Text;

namespace Alife.Function.DataAgent;

public sealed record DataAgentGraphHandshakeReplayReportArtifactIndex(
    string Path,
    string ArtifactPath,
    string ReplayId,
    int ComparisonCount,
    bool DefaultResultChanged,
    bool ManualOnly,
    bool StartsRuntime,
    bool InstallsDependencies,
    bool StoresSecrets,
    bool StoresSql,
    bool StoresHiddenContext);

public static class DataAgentGraphHandshakeReplayReportArtifactIndexWriter
{
    public static DataAgentGraphHandshakeReplayReportArtifactIndex Write(
        DataAgentGraphHandshakeReplayReport report,
        DataAgentGraphHandshakeReplayReportArtifact artifact,
        string indexPath)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(artifact);
        if (string.IsNullOrWhiteSpace(indexPath))
            throw new ArgumentException("Index path is required.", nameof(indexPath));

        string fullIndexPath = Path.GetFullPath(indexPath);
        if (Directory.Exists(fullIndexPath))
            throw new IOException("Index path must be a file path, not a directory.");

        string? directory = Path.GetDirectoryName(fullIndexPath);
        if (string.IsNullOrWhiteSpace(directory) == false)
            Directory.CreateDirectory(directory);

        string artifactPath = Path.GetFullPath(artifact.Path);
        string text = FormatIndex(report, artifactPath);
        File.WriteAllText(fullIndexPath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return new DataAgentGraphHandshakeReplayReportArtifactIndex(
            fullIndexPath,
            artifactPath,
            report.ReplayId,
            report.ComparisonCount,
            report.DefaultResultChanged,
            ManualOnly: true,
            StartsRuntime: false,
            InstallsDependencies: false,
            StoresSecrets: false,
            StoresSql: false,
            StoresHiddenContext: false);
    }

    static string FormatIndex(DataAgentGraphHandshakeReplayReport report, string artifactPath)
    {
        List<string> lines =
        [
            "# DataAgent Manual Artifact Index",
            "",
            "manual_artifact_index=true",
            "manifest_writer=true",
            "manual_only=true",
            "starts_runtime=false",
            "installs_dependencies=false",
            "stores_secrets=false",
            "stores_sql=false",
            "stores_hidden_context=false",
            $"artifact_path={SafePathToken(artifactPath)}",
            $"replay_id={SafeToken(report.ReplayId)}",
            $"comparison_count={report.ComparisonCount}",
            $"default_result_changed={LowerBool(report.DefaultResultChanged)}",
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
