# QChat Dual-Provider Vision Routing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make QQ image observation automatic without `@`, use Agnes for normal images, and route high-complexity or retryable Agnes failures to Grok without changing chat or tool authority.

**Architecture:** Preserve the existing image parser and safety formatter, then introduce a provider catalog, deterministic route planner, and per-Bot bounded execution coordinator. The planner selects one primary provider and at most one fallback; the coordinator serializes work and enforces deadline, priority, de-duplication, and circuit-breaker policy. All provider output remains untrusted observation context.

**Tech Stack:** .NET 9, C#, NUnit, `HttpClient`, `System.Text.Json`, QChat / OneBot.

---

## File structure

| File | Responsibility |
|---|---|
| `sources/Alife.Function/Alife.Function.QChat/QChatVisionProviderCatalog.cs` | Local-configurable provider settings and redacted readiness lookup. |
| `sources/Alife.Function/Alife.Function.QChat/QChatGrokImageRecognitionClient.cs` | Grok OpenAI-compatible vision request client and key resolver. |
| `sources/Alife.Function/Alife.Function.QChat/QChatVisionRoutePlanner.cs` | Deterministic primary / fallback selection and retryability rules. |
| `sources/Alife.Function/Alife.Function.QChat/QChatVisionExecutionCoordinator.cs` | Per-Bot bounded queue, owner priority, TTL de-duplication, deadline and circuit state. |
| `sources/Alife.Function/Alife.Function.QChat/QChatVisionProfile.cs` | Primary, fallback and complex-request provider fields, with old Agnes fields retained for compatibility. |
| `sources/Alife.Function/Alife.Function.QChat/QChatImageRecognitionService.cs` | Execute a supplied route plan and format only safe observation context. |
| `sources/Alife.Function/Alife.Function.QChat/QChatService.cs` | Construct provider registry, invoke coordinator, and enable automatic passive-group observation without bypassing reply rules. |
| `sources/Alife.Function/Alife.Function.QChat/QChatVisionReadiness.cs` | Per-profile redacted readiness status for primary and fallback providers. |
| `Tests/Alife.Test.QChat/*Vision*.cs` | Deterministic provider, planner, coordinator, automatic-trigger, isolation and redaction coverage. |

### Task 1: Extend vision profile and provider settings

**Files:**
- Create: `sources/Alife.Function/Alife.Function.QChat/QChatVisionProviderCatalog.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatVisionProfile.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
- Test: `Tests/Alife.Test.QChat/QChatVisionProfileRouterTests.cs`

- [ ] **Step 1: Write failing route-profile tests**

```csharp
[Test]
public void Resolve_DefaultMixuProfileUsesAgnesThenGrok()
{
    QChatVisionProfileDecision decision = QChatVisionProfileRouter.Resolve(
        QChatVisionProfileConfig.CreateDefault(), "mixu", 3340947887);

    Assert.Multiple(() =>
    {
        Assert.That(decision.Kind, Is.EqualTo(QChatVisionProfileDecisionKind.Allow));
        Assert.That(decision.Profile!.PrimaryProvider, Is.EqualTo("agnes"));
        Assert.That(decision.Profile.FallbackProvider, Is.EqualTo("grok"));
        Assert.That(decision.Profile.ComplexRequestProvider, Is.EqualTo("grok"));
    });
}
```

- [ ] **Step 2: Verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "FullyQualifiedName~QChatVisionProfileRouterTests" --no-restore -v:minimal
```

Expected: compile failure because the three provider route fields do not exist.

- [ ] **Step 3: Add backwards-compatible provider configuration**

```csharp
public sealed class QChatVisionProviderSettings
{
    public string ProviderId { get; set; } = "";
    public string Model { get; set; } = "";
    public string ApiEndpoint { get; set; } = "";
    public string ApiKeyEnvironmentVariable { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public sealed class QChatVisionProviderCatalog
{
    public List<QChatVisionProviderSettings> Providers { get; set; } = [];
    public QChatVisionProviderSettings? Find(string? providerId) =>
        Providers.FirstOrDefault(item => string.Equals(item.ProviderId, providerId?.Trim(), StringComparison.OrdinalIgnoreCase));
}

public sealed class QChatVisionProfile
{
    public string PrimaryProvider { get; set; } = "agnes";
    public string FallbackProvider { get; set; } = "grok";
    public string ComplexRequestProvider { get; set; } = "grok";
    // Retain Provider as a deserialization compatibility alias; new routing uses PrimaryProvider.
}
```

