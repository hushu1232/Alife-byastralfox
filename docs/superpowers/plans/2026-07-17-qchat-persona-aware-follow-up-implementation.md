# QChat Persona-Aware Follow-Up Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a disabled-by-default, owner-private QQ follow-up capability that produces at most one natural, persona-aware supplement after a normal model reply and silently cancels it whenever conversation state changes.

**Architecture:** `QChatFollowUpPresencePolicy` maps a completed ordinary reply plus safe local state to a non-binding intent. `QChatConversationFollowUpScheduler` owns only session revision, cancellation, timing, cooldown and quota, then rechecks intent before text generation. `QChatService` stays the only QQ integration point and reuses its final output safety path.

**Tech Stack:** .NET 9, C# records and `CancellationTokenSource`, NUnit 4, existing QChat OneBot test doubles, `XmlFunctionCaller` text-only execution scope.

---

## File structure

- Create: `sources/Alife.Function/Alife.Function.QChat/QChatConversationFollowUpModels.cs` — settings, intent, presence, route/session keys and schedule records.
- Create: `sources/Alife.Function/Alife.Function.QChat/QChatFollowUpPresencePolicy.cs` — deterministic eligibility and natural-continuation cues.
- Create: `sources/Alife.Function/Alife.Function.QChat/QChatConversationFollowUpPresenceAdapters.cs` — XiaYu read-only adapter and Mixu-specific adapter.
- Create: `sources/Alife.Function/Alife.Function.QChat/QChatConversationFollowUpScheduler.cs` — bounded in-memory revision/cancellation/cooldown/quota state machine.
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs` — config, inbound invalidation, ordinary reply hook, text-only generation and normal final send.
- Modify: `sources/Alife.Function/Alife.Function.FunctionCaller/XmlFunctionCaller.cs` — async-local text-only scope that prevents XML tool execution.
- Create: `Tests/Alife.Test.QChat/QChatConversationFollowUpModelsTests.cs`
- Create: `Tests/Alife.Test.QChat/QChatFollowUpPresencePolicyTests.cs`
- Create: `Tests/Alife.Test.QChat/QChatConversationFollowUpSchedulerTests.cs`
- Modify: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`
- Modify: `Tests/Alife.Test.Interpreter/XmlFunctionPolicyTests.cs`

### Task 1: Define immutable settings, identities and intent models

**Files:**

- Create: `sources/Alife.Function/Alife.Function.QChat/QChatConversationFollowUpModels.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs:QChatConfig`
- Create: `Tests/Alife.Test.QChat/QChatConversationFollowUpModelsTests.cs`

- [ ] **Step 1: Write failing settings and identity tests**

```csharp
[Test]
public void DisabledDefaultCannotSchedule()
{
    QChatFollowUpSettings settings = QChatFollowUpSettings.From(new QChatConfig());
    Assert.Multiple(() =>
    {
        Assert.That(settings.Enabled, Is.False);
        Assert.That(settings.CanSchedule, Is.False);
        Assert.That(settings.AllowGroups, Is.False);
        Assert.That(settings.DelayMin, Is.EqualTo(TimeSpan.FromSeconds(8)));
        Assert.That(settings.DelayMax, Is.EqualTo(TimeSpan.FromSeconds(20)));
    });
}

[Test]
public void InvalidBoundsClampAndBotScopedSessionKeysDoNotCollide()
{
    QChatFollowUpSettings settings = QChatFollowUpSettings.From(new QChatConfig
    {
        EnableConversationFollowUp = true,
        FollowUpDelayMinSeconds = 30,
        FollowUpDelayMaxSeconds = 1,
        MaxFollowUpsPerTurn = -1,
        FollowUpDailyLimitPerSession = -2
    });
    Assert.Multiple(() =>
    {
        Assert.That(settings.DelayMin, Is.EqualTo(TimeSpan.FromSeconds(30)));
        Assert.That(settings.DelayMax, Is.EqualTo(TimeSpan.FromSeconds(30)));
        Assert.That(settings.MaxFollowUpsPerTurn, Is.Zero);
        Assert.That(settings.DailyLimitPerSession, Is.Zero);
        Assert.That(QChatFollowUpSessionKey.Create("xiayu", 100, 200).Value,
            Is.Not.EqualTo(QChatFollowUpSessionKey.Create("mixu", 101, 200).Value));
    });
}
```

