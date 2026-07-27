using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentImageAssetStoreTests
{
    [Test]
    public void UpsertsSameShaAsOneAssetAndIncrementsSeenCount()
    {
        IDataAgentStore store = NewStore();
        DateTimeOffset firstSeen = DateTimeOffset.Parse("2026-07-26T00:00:00Z");
        DataAgentImageAssetRecord first = Record(
            "img_aaaaaaaaaaaaaaaaaaaaaaaa",
            new string('a', 64),
            "0000000000000000",
            "first visual summary",
            firstSeen);

        store.UpsertImageAsset(first);
        store.UpsertImageAsset(first with
        {
            VisualSummary = string.Empty,
            LastSeenAt = firstSeen.AddMinutes(1)
        });
        store.UpsertImageAsset(first with
        {
            VisualSummary = string.Empty,
            LastSeenAt = firstSeen.AddMinutes(-1)
        });

        DataAgentImageAssetRecord stored = store.FindImageAssetBySha256(first.Sha256)!;
        Assert.Multiple(() =>
        {
            Assert.That(stored.AssetId, Is.EqualTo(first.AssetId));
            Assert.That(stored.SeenCount, Is.EqualTo(3));
            Assert.That(stored.VisualSummary, Is.EqualTo("first visual summary"));
            Assert.That(stored.LastSeenAt, Is.EqualTo(firstSeen.AddMinutes(1)));
        });
    }

    [Test]
    public void FindsSimilarHashesByHammingDistance()
    {
        IDataAgentStore store = NewStore();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        store.UpsertImageAsset(Record(
            "img_000000000000000000000000",
            new string('0', 64),
            "0000000000000000",
            "zero",
            now));
        store.UpsertImageAsset(Record(
            "img_111111111111111111111111",
            new string('1', 64),
            "0000000000000001",
            "one bit",
            now.AddSeconds(1)));
        store.UpsertImageAsset(Record(
            "img_ffffffffffffffffffffffff",
            new string('f', 64),
            "ffffffffffffffff",
            "far",
            now.AddSeconds(2)));

        IReadOnlyList<DataAgentImageAssetMatch> matches = store.FindSimilarImageAssets(
            "0000000000000000",
            maxDistance: 2,
            maxResults: 10);

        Assert.Multiple(() =>
        {
            Assert.That(matches.Select(match => match.Asset.AssetId), Is.EqualTo(new[]
            {
                "img_000000000000000000000000",
                "img_111111111111111111111111"
            }));
            Assert.That(matches.Select(match => match.HammingDistance), Is.EqualTo(new[] { 0, 1 }));
        });
    }

    [Test]
    public void StoresOnlySanitizedUnderstandingAndRelativePaths()
    {
        IDataAgentStore store = NewStore();
        DataAgentImageAssetRecord record = Record(
            "img_bbbbbbbbbbbbbbbbbbbbbbbb",
            new string('b', 64),
            "0123456789abcdef",
            @"source https://example.invalid/a.jpg owner 3045846738 path C:\secret\image.png",
            DateTimeOffset.UtcNow) with
        {
            OcrText = "api_key=sk-secretvalue123"
        };

        store.UpsertImageAsset(record);
        DataAgentImageAssetRecord stored = store.FindImageAssetById(record.AssetId)!;

        Assert.Multiple(() =>
        {
            Assert.That(stored.RelativePath, Is.EqualTo("QChatImages/" + record.AssetId + ".png"));
            Assert.That(Path.IsPathRooted(stored.RelativePath), Is.False);
            Assert.That(stored.VisualSummary, Does.Contain("[url-hidden]"));
            Assert.That(stored.VisualSummary, Does.Contain("[number-hidden]"));
            Assert.That(stored.VisualSummary, Does.Contain("[path-hidden]"));
            Assert.That(stored.VisualSummary, Does.Not.Contain("example.invalid"));
            Assert.That(stored.VisualSummary, Does.Not.Contain("3045846738"));
            Assert.That(stored.OcrText, Is.EqualTo("[redacted]"));
            Assert.That(stored.OcrText, Does.Not.Contain("sk-secretvalue123"));
        });
    }

    [Test]
    public void RejectsAbsoluteManagedPaths()
    {
        IDataAgentStore store = NewStore();
        DataAgentImageAssetRecord record = Record(
            "img_cccccccccccccccccccccccc",
            new string('c', 64),
            "0123456789abcdef",
            string.Empty,
            DateTimeOffset.UtcNow) with
        {
            RelativePath = @"C:\outside\image.png"
        };

        Assert.That(() => store.UpsertImageAsset(record), Throws.ArgumentException);
    }

    static IDataAgentStore NewStore()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "image-asset-store-tests");
        Directory.CreateDirectory(directory);
        IDataAgentStore store = new SqliteDataAgentStore(Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite"));
        store.Initialize();
        return store;
    }

    static DataAgentImageAssetRecord Record(
        string assetId,
        string sha256,
        string perceptualHash,
        string visualSummary,
        DateTimeOffset timestamp) => new(
        assetId,
        sha256,
        perceptualHash,
        assetId,
        $"QChatImages/{assetId}.png",
        "image/png",
        128,
        32,
        32,
        visualSummary,
        string.Empty,
        timestamp,
        timestamp,
        1);
}
