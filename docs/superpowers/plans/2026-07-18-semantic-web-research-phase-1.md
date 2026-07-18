# Semantic Web Research Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make XiaYu and Mixu automatically research semantically appropriate Owner-private and mentioned-group questions without involving DataAgent, then answer with verified evidence and natural in-character research feedback.

**Architecture:** Add a small QChat-owned semantic router that asks the existing Semantic Kernel chat-completion service for a schema-bound research decision. A separate research executor maps that decision to the existing `AgentPublicSearchService` and `AgentWebResearchService`; its evidence is injected into the normal model input. A delayed narrator uses the same chat-completion service only when research is slow enough to require a natural, in-character acknowledgement.

**Tech Stack:** .NET 9, C#, NUnit, Microsoft Semantic Kernel `IChatCompletionService`, QChat/OneBot, existing Agent web research services.

---

## Scope and delivery boundary

This plan implements one useful vertical slice only:

- semantic routing for Owner-private and mentioned-group messages;
- existing DuckDuckGo/Bing search and webpage-research services, cache and cancellation;
- delayed natural feedback and source-constrained evidence injection.

Tavily/Baidu Qianfan providers, image recognition, scheduled research and proactive push notifications are separate projects after this slice proves latency and real-chat behavior.

## File map

| File | Responsibility |
|---|---|
| `sources/Alife.Function/Alife.Function.QChat/QChatSemanticWebResearchModels.cs` | Config, eligibility, request, decision and evidence records; no HTTP/model calls. |
| `sources/Alife.Function/Alife.Function.QChat/QChatSemanticWebResearchRouter.cs` | Semantic Kernel adapter, JSON-schema prompt, parsing, validation, timeout fallback. |
| `sources/Alife.Function/Alife.Function.QChat/QChatSemanticWebResearchService.cs` | Existing-service execution, cache and untrusted evidence prompt. |
| `sources/Alife.Function/Alife.Function.QChat/QChatSemanticWebResearchNarrator.cs` | One-sentence natural research-started narrator. |
| `sources/Alife.Function/Alife.Function.QChat/QChatService.cs` | Config, Kernel wiring, message-pipeline invocation, delayed feedback, model-input injection. |
| `Tests/Alife.Test.QChat/QChatSemanticWebResearchRouterTests.cs` | Router parser, timeout and uncertainty tests. |
| `Tests/Alife.Test.QChat/QChatSemanticWebResearchServiceTests.cs` | Eligibility, mapping, evidence and cache tests. |
| `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs` | Mentioned-group/Owner integration, no duplicate command research and feedback tests. |
| `docs/semantic-web-research.md` | Operator configuration and behavior guide. |

## Shared contract

Every task uses these names exactly:

```csharp
public enum QChatSemanticWebResearchDepth { Quick, Standard, Deep }

public enum QChatSemanticWebResearchReasonCategory
{
    Temporal, Verification, Niche, Explicit, Stable, Creative, Companion, Unknown
}

public sealed class QChatSemanticWebResearchConfig
{
    public bool Enabled { get; set; }
    public bool EnableOwnerPrivate { get; set; } = true;
    public bool EnableMentionedGroup { get; set; } = true;
    public bool ResearchOnUncertainty { get; set; } = true;
    public int RouterTimeoutMilliseconds { get; set; } = 900;
    public int FeedbackDelayMilliseconds { get; set; } = 1200;
    public int QuickMaxSources { get; set; } = 3;
    public int StandardMaxSources { get; set; } = 3;
    public int DeepMaxSources { get; set; } = 5;
    public int SessionCacheSeconds { get; set; } = 120;
}

public sealed record QChatSemanticWebResearchRequest(
    string AgentId, OneBotMessageEvent MessageEvent, QChatSenderRole SenderRole,
    bool IsMentionedOrWoken, string Question, string RecentContext,
    QChatSemanticWebResearchConfig Config);

public sealed record QChatSemanticWebResearchDecision(
    bool ShouldResearch, bool Uncertain, string Query,
    QChatSemanticWebResearchDepth Depth, int MaxSources,
    QChatSemanticWebResearchReasonCategory ReasonCategory, string Reason);
```

Add one property near the current Internet settings in `QChatConfig`:

