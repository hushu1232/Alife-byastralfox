# QZone Native Autonomy First-Phase Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an independently designed, C#-native QZone read-only/dry-run runtime and persona-aware autonomous posting scheduler without requesting real cookies or issuing real QZone writes.

**Architecture:** `QZoneCookieRuntime` implements the existing `IQZoneRuntime` behind injected cookie and HTTP abstractions. `QZoneAutonomyScheduler` holds only per-agent timing/quota state, while persona adapters produce a non-executable post/comment decision. `QZoneService` remains the C# hard gate and records dry-run outcomes only.

**Tech Stack:** .NET 9, C#, `HttpClient` with injected `HttpMessageHandler`, NUnit 4, existing QChat/QZone modules, local ignored Storage state.

---

## File structure

- Create: `sources/Alife.Function/Alife.Function.QChat/QZoneCookieRuntime.cs` — independent read-only runtime, ephemeral cookie provider interface, HTTP request and response mapping.
- Create: `sources/Alife.Function/Alife.Function.QChat/QZoneAutonomyModels.cs` — settings, decisions, per-agent state, audit-safe snapshot records.
- Create: `sources/Alife.Function/Alife.Function.QChat/QZoneAutonomyScheduler.cs` — fake-clock-friendly 24–42 hour candidate, 48-hour target, window/quota/cooldown/retreat state machine.
- Create: `sources/Alife.Function/Alife.Function.QChat/QZoneAutonomyPersonaPolicy.cs` — XiaYu/Mixu adapters and content policy; no external calls.
- Create: `sources/Alife.Function/Alife.Function.QChat/QZoneAutonomyStateStore.cs` — atomically persists only timestamps, counters and hashes under ignored Storage.
- Modify: `sources/Alife.Function/Alife.Function.QChat/QZoneService.cs` — config defaults, dry-run coordination, audit-only processing and runtime injection seam.
- Create: `Tests/Alife.Test.QChat/QZoneCookieRuntimeTests.cs`
- Create: `Tests/Alife.Test.QChat/QZoneAutonomySchedulerTests.cs`
- Create: `Tests/Alife.Test.QChat/QZoneAutonomyPersonaPolicyTests.cs`
- Create: `Tests/Alife.Test.QChat/QZoneAutonomyStateStoreTests.cs`
- Modify: `Tests/Alife.Test.QChat/QZoneServiceTests.cs`

### Task 1: Define QZone autonomy models and disabled-safe configuration

**Files:**

- Create: `sources/Alife.Function/Alife.Function.QChat/QZoneAutonomyModels.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QZoneService.cs:QZoneServiceConfig`
- Create: `Tests/Alife.Test.QChat/QZoneAutonomyModelsTests.cs`

- [ ] **Step 1: Write failing default and per-agent identity tests**

```csharp
[Test]
public void DefaultAutonomySettingsAreDisabledAndDryRunOnly()
{
    QZoneAutonomySettings settings = QZoneAutonomySettings.From(new QZoneServiceConfig());

    Assert.Multiple(() =>
    {
        Assert.That(settings.Enabled, Is.False);
        Assert.That(settings.DryRunOnly, Is.True);
        Assert.That(settings.PostWindowStart, Is.EqualTo(new TimeOnly(9, 30)));
        Assert.That(settings.PostWindowEnd, Is.EqualTo(new TimeOnly(22, 30)));
        Assert.That(settings.PostHardMinimumInterval, Is.EqualTo(TimeSpan.FromHours(12)));
    });
}

[Test]
public void AgentKeysKeepXiayuAndMixuStateSeparate()
{
    Assert.That(QZoneAutonomyAgentKey.Create("xiayu", 100).Value,
        Is.Not.EqualTo(QZoneAutonomyAgentKey.Create("mixu", 100).Value));
}
```

- [ ] **Step 2: Run the test and verify it fails because autonomy models do not exist**

Run: `& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QZoneAutonomyModelsTests" -v:minimal`

Expected: compilation failure naming `QZoneAutonomySettings` and `QZoneAutonomyAgentKey`.

- [ ] **Step 3: Implement the minimal immutable model surface**

```csharp
public enum QZoneAutonomyAction { Skip, Post, Comment, ReplyOwnComment }
public readonly record struct QZoneAutonomyAgentKey(string Value)
{
    public static QZoneAutonomyAgentKey Create(string agentId, long botId) =>
        new($"qzone:{agentId.Trim().ToLowerInvariant()}:{botId}");
}
public sealed record QZoneAutonomySettings(
    bool Enabled, bool DryRunOnly, TimeOnly PostWindowStart, TimeOnly PostWindowEnd,
    TimeSpan PostHardMinimumInterval, int MaxPostsPerDay,
    int XiayuMaxCommentsPerDay, int MixuMaxCommentsPerDay);
```

