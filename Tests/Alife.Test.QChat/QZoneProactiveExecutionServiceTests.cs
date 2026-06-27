using Alife.Function.Agent;
using Alife.Function.QChat;
using Alife.Framework;
using Autofac;
using System.IO;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public class QZoneProactiveExecutionServiceTests
{
    [Test]
    public async Task RuntimeModuleContainerSharesProactiveBehaviorAcrossQZoneAndControlCenter()
    {
        Character character = new() { Name = "AstralFox" };
        using IContainer container = ChatActivity.BuildModuleContainer(
            [
                typeof(AgentProactiveBehaviorService),
                typeof(AgentControlCenterService),
                typeof(QZoneService)
            ],
            character,
            new ConfigurationSystem(new StorageSystem()));
        AgentProactiveBehaviorService proactive = container.Resolve<AgentProactiveBehaviorService>();
        AgentProactivePendingSuggestion pending = proactive.EnqueuePendingSuggestion(new AgentProactiveSuggestion(
            AgentProactiveActionKind.QZoneReply,
            "reply to private qzone comment",
            AgentAuditRiskLevel.High,
            RequiresOwnerConfirmation: true,
            TargetType: "qzone",
            TargetId: 1001,
            DraftText: "reply target=1001 post=post-a comment=comment-a"));
        proactive.PrepareQZoneReplyContent(pending.Id, "谢谢分享。", "agent");
        proactive.ConfirmPendingSuggestion(pending.Id, "owner");
        container.Resolve<QZoneService>().Configuration = new QZoneServiceConfig
        {
            DryRunExternalActions = true,
            CommentReplyProbability = 1.0
        };
        AgentControlCenterService controlCenter = container.Resolve<AgentControlCenterService>();
        await controlCenter.AwakeAsync(new AwakeContext
        {
            Character = character,
            Services = (IServiceProvider)container,
            KernelBuilder = Kernel.CreateBuilder(),
            ContextBuilder = new ChatHistoryAgentThread()
        });

        AgentProactiveExternalExecutionResult result = await controlCenter.ExecuteProactiveSuggestionFromControlCenter(pending.Id);
        AgentControlCenterSnapshot snapshot = controlCenter.BuildSnapshot(
            new ChatRuntimeState(false, 0, 0, null, []),
            character.Name);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(proactive.GetCompletedSuggestion(pending.Id)?.Status, Is.EqualTo(AgentProactivePendingStatus.Executed));
        Assert.That(snapshot.CompletedProactiveSuggestions.Single(item => item.Id == pending.Id).Status,
            Is.EqualTo(AgentProactivePendingStatus.Executed));
    }

    [Test]
    public async Task ExecuteConfirmedProactiveSuggestionByIdUsesConfirmedSuggestionHistory()
    {
        AgentProactiveBehaviorService proactiveBehavior = new(clock: () => DateTimeOffset.Parse("2026-06-14T12:00:00Z"));
        AgentProactivePendingSuggestion pending = proactiveBehavior.EnqueuePendingSuggestion(new AgentProactiveSuggestion(
            AgentProactiveActionKind.QZoneLike,
            "test qzone suggestion",
            AgentAuditRiskLevel.High,
            RequiresOwnerConfirmation: true,
            TargetType: "qzone",
            TargetId: 1001,
            DraftText: "like target=1001 post=post-a"));
        proactiveBehavior.ConfirmPendingSuggestion(pending.Id, "owner");
        FakeQZoneRuntime runtime = new();
        QZoneService qzone = new(runtime, proactiveBehavior: proactiveBehavior)
        {
            Configuration = new QZoneServiceConfig
            {
                DryRunExternalActions = false,
                PrivateChatContactIds = "1001",
                PrivateContactLikeProbability = 1.0
            }
        };

        QZoneProactiveExecutionResult result = await qzone.ExecuteConfirmedProactiveSuggestion(pending.Id);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(runtime.Likes, Is.EqualTo(new[] { (1001L, "post-a") }));
        Assert.That(proactiveBehavior.GetCompletedSuggestion(pending.Id)?.Status, Is.EqualTo(AgentProactivePendingStatus.Executed));
    }

    [Test]
    public async Task ExecuteConfirmedProactiveSuggestionByIdRejectsRepeatExecution()
    {
        AgentProactiveBehaviorService proactiveBehavior = new(clock: () => DateTimeOffset.Parse("2026-06-14T12:00:00Z"));
        AgentProactivePendingSuggestion pending = proactiveBehavior.EnqueuePendingSuggestion(new AgentProactiveSuggestion(
            AgentProactiveActionKind.QZoneLike,
            "test qzone suggestion",
            AgentAuditRiskLevel.High,
            RequiresOwnerConfirmation: true,
            TargetType: "qzone",
            TargetId: 1001,
            DraftText: "like target=1001 post=post-a"));
        proactiveBehavior.ConfirmPendingSuggestion(pending.Id, "owner");
        FakeQZoneRuntime runtime = new();
        QZoneService qzone = new(runtime, proactiveBehavior: proactiveBehavior)
        {
            Configuration = new QZoneServiceConfig
            {
                DryRunExternalActions = false,
                PrivateChatContactIds = "1001",
                PrivateContactLikeProbability = 1.0
            }
        };

        await qzone.ExecuteConfirmedProactiveSuggestion(pending.Id);
        QZoneProactiveExecutionResult repeated = await qzone.ExecuteConfirmedProactiveSuggestion(pending.Id);

        Assert.That(repeated.Succeeded, Is.False);
        Assert.That(repeated.Message, Does.Contain("already executed"));
        Assert.That(runtime.Likes, Is.EqualTo(new[] { (1001L, "post-a") }));
    }

    [Test]
    public async Task ExecuteConfirmedPreparedReplySuggestionUsesPreparedContent()
    {
        AgentProactiveBehaviorService proactiveBehavior = new(clock: () => DateTimeOffset.Parse("2026-06-14T12:00:00Z"));
        AgentProactivePendingSuggestion pending = proactiveBehavior.EnqueuePendingSuggestion(new AgentProactiveSuggestion(
            AgentProactiveActionKind.QZoneReply,
            "reply to private qzone comment",
            AgentAuditRiskLevel.High,
            RequiresOwnerConfirmation: true,
            TargetType: "qzone",
            TargetId: 1001,
            DraftText: "reply target=1001 post=post-a comment=comment-a"));
        proactiveBehavior.PrepareQZoneReplyContent(pending.Id, "谢谢分享，感觉很不错。", "agent");
        proactiveBehavior.ConfirmPendingSuggestion(pending.Id, "owner");
        FakeQZoneRuntime runtime = new();
        QZoneService qzone = new(runtime, proactiveBehavior: proactiveBehavior)
        {
            Configuration = new QZoneServiceConfig
            {
                DryRunExternalActions = false,
                CommentReplyProbability = 1.0
            }
        };

        QZoneProactiveExecutionResult result = await qzone.ExecuteConfirmedProactiveSuggestion(pending.Id);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(runtime.Replies, Is.EqualTo(new[] { (1001L, "post-a", "comment-a", "谢谢分享，感觉很不错。") }));
    }

    [Test]
    public async Task ExecuteConfirmedLikeSuggestionUsesQZoneService()
    {
        FakeQZoneRuntime runtime = new();
        QZoneService qzone = new(runtime)
        {
            Configuration = new QZoneServiceConfig
            {
                DryRunExternalActions = false,
                PrivateChatContactIds = "1001",
                PrivateContactLikeProbability = 1.0
            }
        };
        QZoneProactiveExecutionService executor = new(qzone, random: () => 0.0);
        AgentProactivePendingSuggestion pending = CreatePending(
            AgentProactivePendingStatus.Confirmed,
            AgentProactiveActionKind.QZoneLike,
            "like target=1001 post=post-a");

        QZoneProactiveExecutionResult result = await executor.ExecuteAsync(pending);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.ActionResult?.Action, Is.EqualTo("like"));
        Assert.That(runtime.Likes, Is.EqualTo(new[] { (1001L, "post-a") }));
    }

    [Test]
    public async Task ExecuteConfirmedSuggestionUsesSecurityGatewayForExternalRequests()
    {
        FakeQZoneRuntime runtime = new();
        QZoneService qzone = new(runtime)
        {
            Configuration = new QZoneServiceConfig
            {
                DryRunExternalActions = false,
                PrivateChatContactIds = "1001",
                PrivateContactLikeProbability = 1.0
            }
        };
        QZoneProactiveExecutionService executor = new(qzone, random: () => 0.0);
        AgentPermissionConfig config = new()
        {
            OwnerUserIds = [10001],
            RequireConfirmationForHighRisk = true
        };
        AgentProactivePendingSuggestion blockedPending = CreatePending(
            AgentProactivePendingStatus.Confirmed,
            AgentProactiveActionKind.QZoneLike,
            "like target=1001 post=post-a");
        AgentProactivePendingSuggestion allowedPending = CreatePending(
            AgentProactivePendingStatus.Confirmed,
            AgentProactiveActionKind.QZoneLike,
            "like target=1001 post=post-b");

        QZoneProactiveExecutionResult blocked = await executor.ExecuteAsync(
            blockedPending,
            new AgentPermissionRequest(
                ActorUserId: 20002,
                Source: AgentRequestSource.GroupChat,
                IsMentioned: true,
                RiskLevel: AgentRiskLevel.Low,
                HasExplicitConfirmation: true,
                Action: "qzone.like"),
            config);
        QZoneProactiveExecutionResult allowed = await executor.ExecuteAsync(
            allowedPending,
            new AgentPermissionRequest(
                ActorUserId: 10001,
                Source: AgentRequestSource.PrivateChat,
                IsMentioned: false,
                RiskLevel: AgentRiskLevel.Low,
                HasExplicitConfirmation: true,
                Action: "qzone.like"),
            config);

        Assert.That(blocked.Succeeded, Is.False);
        Assert.That(blocked.Message, Does.Contain("Owner confirmation required"));
        Assert.That(allowed.Succeeded, Is.True);
        Assert.That(runtime.Likes, Is.EqualTo(new[] { (1001L, "post-b") }));
    }

    [Test]
    public async Task ExecuteConfirmedSuggestionSecurityGatewayAuditsBlockedAndAllowedActions()
    {
        string root = Path.Combine(Path.GetTempPath(), "alife-qzone-gateway-tests", Guid.NewGuid().ToString("N"));
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentActionGatewayService gateway = new(auditLog: audit);
        FakeQZoneRuntime runtime = new();
        QZoneService qzone = new(runtime)
        {
            Configuration = new QZoneServiceConfig
            {
                DryRunExternalActions = false,
                PrivateChatContactIds = "1001",
                PrivateContactLikeProbability = 1.0
            }
        };
        QZoneProactiveExecutionService executor = new(qzone, random: () => 0.0, actionGateway: gateway);
        AgentPermissionConfig config = new()
        {
            OwnerUserIds = [10001],
            RequireConfirmationForHighRisk = true
        };

        QZoneProactiveExecutionResult blocked = await executor.ExecuteAsync(
            CreatePending(AgentProactivePendingStatus.Confirmed, AgentProactiveActionKind.QZoneLike, "like target=1001 post=post-a"),
            new AgentPermissionRequest(
                ActorUserId: 20002,
                Source: AgentRequestSource.GroupChat,
                IsMentioned: true,
                RiskLevel: AgentRiskLevel.Low,
                HasExplicitConfirmation: true,
                Action: "qzone.like"),
            config);
        QZoneProactiveExecutionResult allowed = await executor.ExecuteAsync(
            CreatePending(AgentProactivePendingStatus.Confirmed, AgentProactiveActionKind.QZoneLike, "like target=1001 post=post-b"),
            new AgentPermissionRequest(
                ActorUserId: 10001,
                Source: AgentRequestSource.PrivateChat,
                IsMentioned: false,
                RiskLevel: AgentRiskLevel.Low,
                HasExplicitConfirmation: true,
                Action: "qzone.like"),
            config);
        AgentAuditLogEntry[] entries = audit.GetRecentEntries(10).ToArray();

        Assert.That(blocked.Succeeded, Is.False);
        Assert.That(allowed.Succeeded, Is.True);
        Assert.That(entries.Select(entry => entry.Action), Is.EqualTo(new[] { "qzone.like", "qzone.like" }));
        Assert.That(entries[0].Succeeded, Is.False);
        Assert.That(entries[0].Error, Does.Contain("Owner confirmation required"));
        Assert.That(entries[1].Succeeded, Is.True);
        Assert.That(runtime.Likes, Is.EqualTo(new[] { (1001L, "post-b") }));
    }

    [Test]
    public async Task ExecuteConfirmedProactiveSuggestionByIdCanUseSecurityGateway()
    {
        AgentProactiveBehaviorService proactiveBehavior = new(clock: () => DateTimeOffset.Parse("2026-06-14T12:00:00Z"));
        AgentProactivePendingSuggestion pending = proactiveBehavior.EnqueuePendingSuggestion(new AgentProactiveSuggestion(
            AgentProactiveActionKind.QZoneLike,
            "test qzone suggestion",
            AgentAuditRiskLevel.High,
            RequiresOwnerConfirmation: true,
            TargetType: "qzone",
            TargetId: 1001,
            DraftText: "like target=1001 post=post-a"));
        proactiveBehavior.ConfirmPendingSuggestion(pending.Id, "owner");
        FakeQZoneRuntime runtime = new();
        QZoneService qzone = new(runtime, proactiveBehavior: proactiveBehavior)
        {
            Configuration = new QZoneServiceConfig
            {
                DryRunExternalActions = false,
                PrivateChatContactIds = "1001",
                PrivateContactLikeProbability = 1.0
            }
        };
        AgentPermissionConfig config = new()
        {
            OwnerUserIds = [10001],
            RequireConfirmationForHighRisk = true
        };

        QZoneProactiveExecutionResult blocked = await qzone.ExecuteConfirmedProactiveSuggestion(
            pending.Id,
            new AgentPermissionRequest(
                ActorUserId: 20002,
                Source: AgentRequestSource.GroupChat,
                IsMentioned: true,
                RiskLevel: AgentRiskLevel.Low,
                HasExplicitConfirmation: true,
                Action: "qzone.like"),
            config);

        Assert.That(blocked.Succeeded, Is.False);
        Assert.That(blocked.Message, Does.Contain("Owner confirmation required"));
        Assert.That(runtime.Likes, Is.Empty);
        Assert.That(proactiveBehavior.GetCompletedSuggestion(pending.Id)?.Status, Is.EqualTo(AgentProactivePendingStatus.Confirmed));
    }

    [Test]
    public async Task ExecuteConfirmedReplySuggestionRequiresExplicitContent()
    {
        FakeQZoneRuntime runtime = new();
        QZoneService qzone = new(runtime)
        {
            Configuration = new QZoneServiceConfig
            {
                DryRunExternalActions = false,
                CommentReplyProbability = 1.0
            }
        };
        QZoneProactiveExecutionService executor = new(qzone, random: () => 0.0);
        AgentProactivePendingSuggestion pending = CreatePending(
            AgentProactivePendingStatus.Confirmed,
            AgentProactiveActionKind.QZoneReply,
            "reply target=1001 post=post-a comment=comment-a");

        QZoneProactiveExecutionResult result = await executor.ExecuteAsync(pending);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Message, Does.Contain("content"));
        Assert.That(runtime.Replies, Is.Empty);
    }

    [Test]
    public async Task ExecuteConfirmedReplySuggestionUsesQZoneServiceWhenContentIsPresent()
    {
        FakeQZoneRuntime runtime = new();
        QZoneService qzone = new(runtime)
        {
            Configuration = new QZoneServiceConfig
            {
                DryRunExternalActions = false,
                CommentReplyProbability = 1.0
            }
        };
        QZoneProactiveExecutionService executor = new(qzone, random: () => 0.0);
        AgentProactivePendingSuggestion pending = CreatePending(
            AgentProactivePendingStatus.Confirmed,
            AgentProactiveActionKind.QZoneReply,
            "reply target=1001 post=post-a comment=comment-a content=thanks");

        QZoneProactiveExecutionResult result = await executor.ExecuteAsync(pending);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.ActionResult?.Action, Is.EqualTo("reply"));
        Assert.That(runtime.Replies, Is.EqualTo(new[] { (1001L, "post-a", "comment-a", "thanks") }));
    }

    [Test]
    public async Task ExecuteRejectsUnconfirmedSuggestion()
    {
        QZoneProactiveExecutionService executor = new(new QZoneService());
        AgentProactivePendingSuggestion pending = CreatePending(
            AgentProactivePendingStatus.Pending,
            AgentProactiveActionKind.QZoneLike,
            "like target=1001 post=post-a");

        QZoneProactiveExecutionResult result = await executor.ExecuteAsync(pending);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Message, Does.Contain("confirmed"));
    }

    static AgentProactivePendingSuggestion CreatePending(
        AgentProactivePendingStatus status,
        AgentProactiveActionKind kind,
        string draft)
    {
        return new AgentProactivePendingSuggestion(
            Guid.NewGuid().ToString("N"),
            new AgentProactiveSuggestion(
                kind,
                "test qzone suggestion",
                AgentAuditRiskLevel.High,
                RequiresOwnerConfirmation: true,
                TargetType: "qzone",
                TargetId: 1001,
                DraftText: draft),
            DateTimeOffset.Parse("2026-06-14T12:00:00Z"),
            status,
            "test");
    }

    sealed class FakeQZoneRuntime : IQZoneRuntime
    {
        public List<string> Posts { get; } = new();
        public List<(long TargetId, string PostId, string Content)> Comments { get; } = new();
        public List<(long TargetId, string PostId, string CommentId, string Content)> Replies { get; } = new();
        public List<(long TargetId, string PostId)> Likes { get; } = new();

        public Task PublishPost(string content)
        {
            Posts.Add(content);
            return Task.CompletedTask;
        }

        public Task Comment(long targetId, string postId, string content)
        {
            Comments.Add((targetId, postId, content));
            return Task.CompletedTask;
        }

        public Task ReplyComment(long targetId, string postId, string commentId, string content)
        {
            Replies.Add((targetId, postId, commentId, content));
            return Task.CompletedTask;
        }

        public Task LikePost(long targetId, string postId)
        {
            Likes.Add((targetId, postId));
            return Task.CompletedTask;
        }

        public Task<QZonePostSnapshot?> GetLatestPost(long targetId)
        {
            return Task.FromResult<QZonePostSnapshot?>(null);
        }

        public Task<IReadOnlyList<QZoneCommentSnapshot>> GetLatestComments(long targetId, string postId, int count)
        {
            return Task.FromResult<IReadOnlyList<QZoneCommentSnapshot>>([]);
        }
    }
}
