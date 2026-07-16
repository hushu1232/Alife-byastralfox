namespace Alife.Function.DataAgent;

public sealed record DataAgentV42OperatorEvidenceArtifactWriteResult(
    bool Written,
    string ReasonCode,
    string FileName,
    string FilePath);

public static class DataAgentV42OperatorEvidenceArtifactWriter
{
    public const string FileName = "dataagent-v4.2-operator-evidence-packet.txt";

    public static DataAgentV42OperatorEvidenceArtifactWriteResult Write(
        string outputDirectory,
        DataAgentV42OperatorEvidencePacket? packet)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
            return Rejected("v4_2_artifact_output_directory_missing");

        if (packet is null)
            return Rejected("v4_2_artifact_packet_missing");

        Directory.CreateDirectory(outputDirectory);
        string filePath = Path.GetFullPath(Path.Combine(outputDirectory, FileName));
        string body = DataAgentV42OperatorEvidencePacketFormatter.Format(packet);
        File.WriteAllText(filePath, body);

        return new DataAgentV42OperatorEvidenceArtifactWriteResult(
            Written: true,
            ReasonCode: "v4_2_artifact_written",
            FileName,
            filePath);
    }

    static DataAgentV42OperatorEvidenceArtifactWriteResult Rejected(string reasonCode) =>
        new(
            Written: false,
            reasonCode,
            FileName: "redacted",
            FilePath: string.Empty);
}
