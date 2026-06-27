using Alife.Function.Agent;
using Alife.Function.QChat;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System.IO;
using System.IO.Compression;

namespace Alife.Test.QChat;

[TestFixture]
[Category("Integration")]
public class QChatOwnerNotificationLiveTests
{
    [Test]
    public async Task LiveDirectOneBotSendDiagnostics()
    {
        if (Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_OWNER_NOTIFICATION") != "1")
            Assert.Ignore("Set ALIFE_QCHAT_LIVE_OWNER_NOTIFICATION=1 to send real QQ diagnostics.");

        string url = Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_URL") ?? "ws://127.0.0.1:3001";
        string token = Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_TOKEN") ?? "";
        long ownerId = ReadLongEnvironment("ALIFE_QCHAT_LIVE_OWNER_ID", 3045846738);
        long groupId = ReadLongEnvironment("ALIFE_QCHAT_LIVE_GROUP_ID", 867165927);
        string marker = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");

        OneBotRuntime runtime = new(new OneBotClient(url, token));
        await runtime.ConnectAsync();
        await using (runtime.ConfigureAwait(false))
        {
            TestContext.Out.WriteLine($"Connected OneBot bot id: {runtime.BotId}");

            Exception? privateError = await TrySendAsync(() =>
                runtime.SendPrivateMessage(ownerId, $"[AstralFox live diagnostic {marker}] private send probe"));
            Exception? groupError = await TrySendAsync(() =>
                runtime.SendGroupMessage(groupId, $"[AstralFox live diagnostic {marker}] group send probe"));

            TestContext.Out.WriteLine(privateError == null
                ? $"Private send to {ownerId}: ok"
                : $"Private send to {ownerId}: {privateError.Message}");
            TestContext.Out.WriteLine(groupError == null
                ? $"Group send to {groupId}: ok"
                : $"Group send to {groupId}: {groupError.Message}");

            Assert.That(privateError, Is.Null, "Private owner send failed.");
            Assert.That(groupError, Is.Null, "Group summary send failed.");
        }
    }

    [Test]
    public async Task LiveOwnerNotificationDeliverySendsPrivateDetailsAndGroupSummary()
    {
        if (Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_OWNER_NOTIFICATION") != "1")
            Assert.Ignore("Set ALIFE_QCHAT_LIVE_OWNER_NOTIFICATION=1 to send real QQ owner notifications.");

        string url = Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_URL") ?? "ws://127.0.0.1:3001";
        string token = Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_TOKEN") ?? "";
        long botId = ReadLongEnvironment("ALIFE_QCHAT_LIVE_BOT_ID", 3340947887);
        long ownerId = ReadLongEnvironment("ALIFE_QCHAT_LIVE_OWNER_ID", 3045846738);
        long groupId = ReadLongEnvironment("ALIFE_QCHAT_LIVE_GROUP_ID", 867165927);
        string marker = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");

        OneBotRuntime runtime = new(new OneBotClient(url, token));
        await runtime.ConnectAsync();
        await using (runtime.ConfigureAwait(false))
        {
            QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: runtime)
            {
                Configuration = new QChatConfig
                {
                    Url = url,
                    Token = token,
                    BotId = botId,
                    OwnerId = ownerId
                }
            };
            AgentOwnerNotificationPlan plan = new(
                ShouldNotifyOwner: true,
                TargetSessionId: $"qq:private:{ownerId}",
                PublicGroupSummary: $"[AstralFox F5 live {marker}] Internal control-center items need owner attention. Details were kept private.",
                PrivateMessages: [
                    $"[AstralFox F5 live {marker}] Owner confirmation required: OwnerUserIds protected configuration review."
                ],
                SourceGroupSessionId: $"qq:group:{groupId}");

            QChatOwnerNotificationDeliveryResult result = await service.DeliverOwnerNotificationPlanAsync(plan);

            Assert.That(result.Error, Is.Null);
            Assert.That(result.PrivateSentCount, Is.EqualTo(1));
            Assert.That(result.GroupSummarySent, Is.True);
        }
    }

    [Test]
    public async Task LiveSentenceStreamingDoesNotHardCutUnfinishedQqText()
    {
        if (Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_OWNER_NOTIFICATION") != "1")
            Assert.Ignore("Set ALIFE_QCHAT_LIVE_OWNER_NOTIFICATION=1 to run real QQ streaming diagnostics.");

        string url = Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_URL") ?? "ws://127.0.0.1:3001";
        string token = Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_TOKEN") ?? "";
        long botId = ReadLongEnvironment("ALIFE_QCHAT_LIVE_BOT_ID", 3340947887);
        long ownerId = ReadLongEnvironment("ALIFE_QCHAT_LIVE_OWNER_ID", 3045846738);
        long groupId = ReadLongEnvironment("ALIFE_QCHAT_LIVE_GROUP_ID", 867165927);
        string marker = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        string unfinishedGroupText = $"AstralFox sentence-streaming group no hard cut {marker} abcdefghijklmnopqrstuvwxyz0123456789";
        string unfinishedPrivateText = $"AstralFox sentence-streaming private no hard cut {marker} abcdefghijklmnopqrstuvwxyz0123456789";

        OneBotRuntime runtime = new(new OneBotClient(url, token));
        await runtime.ConnectAsync();
        await using (runtime.ConfigureAwait(false))
        {
            TrackingOneBotRuntime tracking = new(runtime);
            QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: tracking)
            {
                Configuration = new QChatConfig
                {
                    Url = url,
                    Token = token,
                    BotId = botId,
                    OwnerId = ownerId,
                    EnableBalancedTextStreaming = true
                }
            };

            await service.SendChatAsync("group", groupId, unfinishedGroupText);
            await service.SendChatAsync("private", ownerId, unfinishedPrivateText);

            IReadOnlyList<(long Target, string Message)> groupMessages = tracking.GroupMessages
                .Where(message => message.Target == groupId && message.Message.Contains(marker, StringComparison.Ordinal))
                .ToArray();
            IReadOnlyList<(long Target, string Message)> privateMessages = tracking.PrivateMessages
                .Where(message => message.Target == ownerId && message.Message.Contains(marker, StringComparison.Ordinal))
                .ToArray();

            TestContext.Out.WriteLine($"Group unfinished streaming messages to {groupId}: {groupMessages.Count}");
            TestContext.Out.WriteLine($"Private unfinished streaming messages to {ownerId}: {privateMessages.Count}");
            Assert.Multiple(() =>
            {
                Assert.That(groupMessages.Select(message => message.Message), Is.EqualTo(new[] { unfinishedGroupText }));
                Assert.That(privateMessages.Select(message => message.Message), Is.EqualTo(new[] { unfinishedPrivateText }));
            });
        }
    }

    [Test]
    public async Task LiveGroupMembersAndGroupFileUpload()
    {
        if (Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_OWNER_NOTIFICATION") != "1")
            Assert.Ignore("Set ALIFE_QCHAT_LIVE_OWNER_NOTIFICATION=1 to run real QQ group-member and file-upload diagnostics.");

        string url = Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_URL") ?? "ws://127.0.0.1:3001";
        string token = Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_TOKEN") ?? "";
        long groupId = ReadLongEnvironment("ALIFE_QCHAT_LIVE_GROUP_ID", 867165927);
        string marker = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        string filePath = Path.Combine(Path.GetTempPath(), $"astralfox-live-group-file-{marker}.txt");
        string uploadName = $"astralfox-live-group-file-{marker}.txt";
        await File.WriteAllTextAsync(filePath, $"AstralFox live group file upload probe {marker}");

        OneBotRuntime runtime = new(new OneBotClient(url, token));
        await runtime.ConnectAsync();
        await using (runtime.ConfigureAwait(false))
        {
            IReadOnlyList<OneBotGroupMember> members = await runtime.GetGroupMemberList(groupId);
            TestContext.Out.WriteLine($"Group {groupId} member count: {members.Count}");
            Assert.That(members, Is.Not.Empty, "Group member list should not be empty.");
            Assert.That(members.Any(member => member.GroupId == groupId), Is.True);

            try
            {
                await runtime.UploadGroupFile(groupId, filePath, uploadName);
                TestContext.Out.WriteLine($"Group file upload to {groupId}: ok ({uploadName})");
            }
            finally
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
        }
    }

    [Test]
    public async Task LiveStreamingImageAndPrivateFileDiagnostics()
    {
        if (Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_OWNER_NOTIFICATION") != "1")
            Assert.Ignore("Set ALIFE_QCHAT_LIVE_OWNER_NOTIFICATION=1 to run real QQ streaming/image/private-file diagnostics.");

        string url = Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_URL") ?? "ws://127.0.0.1:3001";
        string token = Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_TOKEN") ?? "";
        long botId = ReadLongEnvironment("ALIFE_QCHAT_LIVE_BOT_ID", 3340947887);
        long ownerId = ReadLongEnvironment("ALIFE_QCHAT_LIVE_OWNER_ID", 3045846738);
        long groupId = ReadLongEnvironment("ALIFE_QCHAT_LIVE_GROUP_ID", 867165927);
        string marker = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        string privateFilePath = Path.Combine(Path.GetTempPath(), $"astralfox-live-private-file-{marker}.txt");
        string imagePath = Path.Combine(Path.GetTempPath(), $"astralfox-live-image-{marker}.png");
        await File.WriteAllTextAsync(privateFilePath, $"AstralFox live private file upload probe {marker}");
        await File.WriteAllBytesAsync(imagePath, CreateSolidPng(64, 64));

        try
        {
            OneBotRuntime runtime = new(new OneBotClient(url, token));
            await runtime.ConnectAsync();
            await using (runtime.ConfigureAwait(false))
            {
                TrackingOneBotRuntime tracking = new(runtime);
                QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: tracking)
                {
                    Configuration = new QChatConfig
                    {
                        Url = url,
                        Token = token,
                        BotId = botId,
                        OwnerId = ownerId,
                        EnableBalancedTextStreaming = true
                    }
                };

                await service.SendChatAsync("group", groupId, $"[AstralFox live streaming {marker}] first segment. second segment! final segment.");
                await service.SendChatAsync("private", ownerId, $"[AstralFox live private streaming {marker}] first segment. second segment! final segment.");

                Exception? privateImageError = await TrySendAsync(() =>
                    runtime.SendPrivateMessage(ownerId, $"[CQ:image,file={imagePath.Replace('\\', '/')}]"));
                Exception? groupImageError = await TrySendAsync(() =>
                    runtime.SendGroupMessage(groupId, $"[CQ:image,file={imagePath.Replace('\\', '/')}]"));
                Exception? privateFileError = await TrySendAsync(() =>
                    runtime.UploadPrivateFile(ownerId, privateFilePath, Path.GetFileName(privateFilePath)));

                TestContext.Out.WriteLine($"Group streaming segments to {groupId}: {tracking.GroupMessages.Count(message => message.Target == groupId)}");
                TestContext.Out.WriteLine($"Private streaming segments to {ownerId}: {tracking.PrivateMessages.Count(message => message.Target == ownerId)}");
                TestContext.Out.WriteLine(privateImageError == null
                    ? $"Private image send to {ownerId}: ok ({Path.GetFileName(imagePath)})"
                    : $"Private image send to {ownerId}: {privateImageError.Message}");
                TestContext.Out.WriteLine(groupImageError == null
                    ? $"Group image send to {groupId}: ok ({Path.GetFileName(imagePath)})"
                    : $"Group image send to {groupId}: {groupImageError.Message}");
                TestContext.Out.WriteLine(privateFileError == null
                    ? $"Private file upload to {ownerId}: ok ({Path.GetFileName(privateFilePath)})"
                    : $"Private file upload to {ownerId}: {privateFileError.Message}");

                Assert.Multiple(() =>
                {
                    Assert.That(tracking.GroupMessages.Count(message => message.Target == groupId), Is.GreaterThanOrEqualTo(2));
                    Assert.That(tracking.PrivateMessages.Count(message => message.Target == ownerId), Is.GreaterThanOrEqualTo(2));
                    Assert.That(privateImageError, Is.Null, "Private image send failed.");
                    Assert.That(groupImageError, Is.Null, "Group image send failed.");
                    Assert.That(privateFileError, Is.Null, "Private file upload failed.");
                });
            }
        }
        finally
        {
            if (File.Exists(privateFilePath))
                File.Delete(privateFilePath);
            if (File.Exists(imagePath))
                File.Delete(imagePath);
        }
    }

    [Test]
    public async Task LivePrivateVideoSendDiagnostics()
    {
        if (Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_OWNER_NOTIFICATION") != "1")
            Assert.Ignore("Set ALIFE_QCHAT_LIVE_OWNER_NOTIFICATION=1 to run real QQ private-video diagnostics.");

        string url = Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_URL") ?? "ws://127.0.0.1:3001";
        string token = Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_TOKEN") ?? "";
        long botId = ReadLongEnvironment("ALIFE_QCHAT_LIVE_BOT_ID", 3340947887);
        long ownerId = ReadLongEnvironment("ALIFE_QCHAT_LIVE_OWNER_ID", 3045846738);
        string videoPath = ResolveLiveVideoPath();

        OneBotRuntime runtime = new(new OneBotClient(url, token));
        await runtime.ConnectAsync();
        await using (runtime.ConfigureAwait(false))
        {
            QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: runtime)
            {
                Configuration = new QChatConfig
                {
                    Url = url,
                    Token = token,
                    BotId = botId,
                    OwnerId = ownerId
                }
            };

            await service.QVideo(OneBotMessageType.Private, ownerId, videoPath);
            TestContext.Out.WriteLine($"Private video send to {ownerId}: ok ({Path.GetFileName(videoPath)}, {new FileInfo(videoPath).Length} bytes)");
        }
    }

    [Test]
    public async Task LiveImageFormatDiagnostics()
    {
        if (Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_OWNER_NOTIFICATION") != "1")
            Assert.Ignore("Set ALIFE_QCHAT_LIVE_OWNER_NOTIFICATION=1 to run real QQ image-format diagnostics.");

        string url = Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_URL") ?? "ws://127.0.0.1:3001";
        string token = Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_TOKEN") ?? "";
        long ownerId = ReadLongEnvironment("ALIFE_QCHAT_LIVE_OWNER_ID", 3045846738);
        long groupId = ReadLongEnvironment("ALIFE_QCHAT_LIVE_GROUP_ID", 867165927);
        string marker = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        string imagePath = Path.Combine(Path.GetTempPath(), $"astralfox-live-image-format-{marker}.png");
        byte[] imageBytes = CreateSolidPng(64, 64);
        string imageBase64 = Convert.ToBase64String(imageBytes);
        await File.WriteAllBytesAsync(imagePath, imageBytes);

        try
        {
            OneBotRuntime runtime = new(new OneBotClient(url, token));
            await runtime.ConnectAsync();
            await using (runtime.ConfigureAwait(false))
            {
                string slashPath = imagePath.Replace('\\', '/');
                (string Name, string File)[] variants = [
                    ("local-path", slashPath),
                    ("file-uri", new Uri(imagePath).AbsoluteUri),
                    ("base64", $"base64://{imageBase64}")
                ];
                List<string> successes = [];

                foreach ((string name, string file) in variants)
                {
                    Exception? privateError = await TrySendAsync(() =>
                        runtime.SendPrivateMessage(ownerId, $"[CQ:image,file={file}]"));
                    Exception? groupError = await TrySendAsync(() =>
                        runtime.SendGroupMessage(groupId, $"[CQ:image,file={file}]"));
                    bool ok = privateError == null && groupError == null;
                    if (ok)
                        successes.Add(name);
                    TestContext.Out.WriteLine(ok
                        ? $"Image variant {name}: ok"
                        : $"Image variant {name}: private={privateError?.Message ?? "ok"} group={groupError?.Message ?? "ok"}");
                }

                Assert.That(successes, Is.Not.Empty, "At least one OneBot image file format should work.");
            }
        }
        finally
        {
            if (File.Exists(imagePath))
                File.Delete(imagePath);
        }
    }

    [Test]
    public async Task LiveIncomingPrivateMentionAndPassiveGroupEvents()
    {
        if (Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_INCOMING_EVENTS") != "1")
            Assert.Ignore("Set ALIFE_QCHAT_LIVE_INCOMING_EVENTS=1 and send private, group @, and group passive messages.");

        string url = Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_URL") ?? "ws://127.0.0.1:3001";
        string token = Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_TOKEN") ?? "";
        long botId = ReadLongEnvironment("ALIFE_QCHAT_LIVE_BOT_ID", 3340947887);
        long ownerId = ReadLongEnvironment("ALIFE_QCHAT_LIVE_OWNER_ID", 3045846738);
        long groupId = ReadLongEnvironment("ALIFE_QCHAT_LIVE_GROUP_ID", 867165927);

        OneBotRuntime runtime = new(new OneBotClient(url, token));
        TaskCompletionSource<OneBotMessageEvent> privateMessage = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<OneBotMessageEvent> groupMention = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<OneBotMessageEvent> groupPassive = new(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnEvent(OneBotBaseEvent ev)
        {
            if (ev is not OneBotMessageEvent message)
                return;
            if (message.MessageType == OneBotMessageType.Private && message.UserId == ownerId)
                privateMessage.TrySetResult(message);
            if (message.MessageType == OneBotMessageType.Group && message.GroupId == groupId)
            {
                if (message.GetAtID() == botId)
                    groupMention.TrySetResult(message);
                else
                    groupPassive.TrySetResult(message);
            }
        }

        runtime.EventReceived += OnEvent;
        await runtime.ConnectAsync();
        await using (runtime.ConfigureAwait(false))
        {
            TestContext.Out.WriteLine($"Waiting for private message from {ownerId}, group @{botId}, and passive group message in {groupId}.");
            OneBotMessageEvent privateReceived = await privateMessage.Task.WaitAsync(TimeSpan.FromSeconds(90));
            OneBotMessageEvent mentionReceived = await groupMention.Task.WaitAsync(TimeSpan.FromSeconds(90));
            OneBotMessageEvent passiveReceived = await groupPassive.Task.WaitAsync(TimeSpan.FromSeconds(90));

            TestContext.Out.WriteLine($"Private event: user={privateReceived.UserId} raw={privateReceived.RawMessage}");
            TestContext.Out.WriteLine($"Group mention event: group={mentionReceived.GroupId} user={mentionReceived.UserId} raw={mentionReceived.RawMessage}");
            TestContext.Out.WriteLine($"Group passive event: group={passiveReceived.GroupId} user={passiveReceived.UserId} raw={passiveReceived.RawMessage}");

            Assert.That(privateReceived.MessageType, Is.EqualTo(OneBotMessageType.Private));
            Assert.That(mentionReceived.GetAtID(), Is.EqualTo(botId));
            Assert.That(passiveReceived.GetAtID(), Is.Not.EqualTo(botId));
        }
    }

    static long ReadLongEnvironment(string name, long fallback)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return long.TryParse(value, out long parsed) && parsed > 0 ? parsed : fallback;
    }

    static string ResolveLiveVideoPath()
    {
        string? configured = Environment.GetEnvironmentVariable("ALIFE_QCHAT_LIVE_VIDEO_PATH");
        if (string.IsNullOrWhiteSpace(configured) == false)
            return configured.Trim();

        const string defaultFolder = @"D:\Tencent Files\3045846738\nt_qq\nt_data\Video\2026-06\Ori";
        string? latest = Directory.Exists(defaultFolder)
            ? Directory.GetFiles(defaultFolder, "*.mp4", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
            : null;

        if (string.IsNullOrWhiteSpace(latest))
            throw new FileNotFoundException("No live QQ video mp4 file was found.", defaultFolder);

        return latest;
    }

    static async Task<Exception?> TrySendAsync(Func<Task> send)
    {
        try
        {
            await send();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    static byte[] CreateSolidPng(int width, int height)
    {
        using MemoryStream stream = new();
        stream.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        WritePngChunk(stream, "IHDR", BuildIhdr(width, height));

        byte[] raw = new byte[(width * 3 + 1) * height];
        for (int y = 0; y < height; y++)
        {
            int row = y * (width * 3 + 1);
            raw[row] = 0;
            for (int x = 0; x < width; x++)
            {
                int offset = row + 1 + x * 3;
                raw[offset] = 44;
                raw[offset + 1] = 126;
                raw[offset + 2] = 246;
            }
        }

        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(raw);
        WritePngChunk(stream, "IDAT", compressed.ToArray());
        WritePngChunk(stream, "IEND", []);
        return stream.ToArray();
    }

    static byte[] BuildIhdr(int width, int height)
    {
        byte[] data = new byte[13];
        WriteInt32(data, 0, width);
        WriteInt32(data, 4, height);
        data[8] = 8;
        data[9] = 2;
        data[10] = 0;
        data[11] = 0;
        data[12] = 0;
        return data;
    }

    static void WritePngChunk(Stream stream, string type, byte[] data)
    {
        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        WriteInt32(stream, data.Length);
        stream.Write(typeBytes);
        stream.Write(data);
        WriteInt32(stream, unchecked((int)Crc32(typeBytes, data)));
    }

    static void WriteInt32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        WriteInt32(bytes, 0, value);
        stream.Write(bytes);
    }

    static void WriteInt32(Span<byte> target, int offset, int value)
    {
        target[offset] = (byte)((value >> 24) & 0xff);
        target[offset + 1] = (byte)((value >> 16) & 0xff);
        target[offset + 2] = (byte)((value >> 8) & 0xff);
        target[offset + 3] = (byte)(value & 0xff);
    }

    static uint Crc32(byte[] typeBytes, byte[] data)
    {
        uint crc = 0xffffffff;
        foreach (byte value in typeBytes)
            crc = UpdateCrc32(crc, value);
        foreach (byte value in data)
            crc = UpdateCrc32(crc, value);
        return crc ^ 0xffffffff;
    }

    static uint UpdateCrc32(uint crc, byte value)
    {
        crc ^= value;
        for (int i = 0; i < 8; i++)
            crc = (crc & 1) == 1 ? 0xedb88320 ^ (crc >> 1) : crc >> 1;
        return crc;
    }

    sealed class TrackingOneBotRuntime(IOneBotRuntime inner) : IOneBotRuntime
    {
        public event Action<OneBotBaseEvent>? EventReceived
        {
            add => inner.EventReceived += value;
            remove => inner.EventReceived -= value;
        }

        public long BotId => inner.BotId;
        public bool IsConnected => inner.IsConnected;
        public string Url { get => inner.Url; set => inner.Url = value; }
        public string Token { get => inner.Token; set => inner.Token = value; }
        public List<(long Target, string Message)> GroupMessages { get; } = [];
        public List<(long Target, string Message)> PrivateMessages { get; } = [];

        public Task ConnectAsync() => inner.ConnectAsync();

        public async Task SendGroupMessage(long groupId, string message)
        {
            await inner.SendGroupMessage(groupId, message);
            GroupMessages.Add((groupId, message));
        }

        public async Task SendPrivateMessage(long userId, string message)
        {
            await inner.SendPrivateMessage(userId, message);
            PrivateMessages.Add((userId, message));
        }

        public Task UploadGroupFile(long groupId, string filePath, string name) =>
            inner.UploadGroupFile(groupId, filePath, name);

        public Task UploadPrivateFile(long userId, string filePath, string name) =>
            inner.UploadPrivateFile(userId, filePath, name);

        public Task<OneBotFile?> GetPrivateFileUrl(string fileId) => inner.GetPrivateFileUrl(fileId);
        public Task<OneBotFile?> GetGroupFileUrl(long groupId, string fileId) => inner.GetGroupFileUrl(groupId, fileId);
        public Task<OneBotMessageEvent?> GetMessage(long messageId) => inner.GetMessage(messageId);
        public Task<List<OneBotForwardMessage>?> GetForwardMessage(string forwardId) => inner.GetForwardMessage(forwardId);
        public Task<IReadOnlyList<OneBotGroupInfo>> GetGroupList() => inner.GetGroupList();
        public Task<IReadOnlyList<OneBotGroupMember>> GetGroupMemberList(long groupId) => inner.GetGroupMemberList(groupId);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
