using System;
using System.Threading;
using System.Threading.Tasks;
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatSemanticWebResearchRouterTests
{
    [Test]
    public async Task RouteAsync_ParsesValidatedResearchDecision()
    {
        StubModel model = new(
            """
            {"shouldResearch":true,"uncertain":false,"query":"latest .NET 9 release notes","depth":"standard","maxSources":3,"reasonCategory":"temporal","reason":"release status can change","turnComplete":false}
            """);
        QChatLlmSemanticWebResearchRouter router = new(model);

        QChatSemanticWebResearchDecision decision = await router.RouteAsync(CreateRequest());

        Assert.Multiple(() =>
        {
            Assert.That(decision.ShouldResearch, Is.True);
            Assert.That(decision.Uncertain, Is.False);
            Assert.That(decision.Query, Is.EqualTo("latest .NET 9 release notes"));
            Assert.That(decision.Depth, Is.EqualTo(QChatSemanticWebResearchDepth.Standard));
            Assert.That(decision.MaxSources, Is.EqualTo(3));
            Assert.That(decision.ReasonCategory, Is.EqualTo(QChatSemanticWebResearchReasonCategory.Temporal));
            Assert.That(decision.TurnComplete, Is.False);
            Assert.That(model.SystemPrompt, Does.Contain("appropriate to answer now"));
            Assert.That(model.SystemPrompt, Does.Contain("When genuinely uncertain, choose false"));
            Assert.That(model.SystemPrompt, Does.Contain("research: ...; turn: ..."));
            Assert.That(model.SystemPrompt, Does.Contain("site:<official-domain>"));
            Assert.That(model.SystemPrompt, Does.Contain("site:help.openai.com ChatGPT release notes"));
            Assert.That(model.SystemPrompt, Does.Contain("standard or deep"));
        });
    }

    [Test]
    public async Task RouteAsync_InvalidJsonUsesConfiguredQuickFallback()
    {
        QChatSemanticWebResearchRequest request = CreateRequest(new QChatSemanticWebResearchConfig
        {
            ResearchOnUncertainty = true,
            QuickMaxSources = 2
        });
        QChatLlmSemanticWebResearchRouter router = new(new StubModel("not-json"));

        QChatSemanticWebResearchDecision decision = await router.RouteAsync(request);

        Assert.Multiple(() =>
        {
            Assert.That(decision.ShouldResearch, Is.True);
            Assert.That(decision.Uncertain, Is.True);
            Assert.That(decision.Query, Is.EqualTo(request.Question));
            Assert.That(decision.Depth, Is.EqualTo(QChatSemanticWebResearchDepth.Quick));
            Assert.That(decision.MaxSources, Is.EqualTo(2));
            Assert.That(decision.Reason, Is.EqualTo("router_invalid_response"));
            Assert.That(decision.TurnComplete, Is.Null);
        });
    }

    [Test]
    public async Task RouteAsync_ModelTimeoutUsesQuickFallback()
    {
        QChatSemanticWebResearchRequest request = CreateRequest(new QChatSemanticWebResearchConfig
        {
            ResearchOnUncertainty = true,
            RouterTimeoutMilliseconds = 100
        });
        QChatLlmSemanticWebResearchRouter router = new(new DelayedModel());

        QChatSemanticWebResearchDecision decision = await router.RouteAsync(request);

        Assert.Multiple(() =>
        {
            Assert.That(decision.ShouldResearch, Is.True);
            Assert.That(decision.Uncertain, Is.True);
            Assert.That(decision.Depth, Is.EqualTo(QChatSemanticWebResearchDepth.Quick));
            Assert.That(decision.Reason, Is.EqualTo("router_timeout"));
        });
    }

    [Test]
    public void RouteAsync_CallerCancellationIsRethrown()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        QChatLlmSemanticWebResearchRouter router = new(new DelayedModel());

        Assert.That(
            async () => await router.RouteAsync(CreateRequest(), cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    static QChatSemanticWebResearchRequest CreateRequest(QChatSemanticWebResearchConfig? config = null) => new(
        "xiayu",
        new OneBotMessageEvent { UserId = 1 },
        QChatSenderRole.Owner,
        false,
        "What changed in the latest .NET 9 release?",
        "",
        config ?? new QChatSemanticWebResearchConfig());

    sealed class StubModel(string response) : IQChatSemanticWebResearchModel
    {
        public string SystemPrompt { get; private set; } = "";

        public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
        {
            SystemPrompt = systemPrompt;
            return Task.FromResult(response);
        }
    }

    sealed class DelayedModel : IQChatSemanticWebResearchModel
    {
        public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ContinueWith(
                _ => "",
                cancellationToken,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
    }
}
