using Alife.Framework;
using Alife.Function.Agent;
using Alife.Function.Interpreter;

namespace Alife.Test.Framework;

public class AgentProactiveBehaviorServiceTests
{
    [Test]
    public void ProactiveBehaviorDoesNotSuggestChatWhenDisabled()
    {
        AgentControlCenterService control = new()
        {
            Configuration = new AgentControlCenterConfig
            {
                AllowProactiveChat = false,
                ProactiveChatIntensity = 5
            }
        };
        AgentProactiveBehaviorService service = new(controlCenter: control);

        IReadOnlyList<AgentProactiveSuggestion> suggestions = service.BuildSuggestions(CreateSnapshot());

        Assert.That(suggestions, Has.Count.EqualTo(1));
        Assert.That(suggestions[0].Kind, Is.EqualTo(AgentProactiveActionKind.None));
        Assert.That(suggestions[0].Reason, Does.Contain("disabled"));
        Assert.That(suggestions[0].RequiresOwnerConfirmation, Is.False);
    }

    [Test]
    public void ProactiveBehaviorSuggestsLowRiskBodyActionForRecentOwnerRequest()
    {
        AgentControlCenterService control = new()
        {
            Configuration = new AgentControlCenterConfig
            {
                AllowProactiveChat = true,
                ProactiveChatIntensity = 3
            }
        };
        AgentProactiveBehaviorService service = new(controlCenter: control);
        AgentSelfModelSnapshot snapshot = CreateSnapshot([
            new LifeEvent(
                DateTimeOffset.Parse("2026-06-14T12:00:00Z"),
                LifeEventKind.Communication,
                "QChat",
                "Owner asked the bot to continue improving itself.")
        ]);

        IReadOnlyList<AgentProactiveSuggestion> suggestions = service.BuildSuggestions(snapshot);

        AgentProactiveSuggestion bodySuggestion = suggestions.Single(item =>
            item.Kind == AgentProactiveActionKind.DeskPetExpression);
        Assert.That(bodySuggestion.RiskLevel, Is.EqualTo(AgentAuditRiskLevel.Low));
        Assert.That(bodySuggestion.RequiresOwnerConfirmation, Is.False);
        Assert.That(bodySuggestion.DraftText, Does.Contain("继续"));
    }

    [Test]
    public void ProactiveBehaviorMarksQZoneSuggestionsAsOwnerConfirmedHighRisk()
    {
        AgentControlCenterService control = new()
        {
            Configuration = new AgentControlCenterConfig
            {
                AllowProactiveChat = true,
                ProactiveChatIntensity = 4,
                RequireOwnerConfirmationForHighRiskConfiguration = true
            }
        };
        AgentProactiveBehaviorService service = new(controlCenter: control);
        AgentSelfModelSnapshot snapshot = CreateSnapshot([
            new LifeEvent(
                DateTimeOffset.Parse("2026-06-14T12:00:00Z"),
                LifeEventKind.Communication,
                "QZone",
                "A private chat contact posted a QZone update that may need a comment reply.")
        ]);

        IReadOnlyList<AgentProactiveSuggestion> suggestions = service.BuildSuggestions(snapshot);

        AgentProactiveSuggestion qzoneSuggestion = suggestions.Single(item =>
            item.Kind == AgentProactiveActionKind.QZoneReply);
        Assert.That(qzoneSuggestion.RiskLevel, Is.EqualTo(AgentAuditRiskLevel.High));
        Assert.That(qzoneSuggestion.RequiresOwnerConfirmation, Is.True);
        Assert.That(qzoneSuggestion.DraftText, Is.Not.Empty);
    }

    [Test]
    public void ProactiveBehaviorAppliesCooldownBetweenSuggestions()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-14T12:00:00Z");
        AgentControlCenterService control = new()
        {
            Configuration = new AgentControlCenterConfig
            {
                AllowProactiveChat = true,
                ProactiveChatIntensity = 3
            }
        };
        AgentProactiveBehaviorService service = new(
            controlCenter: control,
            clock: () => now,
            minimumCooldown: TimeSpan.FromMinutes(5));
        AgentSelfModelSnapshot snapshot = CreateSnapshot([
            new LifeEvent(
                now,
                LifeEventKind.Communication,
                "QChat",
                "Owner asked the bot to continue improving itself.")
        ]);

        IReadOnlyList<AgentProactiveSuggestion> first = service.BuildSuggestions(snapshot);
        now = now.AddMinutes(2);
        IReadOnlyList<AgentProactiveSuggestion> second = service.BuildSuggestions(snapshot);
        now = now.AddMinutes(4);
        IReadOnlyList<AgentProactiveSuggestion> third = service.BuildSuggestions(snapshot);