Add `VisionProviderCatalog` to `QChatConfig`. Seed default Profiles for XiaYu and Mixu with `agnes` primary and `grok` fallback / complex provider. Do not put any key value into the default catalog, fixtures, source, or documentation.

- [ ] **Step 4: Verify GREEN**

Run the Step 2 command. Expected: all router tests pass.

- [ ] **Step 5: Commit**

```powershell
git add sources/Alife.Function/Alife.Function.QChat/QChatVisionProviderCatalog.cs sources/Alife.Function/Alife.Function.QChat/QChatVisionProfile.cs sources/Alife.Function/Alife.Function.QChat/QChatService.cs Tests/Alife.Test.QChat/QChatVisionProfileRouterTests.cs
git commit -m "feat(qchat): configure primary and fallback vision providers"
```

### Task 2: Add Grok OpenAI-compatible image client

**Files:**
- Create: `sources/Alife.Function/Alife.Function.QChat/QChatGrokImageRecognitionClient.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatImageRecognitionModels.cs`
- Test: `Tests/Alife.Test.QChat/QChatGrokImageRecognitionClientTests.cs`

- [ ] **Step 1: Write failing Grok request and redaction tests**

```csharp
[Test]
public async Task SendsOpenAiCompatibleImageUrlRequestWithoutLeakingKeyOnFailure()
{
    RecordingHandler handler = new("provider details", HttpStatusCode.BadGateway);
    QChatGrokImageRecognitionClient client = new(new HttpClient(handler), () => "test-key", "https://vision.example.invalid/v1/chat/completions");

    QChatImageRecognitionProviderResult result = await client.AnalyzeAsync(new(
        "https://example.invalid/image.jpg", "Read the screenshot.", "grok-4.5", 120));

    Assert.Multiple(() =>
    {
        Assert.That(handler.Authorization, Is.EqualTo("Bearer test-key"));
        Assert.That(handler.RequestBody, Does.Contain("\"image_url\""));
        Assert.That(result.FailureKind, Is.EqualTo(QChatImageRecognitionFailureKind.HttpError));
        Assert.That(result.FailureReason, Is.EqualTo("http_502"));
        Assert.That(result.FailureReason, Does.Not.Contain("test-key"));
    });
}
```

- [ ] **Step 2: Verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "FullyQualifiedName~QChatGrokImageRecognitionClientTests" --no-restore -v:minimal
```

Expected: compile failure because `QChatGrokImageRecognitionClient` is undefined.

- [ ] **Step 3: Implement the client and environment-only resolver**

Implement `IQChatImageRecognitionClient` using the existing Agnes payload shape: system observation guard, user text part, `image_url` part, non-streaming response, parsed content and safe failure codes. Add:

```csharp
public static class QChatGrokVisionApiKeyResolver
{
    public const string EnvironmentVariableName = "ALIFE_GROK_VISION_API_KEY";
    public static string? Resolve() => QChatVisionApiKeyResolver.Resolve(EnvironmentVariableName);
}
```

`QChatVisionApiKeyResolver.Resolve` checks process, user and machine environment values in that order. It returns no diagnostics and has no config-value fallback. The Grok client must never reuse a text-chat client object, even when both values are intentionally the same.

- [ ] **Step 4: Verify GREEN**

Run the Step 2 command. Expected: payload, missing-key, usage and redaction tests pass without network access.

- [ ] **Step 5: Commit**

```powershell
git add sources/Alife.Function/Alife.Function.QChat/QChatGrokImageRecognitionClient.cs sources/Alife.Function/Alife.Function.QChat/QChatImageRecognitionModels.cs Tests/Alife.Test.QChat/QChatGrokImageRecognitionClientTests.cs
git commit -m "feat(qchat): add Grok vision client"
```

### Task 3: Create deterministic primary / fallback route planning

**Files:**
- Create: `sources/Alife.Function/Alife.Function.QChat/QChatVisionRoutePlanner.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatImageRecognitionModels.cs`
- Test: `Tests/Alife.Test.QChat/QChatVisionRoutePlannerTests.cs`

- [ ] **Step 1: Write failing planner tests**

```csharp
[TestCase("请读出图片里的文字", "grok", "complex_ocr")]
[TestCase("截图里的报错怎么解决", "grok", "complex_ui_or_code")]
[TestCase("what is in this photo", "agnes", "default_image")]
public void Plan_SelectsExpectedPrimary(string text, string provider, string reason)
{
    QChatVisionRoutePlan plan = QChatVisionRoutePlanner.Plan(Profile(), text);
    Assert.That(plan.PrimaryProvider, Is.EqualTo(provider));
    Assert.That(plan.Reason, Is.EqualTo(reason));
}