Add these conservative config fields: `EnableQZoneAutonomy=false`, `QZoneAutonomyDryRunOnly=true`, `QZoneAutonomyPaused=false`, `AutonomyPostWindowStart="09:30"`, `AutonomyPostWindowEnd="22:30"`, `AutonomyMaxPostsPerDay=2`, `AutonomyPostMinimumIntervalHours=12`, `XiayuAutonomyMaxCommentsPerDay=2`, `MixuAutonomyMaxCommentsPerDay=3`. Parsing must clamp malformed windows and non-positive limits to the tested safe defaults.

- [ ] **Step 4: Run the focused test and verify it passes**

Run the Step 2 command. Expected: `2 passed, 0 failed`.

- [ ] **Step 5: Commit the model layer**

Commit: `git add sources/Alife.Function/Alife.Function.QChat/QZoneAutonomyModels.cs sources/Alife.Function/Alife.Function.QChat/QZoneService.cs Tests/Alife.Test.QChat/QZoneAutonomyModelsTests.cs; git commit -m "feat(qzone): define autonomous publishing models"`.

### Task 2: Add a fixed-response-only native QZone runtime

**Files:**

- Create: `sources/Alife.Function/Alife.Function.QChat/QZoneCookieRuntime.cs`
- Create: `Tests/Alife.Test.QChat/QZoneCookieRuntimeTests.cs`

- [ ] **Step 1: Write failing HTTP mapping and write-block tests**

```csharp
[Test]
public async Task LatestPostMapsFixedQZonePayloadWithoutPersistingCookie()
{
    RecordingHandler handler = new("""{"code":0,"data":{"tid":"p1","uin":1001,"content":"hello"}}""");
    QZoneCookieRuntime runtime = new(new StaticCookieProvider("skey=x"), handler);

    QZonePostSnapshot? post = await runtime.GetLatestPost(1001);

    Assert.Multiple(() =>
    {
        Assert.That(post, Is.EqualTo(new QZonePostSnapshot("p1", 1001, "hello")));
        Assert.That(handler.Requests.Single().Headers.Contains("Cookie"), Is.True);
        Assert.That(runtime.GetAuditSafeState().Contains("skey"), Is.False);
    });
}

[Test]
public void WriteOperationsAreUnavailableInFirstPhase()
{
    QZoneCookieRuntime runtime = new(new StaticCookieProvider("skey=x"), new RecordingHandler("{}"));
    Assert.ThrowsAsync<InvalidOperationException>(() => runtime.PublishPost("must not send"));
}
```

- [ ] **Step 2: Run the focused test and verify it fails**

Run: `& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QZoneCookieRuntimeTests" -v:minimal`

Expected: compilation failure naming `QZoneCookieRuntime`.

- [ ] **Step 3: Implement test-injected read-only runtime**

```csharp
public interface IQZoneEphemeralCookieProvider
{
    Task<string> GetCookieAsync(CancellationToken cancellationToken = default);
}

public sealed class QZoneCookieRuntime : IQZoneRuntime
{
    public Task<QZonePostSnapshot?> GetLatestPost(long targetId);
    public Task<IReadOnlyList<QZoneCommentSnapshot>> GetLatestComments(long targetId, string postId, int count);
    public Task PublishPost(string content) => throw new InvalidOperationException("QZone writes are unavailable in first phase.");
    public Task Comment(long targetId, string postId, string content) => throw new InvalidOperationException("QZone writes are unavailable in first phase.");
    public Task ReplyComment(long targetId, string postId, string commentId, string content) => throw new InvalidOperationException("QZone writes are unavailable in first phase.");
    public Task LikePost(long targetId, string postId) => throw new InvalidOperationException("QZone writes are unavailable in first phase.");
}
```

Use an injected `HttpMessageHandler`; every request obtains the cookie in memory, adds it only to the request header, parses only fixed JSON fields, and never writes it to a field, exception, audit record, response or state store. The production factory remains disabled and is not wired to OneBot `get_cookies` in this phase.

- [ ] **Step 4: Run the focused test and verify it passes**

Run the Step 2 command. Expected: `2 passed, 0 failed`.

- [ ] **Step 5: Commit the read-only runtime**

Commit: `git add sources/Alife.Function/Alife.Function.QChat/QZoneCookieRuntime.cs Tests/Alife.Test.QChat/QZoneCookieRuntimeTests.cs; git commit -m "feat(qzone): add read-only cookie runtime"`.