        Assert.That(first.Any(item => item.Kind == AgentProactiveActionKind.DeskPetExpression), Is.True);
        Assert.That(second, Has.Count.EqualTo(1));
        Assert.That(second[0].Kind, Is.EqualTo(AgentProactiveActionKind.None));
        Assert.That(second[0].Reason, Does.Contain("cooldown"));
        Assert.That(third.Any(item => item.Kind == AgentProactiveActionKind.DeskPetExpression), Is.True);
    }

    [Test]
    public void ProactiveBehaviorAuditsSuggestionsWhenAuditLogIsAvailable()
    {
        string root = CreateTempWorkspace();
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentControlCenterService control = new()
        {
            Configuration = new AgentControlCenterConfig
            {
                AllowProactiveChat = true,
                ProactiveChatIntensity = 3
            }
        };
        AgentProactiveBehaviorService service = new(controlCenter: control, auditLog: audit);
        AgentSelfModelSnapshot snapshot = CreateSnapshot([
            new LifeEvent(
                DateTimeOffset.Parse("2026-06-14T12:00:00Z"),
                LifeEventKind.Communication,
                "QChat",
                "Owner asked the bot to continue improving itself.")
        ]);

        service.BuildSuggestions(snapshot);

        IReadOnlyList<AgentAuditLogEntry> entries = audit.GetRecentEntries(10);
        Assert.That(entries.Select(entry => entry.Action), Does.Contain("agent.proactive.suggested"));
        Assert.That(entries.Single(entry => entry.Action == "agent.proactive.suggested").Detail,
            Does.Contain(nameof(AgentProactiveActionKind.DeskPetExpression)));
    }

    [Test]
    public void ProactiveBehaviorIncludesExternalProviderSuggestionsAndAuditsThem()
    {
        string root = CreateTempWorkspace();
        AgentAuditLogService audit = new(Path.Combine(root, "audit.jsonl"));
        AgentControlCenterService control = new()
        {
            Configuration = new AgentControlCenterConfig
            {
                AllowProactiveChat = true,
                ProactiveChatIntensity = 3
            }
        };
        AgentProactiveSuggestion providerSuggestion = new(
            AgentProactiveActionKind.QZoneLike,
            "external qzone suggestion",
            AgentAuditRiskLevel.High,
            RequiresOwnerConfirmation: true,
            TargetType: "qzone",
            TargetId: 1001,
            DraftText: "like target=1001 post=post-a");
        StubSuggestionProvider provider = new(providerSuggestion);
        AgentProactiveBehaviorService service = new(
            controlCenter: control,
            auditLog: audit,
            suggestionProviders: [provider]);

        IReadOnlyList<AgentProactiveSuggestion> suggestions = service.BuildSuggestions(CreateSnapshot([
            new LifeEvent(
                DateTimeOffset.Parse("2026-06-14T12:00:00Z"),
                LifeEventKind.Communication,
                "QZone",
                "target=1001 post=post-a private contact posted a QZone update.")
        ]));

        Assert.That(provider.LastContext?.RecentExperiences.Select(item => item.Source), Does.Contain("QZone"));
        Assert.That(suggestions, Does.Contain(providerSuggestion));
        Assert.That(audit.GetRecentEntries(10).Select(entry => entry.Detail), Has.Some.Contains(nameof(AgentProactiveActionKind.QZoneLike)));
    }

    [Test]
    public void ProactiveBehaviorQueuesHighRiskSuggestionsForOwnerConfirmation()
    {
        AgentControlCenterService control = new()
        {
            Configuration = new AgentControlCenterConfig
            {
                AllowProactiveChat = true,
                ProactiveChatIntensity = 3
            }
        };
        AgentProactiveSuggestion providerSuggestion = new(
            AgentProactiveActionKind.QZoneReply,
            "external qzone reply suggestion",
            AgentAuditRiskLevel.High,
            RequiresOwnerConfirmation: true,
            TargetType: "qzone",
            TargetId: 1001,
            DraftText: "reply target=1001 post=post-a");
        AgentProactiveBehaviorService service = new(
            controlCenter: control,
            suggestionProviders: [new StubSuggestionProvider(providerSuggestion)]);

        service.BuildSuggestions(CreateSnapshot([
            new LifeEvent(
                DateTimeOffset.Parse("2026-06-14T12:00:00Z"),
                LifeEventKind.Communication,
                "QZone",
                "target=1001 post=post-a private contact commented.")
        ]));

        IReadOnlyList<AgentProactivePendingSuggestion> pending = service.GetPendingSuggestions();

        Assert.That(pending, Has.Count.EqualTo(1));
        Assert.That(pending[0].Suggestion, Is.EqualTo(providerSuggestion));
        Assert.That(pending[0].Status, Is.EqualTo(AgentProactivePendingStatus.Pending));
        Assert.That(AgentProactiveBehaviorService.BuildPendingSuggestionConfirmationText(pending[0]),
            Does.Contain("agent_proactive_confirm"));
    }

    [Test]
    public void ProactiveBehaviorCanDismissAndConfirmPendingSuggestions()
    {
        AgentProactiveSuggestion suggestion = new(
            AgentProactiveActionKind.QZoneLike,
            "external qzone like suggestion",
            AgentAuditRiskLevel.High,
            RequiresOwnerConfirmation: true,
            TargetType: "qzone",
            TargetId: 1001,
            DraftText: "like target=1001 post=post-a");
        AgentProactiveBehaviorService service = new(
            controlCenter: new AgentControlCenterService(),
            suggestionProviders: [new StubSuggestionProvider(suggestion)],
            minimumCooldown: TimeSpan.Zero);

        service.BuildSuggestions(CreateSnapshot([
            new LifeEvent(
                DateTimeOffset.Parse("2026-06-14T12:00:00Z"),
                LifeEventKind.Communication,
                "QZone",
                "target=1001 post=post-a private contact posted.")
        ]));
        AgentProactivePendingSuggestion pending = service.GetPendingSuggestions().Single();

        AgentProactivePendingSuggestion confirmed = service.ConfirmPendingSuggestion(pending.Id, "owner");
        Assert.That(confirmed.Status, Is.EqualTo(AgentProactivePendingStatus.Confirmed));
        Assert.That(service.GetPendingSuggestions(), Is.Empty);

        service.BuildSuggestions(CreateSnapshot([
            new LifeEvent(
                DateTimeOffset.Parse("2026-06-14T12:10:00Z"),
                LifeEventKind.Communication,
                "QZone",
                "target=1001 post=post-b private contact posted.")
        ]));
        AgentProactivePendingSuggestion second = service.GetPendingSuggestions().Single();
        AgentProactivePendingSuggestion dismissed = service.DismissPendingSuggestion(second.Id, "owner");

        Assert.That(dismissed.Status, Is.EqualTo(AgentProactivePendingStatus.Dismissed));
        Assert.That(service.GetPendingSuggestions(), Is.Empty);
    }

    [Test]
    public void ProactiveBehaviorCanPrepareQZoneReplyContentBeforeConfirmation()
    {
        AgentProactiveBehaviorService service = new();
        AgentProactivePendingSuggestion pending = service.EnqueuePendingSuggestion(new AgentProactiveSuggestion(
            AgentProactiveActionKind.QZoneReply,
            "reply to qzone comment",
            AgentAuditRiskLevel.High,
            RequiresOwnerConfirmation: true,
            TargetType: "qzone",
            TargetId: 1001,
            DraftText: "reply target=1001 post=post-a comment=comment-a"));

        AgentProactivePendingSuggestion prepared = service.PrepareQZoneReplyContent(
            pending.Id,
            "谢谢分享，感觉很不错。",
            "agent");

        Assert.That(prepared.Status, Is.EqualTo(AgentProactivePendingStatus.Pending));
        Assert.That(prepared.Suggestion.DraftText, Is.EqualTo("reply target=1001 post=post-a comment=comment-a content=\"谢谢分享，感觉很不错。\""));
        Assert.That(service.GetPendingSuggestions().Single().Suggestion.DraftText, Does.Contain("content=\"谢谢分享"));
    }

    [Test]
    public void ProactiveBehaviorPersistsPendingSuggestions()
    {
        string storePath = Path.Combine(CreateTempWorkspace(), "proactive-suggestions.json");
        AgentProactiveBehaviorService writer = new(persistencePath: storePath);
        AgentProactivePendingSuggestion pending = writer.EnqueuePendingSuggestion(new AgentProactiveSuggestion(
            AgentProactiveActionKind.QZoneReply,
            "reply to qzone comment",
            AgentAuditRiskLevel.High,
            RequiresOwnerConfirmation: true,
            TargetType: "qzone",
            TargetId: 1001,
            DraftText: "reply target=1001 post=post-a comment=comment-a"));
        writer.PrepareQZoneReplyContent(pending.Id, "谢谢分享。", "agent");

        AgentProactiveBehaviorService reader = new(persistencePath: storePath);

        AgentProactivePendingSuggestion restored = reader.GetPendingSuggestions().Single();
        Assert.That(restored.Id, Is.EqualTo(pending.Id));
        Assert.That(restored.Status, Is.EqualTo(AgentProactivePendingStatus.Pending));
        Assert.That(restored.Suggestion.DraftText, Does.Contain("content=\"谢谢分享。\""));
    }

    [Test]
    public void ProactiveBehaviorPersistsCompletedAndExecutedSuggestions()
    {
        string storePath = Path.Combine(CreateTempWorkspace(), "proactive-suggestions.json");
        AgentProactiveBehaviorService writer = new(persistencePath: storePath);
        AgentProactivePendingSuggestion pending = writer.EnqueuePendingSuggestion(new AgentProactiveSuggestion(
            AgentProactiveActionKind.QZoneLike,
            "like qzone post",
            AgentAuditRiskLevel.High,
            RequiresOwnerConfirmation: true,
            TargetType: "qzone",
            TargetId: 1001,
            DraftText: "like target=1001 post=post-a"));
        writer.ConfirmPendingSuggestion(pending.Id, "owner");
        writer.MarkSuggestionExecuted(pending.Id, "agent", "dry-run");

        AgentProactiveBehaviorService reader = new(persistencePath: storePath);

        AgentProactivePendingSuggestion restored = reader.GetCompletedSuggestion(pending.Id)!;
        Assert.That(restored, Is.Not.Null);
        Assert.That(restored.Status, Is.EqualTo(AgentProactivePendingStatus.Executed));
        Assert.That(reader.GetPendingSuggestions(), Is.Empty);
    }

    [Test]
    public void ProactiveBehaviorCleanupExpiresStalePendingSuggestionsIntoDismissedHistory()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-14T12:00:00Z");
        AgentProactiveBehaviorService service = new(clock: () => now);
        AgentProactivePendingSuggestion pending = service.EnqueuePendingSuggestion(new AgentProactiveSuggestion(
            AgentProactiveActionKind.QZoneLike,
            "like qzone post",
            AgentAuditRiskLevel.High,
            RequiresOwnerConfirmation: true,
            TargetType: "qzone",
            TargetId: 1001,
            DraftText: "like target=1001 post=post-a"));
        now = now.AddHours(25);

        AgentProactiveCleanupResult result = service.CleanupSuggestions(
            maxPendingAge: TimeSpan.FromHours(24),
            maxCompletedAge: TimeSpan.FromDays(30),
            actor: "agent");

        Assert.That(result.ExpiredPendingCount, Is.EqualTo(1));
        Assert.That(result.RemovedCompletedCount, Is.EqualTo(0));
        Assert.That(service.GetPendingSuggestions(), Is.Empty);
        Assert.That(service.GetCompletedSuggestion(pending.Id)?.Status, Is.EqualTo(AgentProactivePendingStatus.Dismissed));
    }

    [Test]
    public void ProactiveBehaviorCleanupRemovesOldCompletedSuggestions()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-14T12:00:00Z");
        AgentProactiveBehaviorService service = new(clock: () => now);
        AgentProactivePendingSuggestion pending = service.EnqueuePendingSuggestion(new AgentProactiveSuggestion(
            AgentProactiveActionKind.QZoneLike,
            "like qzone post",
            AgentAuditRiskLevel.High,
            RequiresOwnerConfirmation: true,
            TargetType: "qzone",
            TargetId: 1001,
            DraftText: "like target=1001 post=post-a"));
        service.ConfirmPendingSuggestion(pending.Id, "owner");
        service.MarkSuggestionExecuted(pending.Id, "agent", "dry-run");
        now = now.AddDays(31);

        AgentProactiveCleanupResult result = service.CleanupSuggestions(
            maxPendingAge: TimeSpan.FromHours(24),
            maxCompletedAge: TimeSpan.FromDays(30),
            actor: "agent");

        Assert.That(result.ExpiredPendingCount, Is.EqualTo(0));
        Assert.That(result.RemovedCompletedCount, Is.EqualTo(1));
        Assert.That(service.GetCompletedSuggestion(pending.Id), Is.Null);
    }

    [Test]
    public void ProactiveBehaviorRejectsPreparingContentAfterConfirmation()
    {
        AgentProactiveBehaviorService service = new();
        AgentProactivePendingSuggestion pending = service.EnqueuePendingSuggestion(new AgentProactiveSuggestion(
            AgentProactiveActionKind.QZoneReply,
            "reply to qzone comment",
            AgentAuditRiskLevel.High,
            RequiresOwnerConfirmation: true,
            TargetType: "qzone",
            TargetId: 1001,
            DraftText: "reply target=1001 post=post-a comment=comment-a"));
        service.ConfirmPendingSuggestion(pending.Id, "owner");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.PrepareQZoneReplyContent(pending.Id, "late content", "agent"))!;

        Assert.That(exception.Message, Does.Contain("pending"));
    }

    [Test]
    public void ConfirmedQZoneSuggestionExecutionTextUsesExplicitQZoneCommand()
    {
        AgentProactivePendingSuggestion pending = new(
            "qzone\"<>&",
            new AgentProactiveSuggestion(
                AgentProactiveActionKind.QZoneLike,
                "like a private contact post",
                AgentAuditRiskLevel.High,
                RequiresOwnerConfirmation: true,
                TargetType: "qzone",
                DraftText: "like target=1001 post=post-a"),
            DateTimeOffset.Parse("2026-06-14T12:00:00Z"),
            AgentProactivePendingStatus.Confirmed,
            "test");

        string text = AgentProactiveBehaviorService.BuildConfirmedSuggestionExecutionText(pending);

        Assert.That(text, Is.EqualTo("confirm execute <qzone_proactive_execute id=\"qzone&quot;&lt;&gt;&amp;\" />"));
    }

    [Test]
    public void ConfirmedNonQZoneSuggestionExecutionTextExplainsNoExternalExecutor()
    {
        AgentProactivePendingSuggestion pending = new(
            "deskpet",
            new AgentProactiveSuggestion(
                AgentProactiveActionKind.DeskPetExpression,
                "show expression",
                AgentAuditRiskLevel.Low,
                RequiresOwnerConfirmation: false,
                TargetType: "deskpet"),
            DateTimeOffset.Parse("2026-06-14T12:00:00Z"),
            AgentProactivePendingStatus.Confirmed,
            "test");

        string text = AgentProactiveBehaviorService.BuildConfirmedSuggestionExecutionText(pending);

        Assert.That(text, Is.EqualTo("No external executor is registered for DeskPetExpression."));
    }

    [Test]
    public void ProactiveBehaviorExposesXmlTool()
    {
        string[] xmlFunctionNames = typeof(AgentProactiveBehaviorService)
            .GetMethods()
            .Select(method => method.GetCustomAttributes(typeof(XmlFunctionAttribute), inherit: false)
                .OfType<XmlFunctionAttribute>()
                .FirstOrDefault())
            .OfType<XmlFunctionAttribute>()
            .Select(attribute => attribute.Name ?? string.Empty)
            .ToArray();

        Assert.That(xmlFunctionNames, Does.Contain("agent_proactive_status"));
        Assert.That(xmlFunctionNames, Does.Contain("agent_proactive_cleanup"));
        Assert.That(xmlFunctionNames, Does.Contain("agent_proactive_prepare_qzone_reply"));
        Assert.That(xmlFunctionNames, Does.Contain("agent_proactive_confirm"));
        Assert.That(xmlFunctionNames, Does.Contain("agent_proactive_dismiss"));
    }

    static AgentSelfModelSnapshot CreateSnapshot(IReadOnlyList<LifeEvent>? recentExperiences = null)
    {
        AgentStateSnapshot runtime = new(
            "AstralFox",
            IsChatting: false,
            PendingPokeCount: 0,
            ChatHistoryCount: 12,
            LastError: null,
            RecentEvents: [],
            ModuleHealth: [new ModuleHealth("QChat", ModuleHealthStatus.Healthy, "ready")],
            Capabilities: [
                new AgentCapabilityInfo("QChat", EmbodiedCapabilityKind.Communication, "QQ channel.", "ready"),
                new AgentCapabilityInfo("DeskPet", EmbodiedCapabilityKind.Body, "Desktop pet expression.", "ready")
            ]);

        return new AgentSelfModelSnapshot(
            "AstralFox",
            DateTimeOffset.Parse("2026-06-14T12:00:00Z"),
            runtime,
            runtime.Capabilities,
            runtime.ModuleHealth,
            LatestTask: null,
            SafetyBoundaries: ["High-risk actions require owner confirmation."],
            RecentExperiences: recentExperiences ?? []);
    }

    static string CreateTempWorkspace()
    {
        string root = Path.Combine(Path.GetTempPath(), "alife-agent-proactive-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    sealed class StubSuggestionProvider(AgentProactiveSuggestion suggestion) : IAgentProactiveSuggestionProvider
    {
        public AgentProactiveSuggestionContext? LastContext { get; private set; }

        public IReadOnlyList<AgentProactiveSuggestion> BuildSuggestions(AgentProactiveSuggestionContext context)
        {
            LastContext = context;
            return [suggestion];
        }
    }
}
