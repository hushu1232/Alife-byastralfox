using Alife.Function.QChat;
using Alife.Function.Agent;
using Alife.Function.Emotion;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;
using Alife.Framework;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using NUnit.Framework;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace Alife.Test.QChat;

[TestFixture]
public class QChatServiceAdapterTests
{
    [Test]
    public async Task SendChatAsync_UsesInjectedRuntime()
    {
        FakeOneBotRuntime runtime = new();
        FakeLifeEventPublisher publisher = new();
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: runtime, lifeEventPublisher: publisher)
        {
            Configuration = new QChatConfig { BotId = 999 }
        };

        await service.SendChatAsync("group", 123, " hello ");
        await service.SendChatAsync("private", 456, " hi ");

        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] { (123L, "hello") }));
        Assert.That(runtime.PrivateMessages, Is.EqualTo(new[] { (456L, "hi") }));
        Assert.That(publisher.Events.Select(lifeEvent => lifeEvent.Kind), Is.EqualTo(new[] {
            LifeEventKind.Communication,
            LifeEventKind.Communication,
        }));
        Assert.That(publisher.Events.Select(lifeEvent => lifeEvent.Summary), Is.EqualTo(new[] {
            "You sent a QQ group message to 123.",
            "You sent a QQ private message to 456.",
        }));
    }

    [Test]
    public async Task SendChatAsync_CoalescesShortCompleteSentencesWithBalancedStreamingPolicy()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: runtime)
        {
            Configuration = new QChatConfig { BotId = 999 }
        };

        await service.SendChatAsync("group", 123, "第一句。第二句！最后一句");

        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] {
            (123L, "第一句。第二句！最后一句"),
        }));
    }

    [Test]
    public async Task SendChatAsync_DoesNotSplitOpenCqCodeInGroupText()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: runtime)
        {
            Configuration = new QChatConfig { BotId = 999 }
        };

        await service.SendChatAsync("group", 123, "[CQ:at,qq=3045846738]收到。后续继续");

        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] {
            (123L, "[CQ:at,qq=3045846738]收到。后续继续"),
        }));
    }

    [Test]
    public async Task SendChatAsync_DoesNotSplitMediumCompleteReplyJustForStreaming()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: runtime)
        {
            Configuration = new QChatConfig { BotId = 999 }
        };
        string message = "这是第一段完整但不需要单独发送的说明文字，它只是整条回复的一部分。第二句继续补充，不应该为了制造流式效果拆成两条。";

        await service.SendChatAsync("group", 123, message);

        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] { (123L, message) }));
    }

    [Test]
    public void DefaultAppendChatPromptPrefersNaturalAddressingOverGroupAt()
    {
        QChatConfig config = new();

        Assert.That(config.AppendChatPrompt, Does.Contain("\u81ea\u7136\u79f0\u547c"));
        Assert.That(config.AppendChatPrompt, Does.Contain("\u4e0d\u8981\u9ed8\u8ba4@"));
        Assert.That(config.AppendChatPrompt, Does.Not.Contain("\u56de\u590d\u65f6\u8bf7\u52a0\u4e0aCQat\u6807\u7b7e"));
    }

    [Test]
    public void DefaultAppendChatPromptRequiresHonestNoGuessingAndHiddenReasoning()
    {
        QChatConfig config = new();

        Assert.That(config.AppendChatPrompt, Does.Contain("不要展示思考过程"));
        Assert.That(config.AppendChatPrompt, Does.Contain("不能把记忆或猜测当作实时事实"));
        Assert.That(config.AppendChatPrompt, Does.Contain("没有可靠依据时要自然承认不确定"));
    }

    [Test]
    public void DefaultAppendChatPromptAllowsColdShortRepliesWithoutInternalStatus()
    {
        QChatConfig config = new();

        Assert.That(config.AppendChatPrompt, Does.Contain("。/。。。/？/绷"));
        Assert.That(config.AppendChatPrompt, Does.Contain("啧"));
        Assert.That(config.AppendChatPrompt, Does.Contain("不要输出心理状态"));
        Assert.That(config.AppendChatPrompt, Does.Contain("刻薄"));
    }

    [Test]
    public void DefaultAppendChatPromptDefinesXiaYuAsSeventeenYearOldGirlWithCapabilities()
    {
        QChatConfig config = new();

        Assert.That(config.AppendChatPrompt, Does.Contain("夏羽"));
        Assert.That(config.AppendChatPrompt, Does.Contain("17岁少女"));
        Assert.That(config.AppendChatPrompt, Does.Contain("术术"));
        Assert.That(config.AppendChatPrompt, Does.Contain("高智商"));
        Assert.That(config.AppendChatPrompt, Does.Contain("工具"));
        Assert.That(config.AppendChatPrompt, Does.Contain("电脑"));
        Assert.That(config.AppendChatPrompt, Does.Contain("文件"));
        Assert.That(config.AppendChatPrompt, Does.Contain("QQ"));
        Assert.That(config.AppendChatPrompt, Does.Contain("不是QQ内置机器人"));
        Assert.That(config.AppendChatPrompt, Does.Not.Contain("猫娘"));
        Assert.That(config.AppendChatPrompt, Does.Not.Contain("咪绪"));
        Assert.That(config.AppendChatPrompt, Does.Not.Contain("喵"));
    }

    [Test]
    public void ChatTextFilterFramesQqAsXiaYuUsingQqInsteadOfToolTask()
    {
        ExposedFilterQChatService service = new(new FakeOneBotRuntime())
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001
            }
        };

        string filtered = service.FilterForTest("hello");

        Assert.That(filtered, Does.Contain("你刚在QQ里看到"));
        Assert.That(filtered, Does.Contain("夏羽会实际发到QQ的文本"));
        Assert.That(filtered, Does.Contain("不要在QQ里提工具"));
        Assert.That(filtered, Does.Contain("安全标签和路由标签不是QQ内容"));
        Assert.That(filtered, Does.Not.Contain("这是QQ消息，请用QQ工具处理"));
    }

    [Test]
    public void SendChatAsync_SendFailureDoesNotThrowWhenChatContextIsUnavailable()
    {
        FakeOneBotRuntime runtime = new()
        {
            SendException = new InvalidOperationException("network unavailable")
        };
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: runtime)
        {
            Configuration = new QChatConfig { BotId = 999 }
        };

        Assert.DoesNotThrowAsync(async () => await service.SendChatAsync("group", 123, "hello"));
    }

    [Test]
    public async Task QGroupFile_UsesInjectedRuntimeAndCustomName()
    {
        string file = Path.GetTempFileName();
        await File.WriteAllTextAsync(file, "group file");
        FakeOneBotRuntime runtime = new();
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: runtime)
        {
            Configuration = new QChatConfig { BotId = 999 }
        };

        await service.QGroupFile(123, file, "report.txt");

        Assert.That(runtime.GroupFiles, Is.EqualTo(new[] { (123L, file.Replace('\\', '/'), "report.txt") }));
    }

    [Test]
    public async Task QGroupFile_UsesSecurityGatewayForExternalRequests()
    {
        string file = Path.GetTempFileName();
        await File.WriteAllTextAsync(file, "group file");
        FakeOneBotRuntime runtime = new();
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: runtime)
        {
            Configuration = new QChatConfig { BotId = 999 }
        };
        AgentPermissionConfig config = new()
        {
            OwnerUserIds = [10001],
            RequireConfirmationForHighRisk = true
        };

        QChatExternalActionResult blocked = await service.QGroupFile(
            123,
            file,
            "report.txt",
            new AgentPermissionRequest(
                ActorUserId: 20002,
                Source: AgentRequestSource.GroupChat,
                IsMentioned: true,
                RiskLevel: AgentRiskLevel.Low,
                HasExplicitConfirmation: true,
                Action: "qq.group_file_upload"),
            config);
        QChatExternalActionResult needsConfirmation = await service.QGroupFile(
            123,
            file,
            "report.txt",
            new AgentPermissionRequest(
                ActorUserId: 10001,
                Source: AgentRequestSource.PrivateChat,
                IsMentioned: false,
                RiskLevel: AgentRiskLevel.Low,
                HasExplicitConfirmation: false,
                Action: "qq.group_file_upload"),
            config);
        QChatExternalActionResult executed = await service.QGroupFile(
            123,
            file,
            "report.txt",
            new AgentPermissionRequest(
                ActorUserId: 10001,
                Source: AgentRequestSource.PrivateChat,
                IsMentioned: false,
                RiskLevel: AgentRiskLevel.Low,
                HasExplicitConfirmation: true,
                Action: "qq.group_file_upload"),
            config);

        Assert.That(blocked.Executed, Is.False);
        Assert.That(blocked.GatewayDecision.Status, Is.EqualTo(AgentExecutionDecisionStatus.Blocked));
        Assert.That(blocked.GatewayDecision.RiskLevel, Is.EqualTo(AgentRiskLevel.High));
        Assert.That(needsConfirmation.Executed, Is.False);
        Assert.That(needsConfirmation.GatewayDecision.Status, Is.EqualTo(AgentExecutionDecisionStatus.OwnerConfirmationRequired));
        Assert.That(executed.Executed, Is.True);
        Assert.That(runtime.GroupFiles, Is.EqualTo(new[] { (123L, file.Replace('\\', '/'), "report.txt") }));
    }

    [Test]
    public async Task QGroupFile_SecurityGatewayAuditsBlockedAndAllowedActions()
    {
        string file = Path.GetTempFileName();
        await File.WriteAllTextAsync(file, "group file");
        string root = Path.Combine(Path.GetTempPath(), "alife-qchat-gateway-tests", Guid.NewGuid().ToString("N"));
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentActionGatewayService gateway = new(auditLog: audit);
        FakeOneBotRuntime runtime = new();
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: runtime, actionGateway: gateway)
        {
            Configuration = new QChatConfig { BotId = 999 }
        };
        AgentPermissionConfig config = new()
        {
            OwnerUserIds = [10001],
            RequireConfirmationForHighRisk = true
        };

        QChatExternalActionResult blocked = await service.QGroupFile(
            123,
            file,
            "report.txt",
            new AgentPermissionRequest(
                ActorUserId: 20002,
                Source: AgentRequestSource.GroupChat,
                IsMentioned: true,
                RiskLevel: AgentRiskLevel.Low,
                HasExplicitConfirmation: true,
                Action: "qq.group_file_upload"),
            config);
        QChatExternalActionResult executed = await service.QGroupFile(
            123,
            file,
            "report.txt",
            new AgentPermissionRequest(
                ActorUserId: 10001,
                Source: AgentRequestSource.PrivateChat,
                IsMentioned: false,
                RiskLevel: AgentRiskLevel.Low,
                HasExplicitConfirmation: true,
                Action: "qq.group_file_upload"),
            config);
        AgentAuditLogEntry[] entries = audit.GetRecentEntries(10).ToArray();

        Assert.That(blocked.Executed, Is.False);
        Assert.That(executed.Executed, Is.True);
        Assert.That(entries.Select(entry => entry.Action), Is.EqualTo(new[] { "qq.group_file_upload", "qq.group_file_upload" }));
        Assert.That(entries[0].Succeeded, Is.False);
        Assert.That(entries[0].Error, Does.Contain("Blocked"));
        Assert.That(entries[1].Succeeded, Is.True);
        Assert.That(runtime.GroupFiles, Is.EqualTo(new[] { (123L, file.Replace('\\', '/'), "report.txt") }));
    }

    [Test]
    public async Task QPrivateFile_UsesInjectedRuntimeAndDefaultName()
    {
        string file = Path.Combine(Path.GetTempPath(), $"qchat-private-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(file, "private file");
        FakeOneBotRuntime runtime = new();
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: runtime)
        {
            Configuration = new QChatConfig { BotId = 999 }
        };

        await service.QPrivateFile(456, file);

        Assert.That(runtime.PrivateFiles, Is.EqualTo(new[] { (456L, file.Replace('\\', '/'), Path.GetFileName(file)) }));
    }

    [Test]
    public async Task PrivateFileMessageRegistersManagedPendingFileWithoutDownloading()
    {
        string previousStorage = Alife.Platform.AlifePath.StorageFolderPath;
        string storageRoot = Path.Combine(Path.GetTempPath(), "alife-qchat-file-message-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storageRoot);
        try
        {
            Alife.Platform.AlifePath.SetStorageFolderPath(storageRoot, persist: false);
            FakeOneBotRuntime runtime = new();
            runtime.PrivateFileUrls["doc-1"] = new OneBotFile
            {
                Url = "https://example.invalid/report.txt",
                Size = "12"
            };
            CapturingQChatService service = new(new XmlFunctionCaller(new NullLogger<XmlFunctionCaller>()), runtime)
            {
                Configuration = new QChatConfig
                {
                    BotId = 999,
                    OwnerId = 1001
                }
            };
            StartService(service);

            runtime.Raise(new OneBotMessageEvent
            {
                SelfId = 999,
                UserId = 1001,
                RawMessage = "[CQ:file,file=report.txt,file_id=doc-1,file_size=12]"
            });

            QChatInboundMessage inbound = await service.WaitForInboundAsync();

            Assert.That(inbound.Formatted, Does.Contain("managed_file_id="));
            Assert.That(inbound.Formatted, Does.Contain("status=pending-not-downloaded"));
            Assert.That(inbound.Formatted, Does.Contain("qchat_file_download"));
            Assert.That(inbound.Formatted, Does.Not.Contain("https://example.invalid/report.txt"));
            Assert.That(File.Exists(Path.Combine(storageRoot, "AgentWorkspace", "QChatFiles", "pending-index.json")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(storageRoot, "AgentWorkspace", "QChatFiles", "downloads")), Is.False);
        }
        finally
        {
            Alife.Platform.AlifePath.SetStorageFolderPath(previousStorage, persist: false);
        }
    }

    [Test]
    public async Task QChatFileToolsDownloadReadAndDeleteManagedTextFile()
    {
        string previousStorage = Alife.Platform.AlifePath.StorageFolderPath;
        string storageRoot = Path.Combine(Path.GetTempPath(), "alife-qchat-file-tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storageRoot);
        try
        {
            Alife.Platform.AlifePath.SetStorageFolderPath(storageRoot, persist: false);
            QChatManagedFileService managedFiles = new(
                Path.Combine(storageRoot, "AgentWorkspace", "QChatFiles"),
                (_, _) => Task.FromResult(Encoding.UTF8.GetBytes("tool text content")));
            QChatManagedFileRecord record = await managedFiles.RegisterAsync(new QChatManagedFileRegistration(
                MessageType: OneBotMessageType.Private,
                SenderId: 1001,
                GroupId: 0,
                FileId: "tool-file-1",
                OriginalName: "tool.txt",
                Size: 17,
                Url: "https://example.invalid/tool.txt"));
            FakeOneBotRuntime runtime = new();
            QChatService service = new(
                new XmlFunctionCaller(new NullLogger<XmlFunctionCaller>()),
                new NullLogger<QChatService>(),
                oneBotRuntime: runtime,
                managedFileService: managedFiles)
            {
                Configuration = new QChatConfig
                {
                    BotId = 999,
                    OwnerId = 1001
                }
            };
            StartService(service);

            await service.QChatFileList();
            await service.QChatFileDownload(record.Id);
            string afterDownload = GetPendingPokeText(service);
            IReadOnlyList<QChatManagedFileRecord> records = await managedFiles.ListAsync();
            string localPath = records.Single(item => item.Id == record.Id).LocalPath!;

            await service.QChatFileRead(record.Id);
            await service.QChatFileDelete(record.Id);
            string afterDelete = GetPendingPokeText(service);

            Assert.That(afterDownload, Does.Contain(record.Id));
            Assert.That(afterDownload, Does.Contain("tool text content"));
            Assert.That(File.Exists(localPath), Is.False);
            Assert.That(afterDelete, Does.Contain("deleted"));
        }
        finally
        {
            Alife.Platform.AlifePath.SetStorageFolderPath(previousStorage, persist: false);
        }
    }

    [Test]
    public async Task QVideo_SendsCqVideoToGroup()
    {
        string video = Path.Combine(Path.GetTempPath(), $"qchat-video-{Guid.NewGuid():N}.mp4");
        await File.WriteAllTextAsync(video, "fake mp4");
        FakeOneBotRuntime runtime = new();
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: runtime)
        {
            Configuration = new QChatConfig { BotId = 999 }
        };

        await service.QVideo(OneBotMessageType.Group, 123, video);

        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] { (123L, $"[CQ:video,file={video.Replace('\\', '/')}]") }));
    }

    [Test]
    public async Task QVideo_UsesSecurityGatewayForExternalRequests()
    {
        string video = Path.Combine(Path.GetTempPath(), $"qchat-video-{Guid.NewGuid():N}.mp4");
        await File.WriteAllTextAsync(video, "fake mp4");
        FakeOneBotRuntime runtime = new();
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: runtime)
        {
            Configuration = new QChatConfig { BotId = 999 }
        };
        AgentPermissionConfig config = new()
        {
            OwnerUserIds = [10001],
            RequireConfirmationForHighRisk = true
        };

        QChatExternalActionResult blocked = await service.QVideo(
            OneBotMessageType.Group,
            123,
            video,
            new AgentPermissionRequest(
                ActorUserId: 20002,
                Source: AgentRequestSource.GroupChat,
                IsMentioned: true,
                RiskLevel: AgentRiskLevel.Low,
                HasExplicitConfirmation: true,
                Action: "qq.video_send"),
            config);
        QChatExternalActionResult executed = await service.QVideo(
            OneBotMessageType.Group,
            123,
            video,
            new AgentPermissionRequest(
                ActorUserId: 10001,
                Source: AgentRequestSource.PrivateChat,
                IsMentioned: false,
                RiskLevel: AgentRiskLevel.Low,
                HasExplicitConfirmation: true,
                Action: "qq.video_send"),
            config);

        Assert.That(blocked.Executed, Is.False);
        Assert.That(blocked.GatewayDecision.Status, Is.EqualTo(AgentExecutionDecisionStatus.Blocked));
        Assert.That(blocked.GatewayDecision.RiskLevel, Is.EqualTo(AgentRiskLevel.High));
        Assert.That(executed.Executed, Is.True);
        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] { (123L, $"[CQ:video,file={video.Replace('\\', '/')}]") }));
    }

    [Test]
    public async Task OwnerNotificationDeliverySendsPrivateDetailsAndSanitizedGroupSummary()
    {
        string root = Path.Combine(Path.GetTempPath(), "alife-qchat-owner-notification-tests", Guid.NewGuid().ToString("N"));
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentControlCenterService controlCenter = new(auditLog: audit);
        controlCenter.ProposeConfigurationChange("OwnerUserIds", "10001", "agent", "owner identity is protected");
        ChatRuntimeState runtimeState = new(
            IsChatting: false,
            PendingPokeCount: 0,
            ChatHistoryCount: 0,
            LastError: null,
            RecentEvents: []);
        AgentControlCenterSnapshot snapshot = controlCenter.BuildSnapshot(runtimeState, "Kira");
        AgentOwnerNotificationPlan plan = AgentControlCenterService.BuildOwnerNotificationPlan(
            snapshot,
            ownerPrivateSessionId: "qq:private:3045846738",
            sourceGroupSessionId: "qq:group:867165927");
        AgentAuditLogService deliveryAudit = new(Path.Combine(root, "delivery-audit.jsonl"));
        FakeOneBotRuntime runtime = new();
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: runtime, auditLog: deliveryAudit)
        {
            Configuration = new QChatConfig { BotId = 999 }
        };

        QChatOwnerNotificationDeliveryResult result = await service.DeliverOwnerNotificationPlanAsync(plan);

        Assert.That(runtime.PrivateMessages, Has.Count.EqualTo(1));
        Assert.That(runtime.PrivateMessages[0].Target, Is.EqualTo(3045846738));
        Assert.That(runtime.PrivateMessages[0].Message, Does.Contain("OwnerUserIds"));
        Assert.That(runtime.GroupMessages, Has.Count.EqualTo(1));
        Assert.That(runtime.GroupMessages[0].Target, Is.EqualTo(867165927));
        Assert.That(runtime.GroupMessages[0].Message, Does.Contain("owner attention"));
        Assert.That(runtime.GroupMessages[0].Message, Does.Not.Contain("OwnerUserIds"));
        Assert.That(result.PrivateSentCount, Is.EqualTo(1));
        Assert.That(result.GroupSummarySent, Is.True);
        Assert.That(deliveryAudit.GetRecentEntries(10).Select(entry => entry.Action), Is.EqualTo(new[] {
            "qq.owner_notification.private",
            "qq.owner_notification.group_summary",
        }));
    }

    [Test]
    public async Task OwnerNotificationDeliverySkipsWhenPlanDoesNotNeedNotification()
    {
        AgentOwnerNotificationPlan plan = new(
            ShouldNotifyOwner: false,
            TargetSessionId: "qq:private:3045846738",
            PublicGroupSummary: "No owner attention is currently required.",
            PrivateMessages: []);
        FakeOneBotRuntime runtime = new();
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: runtime)
        {
            Configuration = new QChatConfig { BotId = 999 }
        };

        QChatOwnerNotificationDeliveryResult result = await service.DeliverOwnerNotificationPlanAsync(plan);

        Assert.That(runtime.PrivateMessages, Is.Empty);
        Assert.That(runtime.GroupMessages, Is.Empty);
        Assert.That(result.PrivateSentCount, Is.EqualTo(0));
        Assert.That(result.GroupSummarySent, Is.False);
    }

    [Test]
    public void QVideo_RejectsUnsupportedLocalExtension()
    {
        string video = Path.Combine(Path.GetTempPath(), $"qchat-video-{Guid.NewGuid():N}.txt");
        File.WriteAllText(video, "not a video");
        FakeOneBotRuntime runtime = new();
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: runtime)
        {
            Configuration = new QChatConfig { BotId = 999 }
        };

        Assert.ThrowsAsync<InvalidOperationException>(() => service.QVideo(OneBotMessageType.Group, 123, video));
    }

    [Test]
    public void QGroupFile_RejectsDisallowedGroupWhenWhitelistConfigured()
    {
        string file = Path.GetTempFileName();
        File.WriteAllText(file, "group file");
        FakeOneBotRuntime runtime = new();
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: runtime)
        {
            Configuration = new QChatConfig {
                BotId = 999,
                AllowedGroupIds = "999"
            }
        };

        Assert.ThrowsAsync<InvalidOperationException>(() => service.QGroupFile(123, file));
        Assert.That(runtime.GroupFiles, Is.Empty);
    }

    [Test]
    public async Task RelationCacheRefreshesGroupMembersFromRuntime()
    {
        FakeOneBotRuntime runtime = new();
        runtime.GroupMemberLists[123] = [
            new OneBotGroupMember { GroupId = 123, UserId = 1001, Nickname = "Alice", Card = "A-card", Role = "member" },
            new OneBotGroupMember { GroupId = 123, UserId = 1002, Nickname = "Bob", Role = "admin" }
        ];
        QChatRelationCacheService service = new(runtime);

        QChatGroupMemberCacheSnapshot snapshot = await service.RefreshGroupMembersAsync(123);

        Assert.That(snapshot.GroupId, Is.EqualTo(123));
        Assert.That(snapshot.Members.Select(member => member.UserId), Is.EqualTo(new[] { 1001L, 1002L }));
        Assert.That(snapshot.Members[0].DisplayName, Is.EqualTo("A-card"));
        Assert.That(service.TryGetMember(123, 1002)?.DisplayName, Is.EqualTo("Bob"));
    }

    [Test]
    public void RelationCacheReturnsEmptySnapshotForUnknownGroup()
    {
        QChatRelationCacheService service = new(new FakeOneBotRuntime());

        QChatGroupMemberCacheSnapshot snapshot = service.GetCachedGroupMembers(123);

        Assert.That(snapshot.GroupId, Is.EqualTo(123));
        Assert.That(snapshot.Members, Is.Empty);
    }

    [Test]
    public async Task QChatServiceExposesJoinedGroupProviderForControlCenter()
    {
        FakeOneBotRuntime runtime = new()
        {
            GroupLists = [
                new OneBotGroupInfo { GroupId = 867165927, GroupName = "test group", MemberCount = 3, MaxMemberCount = 200 }
            ]
        };
        QChatService service = new(new XmlFunctionCaller(new NullLogger<XmlFunctionCaller>()), new NullLogger<QChatService>(), oneBotRuntime: runtime)
        {
            Configuration = new QChatConfig { BotId = 999, OwnerId = 1001 }
        };

        Assert.That(service, Is.AssignableTo<Alife.Function.Agent.IAgentQChatJoinedGroupProvider>());
        Alife.Function.Agent.AgentQChatJoinedGroupSourceSnapshot snapshot =
            await ((Alife.Function.Agent.IAgentQChatJoinedGroupProvider)service).RefreshAgentJoinedGroupsAsync();

        Assert.That(snapshot.Groups, Has.Count.EqualTo(1));
        Assert.That(snapshot.Groups[0].GroupId, Is.EqualTo(867165927));
        Assert.That(snapshot.Groups[0].GroupName, Is.EqualTo("test group"));
    }

    [Test]
    public async Task RelationCacheJoinedGroupRefreshDuringQqContextRepliesToCurrentGroup()
    {
        FakeOneBotRuntime runtime = new()
        {
            GroupLists =
            [
                new OneBotGroupInfo { GroupId = 867165927, GroupName = "AstralFoxTest", MemberCount = 3, MaxMemberCount = 200 },
                new OneBotGroupInfo { GroupId = 768420784, GroupName = "ACG动漫交流会", MemberCount = 184, MaxMemberCount = 200 }
            ]
        };
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        QChatRelationCacheService relationCache = new(runtime);
        QChatService service = new(
            functionCaller,
            new NullLogger<QChatService>(),
            oneBotRuntime: runtime,
            relationCacheService: relationCache)
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                AllowGroupMemberChat = true,
                AllowGroupMemberMentions = true,
                FlushInterval = 0,
                EnableBalancedTextStreaming = false
            }
        };
        Character character = new() { Name = "QChatRelationReplyTest" };
        ChatHistoryAgentThread thread = new();
        AwakeContext awakeContext = new()
        {
            Character = character,
            ContextBuilder = thread,
            KernelBuilder = Kernel.CreateBuilder()
        };
        await functionCaller.AwakeAsync(awakeContext);
        await relationCache.AwakeAsync(awakeContext);
        await service.AwakeAsync(awakeContext);

        Kernel kernel = Kernel.CreateBuilder().Build();
        ChatBot chatBot = new(null!, thread);
        ChatActivity activity = new(character, kernel, null!, chatBot, []);
        await functionCaller.StartAsync(kernel, activity);
        await relationCache.StartAsync(kernel, activity);
        await service.StartAsync(kernel, activity);
        service.InboundChatDispatcher = _ => relationCache.RefreshJoinedGroups();

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            GroupId = 867165927,
            GroupName = "AstralFoxTest",
            Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
            RawMessage = "[CQ:at,qq=999] 刷新一下你现在加入的群列表，不要靠记忆"
        });

        await WaitUntilAsync(() => runtime.GroupMessages.Count > 0);
        Assert.That(runtime.GroupMessages.Single().Target, Is.EqualTo(867165927));
        Assert.That(runtime.GroupMessages.Single().Message, Does.Contain("AstralFoxTest"));
        Assert.That(runtime.GroupMessages.Single().Message, Does.Contain("ACG动漫交流会"));
    }

    [Test]
    public async Task QChatAllowlistStatusDuringQqContextRepliesToCurrentGroup()
    {
        FakeOneBotRuntime runtime = new();
        QChatConfig config = new()
        {
            BotId = 999,
            OwnerId = 1001,
            AllowedGroupIds = "867165927",
            AllowedPrivateUserIds = "1001",
            AllowMentionOutsideAllowedGroups = false,
            AllowGroupMemberChat = true,
            AllowGroupMemberMentions = true,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        };
        QChatService service = CreateStartedService(runtime, config);
        service.InboundChatDispatcher = _ => service.QChatAllowlistStatus();

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            GroupId = 867165927,
            GroupName = "AstralFoxTest",
            Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
            RawMessage = "[CQ:at,qq=999] 查看白名单状态"
        });

        await WaitUntilAsync(() => runtime.GroupMessages.Count > 0);
        Assert.That(runtime.GroupMessages.Single().Target, Is.EqualTo(867165927));
        Assert.That(runtime.GroupMessages.Single().Message, Does.Contain("867165927"));
        Assert.That(runtime.GroupMessages.Single().Message, Does.Contain("1001"));
        Assert.That(runtime.GroupMessages.Single().Message, Does.Contain("AllowMentionOutsideAllowedGroups: false"));
    }

    [Test]
    public async Task QChatAllowlistUpdateOwnerCanAddGroupDuringQqContext()
    {
        FakeOneBotRuntime runtime = new();
        QChatConfig config = new()
        {
            BotId = 999,
            OwnerId = 1001,
            AllowedGroupIds = "867165927",
            AllowPrivateGuestChat = true,
            EnableBalancedTextStreaming = false
        };
        QChatService service = CreateStartedService(runtime, config);
        service.InboundChatDispatcher = _ => service.QChatAllowlistUpdate("group", "add", 768420784);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "把 768420784 加入群白名单"
        });

        await WaitUntilAsync(() => config.AllowedGroupIds.Contains("768420784", StringComparison.Ordinal));
        Assert.That(config.AllowedGroupIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            Is.EquivalentTo(new[] { "867165927", "768420784" }));
        Assert.That(runtime.PrivateMessages.Single().Message, Does.Contain("768420784"));
    }

    [Test]
    public async Task QChatAllowlistUpdateXmlToolOwnerCanAddGroupDuringQqContext()
    {
        FakeOneBotRuntime runtime = new();
        QChatConfig config = new()
        {
            BotId = 999,
            OwnerId = 1001,
            AllowedGroupIds = "867165927",
            AllowPrivateGuestChat = true,
            EnableBalancedTextStreaming = false
        };
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        QChatService service = new(functionCaller, new NullLogger<QChatService>(), oneBotRuntime: runtime)
        {
            Configuration = config
        };
        Character character = new() { Name = "QChatAllowlistXmlToolTest" };
        ChatHistoryAgentThread thread = new();
        AwakeContext awakeContext = new()
        {
            Character = character,
            ContextBuilder = thread,
            KernelBuilder = Kernel.CreateBuilder()
        };
        await functionCaller.AwakeAsync(awakeContext);
        await service.AwakeAsync(awakeContext);
        Kernel kernel = Kernel.CreateBuilder().Build();
        ChatBot chatBot = new(null!, thread);
        ChatActivity activity = new(character, kernel, null!, chatBot, []);
        await functionCaller.StartAsync(kernel, activity);
        await service.StartAsync(kernel, activity);
        service.InboundChatDispatcher = async _ =>
        {
            RaiseEvent(chatBot, "ChatReceived", "<qchat_allowlist_update target=\"group\" action=\"add\" id=\"768420784\" />");
            await WaitUntilAsync(() => config.AllowedGroupIds.Contains("768420784", StringComparison.Ordinal));
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "把 768420784 加入群白名单"
        });

        await WaitUntilAsync(() => runtime.PrivateMessages.Count > 0);
        Assert.That(config.AllowedGroupIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            Is.EquivalentTo(new[] { "867165927", "768420784" }));
        Assert.That(runtime.PrivateMessages.Single().Message, Does.Contain("768420784"));
    }

    [Test]
    public async Task QChatAllowlistUpdateRejectsNonOwnerDuringQqContext()
    {
        FakeOneBotRuntime runtime = new();
        QChatConfig config = new()
        {
            BotId = 999,
            OwnerId = 1001,
            AllowedGroupIds = "867165927",
            AllowPrivateGuestChat = true,
            EnableBalancedTextStreaming = false
        };
        QChatService service = CreateStartedService(runtime, config);
        service.InboundChatDispatcher = _ => service.QChatAllowlistUpdate("group", "add", 768420784);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2002,
            RawMessage = "把 768420784 加入群白名单"
        });

        await WaitUntilAsync(() => runtime.PrivateMessages.Count > 0);
        Assert.That(config.AllowedGroupIds, Is.EqualTo("867165927"));
        Assert.That(runtime.PrivateMessages.Single().Message, Does.Contain("只有主人"));
    }

    [Test]
    public void RelationCacheExposesXmlTools()
    {
        string[] xmlFunctionNames = typeof(QChatRelationCacheService)
            .GetMethods()
            .Select(method => method.GetCustomAttributes(typeof(Alife.Function.Interpreter.XmlFunctionAttribute), inherit: false)
                .OfType<Alife.Function.Interpreter.XmlFunctionAttribute>()
                .FirstOrDefault())
            .OfType<Alife.Function.Interpreter.XmlFunctionAttribute>()
            .Select(attribute => attribute.Name ?? string.Empty)
            .ToArray();

        Assert.That(xmlFunctionNames, Does.Contain("qchat_group_members_refresh"));
        Assert.That(xmlFunctionNames, Does.Contain("qchat_group_members_cache"));
        Assert.That(xmlFunctionNames, Does.Contain("qchat_joined_groups_refresh"));
        Assert.That(xmlFunctionNames, Does.Contain("qchat_joined_groups_cache"));
    }

    [Test]
    public async Task AwakeRegistersQChatToolInInitialFunctionGuide()
    {
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        QChatService service = new(functionCaller, new NullLogger<QChatService>(), oneBotRuntime: new FakeOneBotRuntime())
        {
            Configuration = new QChatConfig { BotId = 999, OwnerId = 1001 }
        };

        await service.AwakeAsync(new AwakeContext
        {
            Character = new Character { Name = "QChatGuideTest" },
            ContextBuilder = new ChatHistoryAgentThread(),
            KernelBuilder = Kernel.CreateBuilder(),
        });
        string guide = functionCaller.BuildFunctionGuide();

        Assert.That(guide, Does.Contain("qchat"));
        Assert.That(guide, Does.Contain("targetid"));
        Assert.That(guide, Does.Contain("</qchat>"));
        Assert.That(guide, Does.Contain("qchat_quiet_mode"));
        Assert.That(guide, Does.Contain("qchat_allowlist_status"));
        Assert.That(guide, Does.Contain("qchat_allowlist_update"));
        Assert.That(guide, Does.Contain("qchat_joined_groups_refresh"));
        Assert.That(guide, Does.Contain("qchat_joined_groups_cache"));
    }

    [Test]
    public void GetQChatGuideUsesCorrectQChatClosingTagInUsageExample()
    {
        QChatService service = CreateStartedService(new FakeOneBotRuntime(), new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001
        });

        service.GetQChatGuide();

        string guide = GetPendingPokeText(service);
        Assert.That(guide, Does.Contain("</qchat>"));
        Assert.That(guide, Does.Not.Contain("</qchar>"));
    }

    [Test]
    public void GetQChatGuideFramesQqAsSendingCapabilityNotBotIdentity()
    {
        QChatService service = CreateStartedService(new FakeOneBotRuntime(), new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001
        });

        service.GetQChatGuide();

        string guide = GetPendingPokeText(service);
        Assert.That(guide, Does.Contain("QQ发送能力说明"));
        Assert.That(guide, Does.Contain("决定发QQ消息时"));
        Assert.That(guide, Does.Contain("普通群聊不要默认@"));
        Assert.That(guide, Does.Contain("强提醒"));
        Assert.That(guide, Does.Not.Contain("QQ工具使用指南"));
    }

    [Test]
    public void QuietModeControlCanBeChangedByAgentToolOrControlCenter()
    {
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: new FakeOneBotRuntime())
        {
            Configuration = new QChatConfig { BotId = 999, OwnerId = 1001 }
        };

        service.QChatQuietMode(true, "manual-control");

        Assert.That(service.IsQuietModeEnabled, Is.True);
        Assert.That(service.QuietModeReason, Is.EqualTo("manual-control"));
        Assert.That(service.QuietModeChangedAt, Is.Not.Null);

        service.QChatQuietMode(false, "resume-control");

        Assert.That(service.IsQuietModeEnabled, Is.False);
        Assert.That(service.QuietModeReason, Is.EqualTo("resume-control"));
    }

    [Test]
    public void QuietModeControlMirrorsRuntimeStateToConfiguration()
    {
        QChatConfig config = new() { BotId = 999, OwnerId = 1001 };
        QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: new FakeOneBotRuntime())
        {
            Configuration = config
        };

        service.QChatQuietMode(true, "persist-me");

        Assert.That(config.PersistedQuietModeEnabled, Is.True);
        Assert.That(config.PersistedQuietModeReason, Is.EqualTo("persist-me"));
        Assert.That(config.PersistedQuietModeChangedAt, Is.Not.Null);
    }

    [Test]
    public async Task OwnerCanUseQuietModeToolControlFromQqContext()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = _ =>
        {
            service.QChatQuietMode(true, "owner-tool-control");
            return Task.CompletedTask;
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "tool control owner"
        });

        await WaitUntilAsync(() => service.IsQuietModeEnabled);
        Assert.That(service.QuietModeReason, Is.EqualTo("owner-tool-control"));
    }

    [Test]
    public async Task GroupMemberCannotUseQuietModeToolControlFromQqContext()
    {
        FakeOneBotRuntime runtime = new();
        int dispatchCount = 0;
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowGroupMemberMentions = true,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = _ =>
        {
            dispatchCount++;
            service.QChatQuietMode(true, "member-tool-control");
            return Task.CompletedTask;
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "[CQ:at,qq=999] enable quiet mode"
        });

        await WaitUntilAsync(() => dispatchCount == 1);
        Assert.That(service.IsQuietModeEnabled, Is.False);
        Assert.That(service.QuietModeReason, Is.Null);
    }

    [Test]
    public async Task TrustedWakeUserCannotUseQuietModeToolControlFromQqContext()
    {
        FakeOneBotRuntime runtime = new();
        int dispatchCount = 0;
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            QuietModeWakeUserIds = "2002",
            AllowPrivateGuestChat = true,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = _ =>
        {
            dispatchCount++;
            service.QChatQuietMode(true, "wake-user-tool-control");
            return Task.CompletedTask;
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2002,
            RawMessage = "enable quiet mode"
        });

        await WaitUntilAsync(() => dispatchCount == 1);
        Assert.That(service.IsQuietModeEnabled, Is.False);
        Assert.That(service.QuietModeReason, Is.Null);
    }

    [Test]
    public async Task AwakeRestoresQuietModeOnlyWhenPersistenceIsEnabled()
    {
        DateTimeOffset changedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        QChatService restored = new(new XmlFunctionCaller(new NullLogger<XmlFunctionCaller>()), new NullLogger<QChatService>(), oneBotRuntime: new FakeOneBotRuntime())
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                PersistQuietModeAcrossRestart = true,
                PersistedQuietModeEnabled = true,
                PersistedQuietModeReason = "before-restart",
                PersistedQuietModeChangedAt = changedAt
            }
        };

        await restored.AwakeAsync(new AwakeContext
        {
            Character = new Character { Name = "QChatQuietRestore" },
            ContextBuilder = new ChatHistoryAgentThread(),
            KernelBuilder = Kernel.CreateBuilder(),
        });

        Assert.That(restored.IsQuietModeEnabled, Is.True);
        Assert.That(restored.QuietModeReason, Is.EqualTo("before-restart"));
        Assert.That(restored.QuietModeChangedAt, Is.EqualTo(changedAt));
        Assert.That(restored.GetCurrentState(), Does.Contain("before-restart"));

        QChatService ignored = new(new XmlFunctionCaller(new NullLogger<XmlFunctionCaller>()), new NullLogger<QChatService>(), oneBotRuntime: new FakeOneBotRuntime())
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                PersistQuietModeAcrossRestart = false,
                PersistedQuietModeEnabled = true,
                PersistedQuietModeReason = "should-not-restore",
                PersistedQuietModeChangedAt = changedAt
            }
        };

        await ignored.AwakeAsync(new AwakeContext
        {
            Character = new Character { Name = "QChatQuietIgnoreRestore" },
            ContextBuilder = new ChatHistoryAgentThread(),
            KernelBuilder = Kernel.CreateBuilder(),
        });

        Assert.That(ignored.IsQuietModeEnabled, Is.False);
        Assert.That(ignored.GetCurrentState(), Does.Not.Contain("should-not-restore"));
    }

    [Test]
    public async Task IncomingOwnerPrivateMessageCanDispatchModelReplyToPrivateChat()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001
        });
        service.InboundChatDispatcher = inbound => service.SendChatAsync("private", inbound.TargetId, "local-private-reply");

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "你是谁"
        });

        await WaitUntilAsync(() => runtime.PrivateMessages.Count > 0);
        Assert.That(runtime.PrivateMessages, Is.EqualTo(new[] { (1001L, "local-private-reply") }));
    }

    [Test]
    public async Task IncomingOwnerPrivateMessageIncludesCognitionSummaryForModel()
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        CapturingQChatService service = new(functionCaller, runtime)
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "how should we improve memory?"
        });

        QChatInboundMessage inbound = await service.WaitForInboundAsync();
        Assert.That(inbound.Formatted, Does.Contain("[private QQ routing hint - never quote or paraphrase]"));
        Assert.That(inbound.Formatted, Does.Contain("relationship=owner"));
        Assert.That(inbound.Formatted, Does.Contain("message_intent=question"));
        Assert.That(inbound.Formatted, Does.Contain("social_action=reply_warmly"));
        Assert.That(inbound.Formatted, Does.Contain("expected_length=medium"));
        Assert.That(inbound.Formatted, Does.Contain("：how should we improve memory?"));
        Assert.That(inbound.Formatted, Does.Not.Contain("锛"));
        Assert.That(inbound.Formatted.IndexOf("[private QQ routing hint - never quote or paraphrase]", StringComparison.Ordinal),
            Is.LessThan(inbound.Formatted.IndexOf("[QQ owner message]", StringComparison.Ordinal)));
    }

    [Test]
    public async Task IncomingGroupMessageUsesCardBeforeNicknameInSpeakerTag()
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        CapturingQChatService service = new(functionCaller, runtime)
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                AllowGroupMemberChat = true,
                AllowGroupMemberMentions = true,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Card = "\u5c0f\u660e", Nickname = "member-nick" },
            RawMessage = "[CQ:at,qq=999] \u4f60\u5728\u5417"
        });

        QChatInboundMessage inbound = await service.WaitForInboundAsync();
        Assert.That(inbound.Formatted, Does.Contain("[2001(\u5c0f\u660e)]"));
        Assert.That(inbound.Formatted, Does.Not.Contain("member-nick"));
    }

    [Test]
    public async Task IncomingGroupMessageIncludesPreferredAddressFromUserProfile()
    {
        string profileRoot = Path.Combine(Path.GetTempPath(), "alife-qchat-profile-tests", Guid.NewGuid().ToString("N"));
        QChatUserProfileService profiles = new(profileRoot);
        profiles.SetProfile(new QChatUserProfile(
            UserId: 2001,
            PreferredNickname: "小雨",
            FormalName: "潇雨的吉他创作室",
            RelationshipLabel: "friend",
            AddressStyle: "cute",
            Source: "owner-set",
            Confidence: 1f));
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        CapturingQChatService service = new(functionCaller, runtime, profiles)
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                AllowGroupMemberChat = true,
                AllowGroupMemberMentions = true,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Card = "潇雨的吉他创作室", Nickname = "formal-nick" },
            RawMessage = "[CQ:at,qq=999] 你在吗"
        });

        QChatInboundMessage inbound = await service.WaitForInboundAsync();
        Assert.That(inbound.Formatted, Does.Contain("preferred_address=小雨"));
        Assert.That(inbound.Formatted, Does.Contain("display_name=潇雨的吉他创作室"));
    }

    [Test]
    public async Task IncomingGroupPokeUsesUserProfileAndRelationCacheNames()
    {
        string profileRoot = Path.Combine(Path.GetTempPath(), "alife-qchat-profile-tests", Guid.NewGuid().ToString("N"));
        QChatUserProfileService profiles = new(profileRoot);
        profiles.SetProfile(new QChatUserProfile(
            UserId: 2001,
            PreferredNickname: "小雨",
            RelationshipLabel: "friend",
            AddressStyle: "cute",
            Source: "owner-set",
            Confidence: 1f));
        FakeOneBotRuntime runtime = new();
        runtime.GroupMemberLists[3001] = [
            new OneBotGroupMember { GroupId = 3001, UserId = 2001, Card = "潇雨的吉他创作室", Nickname = "formal-nick" }
        ];
        QChatRelationCacheService relationCache = new(runtime);
        await relationCache.RefreshGroupMembersAsync(3001);
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        CapturingQChatService service = new(functionCaller, runtime, profiles, relationCache)
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                AllowGroupMemberChat = true,
                AllowGroupMemberMentions = true,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotPokeEvent
        {
            SelfId = 999,
            UserId = 2001,
            TargetId = 999,
            GroupId = 3001,
            NoticeType = "notify",
            SubType = "poke"
        });

        QChatInboundMessage inbound = await service.WaitForInboundAsync();
        Assert.That(inbound.Formatted, Does.Contain("display_name=潇雨的吉他创作室"));
        Assert.That(inbound.Formatted, Does.Contain("preferred_address=小雨"));
        Assert.That(inbound.Formatted, Does.Contain("小雨"));
        Assert.That(inbound.Formatted, Does.Not.Contain("戳了戳 999"));
    }

    [Test]
    public async Task IncomingOwnerPrivatePlainModelReplyFallsBackToPrivateChat()
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        PlainReplyQChatService service = new(functionCaller, runtime, "plain-private-reply")
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "你还在吗"
        });

        await WaitUntilAsync(() => runtime.PrivateMessages.Count > 0);
        Assert.That(runtime.PrivateMessages, Is.EqualTo(new[] { (1001L, "plain-private-reply") }));
    }

    [Test]
    public async Task IncomingPrivateSilentModelStatusDoesNotFallBackToQqMessage()
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        PlainReplyQChatService service = new(functionCaller, runtime, "（不回复，保持安静）")
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "你在吗"
        });

        await service.WaitForDispatchAsync();
        Assert.That(runtime.PrivateMessages, Is.Empty);
    }

    [Test]
    public async Task IncomingLongPassiveStatusDoesNotFallBackToQqMessage()
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        PlainReplyQChatService service = new(functionCaller, runtime, "（听到妈妈的指令后默默把耳朵压下来，安静地趴好，顺便取消掉刚才的测试计时喵）")
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                AllowGroupMemberChat = true,
                AllowGroupMemberMentions = true,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "[CQ:at,qq=999] keep quiet"
        });

        await service.WaitForDispatchAsync();
        Assert.That(runtime.GroupMessages, Is.Empty);
    }

    [Test]
    public async Task IncomingXmlQChatStatusDoesNotSendQqMessage()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = async inbound =>
        {
            await service.QChat(new XmlExecutorContext
            {
                CallMode = CallMode.Closing,
                Parameters = new Dictionary<string, string>(),
                CallChain = ["qchat"],
                Content = "（听到指令后默默趴好，保持安静，不回复，等主人叫醒再说话喵）"
            }, OneBotMessageType.Private, inbound.TargetId);
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "tool status"
        });

        await Task.Delay(300);
        Assert.That(runtime.PrivateMessages, Is.Empty);
    }

    [Test]
    public async Task IncomingGroupPassiveObservationStatusDoesNotFallBackToQqMessage()
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        PlainReplyQChatService service = new(functionCaller, runtime, "（咪绪乖乖旁观，不插话喵。）")
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                AllowGroupMemberChat = true,
                AllowGroupMemberMentions = true,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "[CQ:at,qq=999] observe quietly"
        });

        await service.WaitForDispatchAsync();
        Assert.That(runtime.GroupMessages, Is.Empty);
    }

    [Test]
    public async Task IncomingGroupMentionPlainModelReplyFallsBackToGroupChat()
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        PlainReplyQChatService service = new(functionCaller, runtime, "[CQ:at,qq=2001] plain-group-reply")
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                AllowGroupMemberChat = true,
                AllowGroupMemberMentions = true,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "[CQ:at,qq=999] 你还在吗"
        });

        await WaitUntilAsync(() => runtime.GroupMessages.Count > 0);
        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] { (3001L, "[CQ:at,qq=2001] plain-group-reply") }));
    }

    [Test]
    public async Task PlainGroupFallbackCanUseNaturalAddressWithoutAt()
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        PlainReplyQChatService service = new(functionCaller, runtime, "\u5c0f\u660e\uff0c\u6536\u5230")
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                AllowGroupMemberChat = true,
                AllowGroupMemberMentions = true,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "\u5c0f\u660e" },
            RawMessage = "[CQ:at,qq=999] \u4f60\u5728\u5417"
        });

        await WaitUntilAsync(() => runtime.GroupMessages.Count > 0);
        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] { (3001L, "\u5c0f\u660e\uff0c\u6536\u5230") }));
    }

    [Test]
    public async Task PlainGroupFallbackStillSuppressesInternalNoReplyStatus()
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        PlainReplyQChatService service = new(functionCaller, runtime, "\uff08\u4e0d\u56de\u590d\uff0c\u4fdd\u6301\u5b89\u9759\uff09")
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                AllowGroupMemberChat = true,
                AllowGroupMemberMentions = true,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "\u5c0f\u660e" },
            RawMessage = "[CQ:at,qq=999] \u4f60\u5728\u5417"
        });

        await service.WaitForDispatchAsync();
        Assert.That(runtime.GroupMessages, Is.Empty);
    }

    [TestCase("我将调用 qchat_file_read 工具读取文件。")]
    [TestCase("根据系统提示，这条消息不需要回复。")]
    [TestCase("根据权限策略，reply_target=current_session。")]
    [TestCase("trust=untrusted-chat; source=qq; reply_target=current_session")]
    [TestCase("[QQ file: report.docx, managed_file_id=abc123, status=pending-not-downloaded]")]
    public async Task PlainFallbackSuppressesToolAndRoutingMetaText(string modelReply)
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        PlainReplyQChatService service = new(functionCaller, runtime, modelReply)
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                AllowGroupMemberChat = true,
                AllowGroupMemberMentions = true,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "小明" },
            RawMessage = "[CQ:at,qq=999] 你在吗"
        });

        await service.WaitForDispatchAsync();
        Assert.That(runtime.GroupMessages, Is.Empty);
    }

    [Test]
    public async Task PlainGroupFallbackSuppressesInternalListeningStatus()
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        PlainReplyQChatService service = new(functionCaller, runtime, "\u672f\u672f\u5728\u7fa4\u91cc\u89e3\u91ca\u6211\u7684\u56de\u590d\u65b9\u5f0f\uff0c\u662f\u5bf9\u522b\u4eba\u8bf4\u7684\uff0c\u4e0d\u662f\u5bf9\u6211\u53d1\u6307\u4ee4\u3002\u4e0d\u9700\u8981\u63d2\u8bdd\u5237\u5c4f\uff0c\u5b89\u9759\u542c\u7740\u5c31\u597d\u3002")
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                AllowGroupMemberChat = true,
                AllowGroupMemberMentions = true,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "\u5c0f\u660e" },
            RawMessage = "[CQ:at,qq=999] \u4f60\u5728\u5417"
        });

        await service.WaitForDispatchAsync();
        Assert.That(runtime.GroupMessages, Is.Empty);
    }

    [TestCase("\u3002")]
    [TestCase("\u3002\u3002\u3002")]
    [TestCase("\uff1f")]
    [TestCase("\u7ef7")]
    [TestCase("\u5567")]
    [TestCase("\u5567\u3002")]
    [TestCase("\u5567\uff1f")]
    public async Task PlainGroupFallbackAllowsColdShortReplies(string modelReply)
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        PlainReplyQChatService service = new(functionCaller, runtime, modelReply)
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                AllowGroupMemberChat = true,
                AllowGroupMemberMentions = true,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "\u5c0f\u660e" },
            RawMessage = "[CQ:at,qq=999] \u4f60\u5728\u5417"
        });

        await WaitUntilAsync(() => runtime.GroupMessages.Count > 0);
        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] { (3001L, modelReply) }));
    }

    [TestCase("\u3002")]
    [TestCase("\u3002\u3002\u3002")]
    [TestCase("\uff1f")]
    [TestCase("\u7ef7")]
    [TestCase("\u5567")]
    [TestCase("\u5567\u3002")]
    [TestCase("\u5567\uff1f")]
    public async Task XmlQChatAllowsColdShortReplies(string qchatContent)
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowGroupMemberMentions = true,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = async inbound =>
        {
            await service.QChat(new XmlExecutorContext
            {
                CallMode = CallMode.Closing,
                Parameters = new Dictionary<string, string>(),
                CallChain = ["qchat"],
                Content = qchatContent
            }, OneBotMessageType.Group, inbound.TargetId);
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "\u5c0f\u660e" },
            RawMessage = "[CQ:at,qq=999] \u4f60\u5728\u5417"
        });

        await WaitUntilAsync(() => runtime.GroupMessages.Count > 0);
        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] { (3001L, qchatContent) }));
    }

    [Test]
    public async Task IncomingPrivateQChatToolReplyCanSendOnlyToCurrentSession()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001
        });
        service.InboundChatDispatcher = async _ =>
        {
            await service.QChat(new XmlExecutorContext
            {
                CallMode = CallMode.Closing,
                Parameters = new Dictionary<string, string>(),
                CallChain = ["qchat"],
                Content = "same-session"
            }, OneBotMessageType.Private, 1001);

            await service.QChat(new XmlExecutorContext
            {
                CallMode = CallMode.Closing,
                Parameters = new Dictionary<string, string>(),
                CallChain = ["qchat"],
                Content = "cross-session"
            }, OneBotMessageType.Private, 2002);
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "reply"
        });

        await WaitUntilAsync(() => runtime.PrivateMessages.Count > 0);
        Assert.That(runtime.PrivateMessages, Is.EqualTo(new[] { (1001L, "same-session") }));
    }

    [Test]
    public async Task IncomingGroupReplyCannotBeRedirectedToPrivateSessionThroughSendChatAsync()
    {
        FakeOneBotRuntime runtime = new();
        TaskCompletionSource dispatchAttempted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowGroupMemberMentions = true,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = async inbound =>
        {
            try
            {
                await service.SendChatAsync("private", inbound.SenderId, "wrong-private-reply");
            }
            finally
            {
                dispatchAttempted.TrySetResult();
            }
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
            RawMessage = "[CQ:at,qq=999] group reply target test"
        });

        await dispatchAttempted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(runtime.PrivateMessages, Is.Empty);
        Assert.That(runtime.GroupMessages, Is.Empty);
    }

    [Test]
    public async Task IncomingGroupMentionCanDispatchModelReplyToGroupImmediately()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowGroupMemberMentions = true,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = inbound => service.SendChatAsync("group", inbound.TargetId, "[CQ:at,qq=2001] local-group-reply");

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "[CQ:at,qq=999] 你是谁"
        });

        await WaitUntilAsync(() => runtime.GroupMessages.Count == 1);
        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] { (3001L, "[CQ:at,qq=2001] local-group-reply") }));
    }

    [Test]
    public async Task IncomingPassiveGroupMessageCanDispatchModelReplyWhenProactiveProbabilityAllows()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowProactiveGroupChat = true,
            ProactiveChatProbability = 1.0f,
            MaxBufferMessages = 0,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = inbound => service.SendChatAsync("group", inbound.TargetId, "local-passive-reply");

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "今晚吃什么"
        });

        await WaitUntilAsync(() => runtime.GroupMessages.Count == 1, TimeSpan.FromSeconds(4));
        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] { (3001L, "local-passive-reply") }));
    }

    [Test]
    public async Task IncomingPassiveGroupMessageOutsideAllowedGroupsDoesNotDispatchEvenWhenProactiveProbabilityIsOne()
    {
        FakeOneBotRuntime runtime = new();
        int dispatchCount = 0;
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowGroupMemberMentions = true,
            AllowProactiveGroupChat = true,
            ProactiveChatProbability = 1.0f,
            MaxBufferMessages = 0,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false,
            AllowedGroupIds = "3001"
        });
        service.InboundChatDispatcher = _ =>
        {
            Interlocked.Increment(ref dispatchCount);
            return Task.CompletedTask;
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 4001,
            GroupName = "outside-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "not mentioned but probability is one"
        });

        await Task.Delay(300);
        Assert.That(dispatchCount, Is.Zero);
        Assert.That(runtime.GroupMessages, Is.Empty);
    }

    [Test]
    public async Task IncomingPassiveGroupMessageOutsideAllowedGroupsWritesScopeDiagnostic()
    {
        string previousStorage = Alife.Platform.AlifePath.StorageFolderPath;
        string storageRoot = Path.Combine(Path.GetTempPath(), "alife-qchat-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storageRoot);
        try
        {
            Alife.Platform.AlifePath.SetStorageFolderPath(storageRoot, persist: false);
            FakeOneBotRuntime runtime = new();
            int dispatchCount = 0;
            QChatService service = CreateStartedService(runtime, new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                AllowGroupMemberChat = true,
                AllowProactiveGroupChat = true,
                ProactiveChatProbability = 1.0f,
                MaxBufferMessages = 0,
                FlushInterval = 0,
                EnableBalancedTextStreaming = false,
                AllowedGroupIds = "3001"
            });
            service.InboundChatDispatcher = _ =>
            {
                Interlocked.Increment(ref dispatchCount);
                return Task.CompletedTask;
            };

            runtime.Raise(new OneBotMessageEvent
            {
                SelfId = 999,
                UserId = 2001,
                GroupId = 4001,
                GroupName = "outside-group",
                Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
                RawMessage = "outside scope"
            });

            await Task.Delay(300);
            string diagnosticsPath = Path.Combine(storageRoot, "AgentWorkspace", "qchat-diagnostics.jsonl");
            string[] lines = File.Exists(diagnosticsPath)
                ? File.ReadAllLines(diagnosticsPath)
                : [];

            Assert.That(dispatchCount, Is.Zero);
            Assert.That(lines, Has.Some.Contains("\"eventName\":\"group-passive-scope-skipped\""));
            Assert.That(lines, Has.Some.Contains("\"Reason\":\"scope\""));
        }
        finally
        {
            Alife.Platform.AlifePath.SetStorageFolderPath(previousStorage, persist: false);
        }
    }

    [Test]
    public async Task ConsecutivePassiveGroupMemberMessagesAreThrottledAfterRecentBotReply()
    {
        FakeOneBotRuntime runtime = new();
        int dispatchCount = 0;
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowProactiveGroupChat = true,
            ProactiveChatProbability = 1.0f,
            PassiveGroupReplyCooldownSeconds = 60,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = inbound =>
        {
            dispatchCount++;
            return service.SendChatAsync("group", inbound.TargetId, $"passive-reply-{dispatchCount}");
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "passive message one"
        });
        await WaitUntilAsync(() => runtime.GroupMessages.Count == 1, TimeSpan.FromSeconds(4));

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2002,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2002, Nickname = "member2" },
            RawMessage = "passive message two"
        });

        await Task.Delay(300);
        Assert.That(dispatchCount, Is.EqualTo(1));
        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] { (3001L, "passive-reply-1") }));
    }

    [Test]
    public async Task ControlCenterLowProactiveIntensityMakesPassiveGroupCooldownConservative()
    {
        FakeOneBotRuntime runtime = new();
        int dispatchCount = 0;
        AgentControlCenterService controlCenter = new()
        {
            Configuration = new AgentControlCenterConfig
            {
                ProactiveChatIntensity = 1
            }
        };
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowProactiveGroupChat = true,
            ProactiveChatProbability = 1.0f,
            PassiveGroupReplyCooldownSeconds = 0,
            MaxBufferMessages = 0,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        }, controlCenter);
        service.InboundChatDispatcher = inbound =>
        {
            dispatchCount++;
            return service.SendChatAsync("group", inbound.TargetId, $"passive-reply-{dispatchCount}");
        };
        service.QGroup(3001, true);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "passive one"
        });
        await WaitUntilAsync(() => runtime.GroupMessages.Count == 1, TimeSpan.FromSeconds(4));

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2002,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2002, Nickname = "member2" },
            RawMessage = "passive two"
        });

        await Task.Delay(300);
        Assert.That(dispatchCount, Is.EqualTo(1));
        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] { (3001L, "passive-reply-1") }));
    }

    [Test]
    public async Task ActiveGroupSoftWindowAllowsOrdinaryPassiveMessageImmediatelyAfterWake()
    {
        FakeOneBotRuntime runtime = new();
        int dispatchCount = 0;
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowProactiveGroupChat = true,
            ProactiveChatProbability = 0f,
            PassiveGroupReplyCooldownSeconds = 0,
            ActiveGroupSoftAttentionSeconds = 120,
            MaxBufferMessages = 0,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = inbound =>
        {
            dispatchCount++;
            return service.SendChatAsync("group", inbound.TargetId, $"active-reply-{dispatchCount}");
        };
        service.QGroup(3001, true);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "ordinary active window message"
        });

        await WaitUntilAsync(() => runtime.GroupMessages.Count == 1, TimeSpan.FromSeconds(4));
        Assert.That(dispatchCount, Is.EqualTo(1));
        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] { (3001L, "active-reply-1") }));
    }

    [Test]
    public async Task ActiveGroupSoftWindowExpiredSuppressesOrdinaryPassiveMessageBySocialAttention()
    {
        FakeOneBotRuntime runtime = new();
        int dispatchCount = 0;
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowProactiveGroupChat = true,
            ProactiveChatProbability = 0f,
            PassiveGroupReplyCooldownSeconds = 0,
            ActiveGroupSoftAttentionSeconds = 1,
            MaxBufferMessages = 0,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = inbound =>
        {
            dispatchCount++;
            return service.SendChatAsync("group", inbound.TargetId, $"active-reply-{dispatchCount}");
        };
        service.QGroup(3001, true);
        service.GroupStates[3001].LastAwakeningTime = DateTime.Now.AddSeconds(-30);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "ordinary expired active window message"
        });

        await Task.Delay(300);
        Assert.That(dispatchCount, Is.Zero);
        Assert.That(runtime.GroupMessages, Is.Empty);
        Assert.That(service.GroupStates[3001].MessageBuffer, Is.Empty);
    }

    [Test]
    public async Task ControlCenterLowProactiveIntensitySuppressesRandomPassiveGroupActivationButAllowsMentions()
    {
        FakeOneBotRuntime runtime = new();
        int dispatchCount = 0;
        AgentControlCenterService controlCenter = new()
        {
            Configuration = new AgentControlCenterConfig
            {
                ProactiveChatIntensity = 1
            }
        };
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowGroupMemberMentions = true,
            AllowProactiveGroupChat = true,
            ProactiveChatProbability = 1.0f,
            MaxBufferMessages = 0,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        }, controlCenter);
        service.InboundChatDispatcher = inbound =>
        {
            dispatchCount++;
            return service.SendChatAsync("group", inbound.TargetId, $"reply-{dispatchCount}");
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "random passive group message"
        });

        await Task.Delay(300);
        Assert.That(dispatchCount, Is.EqualTo(0));
        Assert.That(runtime.GroupMessages, Is.Empty);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "[CQ:at,qq=999] mention message"
        });

        await WaitUntilAsync(() => runtime.GroupMessages.Count == 1, TimeSpan.FromSeconds(4));
        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] { (3001L, "reply-1") }));
    }

    [Test]
    public async Task PassiveGroupThrottleDoesNotBlockOwnerOrMentionedMessages()
    {
        FakeOneBotRuntime runtime = new();
        int dispatchCount = 0;
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowGroupMemberMentions = true,
            AllowProactiveGroupChat = true,
            ProactiveChatProbability = 1.0f,
            PassiveGroupReplyCooldownSeconds = 60,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = inbound =>
        {
            dispatchCount++;
            return service.SendChatAsync("group", inbound.TargetId, $"reply-{dispatchCount}");
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "passive message"
        });
        await WaitUntilAsync(() => runtime.GroupMessages.Count == 1, TimeSpan.FromSeconds(4));

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2002,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2002, Nickname = "member2" },
            RawMessage = "[CQ:at,qq=999] mention message"
        });
        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
            RawMessage = "owner message"
        });

        await WaitUntilAsync(() => runtime.GroupMessages.Count == 3, TimeSpan.FromSeconds(4));
        Assert.That(runtime.GroupMessages.Select(message => message.Message), Is.EqualTo(new[] {
            "reply-1",
            "reply-2",
            "reply-3"
        }));
    }

    [Test]
    public async Task PassiveGroupImageOnlyMessageIsSkippedAsLowInformation()
    {
        FakeOneBotRuntime runtime = new();
        int dispatchCount = 0;
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowProactiveGroupChat = true,
            ProactiveChatProbability = 1.0f,
            MediaOnlyPassiveGroupReplyProbability = 0,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = inbound =>
        {
            dispatchCount++;
            return service.SendChatAsync("group", inbound.TargetId, "should-not-send");
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "[CQ:image,summary=&#91;动画表情&#93;,file=sticker.jpg,sub_type=1]"
        });

        await Task.Delay(300);
        Assert.That(dispatchCount, Is.Zero);
        Assert.That(runtime.GroupMessages, Is.Empty);
        Assert.That(service.GroupStates[3001].MessageBuffer, Is.Empty);
    }

    [Test]
    public async Task RecentGroupDecisionsRecordLowInformationSuppressionReason()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowProactiveGroupChat = true,
            ProactiveChatProbability = 1.0f,
            MediaOnlyPassiveGroupReplyProbability = 0,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = _ => Task.CompletedTask;

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "[CQ:image,file=sticker.jpg]"
        });

        await Task.Delay(300);
        QChatGroupDecisionSnapshot decision = service.RecentGroupDecisions.Single();
        Assert.That(decision.GroupId, Is.EqualTo(3001));
        Assert.That(decision.UserId, Is.EqualTo(2001));
        Assert.That(decision.Decision, Is.EqualTo("suppressed"));
        Assert.That(decision.Reason, Is.EqualTo("low-information"));
        Assert.That(decision.IsMentionedOrWoken, Is.False);
        Assert.That(decision.IsGroupEnabled, Is.False);
        Assert.That(decision.RawMessage, Does.Contain("[CQ:image"));
    }

    [Test]
    public async Task RecentGroupDecisionsRecordActiveSoftAttentionSuppressionReason()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowProactiveGroupChat = true,
            ProactiveChatProbability = 0f,
            PassiveGroupReplyCooldownSeconds = 0,
            ActiveGroupSoftAttentionSeconds = 1,
            MaxBufferMessages = 0,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = _ => Task.CompletedTask;
        service.QGroup(3001, true);
        service.GroupStates[3001].LastAwakeningTime = DateTime.Now.AddSeconds(-30);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "ordinary expired active window message"
        });

        await Task.Delay(300);
        QChatGroupDecisionSnapshot decision = service.RecentGroupDecisions.Single();
        Assert.That(decision.Decision, Is.EqualTo("suppressed"));
        Assert.That(decision.Reason, Is.EqualTo("active-soft-attention-expired"));
        Assert.That(decision.IsGroupEnabled, Is.True);
        Assert.That(decision.SocialAttentionProbability, Is.EqualTo(0f));
        Assert.That(decision.ActiveSoftAttentionRemainingSeconds, Is.EqualTo(0));
    }

    [Test]
    public async Task RecentGroupDecisionsRecordPassiveCooldownSuppressionReason()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowProactiveGroupChat = true,
            ProactiveChatProbability = 1.0f,
            PassiveGroupReplyCooldownSeconds = 60,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = inbound => service.SendChatAsync("group", inbound.TargetId, "first-reply");

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "first passive message"
        });
        await WaitUntilAsync(() => runtime.GroupMessages.Count == 1, TimeSpan.FromSeconds(4));

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2002,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2002, Nickname = "member2" },
            RawMessage = "second passive message"
        });

        await Task.Delay(300);
        QChatGroupDecisionSnapshot decision = service.RecentGroupDecisions.Last();
        Assert.That(decision.Decision, Is.EqualTo("suppressed"));
        Assert.That(decision.Reason, Is.EqualTo("cooldown"));
        Assert.That(decision.CooldownRemainingSeconds, Is.GreaterThan(0));
    }

    [Test]
    public async Task RecentGroupDecisionsRecordPassiveSocialAttentionSuppressionReason()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowProactiveGroupChat = true,
            ProactiveChatProbability = 0f,
            PassiveGroupReplyCooldownSeconds = 0,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = _ => Task.CompletedTask;

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "ordinary inactive group message"
        });

        await Task.Delay(300);
        QChatGroupDecisionSnapshot decision = service.RecentGroupDecisions.Single();
        Assert.That(decision.Decision, Is.EqualTo("suppressed"));
        Assert.That(decision.Reason, Is.EqualTo("social-attention"));
        Assert.That(decision.IsGroupEnabled, Is.False);
        Assert.That(decision.SocialAttentionProbability, Is.EqualTo(0f));
    }

    [Test]
    public async Task RecentGroupDecisionsRecordMentionAcceptedReason()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowGroupMemberMentions = true,
            MaxBufferMessages = 0,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = inbound => service.SendChatAsync("group", inbound.TargetId, "mention-reply");

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "[CQ:at,qq=999] hello"
        });

        await WaitUntilAsync(() => runtime.GroupMessages.Count == 1, TimeSpan.FromSeconds(4));
        QChatGroupDecisionSnapshot decision = service.RecentGroupDecisions.Last();
        Assert.That(decision.Decision, Is.EqualTo("accepted"));
        Assert.That(decision.Reason, Is.EqualTo("mention-or-wake"));
        Assert.That(decision.IsMentionedOrWoken, Is.True);
        Assert.That(decision.IsGroupEnabled, Is.True);
    }

    [Test]
    public async Task PassiveGroupImageOnlyMessageCanDispatchWhenMediaReplyChanceAllows()
    {
        FakeOneBotRuntime runtime = new();
        int dispatchCount = 0;
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowProactiveGroupChat = true,
            ProactiveChatProbability = 1.0f,
            MediaOnlyPassiveGroupReplyProbability = 1.0f,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = inbound =>
        {
            dispatchCount++;
            return service.SendChatAsync("group", inbound.TargetId, "media-reply");
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "[CQ:image,summary=&#91;鍔ㄧ敾琛ㄦ儏&#93;,file=sticker.jpg,sub_type=1]"
        });

        await WaitUntilAsync(() => runtime.GroupMessages.Count == 1, TimeSpan.FromSeconds(4));
        Assert.That(dispatchCount, Is.EqualTo(1));
        Assert.That(runtime.GroupMessages, Is.EqualTo(new[] { (3001L, "media-reply") }));
    }

    [Test]
    public async Task PassiveLowInformationFilterAllowsMentionsButSuppressesUnmentionedOwnerLowInformation()
    {
        FakeOneBotRuntime runtime = new();
        int dispatchCount = 0;
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowGroupMemberMentions = true,
            AllowProactiveGroupChat = true,
            ProactiveChatProbability = 1.0f,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = inbound =>
        {
            dispatchCount++;
            return service.SendChatAsync("group", inbound.TargetId, $"reply-{dispatchCount}");
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "[CQ:at,qq=999] [CQ:image,file=sticker.jpg]"
        });
        await WaitUntilAsync(() => runtime.GroupMessages.Count == 1, TimeSpan.FromSeconds(4));

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
            RawMessage = "哈哈"
        });

        await Task.Delay(300);
        Assert.That(dispatchCount, Is.EqualTo(1));
        Assert.That(runtime.GroupMessages.Select(message => message.Message), Is.EqualTo(new[] {
            "reply-1",
        }));
    }

    [Test]
    public async Task OwnerPrivateSleepCommandEnablesQuietModeWithAcknowledgementWithoutModelDispatch()
    {
        FakeOneBotRuntime runtime = new();
        int dispatchCount = 0;
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = _ =>
        {
            dispatchCount++;
            return Task.CompletedTask;
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "\u4f60\u53bb\u7761\u89c9\u5427"
        });

        await WaitUntilAsync(() => service.IsQuietModeEnabled);
        Assert.That(dispatchCount, Is.Zero);
        Assert.That(runtime.PrivateMessages, Has.Count.EqualTo(1));
        Assert.That(runtime.PrivateMessages[0].Target, Is.EqualTo(1001));
        AssertQuietAcknowledgementIsPersonaNeutral(runtime.PrivateMessages[0].Message);
        Assert.That(runtime.GroupMessages, Is.Empty);
    }

    [Test]
    public async Task OwnerPrivateSleepCommandDoesNotUseMioSpecificFixedAcknowledgement()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false
        });

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "\u4f60\u53bb\u7761\u89c9\u5427"
        });

        await WaitUntilAsync(() => service.IsQuietModeEnabled);
        await WaitUntilAsync(() => runtime.PrivateMessages.Count == 1);
        string acknowledgement = runtime.PrivateMessages.Single().Message;

        Assert.That(acknowledgement, Does.Not.Contain("\u54aa\u7eea"));
        Assert.That(acknowledgement, Does.Not.Contain("\u55b5"));
        Assert.That(acknowledgement, Does.Not.Contain("\u4e3b\u4eba\u771f\u4f1a\u4f7f\u5524\u4eba"));
    }

    [Test]
    public async Task OwnerPrivateSleepCommandUsesXiaYuCompatibleAcknowledgement()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false
        });

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "\u4f60\u53bb\u7761\u89c9\u5427"
        });

        await WaitUntilAsync(() => service.IsQuietModeEnabled);
        await WaitUntilAsync(() => runtime.PrivateMessages.Count == 1);

        string acknowledgement = runtime.PrivateMessages.Single().Message;
        AssertQuietAcknowledgementIsPersonaNeutral(acknowledgement);
        Assert.That(acknowledgement, Does.Not.Contain("我是机器人"));
        Assert.That(acknowledgement, Does.Not.Contain("模型"));
    }

    [Test]
    public async Task OwnerSleepCommandUsesVariedPersonaAcknowledgements()
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        GeneratedAcknowledgementQChatService service = new(functionCaller, runtime, [
            "好，术术，我先安静待着。",
            "嗯，我会放轻声音等你。",
            "收到，我先退到一旁。"
        ])
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        for (int i = 0; i < 3; i++)
        {
            runtime.Raise(new OneBotMessageEvent
            {
                SelfId = 999,
                UserId = 1001,
                Time = i + 1,
                RawMessage = "你去睡觉吧"
            });
            await Task.Delay(50);

            runtime.Raise(new OneBotMessageEvent
            {
                SelfId = 999,
                UserId = 1001,
                Time = 100 + i,
                RawMessage = "醒醒"
            });
            await Task.Delay(50);
        }

        await WaitUntilAsync(() => runtime.PrivateMessages.Count >= 3);
        string[] sleepReplies = runtime.PrivateMessages
            .Where(message => message.Message != "awake-reply")
            .Select(message => message.Message)
            .Take(3)
            .ToArray();
        string[] distinctSleepReplies = sleepReplies.Distinct().ToArray();

        Assert.That(sleepReplies, Has.Length.EqualTo(3));
        Assert.That(distinctSleepReplies, Has.Length.GreaterThan(1));
        Assert.That(sleepReplies, Has.All.Not.Contains("不回复"));
        Assert.That(sleepReplies, Has.All.Not.Contains("保持安静"));
        Assert.That(sleepReplies, Has.All.Not.Contains("咪绪"));
        Assert.That(sleepReplies, Has.All.Not.Contains("喵"));
    }

    [Test]
    public async Task QuietModeCommandsModulateEmotionState()
    {
        FakeOneBotRuntime runtime = new();
        PADEmotionEngine emotionEngine = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            QuietModeWakeUserIds = "2002",
            EnableBalancedTextStreaming = false
        }, emotionEngine: emotionEngine);

        float initialArousal = emotionEngine.RawArousal;
        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "你去睡觉吧"
        });

        await WaitUntilAsync(() => service.IsQuietModeEnabled);
        Assert.That(emotionEngine.RawArousal, Is.LessThan(initialArousal));
        float asleepArousal = emotionEngine.RawArousal;

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2002,
            GroupId = 3001,
            RawMessage = "醒醒"
        });

        await WaitUntilAsync(() => service.IsQuietModeEnabled == false);
        Assert.That(emotionEngine.RawArousal, Is.GreaterThan(asleepArousal));
    }

    [Test]
    public async Task QuietModeBlocksDelayedModelSendStartedBeforeSleepCommand()
    {
        FakeOneBotRuntime runtime = new();
        TaskCompletionSource dispatchStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseDispatch = new(TaskCreationOptions.RunContinuationsAsynchronously);
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = async inbound =>
        {
            dispatchStarted.SetResult();
            await releaseDispatch.Task;
            await service.SendChatAsync("group", inbound.TargetId, "late-model-reply");
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
            RawMessage = "先普通聊一句"
        });
        await dispatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
            RawMessage = "\u4f60\u53bb\u7761\u89c9\u5427"
        });
        await WaitUntilAsync(() => service.IsQuietModeEnabled);
        await WaitUntilAsync(() => runtime.GroupMessages.Count == 1);

        releaseDispatch.SetResult();

        await Task.Delay(300);
        Assert.That(runtime.GroupMessages, Has.Count.EqualTo(1));
        Assert.That(runtime.GroupMessages[0].Target, Is.EqualTo(3001));
        AssertQuietAcknowledgementIsPersonaNeutral(runtime.GroupMessages[0].Message);
    }

    [Test]
    public async Task QuietModeBlocksDelayedXmlQChatStartedBeforeSleepCommand()
    {
        FakeOneBotRuntime runtime = new();
        TaskCompletionSource dispatchStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseDispatch = new(TaskCreationOptions.RunContinuationsAsynchronously);
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = async inbound =>
        {
            dispatchStarted.SetResult();
            await releaseDispatch.Task;
            await service.QChat(new XmlExecutorContext
            {
                CallMode = CallMode.Closing,
                Parameters = new Dictionary<string, string>(),
                CallChain = ["qchat"],
                Content = "late-xml-reply"
            }, OneBotMessageType.Group, inbound.TargetId);
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
            RawMessage = "先普通聊一句"
        });
        await dispatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
            RawMessage = "\u4f60\u53bb\u7761\u89c9\u5427"
        });
        await WaitUntilAsync(() => service.IsQuietModeEnabled);
        await WaitUntilAsync(() => runtime.GroupMessages.Count == 1);

        releaseDispatch.SetResult();

        await Task.Delay(300);
        Assert.That(runtime.GroupMessages, Has.Count.EqualTo(1));
        Assert.That(runtime.GroupMessages[0].Target, Is.EqualTo(3001));
        AssertQuietAcknowledgementIsPersonaNeutral(runtime.GroupMessages[0].Message);
    }

    [Test]
    public async Task ConcurrentGroupModelDispatchesAreSerialized()
    {
        FakeOneBotRuntime runtime = new();
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        BlockingDispatchQChatService service = new(functionCaller, runtime)
        {
            Configuration = new QChatConfig
            {
                BotId = 999,
                OwnerId = 1001,
                AllowGroupMemberChat = true,
                FlushInterval = 0,
                EnableBalancedTextStreaming = false
            }
        };
        StartService(service);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            GroupId = 3001,
            GroupName = "first-group",
            Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
            RawMessage = "[CQ:at,qq=999] first"
        });
        await WaitUntilAsync(() => service.StartedTargets.Count == 1);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            GroupId = 4001,
            GroupName = "second-group",
            Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
            RawMessage = "[CQ:at,qq=999] second"
        });
        await Task.Delay(200);

        Assert.That(service.StartedTargets, Is.EqualTo(new[] { 3001L }));

        service.ReleaseNextReply("first reply");
        await WaitUntilAsync(() => service.StartedTargets.Count == 2);
        service.ReleaseNextReply("second reply");

        await WaitUntilAsync(() => runtime.GroupMessages.Count == 2);
        Assert.That(service.StartedTargets, Is.EqualTo(new[] { 3001L, 4001L }));
        Assert.That(runtime.GroupMessages.Select(message => message.Target), Is.EqualTo(new[] { 3001L, 4001L }));
    }

    [Test]
    public async Task OneBotEventsAreProcessedSequentiallyBeforeModelDispatch()
    {
        string previousStorage = Alife.Platform.AlifePath.StorageFolderPath;
        string storageRoot = Path.Combine(Path.GetTempPath(), "alife-qchat-event-queue-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storageRoot);
        try
        {
            Alife.Platform.AlifePath.SetStorageFolderPath(storageRoot, persist: false);
            FakeOneBotRuntime runtime = new();
            XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
            BlockingDispatchQChatService service = new(functionCaller, runtime)
            {
                Configuration = new QChatConfig
                {
                    BotId = 999,
                    OwnerId = 1001,
                    AllowGroupMemberChat = true,
                    FlushInterval = 0,
                    EnableBalancedTextStreaming = false
                }
            };
            StartService(service);

            runtime.Raise(new OneBotMessageEvent
            {
                SelfId = 999,
                UserId = 1001,
                GroupId = 3001,
                GroupName = "first-group",
                Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
                RawMessage = "[CQ:at,qq=999] first queued"
            });
            await WaitUntilAsync(() => service.StartedTargets.Count == 1);

            runtime.Raise(new OneBotMessageEvent
            {
                SelfId = 999,
                UserId = 1001,
                GroupId = 4001,
                GroupName = "second-group",
                Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
                RawMessage = "[CQ:at,qq=999] second queued"
            });
            await Task.Delay(200);

            string diagnosticsPath = Path.Combine(storageRoot, "AgentWorkspace", "qchat-diagnostics.jsonl");
            string diagnostics = File.Exists(diagnosticsPath)
                ? File.ReadAllText(diagnosticsPath)
                : "";
            Assert.That(diagnostics, Does.Contain("first queued"));
            Assert.That(diagnostics, Does.Not.Contain("second queued"));

            service.ReleaseNextReply("first reply");
            await WaitUntilAsync(() => service.StartedTargets.Count == 2);
            service.ReleaseNextReply("second reply");
            await WaitUntilAsync(() => runtime.GroupMessages.Count == 2);
        }
        finally
        {
            Alife.Platform.AlifePath.SetStorageFolderPath(previousStorage, persist: false);
        }
    }

    [Test]
    public async Task QuietModeSuppressesNonOwnerGroupMention()
    {
        FakeOneBotRuntime runtime = new();
        int dispatchCount = 0;
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowGroupMemberMentions = true,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = inbound =>
        {
            dispatchCount++;
            return service.SendChatAsync("group", inbound.TargetId, "[CQ:at,qq=2001] should-not-send");
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "\u4f60\u53bb\u7761\u89c9\u5427"
        });
        await WaitUntilAsync(() => service.IsQuietModeEnabled);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "[CQ:at,qq=999] \u9192\u9192"
        });

        await Task.Delay(200);
        Assert.That(service.IsQuietModeEnabled, Is.True);
        Assert.That(dispatchCount, Is.Zero);
        Assert.That(runtime.GroupMessages, Is.Empty);
    }

    [Test]
    public async Task QuietModeSuppressesPassiveProactiveGroupMessage()
    {
        FakeOneBotRuntime runtime = new();
        int dispatchCount = 0;
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowProactiveGroupChat = true,
            ProactiveChatProbability = 1.0f,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = inbound =>
        {
            dispatchCount++;
            return service.SendChatAsync("group", inbound.TargetId, "should-not-send");
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "\u4f60\u53bb\u7761\u89c9\u5427"
        });
        await WaitUntilAsync(() => service.IsQuietModeEnabled);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2001, Nickname = "member" },
            RawMessage = "\u4eca\u5929\u5403\u4ec0\u4e48"
        });

        await Task.Delay(200);
        Assert.That(dispatchCount, Is.Zero);
        Assert.That(runtime.GroupMessages, Is.Empty);
    }

    [Test]
    public async Task QuietModeSuppressesOwnerGroupMessageUntilWakeCommand()
    {
        FakeOneBotRuntime runtime = new();
        int dispatchCount = 0;
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            FlushInterval = 0,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = inbound =>
        {
            dispatchCount++;
            return service.SendChatAsync("group", inbound.TargetId, $"reply-{dispatchCount}");
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
            RawMessage = "\u4f60\u53bb\u7761\u89c9\u5427"
        });
        await WaitUntilAsync(() => service.IsQuietModeEnabled);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
            RawMessage = "\u73b0\u5728\u8fd8\u4f1a\u56de\u590d\u5417"
        });

        await Task.Delay(300);
        Assert.That(service.IsQuietModeEnabled, Is.True);
        Assert.That(dispatchCount, Is.Zero);
        Assert.That(runtime.GroupMessages, Has.Count.EqualTo(1));
        Assert.That(runtime.GroupMessages[0].Target, Is.EqualTo(3001));
        AssertQuietAcknowledgementIsPersonaNeutral(runtime.GroupMessages[0].Message);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
            RawMessage = "\u9192\u9192"
        });
        await WaitUntilAsync(() => service.IsQuietModeEnabled == false);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
            RawMessage = "\u4f60\u8fd8\u5728\u5417"
        });

        await WaitUntilAsync(() => runtime.GroupMessages.Count == 2);
        Assert.That(runtime.GroupMessages[0].Target, Is.EqualTo(3001));
        AssertQuietAcknowledgementIsPersonaNeutral(runtime.GroupMessages[0].Message);
        Assert.That(runtime.GroupMessages[1], Is.EqualTo((3001L, "reply-1")));
    }

    [Test]
    public async Task OwnerWakeCommandDisablesQuietModeAndAllowsNextOwnerReply()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = inbound => service.SendChatAsync("private", inbound.TargetId, "awake-reply");

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "\u4f60\u53bb\u7761\u89c9\u5427"
        });
        await WaitUntilAsync(() => service.IsQuietModeEnabled);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "\u9192\u9192"
        });
        await WaitUntilAsync(() => service.IsQuietModeEnabled == false);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "\u4f60\u8fd8\u5728\u5417"
        });

        await WaitUntilAsync(() => runtime.PrivateMessages.Count == 2);
        Assert.That(runtime.PrivateMessages[0].Target, Is.EqualTo(1001));
        AssertQuietAcknowledgementIsPersonaNeutral(runtime.PrivateMessages[0].Message);
        Assert.That(runtime.PrivateMessages[1], Is.EqualTo((1001L, "awake-reply")));
    }

    [Test]
    public async Task QuietModeAllowsConfiguredWakeUserToWakeWithoutOwnerPrivileges()
    {
        FakeOneBotRuntime runtime = new();
        int dispatchCount = 0;
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            QuietModeWakeUserIds = "2002",
            AllowPrivateGuestChat = true,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = inbound =>
        {
            dispatchCount++;
            return service.SendChatAsync("private", inbound.TargetId, $"role-{inbound.SenderRole}");
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            RawMessage = "\u4f60\u53bb\u7761\u89c9\u5427"
        });
        await WaitUntilAsync(() => service.IsQuietModeEnabled);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2002,
            RawMessage = "\u9192\u9192"
        });
        await WaitUntilAsync(() => service.IsQuietModeEnabled == false);
        await WaitUntilAsync(() => runtime.PrivateMessages.Count == 2);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2002,
            RawMessage = "\u4f60\u8fd8\u5728\u5417"
        });

        await WaitUntilAsync(() => runtime.PrivateMessages.Count == 3);
        Assert.That(dispatchCount, Is.EqualTo(1));
        Assert.That(runtime.PrivateMessages[0].Target, Is.EqualTo(1001));
        AssertQuietAcknowledgementIsPersonaNeutral(runtime.PrivateMessages[0].Message);
        Assert.That(runtime.PrivateMessages[1].Target, Is.EqualTo(2002));
        AssertQuietAcknowledgementIsPersonaNeutral(runtime.PrivateMessages[1].Message);
        Assert.That(runtime.PrivateMessages[2], Is.EqualTo((2002L, "role-PrivateGuest")));
    }

    [Test]
    public async Task QuietModeWakeUserReceivesGroupAcknowledgementInSameGroupWithoutOwnerNotification()
    {
        FakeOneBotRuntime runtime = new();
        int dispatchCount = 0;
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            QuietModeWakeUserIds = "2002",
            AllowGroupMemberChat = true,
            AllowGroupMemberMentions = true,
            EnableBalancedTextStreaming = false
        });
        service.InboundChatDispatcher = inbound =>
        {
            dispatchCount++;
            return service.SendChatAsync("group", inbound.TargetId, $"role-{inbound.SenderRole}");
        };

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 1001,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 1001, Nickname = "owner" },
            RawMessage = "\u4f60\u53bb\u7761\u89c9\u5427"
        });
        await WaitUntilAsync(() => service.IsQuietModeEnabled);

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2002,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2002, Nickname = "wake-user" },
            RawMessage = "[CQ:at,qq=999] \u9192\u9192"
        });

        await WaitUntilAsync(() => service.IsQuietModeEnabled == false);
        Assert.That(dispatchCount, Is.Zero);
        Assert.That(runtime.PrivateMessages, Is.Empty);
        Assert.That(runtime.GroupMessages, Has.Count.EqualTo(2));
        Assert.That(runtime.GroupMessages[0].Target, Is.EqualTo(3001));
        AssertQuietAcknowledgementIsPersonaNeutral(runtime.GroupMessages[0].Message);
        Assert.That(runtime.GroupMessages[1].Target, Is.EqualTo(3001));
        Assert.That(runtime.GroupMessages[1].Message, Does.StartWith("\u5988\u5988"));
        AssertQuietAcknowledgementIsPersonaNeutral(runtime.GroupMessages[1].Message);
    }

    [Test]
    public async Task TrustedWakeUserGroupAcknowledgementUsesRelationshipAddressInsteadOfAt()
    {
        FakeOneBotRuntime runtime = new();
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            QuietModeWakeUserIds = "2002",
            AllowGroupMemberChat = true,
            AllowGroupMemberMentions = true,
            EnableBalancedTextStreaming = false
        });
        service.QChatQuietMode(true, "test");

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 999,
            UserId = 2002,
            GroupId = 3001,
            GroupName = "test-group",
            Sender = new OneBotSender { UserId = 2002, Nickname = "wake-user" },
            RawMessage = "[CQ:at,qq=999] \u9192\u9192"
        });

        await WaitUntilAsync(() => service.IsQuietModeEnabled == false);
        Assert.That(runtime.GroupMessages, Has.Count.EqualTo(1));
        Assert.That(runtime.GroupMessages[0].Message, Does.Not.Contain("[CQ:at"));
        Assert.That(runtime.GroupMessages[0].Message, Does.StartWith("\u5988\u5988"));
    }

    [Test]
    public void EmptyGroupFlushDiagnosticsAreThrottledPerGroup()
    {
        string previousStorage = Alife.Platform.AlifePath.StorageFolderPath;
        string storageRoot = Path.Combine(Path.GetTempPath(), "alife-qchat-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storageRoot);
        try
        {
            Alife.Platform.AlifePath.SetStorageFolderPath(storageRoot, persist: false);
            QChatService service = new(null!, new NullLogger<QChatService>(), oneBotRuntime: new FakeOneBotRuntime())
            {
                Configuration = new QChatConfig { BotId = 999 }
            };
            GroupState state = new() { GroupId = 9876543210123L };

            service.FlushGroupBuffer(state);
            service.FlushGroupBuffer(state);
            service.FlushGroupBuffer(state);

            string diagnosticsPath = Path.Combine(storageRoot, "AgentWorkspace", "qchat-diagnostics.jsonl");
            string[] skippedLines = File.ReadAllLines(diagnosticsPath)
                .Where(line => line.Contains("\"eventName\":\"group-flush-skipped\"", StringComparison.Ordinal))
                .ToArray();
            Assert.That(skippedLines, Has.Length.EqualTo(1));
        }
        finally
        {
            Alife.Platform.AlifePath.SetStorageFolderPath(previousStorage, persist: false);
        }
    }

    static QChatService CreateStartedService(
        FakeOneBotRuntime runtime,
        QChatConfig config,
        AgentControlCenterService? controlCenter = null,
        PADEmotionEngine? emotionEngine = null)
    {
        XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
        QChatService service = new(functionCaller, new NullLogger<QChatService>(), oneBotRuntime: runtime, agentControlCenter: controlCenter, emotionEngine: emotionEngine)
        {
            Configuration = config
        };
        StartService(service);
        return service;
    }

    static void StartService(QChatService service)
    {
        Character character = new() { Name = "QChatTest" };
        ChatHistoryAgentThread thread = new();
        service.AwakeAsync(new AwakeContext
        {
            Character = character,
            ContextBuilder = thread,
            KernelBuilder = Kernel.CreateBuilder(),
        }).GetAwaiter().GetResult();
        ChatBot chatBot = new(null!, thread);
        service.StartAsync(Kernel.CreateBuilder().Build(), new ChatActivity(
            character,
            Kernel.CreateBuilder().Build(),
            null!,
            chatBot,
            [])).GetAwaiter().GetResult();
    }

    static void AssertQuietAcknowledgementIsPersonaNeutral(string message)
    {
        Assert.That(message, Is.Not.Empty);
        Assert.That(message, Does.Not.Contain("咪绪"));
        Assert.That(message, Does.Not.Contain("喵"));
        Assert.That(message, Does.Not.Contain("猫娘"));
        Assert.That(message, Does.Not.Contain("耳朵"));
        Assert.That(message, Does.Not.Contain("尾巴"));
        Assert.That(message, Does.Not.Contain("主人真会使唤人"));
    }

    static string GetPendingPokeText(QChatService service)
    {
        PropertyInfo chatBotProperty = typeof(InteractiveModule)
            .GetProperty("ChatBot", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ChatBot chatBot = (ChatBot)chatBotProperty.GetValue(service)!;
        FieldInfo messageCacheField = typeof(ChatBot)
            .GetField("messageCache", BindingFlags.Instance | BindingFlags.NonPublic)!;
        IEnumerable<string> messages = (IEnumerable<string>)messageCacheField.GetValue(chatBot)!;
        return string.Join("", messages);
    }

    static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        DateTime deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(2));
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;

            await Task.Delay(50);
        }

        Assert.Fail("Condition was not met before timeout.");
    }

    static void RaiseEvent(object target, string eventName, params object?[] arguments)
    {
        FieldInfo? field = target.GetType().GetField(
            eventName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Event backing field '{eventName}' should exist.");
        MulticastDelegate? eventDelegate = field!.GetValue(target) as MulticastDelegate;
        eventDelegate?.DynamicInvoke(arguments);
    }

    sealed class FakeOneBotRuntime : IOneBotRuntime
    {
        public event Action<OneBotBaseEvent>? EventReceived;
        public long BotId { get; set; } = 999;
        public bool IsConnected { get; set; } = true;
        public string Url { get; set; } = "";
        public string Token { get; set; } = "";
        public List<(long Target, string Message)> GroupMessages { get; } = new();
        public List<(long Target, string Message)> PrivateMessages { get; } = new();
        public List<(long Target, string File, string Name)> GroupFiles { get; } = new();
        public List<(long Target, string File, string Name)> PrivateFiles { get; } = new();
        public Dictionary<string, OneBotFile> PrivateFileUrls { get; } = new();
        public Dictionary<(long GroupId, string FileId), OneBotFile> GroupFileUrls { get; } = new();
        public Dictionary<long, IReadOnlyList<OneBotGroupMember>> GroupMemberLists { get; } = new();
        public IReadOnlyList<OneBotGroupInfo> GroupLists { get; set; } = [];
        public Exception? SendException { get; set; }

        public Task ConnectAsync() => Task.CompletedTask;
        public Task SendGroupMessage(long groupId, string message)
        {
            if (SendException != null)
                throw SendException;
            GroupMessages.Add((groupId, message));
            return Task.CompletedTask;
        }

        public Task SendPrivateMessage(long userId, string message)
        {
            if (SendException != null)
                throw SendException;
            PrivateMessages.Add((userId, message));
            return Task.CompletedTask;
        }

        public Task UploadGroupFile(long groupId, string filePath, string name)
        {
            GroupFiles.Add((groupId, filePath, name));
            return Task.CompletedTask;
        }

        public Task UploadPrivateFile(long userId, string filePath, string name)
        {
            PrivateFiles.Add((userId, filePath, name));
            return Task.CompletedTask;
        }
        public Task<OneBotFile?> GetPrivateFileUrl(string fileId) =>
            Task.FromResult(PrivateFileUrls.TryGetValue(fileId, out OneBotFile? file) ? file : null);
        public Task<OneBotFile?> GetGroupFileUrl(long groupId, string fileId) =>
            Task.FromResult(GroupFileUrls.TryGetValue((groupId, fileId), out OneBotFile? file) ? file : null);
        public Task<OneBotMessageEvent?> GetMessage(long messageId) => Task.FromResult<OneBotMessageEvent?>(null);
        public Task<List<OneBotForwardMessage>?> GetForwardMessage(string forwardId) => Task.FromResult<List<OneBotForwardMessage>?>([]);
        public Task<IReadOnlyList<OneBotGroupInfo>> GetGroupList() => Task.FromResult(GroupLists);
        public Task<IReadOnlyList<OneBotGroupMember>> GetGroupMemberList(long groupId)
        {
            return Task.FromResult(GroupMemberLists.TryGetValue(groupId, out IReadOnlyList<OneBotGroupMember>? members)
                ? members
                : Array.Empty<OneBotGroupMember>());
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Raise(OneBotBaseEvent ev) => EventReceived?.Invoke(ev);
    }

    sealed class GeneratedAcknowledgementQChatService(
        XmlFunctionCaller functionCaller,
        IOneBotRuntime runtime,
        IReadOnlyList<string> acknowledgements) : QChatService(functionCaller, new NullLogger<QChatService>(), oneBotRuntime: runtime)
    {
        int index;

        protected override Task<string> GenerateQuietModeAcknowledgementAsync(string prompt)
        {
            int selected = Interlocked.Increment(ref index) - 1;
            return Task.FromResult(acknowledgements[selected % acknowledgements.Count]);
        }
    }

    sealed class PlainReplyQChatService(
        XmlFunctionCaller functionCaller,
        IOneBotRuntime runtime,
        string reply) : QChatService(functionCaller, new NullLogger<QChatService>(), oneBotRuntime: runtime)
    {
        readonly TaskCompletionSource dispatchCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitForDispatchAsync() => dispatchCompletion.Task.WaitAsync(TimeSpan.FromSeconds(2));

        protected override Task<string> DispatchToModelAsync(QChatInboundMessage message)
        {
            dispatchCompletion.TrySetResult();
            return Task.FromResult(reply);
        }
    }

    sealed class ExposedFilterQChatService(IOneBotRuntime runtime)
        : QChatService(new XmlFunctionCaller(new NullLogger<XmlFunctionCaller>()), new NullLogger<QChatService>(), oneBotRuntime: runtime)
    {
        public string FilterForTest(string text) => ChatTextFilter(text);
    }

    sealed class BlockingDispatchQChatService(
        XmlFunctionCaller functionCaller,
        IOneBotRuntime runtime) : QChatService(functionCaller, new NullLogger<QChatService>(), oneBotRuntime: runtime)
    {
        readonly Queue<TaskCompletionSource<string>> pendingReplies = new();
        readonly object gate = new();

        public List<long> StartedTargets { get; } = new();

        public void ReleaseNextReply(string reply)
        {
            TaskCompletionSource<string>? pending = null;
            lock (gate)
            {
                if (pendingReplies.Count > 0)
                    pending = pendingReplies.Dequeue();
            }

            Assert.That(pending, Is.Not.Null, "Expected a pending model dispatch.");
            pending!.SetResult(reply);
        }

        protected override Task<string> DispatchToModelAsync(QChatInboundMessage message)
        {
            TaskCompletionSource<string> reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (gate)
            {
                StartedTargets.Add(message.TargetId);
                pendingReplies.Enqueue(reply);
            }

            return reply.Task;
        }
    }

    sealed class CapturingQChatService(
        XmlFunctionCaller functionCaller,
        IOneBotRuntime runtime,
        QChatUserProfileService? userProfileService = null,
        QChatRelationCacheService? relationCacheService = null) : QChatService(
            functionCaller,
            new NullLogger<QChatService>(),
            oneBotRuntime: runtime,
            relationCacheService: relationCacheService,
            userProfileService: userProfileService)
    {
        readonly TaskCompletionSource<QChatInboundMessage> inboundCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<QChatInboundMessage> WaitForInboundAsync() =>
            inboundCompletion.Task.WaitAsync(TimeSpan.FromSeconds(2));

        protected override Task<string> DispatchToModelAsync(QChatInboundMessage message)
        {
            inboundCompletion.TrySetResult(message);
            return Task.FromResult("");
        }
    }

    sealed class FakeLifeEventPublisher : ILifeEventPublisher
    {
        public List<LifeEvent> Events { get; } = new();
        public void Publish(LifeEvent lifeEvent) => Events.Add(lifeEvent);
    }
}