- [ ] **Step 2: Run and verify the focused tests fail because models are absent**

Run `& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatConversationFollowUpModelsTests" -v:minimal`.

Expected: compilation failure naming `QChatFollowUpSettings` and `QChatFollowUpSessionKey`.

- [ ] **Step 3: Implement the minimal models and configuration**

```csharp
public enum QChatFollowUpIntent { None, WarmCoda, PracticalAddendum, EmotionalAfterthought, DoNotInterrupt }

public readonly record struct QChatFollowUpSessionKey(string Value)
{
    public static QChatFollowUpSessionKey Create(string agentId, long botId, long peerUserId) =>
        new($"qq:{agentId.Trim().ToLowerInvariant()}:{botId}:private:{peerUserId}");
}

public sealed record QChatFollowUpSettings(
    bool Enabled, bool OwnerPrivateOnly, bool AllowGroups, TimeSpan DelayMin, TimeSpan DelayMax,
    int MaxFollowUpsPerTurn, TimeSpan SessionCooldown, int DailyLimitPerSession)
{
    public bool CanSchedule => Enabled && MaxFollowUpsPerTurn > 0 && DailyLimitPerSession > 0;
    public static QChatFollowUpSettings From(QChatConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        int minSeconds = Math.Max(1, config.FollowUpDelayMinSeconds);
        int maxSeconds = Math.Max(minSeconds, config.FollowUpDelayMaxSeconds);
        return new QChatFollowUpSettings(
            config.EnableConversationFollowUp,
            config.ConversationFollowUpOwnerPrivateOnly,
            config.AllowConversationFollowUpInGroups,
            TimeSpan.FromSeconds(minSeconds),
            TimeSpan.FromSeconds(maxSeconds),
            Math.Max(0, config.MaxFollowUpsPerTurn),
            TimeSpan.FromMinutes(Math.Max(0, config.FollowUpSessionCooldownMinutes)),
            Math.Max(0, config.FollowUpDailyLimitPerSession));
    }
}
```

Add these fields beside the existing settle-window fields in `QChatConfig`:

```csharp
public bool EnableConversationFollowUp { get; set; }
public bool ConversationFollowUpOwnerPrivateOnly { get; set; } = true;
public bool AllowConversationFollowUpInGroups { get; set; }
public int FollowUpDelayMinSeconds { get; set; } = 8;
public int FollowUpDelayMaxSeconds { get; set; } = 20;
public int MaxFollowUpsPerTurn { get; set; } = 1;
public int FollowUpSessionCooldownMinutes { get; set; } = 15;
public int FollowUpDailyLimitPerSession { get; set; } = 6;
```

Also define `QChatFollowUpPresence`, `QChatFollowUpPresenceContext`, `QChatFollowUpScheduleRequest`, `QChatFollowUpExecutionResult`, and `QChatFollowUpGenerationRequest`. They may carry only identity, intent, revision, timestamps, safe message text and eligibility booleans; never prompts, tool output, credentials or persistence paths.

- [ ] **Step 4: Run the focused tests and verify they pass**

Run the Step 2 command. Expected: `2 passed, 0 failed`.

- [ ] **Step 5: Commit the independent model layer**

Commit: `git add sources/Alife.Function/Alife.Function.QChat/QChatConversationFollowUpModels.cs sources/Alife.Function/Alife.Function.QChat/QChatService.cs Tests/Alife.Test.QChat/QChatConversationFollowUpModelsTests.cs; git commit -m "feat(qchat): define conversation follow-up models"`.

### Task 2: Implement persona-aware presence policy without persona-state mutation

**Files:**

- Create: `sources/Alife.Function/Alife.Function.QChat/QChatFollowUpPresencePolicy.cs`
- Create: `sources/Alife.Function/Alife.Function.QChat/QChatConversationFollowUpPresenceAdapters.cs`
- Create: `Tests/Alife.Test.QChat/QChatFollowUpPresencePolicyTests.cs`

- [ ] **Step 1: Write failing intent and adapter tests**