[Test]
public void ShouldFallback_AllowsTimeoutButRejectsMissingPublicUrl()
{
    Assert.That(QChatVisionRoutePlanner.ShouldFallback(QChatImageRecognitionFailureKind.Timeout), Is.True);
    Assert.That(QChatVisionRoutePlanner.ShouldFallback(QChatImageRecognitionFailureKind.MissingPublicUrl), Is.False);
}

[Test]
public void Plan_DoesNotFallbackToSameOrDisabledProvider()
{
    QChatVisionProfile profile = Profile();
    profile.FallbackProvider = "agnes";
    Assert.That(QChatVisionRoutePlanner.Plan(profile, "normal photo").FallbackProvider, Is.Null);

    profile.FallbackProvider = "grok";
    QChatVisionProviderCatalog catalog = new()
    {
        Providers = [new() { ProviderId = "grok", Enabled = false }]
    };
    Assert.That(QChatVisionRoutePlanner.Plan(profile, "normal photo", catalog).FallbackProvider, Is.Null);
}
```

- [ ] **Step 2: Verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "FullyQualifiedName~QChatVisionRoutePlannerTests" --no-restore -v:minimal
```

Expected: compile failure because the route plan and planner are undefined.

- [ ] **Step 3: Implement route plan and retryability classifier**

```csharp
public sealed record QChatVisionRoutePlan(
    string PrimaryProvider,
    string? FallbackProvider,
    string Reason,
    TimeSpan TotalTimeout);

public static bool ShouldFallback(QChatImageRecognitionFailureKind kind) => kind is
    QChatImageRecognitionFailureKind.MissingApiKey or
    QChatImageRecognitionFailureKind.Timeout or
    QChatImageRecognitionFailureKind.HttpError or
    QChatImageRecognitionFailureKind.InvalidResponse;
```

Use deterministic Chinese and English intent keywords for OCR, screenshot, table, chart, code and UI. Only use the message text, never provider output. If primary and fallback ids are equal or the fallback is blank / disabled, return no fallback.

- [ ] **Step 4: Verify GREEN**

Run the Step 2 command. Expected: direct-Grok, Agnes default and non-retryable-skip cases pass.

- [ ] **Step 5: Commit**

```powershell
git add sources/Alife.Function/Alife.Function.QChat/QChatVisionRoutePlanner.cs sources/Alife.Function/Alife.Function.QChat/QChatImageRecognitionModels.cs Tests/Alife.Test.QChat/QChatVisionRoutePlannerTests.cs
git commit -m "feat(qchat): plan deterministic vision fallback routes"
```

