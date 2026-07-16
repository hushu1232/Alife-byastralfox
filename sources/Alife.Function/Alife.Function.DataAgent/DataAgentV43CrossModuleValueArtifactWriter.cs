namespace Alife.Function.DataAgent;

public sealed record DataAgentV43CrossModuleValueArtifactWriteResult(
    bool Written,
    string ReasonCode,
    string FileName,
    string FilePath);

public static class DataAgentV43CrossModuleValueArtifactWriter
{
    public const string FileName = "dataagent-v4.3-cross-module-value-score.txt";

    public static DataAgentV43CrossModuleValueArtifactWriteResult Write(
        string outputDirectory,
        DataAgentV43CrossModuleValueResult? result)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
            return Rejected("v4_3_artifact_output_directory_missing");
        if (result is null)
            return Rejected("v4_3_artifact_result_missing");

        Directory.CreateDirectory(outputDirectory);
        string filePath = Path.GetFullPath(Path.Combine(outputDirectory, FileName));
        File.WriteAllText(filePath, DataAgentV43CrossModuleValueFormatter.Format(result));
        return new DataAgentV43CrossModuleValueArtifactWriteResult(
            Written: true,
            ReasonCode: "v4_3_artifact_written",
            FileName,
            filePath);
    }

    static DataAgentV43CrossModuleValueArtifactWriteResult Rejected(string reasonCode) =>
        new(false, reasonCode, "redacted", string.Empty);
}