### Task 3: Implement persistent randomized autonomy scheduling

**Files:**

- Create: `sources/Alife.Function/Alife.Function.QChat/QZoneAutonomyStateStore.cs`
- Create: `sources/Alife.Function/Alife.Function.QChat/QZoneAutonomyScheduler.cs`
- Create: `Tests/Alife.Test.QChat/QZoneAutonomyStateStoreTests.cs`
- Create: `Tests/Alife.Test.QChat/QZoneAutonomySchedulerTests.cs`

- [ ] **Step 1: Write failing fake-clock tests**

```csharp
[Test]
public void SuccessfulPostSchedulesIndependentCandidateBetween24And42Hours()
{
    DateTimeOffset now = Start;
    QZoneAutonomyScheduler scheduler = new(() => now, random: () => 0.5);

    scheduler.RecordPostSucceeded(XiaYu, now);
    scheduler.RecordPostSucceeded(Mixu, now.AddHours(3));

    QZoneAutonomyState xiaYu = scheduler.GetState(XiaYu);
    QZoneAutonomyState mixu = scheduler.GetState(Mixu);
    Assert.Multiple(() =>
    {
        Assert.That(xiaYu.NextPostCandidateAt, Is.InRange(now.AddHours(24), now.AddHours(42)));
        Assert.That(mixu.NextPostCandidateAt, Is.Not.EqualTo(xiaYu.NextPostCandidateAt));
    });
}

[Test]
public void PauseWindowQuotaAndMinimumIntervalBeatOverdueAutonomyGoal()
{
    QZoneAutonomyDecision decision = scheduler.EvaluatePostCandidate(OverdueContext with
    {
        IsPaused = true,
        IsWithinPostWindow = false,
        PostsToday = 2
    });
    Assert.That(decision.Action, Is.EqualTo(QZoneAutonomyAction.Skip));
    Assert.That(decision.ReasonCode, Is.EqualTo("paused"));
}
```

- [ ] **Step 2: Run scheduler tests and verify they fail**

Run: `& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QZoneAutonomySchedulerTests" -v:minimal`

Expected: compilation failure naming `QZoneAutonomyScheduler`.

- [ ] **Step 3: Implement atomic state store and scheduler**

The store serializes only agent key, timestamps, counters, cooldown timestamps, candidate timestamp, failure kind and the last eight SHA-256 content hashes under `Storage/QZoneAutonomy/`; use temporary-file then replace semantics. The scheduler accepts injected clock/random, schedules a uniform 24–42 hour candidate after success, records missed 48-hour targets without catch-up, and returns skip reasons in priority order: disabled, paused, dry-run-disabled, outside-window, daily-limit, minimum-interval, retry-backoff, not-due.

```csharp
public sealed class QZoneAutonomyScheduler
{
    public void RecordPostSucceeded(QZoneAutonomyAgentKey key, DateTimeOffset now);
    public QZoneAutonomyState GetState(QZoneAutonomyAgentKey key);
    public QZoneAutonomyDecision EvaluatePostCandidate(QZoneAutonomyContext context);
    public void RecordDryRunOutcome(QZoneAutonomyAgentKey key, QZoneAutonomyAction action, bool succeeded, string reasonCode);
}
```

- [ ] **Step 4: Run state-store and scheduler tests and verify they pass**

Run: `& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QZoneAutonomy" -v:minimal`

Expected: all autonomy model, store and scheduler tests pass with no real clock or network dependency.

- [ ] **Step 5: Commit scheduling state**

Commit: `git add sources/Alife.Function/Alife.Function.QChat/QZoneAutonomyStateStore.cs sources/Alife.Function/Alife.Function.QChat/QZoneAutonomyScheduler.cs Tests/Alife.Test.QChat/QZoneAutonomyStateStoreTests.cs Tests/Alife.Test.QChat/QZoneAutonomySchedulerTests.cs; git commit -m "feat(qzone): schedule randomized autonomous posts"`.

### Task 4: Add role-specific decisions and dry-run-only service coordination

**Files:**

- Create: `sources/Alife.Function/Alife.Function.QChat/QZoneAutonomyPersonaPolicy.cs`
- Create: `Tests/Alife.Test.QChat/QZoneAutonomyPersonaPolicyTests.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QZoneService.cs`
- Modify: `Tests/Alife.Test.QChat/QZoneServiceTests.cs`

- [ ] **Step 1: Write failing persona and service-boundary tests**