### Task 4: Make passive group images automatically eligible

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatImageRecognitionPolicy.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatVisionProfile.cs`
- Modify: `Tests/Alife.Test.QChat/QChatImageRecognitionPolicyTests.cs`
- Modify: `Tests/Alife.Test.QChat/QChatImageRecognitionServiceTests.cs`

- [ ] **Step 1: Change the passive-group test to the approved behavior**

```csharp
[Test]
public void NonOwnerPassiveGroupImageIsAnalyzedWithoutMentionWhenEnabled()
{
    QChatImageRecognitionPolicyDecision decision = Decide(
        QChatSenderRole.GroupMember, OneBotMessageType.Group,
        isMentionedOrWoken: false, isPassiveGroupMessage: true);

    Assert.Multiple(() =>
    {
        Assert.That(decision.Action, Is.EqualTo(QChatImageRecognitionAction.Analyze));
        Assert.That(decision.Reason, Is.EqualTo("passive_group_image"));
    });
}
```

- [ ] **Step 2: Verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "FullyQualifiedName~QChatImageRecognitionPolicyTests|FullyQualifiedName~QChatImageRecognitionServiceTests" --no-restore -v:minimal
```

Expected: the revised test fails with `passive_group_image_disabled`.

- [ ] **Step 3: Enable automatic passive-group observation without changing reply authorization**

Set the default `AnalyzePassiveGroupImages` to `true` in the default QChat configuration. Keep all existing QChat group acceptance, message filtering, quiet mode and reply dispatch logic unchanged. The image policy alone decides observation eligibility; it must not set `IsMentionedOrWoken` or cause a QQ reply.

- [ ] **Step 4: Verify GREEN**

Run the Step 2 command. Expected: passive group images request recognition, while the service test still verifies that image URL is absent from internal formatted output.

- [ ] **Step 5: Commit**

```powershell
git add sources/Alife.Function/Alife.Function.QChat/QChatImageRecognitionPolicy.cs sources/Alife.Function/Alife.Function.QChat/QChatVisionProfile.cs Tests/Alife.Test.QChat/QChatImageRecognitionPolicyTests.cs Tests/Alife.Test.QChat/QChatImageRecognitionServiceTests.cs
git commit -m "feat(qchat): automatically observe passive group images"
```

### Task 5: Add bounded execution, de-duplication, priority and fallback

**Files:**
- Create: `sources/Alife.Function/Alife.Function.QChat/QChatVisionExecutionCoordinator.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatImageRecognitionService.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatImageRecognitionModels.cs`
- Test: `Tests/Alife.Test.QChat/QChatVisionExecutionCoordinatorTests.cs`

- [ ] **Step 1: Write failing coordinator tests**