```csharp
public QChatSemanticWebResearchConfig SemanticWebResearch { get; set; } = new();
```

### Task 1: Add contracts and the non-semantic eligibility boundary

**Files:**
- Create: `sources/Alife.Function/Alife.Function.QChat/QChatSemanticWebResearchModels.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs:28-128`
- Create: `Tests/Alife.Test.QChat/QChatSemanticWebResearchServiceTests.cs`

- [ ] **Step 1: Write failing eligibility tests**

```csharp
[Test]
public void IsEligible_AllowsOwnerPrivateAndMentionedGroupOnly()
{
    QChatSemanticWebResearchConfig config = new() { Enabled = true };
    OneBotMessageEvent privateMessage = new() { MessageType = OneBotMessageType.Private };
    OneBotMessageEvent groupMessage = new() { MessageType = OneBotMessageType.Group };

    Assert.Multiple(() =>
    {
        Assert.That(QChatSemanticWebResearchEligibility.IsEligible(
            config, privateMessage, QChatSenderRole.Owner, false), Is.True);
        Assert.That(QChatSemanticWebResearchEligibility.IsEligible(
            config, groupMessage, QChatSenderRole.GroupMember, true), Is.True);
        Assert.That(QChatSemanticWebResearchEligibility.IsEligible(
            config, groupMessage, QChatSenderRole.GroupMember, false), Is.False);
        Assert.That(QChatSemanticWebResearchEligibility.IsEligible(
            config, privateMessage, QChatSenderRole.PrivateGuest, false), Is.False);
    });
}
```

- [ ] **Step 2: Run the test and verify it fails**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatSemanticWebResearchServiceTests" -v:minimal
```

Expected: compilation fails because semantic-web-research types do not exist.

- [ ] **Step 3: Implement models and eligibility**

Implement the shared contract and:

```csharp
public static class QChatSemanticWebResearchEligibility
{
    public static bool IsEligible(
        QChatSemanticWebResearchConfig config,
        OneBotMessageEvent messageEvent,
        QChatSenderRole senderRole,
        bool isMentionedOrWoken)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(messageEvent);

        if (!config.Enabled)
            return false;

        return messageEvent.MessageType switch
        {
            OneBotMessageType.Private => senderRole == QChatSenderRole.Owner && config.EnableOwnerPrivate,
            OneBotMessageType.Group => config.EnableMentionedGroup && isMentionedOrWoken,
            _ => false
        };
    }
}
```

- [ ] **Step 4: Run the test and verify it passes**

Run the command from Step 2.

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add sources/Alife.Function/Alife.Function.QChat/QChatSemanticWebResearchModels.cs sources/Alife.Function/Alife.Function.QChat/QChatService.cs Tests/Alife.Test.QChat/QChatSemanticWebResearchServiceTests.cs
git commit -m "feat(qchat): add semantic web research contracts"
```

### Task 2: Build a schema-bound semantic router with quick-search fallback

**Files:**
- Create: `sources/Alife.Function/Alife.Function.QChat/QChatSemanticWebResearchRouter.cs`
- Create: `Tests/Alife.Test.QChat/QChatSemanticWebResearchRouterTests.cs`

- [ ] **Step 1: Write failing parser and fallback tests**

```csharp
[Test]
public async Task RouteAsync_ParsesValidatedDecision()
{
    IQChatSemanticWebResearchModel model = new StubModel(
        "{\"shouldResearch\":true,\"uncertain\":false,\"query\":\"latest .NET 9 release notes\",\"depth\":\"standard\",\"maxSources\":3,\"reasonCategory\":\"temporal\",\"reason\":\"release status changes\"}");
    QChatLlmSemanticWebResearchRouter router = new(model);

    QChatSemanticWebResearchDecision actual = await router.RouteAsync(CreateRequest());

    Assert.Multiple(() =>
    {
        Assert.That(actual.ShouldResearch, Is.True);
        Assert.That(actual.Query, Is.EqualTo("latest .NET 9 release notes"));
        Assert.That(actual.Depth, Is.EqualTo(QChatSemanticWebResearchDepth.Standard));
        Assert.That(actual.MaxSources, Is.EqualTo(3));
    });
}

[Test]
public async Task RouteAsync_InvalidJsonUsesConfiguredQuickFallback()
{
    QChatSemanticWebResearchRequest request = CreateRequest() with
    {
        Config = new QChatSemanticWebResearchConfig { ResearchOnUncertainty = true }
    };

    QChatSemanticWebResearchDecision actual =
        await new QChatLlmSemanticWebResearchRouter(new StubModel("not-json")).RouteAsync(request);

    Assert.That(actual.ShouldResearch, Is.True);
    Assert.That(actual.Uncertain, Is.True);
    Assert.That(actual.Depth, Is.EqualTo(QChatSemanticWebResearchDepth.Quick));
    Assert.That(actual.Query, Is.EqualTo(request.Question));
}
```

