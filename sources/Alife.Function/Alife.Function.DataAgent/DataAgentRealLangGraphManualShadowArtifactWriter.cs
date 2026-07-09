namespace Alife.Function.DataAgent;

public sealed record DataAgentRealLangGraphManualShadowArtifactWriteResult(
    bool Written,
    string ReasonCode,
    string FileName,
    string FilePath);

public static class DataAgentRealLangGraphManualShadowArtifactWriter
{
    public const string FileName = "dataagent-v4.0-real-langgraph-manual-shadow.txt";

    public static DataAgentRealLangGraphManualShadowArtifactWriteResult Write(
        string outputDirectory,
        DataAgentRealLangGraphManualShadowResult? result)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return new DataAgentRealLangGraphManualShadowArtifactWriteResult(
                Written: false,
                ReasonCode: "artifact_output_directory_missing",
                FileName: "redacted",
                FilePath: string.Empty);
        }

        if (result is null)
        {
            return new DataAgentRealLangGraphManualShadowArtifactWriteResult(
                Written: false,
                ReasonCode: "artifact_result_missing",
                FileName: "redacted",
                FilePath: string.Empty);
        }

        Directory.CreateDirectory(outputDirectory);
        string filePath = Path.GetFullPath(Path.Combine(outputDirectory, FileName));
        string formattedResult = DataAgentRealLangGraphManualShadowFormatter.Format(result).TrimEnd();
        string body = string.Join(
            Environment.NewLine,
            "artifact_writer=true",
            "artifact_name=dataagent-v4.0-real-langgraph-manual-shadow",
            formattedResult,
            string.Empty);

        File.WriteAllText(filePath, body);

        return new DataAgentRealLangGraphManualShadowArtifactWriteResult(
            Written: true,
            ReasonCode: "artifact_written",
            FileName,
            filePath);
    }
}