```csharp
[Test]
public async Task ExecutesAgnesThenGrokExactlyOnceAfterRetryableFailure()
{
    RecordingClient agnes = RecordingClient.Fail("agnes", QChatImageRecognitionFailureKind.Timeout);
    RecordingClient grok = RecordingClient.Success("grok", "ocr result");
    QChatVisionExecutionCoordinator coordinator = new(new Dictionary<string, IQChatImageRecognitionClient>
    {
        ["agnes"] = agnes,
        ["grok"] = grok
    });

    QChatImageRecognitionProviderResult result = await coordinator.AnalyzeAsync(
        botId: 1, ownerPriority: false, imageKey: "image-a",
        new QChatVisionRoutePlan("agnes", "grok", "default_image", TimeSpan.FromSeconds(12)),
        Request());

    Assert.Multiple(() =>
    {
        Assert.That(result.Success, Is.True);
        Assert.That(result.ProviderName, Is.EqualTo("grok"));
        Assert.That(agnes.Calls, Is.EqualTo(1));
        Assert.That(grok.Calls, Is.EqualTo(1));
    });
}

[Test]
public async Task DeduplicatesSameImageWithinTtl()
{
    TaskCompletionSource<QChatImageRecognitionProviderResult> primaryCompletion = new();
    RecordingClient agnes = RecordingClient.Waiting("agnes", primaryCompletion);
    QChatVisionExecutionCoordinator coordinator = new(new Dictionary<string, IQChatImageRecognitionClient>
    {
        ["agnes"] = agnes
    }, duplicateTtl: TimeSpan.FromMinutes(1));
    QChatVisionRoutePlan route = new("agnes", null, "default_image", TimeSpan.FromSeconds(12));

    Task<QChatImageRecognitionProviderResult> first = coordinator.AnalyzeAsync(
        botId: 1, ownerPriority: false, imageKey: "normalized-image-key", route, Request());
    Task<QChatImageRecognitionProviderResult> second = coordinator.AnalyzeAsync(
        botId: 1, ownerPriority: false, imageKey: "normalized-image-key", route, Request());

    await agnes.WaitForCallAsync();
    primaryCompletion.SetResult(QChatImageRecognitionProviderResult.Ok("agnes", "agnes-2.0-flash", "cat"));

    QChatImageRecognitionProviderResult[] results = await Task.WhenAll(first, second);
    Assert.Multiple(() =>
    {
        Assert.That(agnes.Calls, Is.EqualTo(1));
        Assert.That(results.Select(result => result.Success), Is.All.True);
        Assert.That(results.Select(result => result.ProviderName), Is.All.EqualTo("agnes"));
    });
}

[Test]
public async Task OwnerItemRunsBeforeWaitingGuestItemForTheSameBot()
{
    TaskCompletionSource<QChatImageRecognitionProviderResult> firstCompletion = new();
    RecordingClient agnes = RecordingClient.WaitingFirstThenSuccess("agnes", firstCompletion);
    QChatVisionExecutionCoordinator coordinator = new(new Dictionary<string, IQChatImageRecognitionClient>
    {
        ["agnes"] = agnes
    });
    QChatVisionRoutePlan route = new("agnes", null, "default_image", TimeSpan.FromSeconds(12));

    Task<QChatImageRecognitionProviderResult> active = coordinator.AnalyzeAsync(1, false, "active", route, Request("active"));
    await agnes.WaitForCallAsync();
    Task<QChatImageRecognitionProviderResult> guest = coordinator.AnalyzeAsync(1, false, "guest", route, Request("guest"));
    Task<QChatImageRecognitionProviderResult> owner = coordinator.AnalyzeAsync(1, true, "owner", route, Request("owner"));
    firstCompletion.SetResult(QChatImageRecognitionProviderResult.Ok("agnes", "agnes-2.0-flash", "active"));

    await Task.WhenAll(active, owner, guest);
    Assert.That(agnes.RequestLabels, Is.EqualTo(new[] { "active", "owner", "guest" }));
}
```

- [ ] **Step 2: Verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "FullyQualifiedName~QChatVisionExecutionCoordinatorTests" --no-restore -v:minimal
```

Expected: compile failure because the coordinator does not exist.

- [ ] **Step 3: Implement coordinator semantics**

Expose only this operation:

```csharp
public Task<QChatImageRecognitionProviderResult> AnalyzeAsync(
    long botId,
    bool ownerPriority,
    string imageKey,
    QChatVisionRoutePlan route,
    QChatImageRecognitionProviderRequest request,
    CancellationToken cancellationToken = default);