```csharp
[Test]
public void RiskTaskGroupOrPendingMediaAlwaysReturnsDoNotInterrupt()
{
    foreach (QChatFollowUpPresenceContext context in UnsafeContexts())
        Assert.That(policy.Evaluate(context, new MixuFollowUpPresenceAdapter()).Intent,
            Is.EqualTo(QChatFollowUpIntent.DoNotInterrupt));
}

[Test]
public void XiayuSoftOwnerPrivateClosingCueBecomesWarmCodaWithoutMutatingState()
{
    XiaYuSelfState state = XiaYuSelfState.CreateDefault("xiayu", Now);
    state.Mood = "softened";
    state.CurrentFocus = "owner_private";
    XiaYuSelfState before = state.Clone();
    QChatFollowUpPresence presence = policy.Evaluate(OwnerPrivateContext("我先去忙了", "好，别太累"),
        new XiaYuFollowUpPresenceAdapter(state, TenderStrategy));
    Assert.Multiple(() =>
    {
        Assert.That(presence.Intent, Is.EqualTo(QChatFollowUpIntent.WarmCoda));
        Assert.That(state.Mood, Is.EqualTo(before.Mood));
        Assert.That(state.CurrentFocus, Is.EqualTo(before.CurrentFocus));
    });
}

[Test]
public void XiayuVigilanceOrSilentStrategyStopsAndMixuDoesNotReferenceXiaYuState()
{
    Assert.That(policy.Evaluate(OwnerPrivateContext("晚安", "晚安"),
        new XiaYuFollowUpPresenceAdapter(VigilantState, SilentStrategy)).Intent,
        Is.EqualTo(QChatFollowUpIntent.DoNotInterrupt));
    Assert.That(typeof(MixuFollowUpPresenceAdapter).GetProperties()
        .Any(property => property.PropertyType == typeof(XiaYuSelfState)), Is.False);
}
```

- [ ] **Step 2: Run and verify the policy tests fail**

Run `& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatFollowUpPresencePolicyTests" -v:minimal`.

Expected: compilation failure naming `QChatFollowUpPresencePolicy`.

- [ ] **Step 3: Implement policy and adapters**

```csharp
public sealed class QChatFollowUpPresencePolicy
{
    public QChatFollowUpPresence Evaluate(QChatFollowUpPresenceContext context, IQChatFollowUpPresenceAdapter adapter)
    {
        if (!context.IsOwnerPrivate || context.IsRiskConversation || context.IsDeterministicTask ||
            context.HasPendingMedia || context.IsQuiet || context.ModelReplyWasBlocked)
            return QChatFollowUpPresence.DoNotInterrupt;
        QChatFollowUpPresence persona = adapter.Evaluate(context);
        return persona.Intent is QChatFollowUpIntent.None or QChatFollowUpIntent.DoNotInterrupt
            ? persona : HasNaturalContinuationCue(context.SourceText, context.ReplyText)
                ? persona : QChatFollowUpPresence.None;
    }
}
```

`HasNaturalContinuationCue` must use a bounded ordinal check for only `晚安`, `先忙`, `回头`, `再见`, `下次`, `…` and `...`; it must not inspect raw CQ URLs, tool output, hidden prompts or private reasoning. The XiaYu adapter returns only `WarmCoda`, and returns `DoNotInterrupt` for high vigilance, `Silent`, high pressure, Timer or absent owner-private focus. The Mixu adapter uses only Mixu relation labels and safe context to return `WarmCoda` or `EmotionalAfterthought`; it must not receive `XiaYuSelfState`.

- [ ] **Step 4: Run the policy tests and verify they pass**

Run the Step 2 command. Expected: `3 passed, 0 failed`.

- [ ] **Step 5: Commit the presence layer**

Commit: `git add sources/Alife.Function/Alife.Function.QChat/QChatFollowUpPresencePolicy.cs sources/Alife.Function/Alife.Function.QChat/QChatConversationFollowUpPresenceAdapters.cs Tests/Alife.Test.QChat/QChatFollowUpPresencePolicyTests.cs; git commit -m "feat(qchat): evaluate persona-aware follow-up presence"`.

### Task 3: Build a bounded, cancellable per-session state machine

**Files:**

- Create: `sources/Alife.Function/Alife.Function.QChat/QChatConversationFollowUpScheduler.cs`
- Create: `Tests/Alife.Test.QChat/QChatConversationFollowUpSchedulerTests.cs`

