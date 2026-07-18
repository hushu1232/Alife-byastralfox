using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Alife.Function.Agent;
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatSemanticWebResearchServiceTests
{
    [Test]
    public void MultiSourceSearch_DefaultsToDisabledSafeBuiltInsAndPluginDetection()
    {
        AgentMultiSourceSearchConfig config = new();

        Assert.Multiple(() =>
        {
            Assert.That(config.Enabled, Is.False);
            Assert.That(config.ParallelBuiltInProviders, Is.True);
            Assert.That(config.PerProviderTimeoutMilliseconds, Is.EqualTo(1500));
            Assert.That(config.MaxMergedResults, Is.EqualTo(5));
            Assert.That(config.FailureThreshold, Is.EqualTo(3));
            Assert.That(config.CircuitBreakSeconds, Is.EqualTo(60));
            Assert.That(config.DetectSmartWebSearchPlugin, Is.True);
        });
    }

    [Test]
    public void IsEligible_AllowsEnabledOwnerPrivateMessage()
    {
        QChatSemanticWebResearchConfig config = new() { Enabled = true };
        OneBotMessageEvent message = new();

        bool actual = QChatSemanticWebResearchEligibility.IsEligible(
            config,
            message,
            QChatSenderRole.Owner,
            isMentionedOrWoken: false);

        Assert.That(actual, Is.True);
    }

    [Test]
    public void IsEligible_AllowsEnabledMentionedGroupMessage()
    {
        QChatSemanticWebResearchConfig config = new() { Enabled = true };
        OneBotMessageEvent message = new() { GroupId = 1 };

        bool actual = QChatSemanticWebResearchEligibility.IsEligible(
            config,
            message,
            QChatSenderRole.GroupMember,
            isMentionedOrWoken: true);

        Assert.That(actual, Is.True);
    }

    [Test]
    public void IsEligible_DeniesUnmentionedGroupMessage()
    {
        QChatSemanticWebResearchConfig config = new() { Enabled = true };
        OneBotMessageEvent message = new() { GroupId = 1 };

        bool actual = QChatSemanticWebResearchEligibility.IsEligible(
            config,
            message,
            QChatSenderRole.GroupMember,
            isMentionedOrWoken: false);

        Assert.That(actual, Is.False);
    }

    [Test]
    public void IsEligible_DeniesPrivateGuest()
    {
        QChatSemanticWebResearchConfig config = new() { Enabled = true };
        OneBotMessageEvent message = new();

        bool actual = QChatSemanticWebResearchEligibility.IsEligible(
            config,
            message,
            QChatSenderRole.PrivateGuest,
            isMentionedOrWoken: false);

        Assert.That(actual, Is.False);
    }

    [Test]
    public void IsEligible_DeniesWhenFeatureIsDisabled()
    {
        QChatSemanticWebResearchConfig config = new() { Enabled = false };
        OneBotMessageEvent message = new();

        bool actual = QChatSemanticWebResearchEligibility.IsEligible(
            config,
            message,
            QChatSenderRole.Owner,
            isMentionedOrWoken: false);

        Assert.That(actual, Is.False);
    }

    [Test]
    public async Task ExecuteAsync_MentionedGroupUsesSearchOnlyEvidence()
    {
        RecordingResearchService research = new();
        QChatSemanticWebResearchService service = new(
            new FixedRouter(CreateDecision(QChatSemanticWebResearchDepth.Standard)),
            research);

        QChatSemanticWebResearchEvidence evidence = await service.ExecuteAsync(CreateMentionedGroupRequest());

        Assert.Multiple(() =>
        {
            Assert.That(research.LastRequest, Is.Not.Null);
            Assert.That(research.LastRequest!.ActorRole, Is.EqualTo(AgentWebAccessActorRole.GroupMember));
            Assert.That(research.LastRequest.Config.EnableAutoRead, Is.False);
            Assert.That(research.LastRequest.Config.EnablePublicFetch, Is.False);
            Assert.That(evidence.Researched, Is.True);
            Assert.That(evidence.ModelPrompt, Does.Contain("UNTRUSTED EXTERNAL CONTEXT"));
            Assert.That(evidence.ModelPrompt, Does.Contain("https://example.test/source"));
        });
    }

    [Test]
    public async Task ExecuteAsync_OwnerStandardEnablesPageResearch()
    {
        RecordingResearchService research = new();
        QChatSemanticWebResearchService service = new(
            new FixedRouter(CreateDecision(QChatSemanticWebResearchDepth.Standard)),
            research);

        await service.ExecuteAsync(CreateOwnerPrivateRequest());

        Assert.Multiple(() =>
        {
            Assert.That(research.LastRequest!.ActorRole, Is.EqualTo(AgentWebAccessActorRole.Owner));
            Assert.That(research.LastRequest.Config.EnableAutoRead, Is.True);
            Assert.That(research.LastRequest.Config.EnablePublicFetch, Is.True);
            Assert.That(research.LastRequest.Config.EnableBrowserSnapshot, Is.True);
        });
    }

    [Test]
    public async Task ExecuteAsync_ReusesSuccessfulSessionCache()
    {
        RecordingResearchService research = new();
        QChatSemanticWebResearchService service = new(
            new FixedRouter(CreateDecision(QChatSemanticWebResearchDepth.Quick)),
            research);
        QChatSemanticWebResearchRequest request = CreateOwnerPrivateRequest();

        await service.ExecuteAsync(request);
        await service.ExecuteAsync(request);

        Assert.That(research.CallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ExecuteAsync_DoesNotCacheFailedResearch()
    {
        RecordingResearchService research = new(success: false);
        QChatSemanticWebResearchService service = new(
            new FixedRouter(CreateDecision(QChatSemanticWebResearchDepth.Quick)),
            research);
        QChatSemanticWebResearchRequest request = CreateOwnerPrivateRequest();

        await service.ExecuteAsync(request);
        await service.ExecuteAsync(request);

        Assert.That(research.CallCount, Is.EqualTo(2));
    }

    static QChatSemanticWebResearchRequest CreateOwnerPrivateRequest() => new(
        "xiayu",
        new OneBotMessageEvent { UserId = 7 },
        QChatSenderRole.Owner,
        false,
        "What changed in .NET 9?",
        "",
        new QChatSemanticWebResearchConfig { Enabled = true, SessionCacheSeconds = 120 });

    static QChatSemanticWebResearchRequest CreateMentionedGroupRequest() => new(
        "mixu",
        new OneBotMessageEvent { GroupId = 8, UserId = 9 },
        QChatSenderRole.GroupMember,
        true,
        "What changed in .NET 9?",
        "",
        new QChatSemanticWebResearchConfig { Enabled = true, SessionCacheSeconds = 120 });

    static QChatSemanticWebResearchDecision CreateDecision(QChatSemanticWebResearchDepth depth) => new(
        true,
        false,
        "latest .NET 9 release notes",
        depth,
        3,
        QChatSemanticWebResearchReasonCategory.Temporal,
        "release state changes");

    sealed class FixedRouter(QChatSemanticWebResearchDecision decision) : IQChatSemanticWebResearchRouter
    {
        public Task<QChatSemanticWebResearchDecision> RouteAsync(
            QChatSemanticWebResearchRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(decision);
    }

    sealed class RecordingResearchService(bool success = true) : IAgentWebResearchService
    {
        public int CallCount { get; private set; }
        public AgentWebResearchRequest? LastRequest { get; private set; }

        public Task<AgentWebResearchResult> ResearchAsync(
            AgentWebResearchRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(new AgentWebResearchResult(
                success,
                success ? "ok" : "search_failed",
                request.Query,
                success ? "A current source was found." : "No reliable source was found.",
                success
                    ? new List<AgentWebResearchEvidence>
                    {
                        new("Official source", "https://example.test/source", "Current release details", "docs")
                    }
                    : []));
        }
    }
}
