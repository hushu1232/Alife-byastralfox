namespace Alife.Function.DataAgent;

public sealed record DataAgentImageAssetRecord(
    string AssetId,
    string Sha256,
    string PerceptualHash,
    string ManagedFileId,
    string RelativePath,
    string MediaType,
    long ByteLength,
    int PixelWidth,
    int PixelHeight,
    string VisualSummary,
    string OcrText,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    int SeenCount);

public sealed record DataAgentImageAssetMatch(
    DataAgentImageAssetRecord Asset,
    int HammingDistance);