- [ ] **Step 2: Run router tests and verify failure**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatSemanticWebResearchRouterTests" -v:minimal
```

Expected: compilation fails because router/model types are missing.

- [ ] **Step 3: Implement adapter, prompt and strict JSON validation**

Define:

```csharp
public interface IQChatSemanticWebResearchModel
{
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);
}

public interface IQChatSemanticWebResearchRouter
{
    Task<QChatSemanticWebResearchDecision> RouteAsync(
        QChatSemanticWebResearchRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class QChatSemanticKernelWebResearchModel(IChatCompletionService chatCompletionService)
    : IQChatSemanticWebResearchModel
{
    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        ChatHistory history = [];
        history.AddSystemMessage(systemPrompt);
        history.AddUserMessage(userPrompt);
        ChatMessageContent response = await chatCompletionService.GetChatMessageContentAsync(
            history, cancellationToken: cancellationToken);
        return response.Content ?? string.Empty;
    }
}
```

Implement `QChatLlmSemanticWebResearchRouter` with a JSON-only system prompt that decides research semantically, not by a keyword list. Parse one object using `JsonDocument`. Reject non-objects, unknown depth/category, empty research query, query longer than 160 characters and `maxSources` outside 1..5. Link the caller token with a timeout of `Math.Max(100, RouterTimeoutMilliseconds)`. Non-caller cancellation, malformed output and model errors return a Quick decision using the original question only when `ResearchOnUncertainty` is true; caller cancellation must be rethrown.

- [ ] **Step 4: Add a timeout test and run the suite**

Use a fake model that awaits `Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)`; configure a 100 ms timeout and assert Quick fallback. Run the command from Step 2.

Expected: PASS. No router source or test references DataAgent.

- [ ] **Step 5: Commit**

```powershell
git add sources/Alife.Function/Alife.Function.QChat/QChatSemanticWebResearchRouter.cs Tests/Alife.Test.QChat/QChatSemanticWebResearchRouterTests.cs
git commit -m "feat(qchat): route web research semantically"
```

### Task 3: Execute decisions with existing research services and short-term cache

**Files:**
- Create: `sources/Alife.Function/Alife.Function.QChat/QChatSemanticWebResearchService.cs`
- Modify: `sources/Alife.Function/Alife.Function.MessageFilter/AgentWebResearchService.cs`
- Modify: `Tests/Alife.Test.QChat/QChatSemanticWebResearchServiceTests.cs`

- [ ] **Step 1: Write failing execution tests**

```csharp
[Test]
public async Task ExecuteAsync_MentionedGroupUsesSearchOnlyEvidence()
{
    RecordingResearchService research = new();
    QChatSemanticWebResearchService service = CreateService(
        new FixedRouter(ResearchDecision(QChatSemanticWebResearchDepth.Standard)), research);

    QChatSemanticWebResearchEvidence evidence = await service.ExecuteAsync(CreateMentionedGroupRequest());

    Assert.Multiple(() =>
    {
        Assert.That(research.LastRequest!.ActorRole, Is.EqualTo(AgentWebAccessActorRole.GroupMember));
        Assert.That(research.LastRequest.Config.EnableAutoRead, Is.False);
        Assert.That(evidence.Researched, Is.True);
        Assert.That(evidence.ModelPrompt, Does.Contain("UNTRUSTED EXTERNAL CONTEXT"));
    });
}