- [ ] **Step 1: Write failing revision, cancellation and quota tests with controllable delay**

```csharp
[Test]
public async Task NewInboundMessageCancelsPendingPlanAndInvalidatesRevision()
{
    FakeFollowUpDelay delay = new();
    QChatConversationFollowUpScheduler scheduler = new(Now, delay.WaitAsync, () => TimeSpan.FromSeconds(8));
    QChatFollowUpScheduleRequest request = OwnerRequest("xiayu", 1, 1001, QChatFollowUpIntent.WarmCoda);

    Assert.That(scheduler.TrySchedule(request), Is.True);
    scheduler.ObserveInbound(request.SessionKey, Now.AddSeconds(1));
    delay.ReleaseAll();

    QChatFollowUpExecutionResult result = await scheduler.WaitForResultAsync(request.SessionKey);
    Assert.That(result.Kind, Is.EqualTo(QChatFollowUpExecutionKind.CancelledByNewInput));
}

[Test]
public async Task SchedulerAllowsOneTurnThenEnforcesCooldownAndDailyLimit()
{
    // Release an eligible request, mark it sent, advance the fake clock by cooldown, and repeat.
    Assert.That(sixth.Kind, Is.EqualTo(QChatFollowUpExecutionKind.Eligible));
    Assert.That(seventh.Kind, Is.EqualTo(QChatFollowUpExecutionKind.DroppedDailyLimit));
}

[Test]
public async Task RevisionMismatchAndPresenceRevalidationNeverBecomeEligible()
{
    Assert.That(revisionResult.Kind, Is.EqualTo(QChatFollowUpExecutionKind.DroppedRevision));
    Assert.That(revalidationResult.Kind, Is.EqualTo(QChatFollowUpExecutionKind.DroppedPresence));
}
```

- [ ] **Step 2: Run and verify scheduler tests fail**

Run `& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatConversationFollowUpSchedulerTests" -v:minimal`.

Expected: compilation failure naming `QChatConversationFollowUpScheduler`.

- [ ] **Step 3: Implement session ownership, time and cancellation**

```csharp
public sealed class QChatConversationFollowUpScheduler : IAsyncDisposable
{
    public void ObserveInbound(QChatFollowUpSessionKey key, DateTimeOffset now);
    public void ObserveNormalReply(QChatFollowUpSessionKey key, DateTimeOffset now);
    public bool TrySchedule(QChatFollowUpScheduleRequest request);
    public Task<QChatFollowUpExecutionResult> WaitForResultAsync(QChatFollowUpSessionKey key);
    public void Complete(QChatFollowUpSessionKey key, QChatFollowUpExecutionKind completion, DateTimeOffset now);
}
```

Use one lock and a bounded `Dictionary<QChatFollowUpSessionKey, QChatFollowUpSession>`. Each state holds revision, last user/reply timestamps, cooldown, local date/count, per-turn count, pending request, pending `CancellationTokenSource` and a `TaskCompletionSource<QChatFollowUpExecutionResult>`. Any inbound event increments revision and cancels a pending token. The delayed task captures `scheduledRevision` and returns `DroppedRevision` unless it exactly matches current revision after delay. Reset the daily counter on date rollover and remove idle sessions older than 24 hours, disposing their tokens. Do not persist sessions or call external services.

- [ ] **Step 4: Run scheduler tests and verify they pass**

Run the Step 2 command. Expected: `3 passed, 0 failed`; no test uses wall-clock `Task.Delay`.

- [ ] **Step 5: Commit scheduler**

Commit: `git add sources/Alife.Function/Alife.Function.QChat/QChatConversationFollowUpScheduler.cs Tests/Alife.Test.QChat/QChatConversationFollowUpSchedulerTests.cs; git commit -m "feat(qchat): schedule cancellable conversation follow-ups"`.

### Task 4: Add text-only model execution scope

**Files:**

- Modify: `sources/Alife.Function/Alife.Function.FunctionCaller/XmlFunctionCaller.cs`
- Modify: `Tests/Alife.Test.Interpreter/XmlFunctionPolicyTests.cs`

- [ ] **Step 1: Write the failing async-local scope test**

