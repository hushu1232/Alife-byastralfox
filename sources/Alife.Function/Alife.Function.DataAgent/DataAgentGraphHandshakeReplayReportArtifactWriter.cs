using System.Text;

namespace Alife.Function.DataAgent;

public sealed record DataAgentGraphHandshakeReplayReportArtifact(
    string Path,
    long BytesWritten,
    bool ManualOnly,
    bool StartsRuntime,
    bool InstallsDependencies,
    bool StoresSecrets,
    bool StoresSql,
    bool StoresHiddenContext,
    bool DefaultResultChanged);

public static class DataAgentGraphHandshakeReplayReportArtifactWriter
{
    public static DataAgentGraphHandshakeReplayReportArtifact Write(
        DataAgentGraphHandshakeReplayReport report,
        string outputPath)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path is required.", nameof(outputPath));

        string fullPath = Path.GetFullPath(outputPath);
        if (Directory.Exists(fullPath))
            throw new IOException("Output path must be a file path, not a directory.");

        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory) == false)
            Directory.CreateDirectory(directory);

        string artifactText = FormatArtifact(report);
        File.WriteAllText(fullPath, artifactText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return new DataAgentGraphHandshakeReplayReportArtifact(
            fullPath,
            new FileInfo(fullPath).Length,
            ManualOnly: true,
            StartsRuntime: false,
            InstallsDependencies: false,
            StoresSecrets: false,
            StoresSql: false,
            StoresHiddenContext: false,
            report.DefaultResultChanged);
    }

    static string FormatArtifact(DataAgentGraphHandshakeReplayReport report)
    {
        return string.Join(
            Environment.NewLine,
            "# DataAgent Manual Replay Report Artifact",
            "",
            "manual_replay_report_artifact=true",
            "artifact_writer=true",
            "manual_only=true",
            "starts_runtime=false",
            "installs_dependencies=false",
            "stores_secrets=false",
            "stores_sql=false",
            "stores_hidden_context=false",
            $"default_result_changed={LowerBool(report.DefaultResultChanged)}",
            "",
            DataAgentGraphHandshakeReplayReportFormatter.FormatMarkdown(report),
            string.Empty);
    }

    static string LowerBool(bool value) => value ? "true" : "false";
}