```csharp
[Test]
public void XiayuVigilanceSkipsAndMixuDoesNotReadXiaYuState()
{
    QZoneAutonomyDecision xiaYu = policy.Evaluate(XiaYuContext with { Vigilance = 0.9, IsSilent = true });
    QZoneAutonomyDecision mixu = policy.Evaluate(MixuContext);

    Assert.Multiple(() =>
    {
        Assert.That(xiaYu.Action, Is.EqualTo(QZoneAutonomyAction.Skip));
        Assert.That(xiaYu.ReasonCode, Is.EqualTo("xiayu_silent_or_vigilant"));
        Assert.That(mixu.Action, Is.EqualTo(QZoneAutonomyAction.Post));
    });
}

[Test]
public async Task AutonomyServiceRecordsDryRunWithoutCallingRuntimeWrite()
{
    FakeQZoneRuntime runtime = new();
    QZoneService service = new(runtime, autonomyScheduler: DueScheduler, autonomyPolicy: PostingMixuPolicy)
    {
        Configuration = EnabledDryRunConfig
    };

    QZoneAutonomyRunResult result = await service.RunAutonomyOnceAsync(MixuRoute);

    Assert.Multiple(() =>
    {
        Assert.That(result.Executed, Is.False);
        Assert.That(result.ReasonCode, Is.EqualTo("dry_run"));
        Assert.That(runtime.PublishCalls, Is.Zero);
    });
}
```

- [ ] **Step 2: Run persona/service tests and verify they fail**

Run: `& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QZoneAutonomyPersonaPolicyTests|FullyQualifiedName~AutonomyService" -v:minimal`

Expected: compilation failure naming `QZoneAutonomyPersonaPolicy` and `RunAutonomyOnceAsync`.

- [ ] **Step 3: Implement pure persona policy and dry-run coordination**

```csharp
public interface IQZoneAutonomyPersonaPolicy
{
    QZoneAutonomyDecision Evaluate(QZoneAutonomyContext context);
}

public sealed class QZoneAutonomyPersonaPolicy : IQZoneAutonomyPersonaPolicy
{
    public QZoneAutonomyDecision Evaluate(QZoneAutonomyContext context);
}
```

The XiaYu branch accepts only a read-only scalar snapshot and returns `Skip` for silent/vigilant/high-pressure states. The Mixu branch uses only Mixu role and relationship signals; it cannot accept `XiaYuSelfState`. Both branches return a topic/style/length envelope rather than final text. `QZoneService.RunAutonomyOnceAsync` must first enforce existing QZone enablement, autonomy enablement, pause, dry-run, window, quota and scheduler decision. It records an audit-safe result and returns before `IQZoneRuntime.PublishPost`, `Comment`, `LikePost` or `ReplyComment` when first-phase dry-run is active.

- [ ] **Step 4: Run persona/service tests and verify they pass**

Run the Step 2 command. Expected: all selected tests pass and fake runtime write counts remain zero.

- [ ] **Step 5: Commit persona dry-run coordination**

Commit: `git add sources/Alife.Function/Alife.Function.QChat/QZoneAutonomyPersonaPolicy.cs sources/Alife.Function/Alife.Function.QChat/QZoneService.cs Tests/Alife.Test.QChat/QZoneAutonomyPersonaPolicyTests.cs Tests/Alife.Test.QChat/QZoneServiceTests.cs; git commit -m "feat(qzone): add persona-aware dry-run autonomy"`.

### Task 5: Verify first-phase boundaries and integration quality

**Files:**

- Modify only direct defects found in Tasks 1–4.

- [ ] **Step 1: Build the affected QChat project**

Run: `& "C:\Users\hu shu\.dotnet\dotnet.exe" build Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore -v:minimal`

Expected: zero errors; record any existing warnings separately.

- [ ] **Step 2: Run focused autonomy and full QChat tests**

Run these commands:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QZoneAutonomy|FullyQualifiedName~QZoneCookieRuntime" -v:minimal
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --no-build -v:minimal
```

Expected: focused autonomy tests pass; full QChat suite has zero failures and retains only explicit live-test skips.

- [ ] **Step 3: Prove no first-phase external state was touched**

Run: `git diff --check HEAD~4..HEAD` and inspect staged/tracked paths with `git status --short --branch`.

Expected: no whitespace errors; no credentials, `Storage`, `Runtime`, `Outputs`, logs, screenshots or generated artifacts; tests assert cookie provider and HTTP runtime are fake-only and write calls are zero.

- [ ] **Step 4: Commit only a directly required verification correction**

If a direct verification correction was made, commit it as `test(qzone): verify dry-run autonomy boundaries`; otherwise do not create an empty commit.
