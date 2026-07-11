namespace Alife.Function.DataAgent;

public sealed record DataAgentV45ProductionClosureArtifactWriteResult(
    bool Written,
    string ReasonCode,
    string FileName,
    string FilePath);

public static class DataAgentV45ProductionClosureArtifactWriter
{
    public const string FileName = "dataagent-v4.5-production-closure.txt";

    public static DataAgentV45ProductionClosureArtifactWriteResult Write(
        string outputDirectory,
        DataAgentV45ProductionClosureResult? result)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
            return Rejected("v4_5_artifact_output_directory_missing");
        if (result is null)
            return Rejected("v4_5_artifact_result_missing");

        Directory.CreateDirectory(outputDirectory);
        string filePath = Path.GetFullPath(Path.Combine(outputDirectory, FileName));
        File.WriteAllText(filePath, DataAgentV45ProductionClosureFormatter.Format(result));
        return new DataAgentV45ProductionClosureArtifactWriteResult(
            true,
            "v4_5_artifact_written",
            FileName,
            filePath);
    }

    static DataAgentV45ProductionClosureArtifactWriteResult Rejected(string reasonCode) =>
        new(false, reasonCode, "redacted", string.Empty);
}
