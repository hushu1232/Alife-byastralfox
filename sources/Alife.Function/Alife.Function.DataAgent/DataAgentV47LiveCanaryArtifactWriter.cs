namespace Alife.Function.DataAgent;

public sealed record DataAgentV47LiveCanaryArtifactWriteResult(
    bool Written,
    string ReasonCode,
    string FileName,
    string FilePath);

public static class DataAgentV47LiveCanaryArtifactWriter
{
    public const string FileName = "dataagent-v4.7-live-canary-closure.txt";

    public static DataAgentV47LiveCanaryArtifactWriteResult Write(
        string outputDirectory,
        DataAgentV47LiveCanaryResult? result)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
            return Rejected("v4_7_artifact_output_directory_missing");
        if (result is null)
            return Rejected("v4_7_artifact_result_missing");
        if (result.Accepted == false)
            return Rejected("v4_7_artifact_result_rejected");

        try
        {
            Directory.CreateDirectory(outputDirectory);
            string filePath = Path.GetFullPath(Path.Combine(outputDirectory, FileName));
            File.WriteAllText(filePath, DataAgentV47LiveCanaryClosureFormatter.Format(result));
            return new(true, "v4_7_artifact_written", FileName, filePath);
        }
        catch (Exception)
        {
            return Rejected("v4_7_artifact_write_failed");
        }
    }

    static DataAgentV47LiveCanaryArtifactWriteResult Rejected(string reasonCode) =>
        new(false, reasonCode, "redacted", string.Empty);
}