```

Maintain independent per-Bot state. Enforce a single active task, bounded waiting items, owner-first dequeue, normalized-image-key TTL de-duplication, one total deadline, and a short per-provider circuit after repeated retryable failures. Do not enqueue when the request has no public URL or when the policy already skipped it. On fallback, reuse the same request and only call the fallback once.

- [ ] **Step 4: Verify GREEN**

Run the Step 2 command. Expected: fallback once, no fallback for `MissingPublicUrl`, duplicate suppression, owner-first order, per-Bot isolation and circuit recovery all pass.

- [ ] **Step 5: Commit**

```powershell
git add sources/Alife.Function/Alife.Function.QChat/QChatVisionExecutionCoordinator.cs sources/Alife.Function/Alife.Function.QChat/QChatImageRecognitionService.cs sources/Alife.Function/Alife.Function.QChat/QChatImageRecognitionModels.cs Tests/Alife.Test.QChat/QChatVisionExecutionCoordinatorTests.cs
git commit -m "feat(qchat): coordinate bounded vision fallback execution"
```

### Task 6: Wire provider registry and coordinator through QChat service

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatImageRecognitionService.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatVisionReadiness.cs`
- Modify: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`
- Modify: `Tests/Alife.Test.QChat/QChatVisionReadinessTests.cs`

- [ ] **Step 1: Write failing integration tests**

```csharp
[Test]
public async Task PassiveGroupImageIsObservedWithoutMentionButDoesNotForceReply()
{
    TaskCompletionSource<object?> observationCalled = new();
    FakeOneBotRuntime runtime = new();
    RecordingImageRecognitionClient client = new("agnes", () => observationCalled.TrySetResult(null));
    CapturingQChatService service = new(
        new XmlFunctionCaller(new NullLogger<XmlFunctionCaller>()),
        runtime,
        imageRecognitionService: new QChatImageRecognitionService(client))
    {
        Configuration = new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            EnableImageRecognition = true,
            AnalyzePassiveGroupImages = true,
            EnableBalancedTextStreaming = false,
            AllowedGroupIds = "881"
        }
    };
    StartService(service);

    runtime.Raise(new OneBotMessageEvent
    {
        SelfId = 999,
        MessageType = OneBotMessageType.Group,
        GroupId = 881,
        UserId = 441,
        RawMessage = "[CQ:image,file=normal.jpg,url=https://example.invalid/normal.jpg]"
    });
    await observationCalled.Task.WaitAsync(TimeSpan.FromSeconds(1));

    Assert.Multiple(() =>
    {
        Assert.That(client.CallCount, Is.EqualTo(1));
        Assert.That(runtime.GroupMessages, Is.Empty);
        Assert.That(runtime.PrivateMessages, Is.Empty);
    });
}

[Test]
public void ReadinessReportsPrimaryAndFallbackWithoutCredentialValues()
{
    QChatVisionReadinessStatus status = QChatVisionReadiness.Evaluate(
        EnabledConfig() with { VisionProfiles = ProfileWithAgnesAndGrok() },
        new Dictionary<string, Func<string?>>
        {
            ["agnes"] = () => "test-key-a",
            ["grok"] = () => "test-key-g"
        });
    Assert.That(status.Provider, Is.EqualTo("agnes"));
    Assert.That(status.FallbackProvider, Is.EqualTo("grok"));
    string json = JsonSerializer.Serialize(status);
    Assert.Multiple(() =>
    {
        Assert.That(json, Does.Not.Contain("test-key-a"));
        Assert.That(json, Does.Not.Contain("test-key-g"));
    });
}
```

- [ ] **Step 2: Verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "FullyQualifiedName~PassiveGroupImageIsObservedWithoutMentionButDoesNotForceReply|FullyQualifiedName~QChatVisionReadinessTests" --no-restore -v:minimal
```

Expected: integration test cannot inject the registry / coordinator and readiness has no fallback field.

- [ ] **Step 3: Replace Agnes-only service construction**

Replace the `ImageRecognitionService` Agnes-only branch with a registry assembled from the local provider catalog and injected test clients. Resolve the per-Bot profile, call `QChatVisionRoutePlanner`, then submit work through `QChatVisionExecutionCoordinator`. Keep `BuildImageAnalysisPromptAsync` asynchronous and preserve existing deferred conversation-settle behavior.

The service must return `null` for unavailable / skipped images and must not convert image analysis into a tool execution, a mention, a chat authorization decision or a QQ reply.

- [ ] **Step 4: Verify GREEN**

Run the Step 2 command. Expected: automatic passive observation occurs, no forced reply occurs, and readiness output remains redacted.

- [ ] **Step 5: Commit**

```powershell
git add sources/Alife.Function/Alife.Function.QChat/QChatService.cs sources/Alife.Function/Alife.Function.QChat/QChatImageRecognitionService.cs sources/Alife.Function/Alife.Function.QChat/QChatVisionReadiness.cs Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs Tests/Alife.Test.QChat/QChatVisionReadinessTests.cs
git commit -m "feat(qchat): route automatic vision through Agnes and Grok"
```

### Task 7: Verify authority, safety and full QChat regression suite

