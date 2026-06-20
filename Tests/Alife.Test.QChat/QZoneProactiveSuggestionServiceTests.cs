using Alife.Framework;
using Alife.Function.Agent;
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public class QZoneProactiveSuggestionServiceTests
{
    [Test]
    public void QZoneInteractionConfigDefaultsToEnabled()
    {
        QZoneInteractionConfig config = new();

        Assert.That(config.EnableQZone, Is.True);
        Assert.That(config.CommentReplyProbability, Is.EqualTo(0.8));
    }

    [Test]
    public void DefaultPolicySuggestsOwnerConfirmedReplyForCommentOpportunity()
    {
        QZoneProactiveSuggestionService service = new(random: () => 0.1);

        IReadOnlyList<AgentProactiveSuggestion> suggestions = service.BuildSuggestions(
            new QZoneProactiveInteraction(
                TargetId: 1001,
                PostId: "post-a",
                CommentId: "comment-a",
                Summary: "Private contact commented on a QZone post.",
                AllowReply: true,
                AllowLike: false));

        AgentProactiveSuggestion reply = suggestions.Single(item => item.Kind == AgentProactiveActionKind.QZoneReply);
        Assert.That(reply.RiskLevel, Is.EqualTo(AgentAuditRiskLevel.High));
        Assert.That(reply.RequiresOwnerConfirmation, Is.True);
        Assert.That(reply.TargetType, Is.EqualTo("qzone"));
        Assert.That(reply.TargetId, Is.EqualTo(1001));
        Assert.That(reply.DraftText, Does.Contain("comment-a"));
    }

    [Test]
    public async Task RelationCacheDisplayNameIsIncludedInQZoneSuggestions()
    {
        FakeOneBotRuntime runtime = new();
        runtime.GroupMemberLists[123] = [
            new OneBotGroupMember { GroupId = 123, UserId = 1001, Card = "Alice-card", Nickname = "Alice" }
        ];
        QChatRelationCacheService relationCache = new(runtime);
        await relationCache.RefreshGroupMembersAsync(123);
        QZoneProactiveSuggestionService service = new(
            new QZoneInteractionConfig
            {
                PrivateChatContactIds = "1001",
                PrivateContactLikeProbability = 1.0
            },
            relationCache,
            random: () => 0.0);

        IReadOnlyList<AgentProactiveSuggestion> suggestions = service.BuildSuggestions(
            new QZoneProactiveInteraction(
                TargetId: 1001,
                PostId: "post-a",
                CommentId: null,
                Summary: "Private chat contact posted a QZone update.",
                AllowReply: false,
                AllowLike: true));

        AgentProactiveSuggestion like = suggestions.Single(item => item.Kind == AgentProactiveActionKind.QZoneLike);
        Assert.That(like.RiskLevel, Is.EqualTo(AgentAuditRiskLevel.High));
        Assert.That(like.RequiresOwnerConfirmation, Is.True);
        Assert.That(like.Reason, Does.Contain("Alice-card"));
    }

    [Test]
    public void QZoneSuggestionServiceActsAsAgentProactiveProviderForLifeEvents()
    {
        QZoneProactiveSuggestionService service = new(random: () => 0.1);
        AgentSelfModelSnapshot snapshot = CreateSnapshot([
            new LifeEvent(
                DateTimeOffset.Parse("2026-06-14T12:00:00Z"),
                LifeEventKind.Communication,
                "QZone",
                "target=1001 post=post-a comment=comment-a Private contact commented on a QZone post.")
        ]);

        Assert.That(service, Is.AssignableTo<IAgentProactiveSuggestionProvider>());
        IReadOnlyList<AgentProactiveSuggestion> suggestions =
            ((IAgentProactiveSuggestionProvider)service).BuildSuggestions(
                new AgentProactiveSuggestionContext(snapshot, snapshot.RecentExperiences));

        AgentProactiveSuggestion reply = suggestions.Single(item => item.Kind == AgentProactiveActionKind.QZoneReply);
        Assert.That(reply.TargetId, Is.EqualTo(1001));
        Assert.That(reply.DraftText, Does.Contain("post-a"));
        Assert.That(reply.DraftText, Does.Contain("comment-a"));
        Assert.That(reply.RequiresOwnerConfirmation, Is.True);
    }

    sealed class FakeOneBotRuntime : IOneBotRuntime
    {
        public event Action<OneBotBaseEvent>? EventReceived;
        public long BotId { get; set; } = 999;
        public bool IsConnected { get; set; } = true;
        public string Url { get; set; } = "";
        public string Token { get; set; } = "";
        public Dictionary<long, IReadOnlyList<OneBotGroupMember>> GroupMemberLists { get; } = new();

        public Task ConnectAsync() => Task.CompletedTask;
        public Task SendGroupMessage(long groupId, string message) => Task.CompletedTask;
        public Task SendPrivateMessage(long userId, string message) => Task.CompletedTask;
        public Task UploadGroupFile(long groupId, string filePath, string name) => Task.CompletedTask;
        public Task UploadPrivateFile(long userId, string filePath, string name) => Task.CompletedTask;
        public Task<OneBotFile?> GetPrivateFileUrl(string fileId) => Task.FromResult<OneBotFile?>(null);
        public Task<OneBotFile?> GetGroupFileUrl(long groupId, string fileId) => Task.FromResult<OneBotFile?>(null);
        public Task<OneBotMessageEvent?> GetMessage(long messageId) => Task.FromResult<OneBotMessageEvent?>(null);
        public Task<List<OneBotForwardMessage>?> GetForwardMessage(string forwardId) => Task.FromResult<List<OneBotForwardMessage>?>([]);
        public Task<IReadOnlyList<OneBotGroupInfo>> GetGroupList() => Task.FromResult<IReadOnlyList<OneBotGroupInfo>>([]);

        public Task<IReadOnlyList<OneBotGroupMember>> GetGroupMemberList(long groupId)
        {
            return Task.FromResult(GroupMemberLists.TryGetValue(groupId, out IReadOnlyList<OneBotGroupMember>? members)
                ? members
                : Array.Empty<OneBotGroupMember>());
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    static AgentSelfModelSnapshot CreateSnapshot(IReadOnlyList<LifeEvent> recentExperiences)
    {
        AgentStateSnapshot runtime = new(
            "AstralFox",
            IsChatting: false,
            PendingPokeCount: 0,
            ChatHistoryCount: 12,
            LastError: null,
            RecentEvents: [],
            ModuleHealth: [],
            Capabilities: []);

        return new AgentSelfModelSnapshot(
            "AstralFox",
            DateTimeOffset.Parse("2026-06-14T12:00:00Z"),
            runtime,
            runtime.Capabilities,
            runtime.ModuleHealth,
            LatestTask: null,
            SafetyBoundaries: ["High-risk actions require owner confirmation."],
            RecentExperiences: recentExperiences);
    }
}
