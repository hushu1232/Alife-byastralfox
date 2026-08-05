using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Alife.Function.DataAgent;
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatImageAssetServiceTests
{
    [Test]
    public async Task ArchivesOneBotImageWithStableHashesAndRelativeMetadata()
    {
        string imagePath = CreateImage();
        ImageRuntime runtime = new(imagePath);
        IDataAgentStore store = NewStore();
        string storageRoot = NewStorageRoot();
        QChatImageAssetService service = new(runtime, store, storageRoot);
        QChatImageCandidate candidate = new(
            "[CQ:image,file=qq-image,url=https://example.invalid/image.png]",
            "https://example.invalid/image.png",
            "qq-image",
            null);

        QChatPreparedImageAsset? first = await service.PrepareAsync(candidate);
        QChatPreparedImageAsset? second = await service.PrepareAsync(candidate);

        Assert.That(first, Is.Not.Null);
        Assert.That(second, Is.Not.Null);
        DataAgentImageAssetRecord stored = store.FindImageAssetById(first!.Record.AssetId)!;
        Assert.Multiple(() =>
        {
            Assert.That(first.Record.AssetId, Does.Match("^img_[0-9a-f]{24}$"));
            Assert.That(first.Record.Sha256, Does.Match("^[0-9a-f]{64}$"));
            Assert.That(first.Record.PerceptualHash, Does.Match("^[0-9a-f]{16}$"));
            Assert.That(first.Record.PixelWidth, Is.EqualTo(9));
            Assert.That(first.Record.PixelHeight, Is.EqualTo(8));
            Assert.That(first.Record.RelativePath, Does.StartWith("QChatImages/"));
            Assert.That(Path.IsPathRooted(first.Record.RelativePath), Is.False);
            Assert.That(first.Record.RelativePath, Does.Not.Contain("example.invalid"));
            Assert.That(first.Record.RelativePath, Does.Not.Contain("qq-image"));
            Assert.That(stored.SeenCount, Is.EqualTo(2));
            Assert.That(runtime.GetImageCalls, Is.EqualTo(2));
            Assert.That(File.Exists(Path.Combine(storageRoot, Path.GetFileName(stored.RelativePath))), Is.True);
        });
    }

    [Test]
    public async Task ReusesStoredUnderstandingForTheSameSha()
    {
        string imagePath = CreateImage();
        ImageRuntime runtime = new(imagePath);
        IDataAgentStore store = NewStore();
        QChatImageAssetService assets = new(runtime, store, NewStorageRoot());
        CountingVisionClient client = new("a small monochrome test image");
        QChatImageRecognitionService service = new(client, imageAssetService: assets);
        QChatImageRecognitionContext context = new(
            EnabledConfig(),
            Message("[CQ:image,file=qq-image,url=https://example.invalid/image.png]"),
            QChatSenderRole.Owner,
            IsMentionedOrWoken: false,
            IsPassiveGroupMessage: false);

        string? first = await service.BuildPromptAsync(context);
        string? second = await service.BuildPromptAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(first, Does.Contain("image_1_asset_id=img_"));
            Assert.That(second, Does.Contain("provider=dataagent-image-cache"));
            Assert.That(second, Does.Contain("image_1_summary=a small monochrome test image"));
            Assert.That(second, Does.Not.Contain(imagePath));
            Assert.That(second, Does.Not.Contain("https://example.invalid/image.png"));
            Assert.That(client.Calls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task PassiveGroupSkipDoesNotCallOneBotImageAction()
    {
        ImageRuntime runtime = new(CreateImage());
        QChatImageAssetService assets = new(runtime, NewStore(), NewStorageRoot());
        CountingVisionClient client = new("unused");
        QChatImageRecognitionService service = new(client, imageAssetService: assets);
        QChatConfig config = EnabledConfig();
        config.AnalyzePassiveGroupImages = false;

        string? prompt = await service.BuildPromptAsync(new QChatImageRecognitionContext(
            config,
            Message("[CQ:image,file=qq-image,url=https://example.invalid/image.png]", OneBotMessageType.Group),
            QChatSenderRole.GroupMember,
            IsMentionedOrWoken: false,
            IsPassiveGroupMessage: true));

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Is.Null);
            Assert.That(runtime.GetImageCalls, Is.Zero);
            Assert.That(client.Calls, Is.Zero);
        });
    }

    [Test]
    public void SafeDownloaderRejectsPrivateAddressBeforeConnecting()
    {
        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await QChatSafeImageDownloader.DownloadAsync("https://127.0.0.1/image.jpg", 1024));

        Assert.That(exception!.Message, Is.EqualTo("image_url_not_allowed"));
    }

    static QChatConfig EnabledConfig() => new()
    {
        EnableImageRecognition = true,
        ImageRecognitionProvider = "agnes",
        AgnesVisionModel = "agnes-2.0-flash",
        ImageRecognitionMaxTokens = 80,
        MaxImagesPerMessage = 2,
        AnalyzeOwnerPrivateImages = true,
        AnalyzeOwnerGroupImages = true,
        AnalyzePrivateGuestImages = true,
        AnalyzeMentionedGroupImages = true,
        AnalyzePassiveGroupImages = false
    };

    static OneBotMessageEvent Message(
        string rawMessage,
        OneBotMessageType messageType = OneBotMessageType.Private) => new()
    {
        RawMessage = rawMessage,
        UserId = 1001,
        GroupId = messageType == OneBotMessageType.Group ? 2001 : 0
    };

    static IDataAgentStore NewStore()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "qchat-image-asset-db");
        Directory.CreateDirectory(directory);
        IDataAgentStore store = new SqliteDataAgentStore(Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite"));
        store.Initialize();
        return store;
    }

    static string NewStorageRoot()
    {
        string path = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "qchat-image-assets",
            Guid.NewGuid().ToString("N"),
            "QChatImages");
        Directory.CreateDirectory(path);
        return path;
    }

    static string CreateImage()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "qchat-image-inputs");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"{Guid.NewGuid():N}.png");
        byte[] pixels =
        [
            255, 220, 190, 160, 130, 100, 70, 40, 10,
            10, 40, 70, 100, 130, 160, 190, 220, 255,
            255, 220, 190, 160, 130, 100, 70, 40, 10,
            10, 40, 70, 100, 130, 160, 190, 220, 255,
            255, 220, 190, 160, 130, 100, 70, 40, 10,
            10, 40, 70, 100, 130, 160, 190, 220, 255,
            255, 220, 190, 160, 130, 100, 70, 40, 10,
            10, 40, 70, 100, 130, 160, 190, 220, 255
        ];
        BitmapSource bitmap = BitmapSource.Create(9, 8, 96, 96, PixelFormats.Gray8, null, pixels, 9);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    sealed class CountingVisionClient(string content) : IQChatImageRecognitionClient
    {
        public string ProviderName => "agnes";
        public int Calls { get; private set; }

        public Task<QChatImageRecognitionProviderResult> AnalyzeAsync(
            QChatImageRecognitionProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(QChatImageRecognitionProviderResult.Ok(
                ProviderName,
                request.Model,
                content));
        }
    }

    sealed class ImageRuntime(string imagePath) : IOneBotRuntime
    {
        public event Action<OneBotBaseEvent>? EventReceived;
        public long BotId => 1;
        public bool IsConnected => true;
        public string Url { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public int GetImageCalls { get; private set; }

        public Task<T?> CallActionAsync<T>(string action, object? parameters = null)
        {
            if (action != "get_image")
                return Task.FromResult(default(T));
            GetImageCalls++;
            return Task.FromResult((T?)(object)new OneBotFile { Path = imagePath });
        }

        public Task ConnectAsync() => Task.CompletedTask;
        public Task SendGroupMessage(long groupId, string message) => Task.CompletedTask;
        public Task SendPrivateMessage(long userId, string message) => Task.CompletedTask;
        public Task UploadGroupFile(long groupId, string filePath, string name) => Task.CompletedTask;
        public Task UploadPrivateFile(long userId, string filePath, string name) => Task.CompletedTask;
        public Task<OneBotFile?> GetPrivateFileUrl(string fileId) => Task.FromResult<OneBotFile?>(null);
        public Task<OneBotFile?> GetGroupFileUrl(long groupId, string fileId) => Task.FromResult<OneBotFile?>(null);
        public Task<OneBotMessageEvent?> GetMessage(long messageId) => Task.FromResult<OneBotMessageEvent?>(null);
        public Task<List<OneBotForwardMessage>?> GetForwardMessage(string forwardId) => Task.FromResult<List<OneBotForwardMessage>?>(null);
        public Task<IReadOnlyList<OneBotGroupInfo>> GetGroupList() => Task.FromResult<IReadOnlyList<OneBotGroupInfo>>([]);
        public Task<IReadOnlyList<OneBotGroupMember>> GetGroupMemberList(long groupId) => Task.FromResult<IReadOnlyList<OneBotGroupMember>>([]);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