**Files:**
- Verify: `Tests/Alife.Test.QChat/Alife.Test.QChat.csproj`
- Verify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`

- [ ] **Step 1: Run focused vision and authority regressions**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "FullyQualifiedName~QChatAgnesImageRecognitionClientTests|FullyQualifiedName~QChatGrokImageRecognitionClientTests|FullyQualifiedName~QChatImageRecognitionServiceTests|FullyQualifiedName~QChatImageRecognitionPolicyTests|FullyQualifiedName~QChatVisionProfileRouterTests|FullyQualifiedName~QChatVisionRoutePlannerTests|FullyQualifiedName~QChatVisionExecutionCoordinatorTests|FullyQualifiedName~QChatVisionReadinessTests|FullyQualifiedName~QChatCapabilityPolicyTests|FullyQualifiedName~MixuPredecessorReceivesPersonalizedCSharpPermissionDenialWithoutOwnerPrivileges" --no-restore -v:minimal
```

Expected: zero failed tests; image observations do not change capability decisions or role authority.

- [ ] **Step 2: Build and run the complete QChat suite**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" build Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore -v:minimal
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --no-build -v:minimal
git diff --check
```

Expected: build has zero errors, all non-live QChat tests pass, and no whitespace error exists.

- [ ] **Step 3: Perform read-only boundary review**

Verify the diff contains no credentials, no local `Storage`, no key value, no DataAgent / LangGraph execution change, no QQ tool / desktop authority change, and no logging of URLs, request headers or provider raw bodies.

- [ ] **Step 4: Commit any final test-only correction**

```powershell
git add Tests/Alife.Test.QChat
git commit -m "test(qchat): verify dual-provider vision boundaries"
```

Only make this commit if a final test-only correction was required; otherwise do not create an empty commit.

### Task 8: Install local-only configuration and perform an authorized live check

**Files:**
- Modify locally only: `Storage/Character/<role>/Configuration/Alife.Function.QChat.QChatService.json`
- Verify locally only: user or process environment variables for Agnes and Grok vision

- [ ] **Step 1: Configure local provider catalog without credentials in tracked files**

Set both role Profiles to Agnes primary, Grok fallback and Grok complex provider. Enable passive group image observation, keep the per-message limit at two, and configure queue / TTL / timeout values from the accepted spec. Do not add a key property value to JSON.

The Git-ignored local JSON may contain provider identifiers and public endpoints only. Its relevant shape is:

```json
{
  "EnableImageRecognition": true,
  "AnalyzePassiveGroupImages": true,
  "MaxImagesPerMessage": 2,
  "VisionProfiles": {
    "EnablePerAgentVisionProfiles": true,
    "Profiles": [
      {
        "AgentId": "xiayu",
        "BotId": 2905391496,
        "PrimaryProvider": "agnes",
        "FallbackProvider": "grok",
        "ComplexRequestProvider": "grok",
        "MaxImagesPerMessage": 2,
        "RequiresPublicUrl": true,
        "Enabled": true
      },
      {
        "AgentId": "mixu",
        "BotId": 3340947887,
        "PrimaryProvider": "agnes",
        "FallbackProvider": "grok",
        "ComplexRequestProvider": "grok",
        "MaxImagesPerMessage": 2,
        "RequiresPublicUrl": true,
        "Enabled": true
      }
    ]
  }
}
```

- [ ] **Step 2: Set local environment values outside Git**

Set the two vision environment variable values in the user or process environment. Use the actual local values only; never echo them, serialize them, write them to Markdown, add them to test output or commit them.

- [ ] **Step 3: Verify local-only boundaries**

```powershell
git check-ignore -v -- Storage
git status --short --ignored=matching Storage
```

Expected: local configuration and runtime state are ignored and untracked.

- [ ] **Step 4: Request explicit authorization before external live call**

Ask the user for permission to submit exactly one non-sensitive test image. On approval, test Agnes normal routing first, then a text-attached OCR / screenshot request for Grok. Report only provider status, elapsed time, model id and safe success / failure code; do not reproduce the image URL, image content, key or raw response.

## Final handoff

Run `superpowers:finishing-a-development-branch` after implementation, fresh verification and local configuration checks. Local configuration remains outside Git; do not push unless the user explicitly requests GitHub upload.