[Test]
public async Task ExecuteAsync_ReusesSuccessfulSessionCache()
{
    RecordingResearchService research = new();
    QChatSemanticWebResearchService service = CreateService(
        new FixedRouter(ResearchDecision(QChatSemanticWebResearchDepth.Quick)), research);

    await service.ExecuteAsync(CreateOwnerPrivateRequest());
    await service.ExecuteAsync(CreateOwnerPrivateRequest());

    Assert.That(research.CallCount, Is.EqualTo(1));
}
```

- [ ] **Step 2: Run execution tests and verify failure**

Run the Task 1 command filtered to `QChatSemanticWebResearchServiceTests`.

Expected: compilation fails because executor/evidence/cache types are absent.

- [ ] **Step 3: Implement research executor and cache**

Add:

```csharp
public sealed record QChatSemanticWebResearchEvidence(
    bool Researched,
    QChatSemanticWebResearchDecision Decision,
    AgentWebResearchResult? Result,
    string ModelPrompt);

public interface IQChatWebResearchService
{
    Task<AgentWebResearchResult> ResearchAsync(
        AgentWebResearchRequest request,
        CancellationToken cancellationToken = default);
}
```

Make `AgentWebResearchService` implement `IQChatWebResearchService`. The executor must:

1. skip ineligible requests and `ShouldResearch == false` decisions;
2. use `Owner` for Owner private messages and `GroupMember` for mentioned groups;
3. make Quick search-only for every role; make Standard/Deep search-only for groups and permit existing auto-read/public-fetch/browser snapshot only for Owner;
4. cache successful immutable evidence by agent ID, message type, conversation target, normalized query, depth and source count for `SessionCacheSeconds`; remove expired entries before execution and never cache failures;
5. wrap result text with `ExternalContextFormatter.WrapUntrusted("semantic-web-research", ...)` and place only result-provided title/URL pairs in `ModelPrompt`.

- [ ] **Step 4: Run focused tests**

Run the Task 1 command. Add and pass a failure-not-cached test.

Expected: PASS for mapping, cache, formatting and failed-result behavior.

- [ ] **Step 5: Commit**

```powershell
git add sources/Alife.Function/Alife.Function.QChat/QChatSemanticWebResearchService.cs sources/Alife.Function/Alife.Function.MessageFilter/AgentWebResearchService.cs Tests/Alife.Test.QChat/QChatSemanticWebResearchServiceTests.cs
git commit -m "feat(qchat): execute semantic web research"
```

### Task 4: Wire evidence into QChat before normal model dispatch

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs:294-370,2612-2780,3100-3270,3940-4010,5507-5825`
- Modify: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`

- [ ] **Step 1: Write failing adapter tests**

```csharp
[Test]
public async Task MentionedGroupQuestion_InjectsSemanticResearchEvidenceBeforeModelDispatch()
{
    RecordingSemanticResearchService research = new(EvidenceFor("official release", "https://example.test/release"));
    QChatService service = CreateService(
        config: ConfigWithSemanticResearch(enabled: true),
        semanticResearchService: research);

    await DispatchMentionedGroupMessageAsync(service, "@夏羽 .NET 9 现在有什么变化？");

    Assert.Multiple(() =>
    {
        Assert.That(research.CallCount, Is.EqualTo(1));
        Assert.That(RecordedModelInput(service), Does.Contain("https://example.test/release"));
        Assert.That(RecordedModelInput(service), Does.Contain("UNTRUSTED EXTERNAL CONTEXT"));
    });
}

[Test]
public async Task UnmentionedGroupQuestion_DoesNotInvokeSemanticResearch()
{
    RecordingSemanticResearchService research = new(EvidenceFor("unused", "https://example.test"));
    QChatService service = CreateService(
        config: ConfigWithSemanticResearch(enabled: true),
        semanticResearchService: research);

    await DispatchGroupMessageAsync(service, "今天有什么新消息？");

    Assert.That(research.CallCount, Is.Zero);
}
```

- [ ] **Step 2: Run adapter tests and verify failure**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~SemanticResearch" -v:minimal
```

Expected: compilation failure because QChatService has no injected semantic executor or research prompt block.

- [ ] **Step 3: Implement QChat wiring**