```csharp
[Test]
public void TextOnlyResponseScopeIsNestableAndRestoresState()
{
    XmlFunctionCaller caller = new(NullLogger<XmlFunctionCaller>.Instance);
    Assert.That(caller.IsTextOnlyResponseScopeActive, Is.False);
    using (caller.UseTextOnlyResponseScope())
    {
        Assert.That(caller.IsTextOnlyResponseScopeActive, Is.True);
        using (caller.UseTextOnlyResponseScope())
            Assert.That(caller.IsTextOnlyResponseScopeActive, Is.True);
    }
    Assert.That(caller.IsTextOnlyResponseScopeActive, Is.False);
}
```

- [ ] **Step 2: Run and verify the Interpreter test fails**

Run `& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.Interpreter\Alife.Test.Interpreter.csproj --no-restore --filter "FullyQualifiedName~XmlFunctionPolicyTests" -v:minimal`.

Expected: compilation failure naming `UseTextOnlyResponseScope`.

- [ ] **Step 3: Implement scope and tool-feed suppression**

```csharp
readonly AsyncLocal<int> textOnlyResponseDepth = new();
public bool IsTextOnlyResponseScopeActive => textOnlyResponseDepth.Value > 0;
public IDisposable UseTextOnlyResponseScope() => new TextOnlyResponseScope(textOnlyResponseDepth);

void OnChatReceived(string text)
{
    if (IsTextOnlyResponseScopeActive)
        return;
    executor.Feed(text);
}
```

At the start of `OnChatSent`, return while the scope is active so no XML is flushed for that text-only turn. `TextOnlyResponseScope.Dispose()` decrements only the current async-flow depth and never throws. Ordinary model turns remain untouched.

- [ ] **Step 4: Run Interpreter tests and verify they pass**

Run the Step 2 command. Expected: existing policy tests plus the new test pass.

- [ ] **Step 5: Commit text-only isolation**

Commit: `git add sources/Alife.Function/Alife.Function.FunctionCaller/XmlFunctionCaller.cs Tests/Alife.Test.Interpreter/XmlFunctionPolicyTests.cs; git commit -m "feat(functions): add text-only response scope"`.

### Task 5: Wire only ordinary QChat replies into policy, scheduler and safe final output

**Files:**

- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
- Modify: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`

- [ ] **Step 1: Write failing QChat integration tests with fake runtime and overridden model methods**

```csharp
[Test]
public async Task OwnerPrivatePlainReplyCanSendOneApprovedFollowUpThroughNormalPipeline()
{
    FakeOneBotRuntime runtime = new();
    FollowUpReplyQChatService service = CreateFollowUpService(runtime, "好，别太累", "忙完和我说一声");
    service.Configuration = EnabledOwnerFollowUpConfig(delaySeconds: 1);

    runtime.Raise(OwnerPrivateMessage("我先去忙了"));
    await WaitUntilAsync(() => runtime.PrivateMessages.Count == 2);

    Assert.That(runtime.PrivateMessages.Select(message => message.Message),
        Is.EqualTo(new[] { "好，别太累", "忙完和我说一声" }));
}

[Test]
public async Task NewInboundTaskFeedbackAndSkipPreventFollowUpSend()
{
    // Before FakeFollowUpDelay releases, raise second owner message; then exercise one deterministic command;
    // in a third isolated case return [skip] from GenerateConversationFollowUpAsync.
    Assert.That(runtime.PrivateMessages.Count, Is.EqualTo(expectedNormalMessagesOnly));
}

[Test]
public async Task FollowUpCannotInvokeXmlToolOrBypassOutputSafety()
{
    // Override generator to return <qchat type="private" targetId="1001">x</qchat>.
    // The runtime must record no second/third tool-originated message.
    Assert.That(runtime.PrivateMessages.Count, Is.EqualTo(1));
}
```

`FollowUpReplyQChatService` must derive from `QChatService` just like existing `PlainReplyQChatService`, override `DispatchToModelAsync` for the first plain reply, override the new protected follow-up generator, and use an injected fake delay/scheduler. No test starts QQ, calls a real model, or uses network I/O.

- [ ] **Step 2: Run and verify focused integration tests fail**

Run `& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~FollowUp" -v:minimal`.

Expected: compilation failure naming `FollowUpReplyQChatService` and the protected follow-up hook.