1. Add optional `IQChatSemanticWebResearchRouter?` and `IQChatWebResearchService?` constructor injections after browser dependencies; retain injected/resolved fields like the existing search-provider fields.
2. Add `ConfigureSemanticWebResearchFromKernel(Kernel kernel)` beside `ConfigureProfileLearningFromKernel`. When enabled without an injected router, obtain `IChatCompletionService` and construct `QChatLlmSemanticWebResearchRouter(new QChatSemanticKernelWebResearchModel(...))`.
3. Invoke that configurator immediately after `ConfigureProfileLearningFromKernel(kernel)` in `StartAsync`.
4. Extract the existing `TryHandlePublicInternetCommandAsync` construction into a private factory returning `IQChatWebResearchService` backed by `ResolvePublicSearchService(config)`, `AgentWebAccessService`, `injectedInternetService`, `injectedBrowserProvider` and `BrowserSiteExperienceStore`. Do not create or reference a DataAgent object.
5. In the `OneBotMessageEvent` path, after `isMentionedOrWoken` is known and before `BuildFormattedModelInput`, build the request with `recentEventMemory.BuildRecentContextBlock(... limit: 6, maxCharacters: 1200 ...)` and await the executor when eligibility succeeds.
6. Add nullable `researchEvidencePrompt` to `BuildFormattedModelInput` and insert it after `selfStateBlock` and before `imageBlock`. Explicit public-internet commands already return earlier and must not be researched twice.

- [ ] **Step 4: Run affected regression tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~SemanticResearch|FullyQualifiedName~PublicSearch|FullyQualifiedName~WebResearch" -v:minimal
```

Expected: PASS; explicit `/search` behavior stays unchanged and no test invokes DataAgent.

- [ ] **Step 5: Commit**

```powershell
git add sources/Alife.Function/Alife.Function.QChat/QChatService.cs Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs
git commit -m "feat(qchat): inject semantic web evidence into replies"
```

### Task 5: Send delayed natural research feedback, never canned text

**Files:**
- Create: `sources/Alife.Function/Alife.Function.QChat/QChatSemanticWebResearchNarrator.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs:3100-3270`
- Modify: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`

- [ ] **Step 1: Write failing feedback tests**

```csharp
[Test]
public async Task SlowResearch_SendsNarratedFeedbackBeforeFinalDispatch()
{
    DelayedSemanticResearchService research = new(TimeSpan.FromSeconds(5), EvidenceFor("source", "https://example.test"));
    QChatService service = CreateService(
        config: ConfigWithSemanticResearch(enabled: true, feedbackDelayMilliseconds: 20),
        semanticResearchService: research,
        semanticResearchNarrator: new StubNarrator("我去把这件事核实清楚。"));

    await DispatchOwnerPrivateMessageAsync(service, "今天这个项目有什么新进展？");

    Assert.That(SentPrivateMessages(service), Does.Contain("我去把这件事核实清楚。"));
}

[Test]
public async Task FastResearch_DoesNotSendIntermediateFeedback()
{
    QChatService service = CreateService(
        config: ConfigWithSemanticResearch(enabled: true, feedbackDelayMilliseconds: 1000),
        semanticResearchService: new ImmediateSemanticResearchService(EvidenceFor("source", "https://example.test")),
        semanticResearchNarrator: new StubNarrator("不会发送"));

    await DispatchOwnerPrivateMessageAsync(service, "请核验这个信息");

    Assert.That(SentPrivateMessages(service), Does.Not.Contain("不会发送"));
}
```

- [ ] **Step 2: Run feedback tests and verify failure**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~SemanticResearch.*Feedback" -v:minimal
```

Expected: compilation failure because narrator and delayed feedback race do not exist.

- [ ] **Step 3: Implement narrator and race**

Define:

```csharp
public interface IQChatSemanticWebResearchNarrator
{
    Task<string?> CreateStartedAsync(
        string agentId, QChatSenderRole senderRole, OneBotMessageType messageType,
        string question, CancellationToken cancellationToken = default);
}
```

Implement `QChatSemanticKernelWebResearchNarrator` with `IChatCompletionService`. Its system prompt must ask for one short natural Chinese sentence, keep role/persona context, prohibit invented facts/sources/progress and return only the sentence.

In QChatService, race the research task against `Task.Delay(FeedbackDelayMilliseconds, cancellationToken)`. If research wins, dispatch evidence immediately with no intermediate reply. If delay wins, call the narrator with a linked 800 ms timeout, trim to 80 characters, and use existing `SendCommandReplyAsync` for the active private user/group target. Await the original research task afterward. Do not send feedback on cancellation, narrator failure or empty narrator output.

- [ ] **Step 4: Add cancellation test and run feedback tests**

Add a cancellation test that cancels before the delay and asserts the OneBot fake sent no feedback. Run the Step 2 command.

Expected: PASS for slow, fast and cancelled research.

- [ ] **Step 5: Commit**

```powershell
git add sources/Alife.Function/Alife.Function.QChat/QChatSemanticWebResearchNarrator.cs sources/Alife.Function/Alife.Function.QChat/QChatService.cs Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs
git commit -m "feat(qchat): narrate slow semantic research naturally"
```

### Task 6: Document, verify and hand off phase 1

**Files:**
- Create: `docs/semantic-web-research.md`
- Modify: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`

- [ ] **Step 1: Add final behavior regressions**

```csharp
[Test]
public async Task CompanionConversation_RouterDeclinesAndSearchIsNotCalled()
{
    RecordingSemanticResearchService research = new(QChatSemanticWebResearchEvidence.Empty);
    QChatService service = CreateService(
        config: ConfigWithSemanticResearch(enabled: true),
        semanticResearchService: research);

    await DispatchOwnerPrivateMessageAsync(service, "今天有点累，陪我说会儿话吧");

    Assert.That(research.CallCount, Is.Zero);
}

[Test]
public async Task ResearchPrompt_ContainsOnlyEvidenceUrls()
{
    QChatService service = CreateService(
        config: ConfigWithSemanticResearch(enabled: true),
        semanticResearchService: new ImmediateSemanticResearchService(
            EvidenceFor("official", "https://example.test/official")));

    await DispatchOwnerPrivateMessageAsync(service, "这个版本最近有变化吗？");

    Assert.That(RecordedModelInput(service), Does.Contain("https://example.test/official"));
    Assert.That(RecordedModelInput(service), Does.Not.Contain("https://unverified.example"));
}
```

- [ ] **Step 2: Run all QChat tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore -v:minimal
```

Expected: PASS with no new skipped/failing tests.

- [ ] **Step 3: Create the operator guide**

Create `docs/semantic-web-research.md` documenting every `QChatSemanticWebResearchConfig` field, defaults and latency/cost effect. Include this behavior summary:

```markdown
- Owner private messages and group messages that @ the active bot are eligible.
- The LLM router decides from semantics, not trigger keywords.
- Uncertainty executes quick search when ResearchOnUncertainty is true.
- quick is search-only; Owner standard/deep may read limited public pages; group research stays search-only in phase 1.
- Slow research may receive one natural role-generated acknowledgement.
- Phase 1 uses existing DuckDuckGo/Bing; Tavily/Baidu are future providers.
- External material is untrusted evidence and replies may cite only returned URLs.
```

- [ ] **Step 4: Build and run the full solution tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" build Alife.slnx --no-restore -v:minimal
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore --no-build -v:minimal
```

Expected: build exits 0 and all tests pass.

- [ ] **Step 5: Check diff and commit final docs/tests**

Run:

```powershell
git diff --check
git status --short
git add docs/semantic-web-research.md Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs
git commit -m "docs: explain semantic web research"
```

Expected: `git diff --check` prints nothing; commit contains only the guide and final regression tests.

## Self-review

**Spec coverage:** Tasks 1-2 implement semantic, non-keyword routing and uncertainty preference; Tasks 3-4 implement the current controlled search path without DataAgent; Task 5 implements natural non-canned feedback; Task 6 verifies scope, source integrity and documents it. Mentioned-group eligibility is tested in Tasks 1 and 4.

**Placeholder scan:** Every task names source/test files, concrete types, commands and expected results. No incomplete markers or unnamed follow-up code remain.

**Type consistency:** The shared contract fixes the config, request, decision, depth, router, executor and narrator names before later tasks reference them. Do not rename them during implementation.