- [ ] **Step 3: Invalidate on every inbound message and schedule only normal model replies**

Before `DispatchInboundChatAsync` schedules or dispatches an inbound QQ message, call a private `ObserveFollowUpInboundActivity(QChatInboundMessage message)`. It creates `QChatFollowUpSessionKey` from resolved agent id, bot id and private peer and invalidates only that follow-up session; group input must never create a follow-up plan.

Add `TryScheduleFollowUpAfterNormalReplyAsync` only after successful sends in these two normal-model paths:

1. the plain-text fallback in `DispatchInboundChatCoreAsync`, after `SendTextOrMediaMessageAsync` returns; and
2. the successful `QChat(XmlExecutorContext, ...)` content-function path when `type` and `targetId` exactly equal the current `QChatReplySession` private owner conversation.

Never call it from `SendCommandReplyAsync`, deterministic task feedback, `QChatCrossSessionSend`, desktop/file actions, poke actions, TTS completion, vision completion or group output. This keeps C# task/permission feedback and non-conversation messages outside the feature.

- [ ] **Step 4: Implement text-only optional generation and final send**

```csharp
protected virtual async Task<string> GenerateConversationFollowUpAsync(
    QChatFollowUpGenerationRequest request,
    CancellationToken cancellationToken)
{
    using IDisposable textOnly = functionService.UseTextOnlyResponseScope();
    using IDisposable route = functionService.UseToolRouteState(ToolRouteState.Empty);
    return await ChatBot.ChatAsync(BuildConversationFollowUpPrompt(request), AuthorRole.System);
}
```

`BuildConversationFollowUpPrompt` must require exact `[skip]` or one 0–20-character plain-text message. It must prohibit pressure questions, new facts, task commitments, XML/CQ syntax, URLs, tools, timers and meta-language. Before sending, reject `[skip]`, empty text, more than 20 trimmed characters, and text containing `<`, `>`, `[CQ:`, `http://` or `https://`. Re-run `QChatFollowUpPresencePolicy` on the delayed callback. Only then use existing quiet-mode and persona-disclosure checks and call `SendTextOrMediaMessageAsync(Private, peer, text, streamText: false)`. Do not call `QChat` XML functions to send a follow-up.

Any cancelled revision, current `DoNotInterrupt`, generation failure, `[skip]`, rejected output or send failure records a body-free diagnostic and ends silently without retries. A successful send completes the scheduler so cooldown and daily limit advance.

- [ ] **Step 5: Run focused and full QChat tests**

Run these commands:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~FollowUp" -v:minimal
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore -v:minimal
```

Expected: focused tests pass; full QChat suite has zero failures and retains only explicitly skipped live tests.

- [ ] **Step 6: Commit QChat integration**

Commit: `git add sources/Alife.Function/Alife.Function.QChat/QChatService.cs Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs; git commit -m "feat(qchat): add cancellable persona-aware follow-ups"`.

### Task 6: Verify cross-project safety and code hygiene

**Files:**

- Modify only files from Tasks 1–5 if verification exposes a directly related defect.

- [ ] **Step 1: Build affected projects without restore**

Run these commands:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" build sources\Alife.Function\Alife.Function.QChat\Alife.Function.QChat.csproj --no-restore -v:minimal
& "C:\Users\hu shu\.dotnet\dotnet.exe" build Tests\Alife.Test.Interpreter\Alife.Test.Interpreter.csproj --no-restore -v:minimal
```

Expected: zero errors; distinguish any pre-existing warnings from new warnings.

- [ ] **Step 2: Run every affected test project**

Run these commands:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore -v:minimal
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.Interpreter\Alife.Test.Interpreter.csproj --no-restore -v:minimal
```

Expected: zero failures. Do not start QQ, make network calls, invoke live models, upload images or synthesize TTS.

- [ ] **Step 3: Check diff and credential hygiene**

Run `git diff --check HEAD~5..HEAD` and `git status --short --branch`.

Expected: no whitespace errors; only intended source/test/doc changes; no Storage, Runtime, logs, screenshots, generated output or credential-containing file is staged.

- [ ] **Step 4: Commit only a required verification correction**

If, and only if, a directly related verification correction was necessary, commit it as `test(qchat): verify follow-up safety boundaries`. Do not create an empty commit.
