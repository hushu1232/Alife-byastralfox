# QChat Layered Contract Enhancement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add layered QChat contract tests for prompt leak prevention, continuation loop invariants, and optional TTS/semantic-window behavior while preserving the required/optional baseline split in the static engineering map.

**Architecture:** Required baseline tests target symbols that exist in a clean checkout: visible reply policy and continuation policy. Optional active-workspace tests target features present in this working tree but not guaranteed by a clean checkout: semantic settle windows and voice warmup. The static checker and engineering map are updated so required test anchors control failure and optional test anchors remain informative.

**Tech Stack:** C#/.NET 9, NUnit, PowerShell 5-compatible static checker, Markdown.

---

## File Structure

- Create `Tests/Alife.Test.QChat/QChatPromptLeakContractTests.cs`: required prompt leak contract tests.
- Modify `Tests/Alife.Test.QChat/QChatContinuationPolicyTests.cs`: required continuation invariant tests.
- Modify `Tests/Alife.Test.QChat/QChatSemanticSettleWindowTests.cs`: optional semantic settle invariant tests.
- Modify `Tests/Alife.Test.QChat/QChatVoiceWarmupCoordinatorTests.cs`: optional TTS warmup contract tests.
- Modify `tools/check-qchat-engineering-map.ps1`: add required/optional contract test anchors.
- Modify `docs/qchat-harness-loop-prompt-engineering.md`: document the layered contract tests.

Do not stage or revert unrelated pre-existing dirty files.

---

### Task 1: Required Prompt Leak Contract Tests

**Files:**
- Create: `Tests/Alife.Test.QChat/QChatPromptLeakContractTests.cs`

- [ ] **Step 1: Create the test file**

Create `Tests/Alife.Test.QChat/QChatPromptLeakContractTests.cs`:

```csharp
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatPromptLeakContractTests
{
    [TestCase("\u5FC3\u7406\u72B6\u6001\uFF1A\u4FDD\u6301\u5B89\u9759\u89C2\u5BDF\u3002")]
    [TestCase("\u5185\u5FC3\uFF1A\u8FD9\u6BB5\u4E0D\u80FD\u53D1\u51FA\u53BB\u3002")]
    [TestCase("\u72B6\u6001\uFF1A\u5F85\u673A\u3002")]
    [TestCase("\uFF08\u4E0D\u56DE\u590D\uFF0C\u4FDD\u6301\u5B89\u9759\uFF09")]
    public void InternalStateTextDoesNotBecomePrivateVisibleReply(string text)
    {
        QChatVisibleReplyPolicy policy = new();

        QChatVisibleReplyResult result = policy.Normalize(
            text,
            QChatConversationKind.Private,
            shouldReply: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.ShouldSend, Is.False);
            Assert.That(result.Text, Is.Empty);
        });
    }

    [Test]
    public void GroupNoReplyFallsBackToReactionInsteadOfLeakingInternalState()
    {
        QChatVisibleReplyPolicy policy = new(["\u3002"]);

        QChatVisibleReplyResult result = policy.Normalize(
            "\u5FC3\u7406\u72B6\u6001\uFF1A\u6C89\u9ED8\u65C1\u89C2\u3002",
            QChatConversationKind.Group,
            shouldReply: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.ShouldSend, Is.True);
            Assert.That(result.Text, Is.EqualTo("\u3002"));
            Assert.That(result.Text, Does.Not.Contain("\u5FC3\u7406\u72B6\u6001"));
            Assert.That(result.Reason, Does.Contain("group no-reply reaction"));
        });
    }

    [Test]
    public void VisibleTechnicalStatusTextIsNotBlockedAsInternalPromptState()
    {
        QChatVisibleReplyPolicy policy = new();

        QChatVisibleReplyResult result = policy.Normalize(
            "\u72B6\u6001\u7801 500 \u8868\u793A\u670D\u52A1\u7AEF\u9519\u8BEF\u3002",
            QChatConversationKind.Private,
            shouldReply: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.ShouldSend, Is.True);
            Assert.That(result.Text, Is.EqualTo("\u72B6\u6001\u7801 500 \u8868\u793A\u670D\u52A1\u7AEF\u9519\u8BEF\u3002"));
        });
    }
}
```

- [ ] **Step 2: Run the focused test**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter FullyQualifiedName~QChatPromptLeakContractTests
```

Expected: tests pass, or the run is blocked by an external build/restore issue such as NuGet `NU1301`. If blocked, record the exact error and do not claim the tests passed.

- [ ] **Step 3: Commit**

Run:

```powershell
git add -- Tests/Alife.Test.QChat/QChatPromptLeakContractTests.cs
git commit -m "Add QChat prompt leak contract tests" --only -- Tests/Alife.Test.QChat/QChatPromptLeakContractTests.cs
```

Expected: commit contains only `Tests/Alife.Test.QChat/QChatPromptLeakContractTests.cs`.

---

### Task 2: Required Continuation Loop Invariant Tests

**Files:**
- Modify: `Tests/Alife.Test.QChat/QChatContinuationPolicyTests.cs`

- [ ] **Step 1: Add continuation invariant tests**

Append these tests inside `QChatContinuationPolicyTests`:

```csharp
    [Test]
    public void DeterministicTaskWithoutFeedbackStillBlocksModelDispatch()
    {
        QChatContinuationDecision decision = QChatContinuationPolicy.Decide(new QChatContinuationContext(
            DeterministicTaskHandled: true,
            SentTaskFeedback: false,
            HasModelReply: false,
            IncomingText: "check file upload status"));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Action, Is.EqualTo(QChatContinuationAction.TaskFeedbackOnly));
            Assert.That(decision.ShouldDispatchModel, Is.False);
            Assert.That(decision.Reason, Is.EqualTo("deterministic-task-handled"));
        });
    }

    [Test]
    public void DeterministicTaskWithExistingModelReplyDoesNotContinueAgain()
    {
        QChatContinuationDecision decision = QChatContinuationPolicy.Decide(new QChatContinuationContext(
            DeterministicTaskHandled: true,
            SentTaskFeedback: true,
            HasModelReply: true,
            IncomingText: "task already completed"));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Action, Is.EqualTo(QChatContinuationAction.StopAfterTaskFeedback));
            Assert.That(decision.ShouldDispatchModel, Is.False);
            Assert.That(decision.Reason, Is.EqualTo("deterministic-task-handled"));
        });
    }

    [Test]
    public void FeedbackFlagAloneDoesNotSuppressNormalConversation()
    {
        QChatContinuationDecision decision = QChatContinuationPolicy.Decide(new QChatContinuationContext(
            DeterministicTaskHandled: false,
            SentTaskFeedback: true,
            HasModelReply: false,
            IncomingText: "continue the previous topic"));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Action, Is.EqualTo(QChatContinuationAction.ReplyNow));
            Assert.That(decision.ShouldDispatchModel, Is.True);
            Assert.That(decision.Reason, Is.EqualTo("normal-conversation"));
        });
    }
```

- [ ] **Step 2: Run the focused test**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter FullyQualifiedName~QChatContinuationPolicyTests
```

Expected: tests pass, or the run is blocked by an external build/restore issue. Record the exact result.

- [ ] **Step 3: Commit**

Run:

```powershell
git add -- Tests/Alife.Test.QChat/QChatContinuationPolicyTests.cs
git commit -m "Add QChat continuation invariant tests" --only -- Tests/Alife.Test.QChat/QChatContinuationPolicyTests.cs
```

Expected: commit contains only `Tests/Alife.Test.QChat/QChatContinuationPolicyTests.cs`.

---

### Task 3: Optional Semantic Settle Contract Tests

**Files:**
- Modify: `Tests/Alife.Test.QChat/QChatSemanticSettleWindowTests.cs`

- [ ] **Step 1: Confirm optional test file exists**

Run:

```powershell
Test-Path -LiteralPath Tests\Alife.Test.QChat\QChatSemanticSettleWindowTests.cs
```

Expected in this active workspace: `True`. If `False`, skip this task and keep semantic settle anchors optional.

- [ ] **Step 2: Add semantic settle invariant tests**

Append these tests inside `QChatSemanticSettleWindowTests`:

```csharp
    [Test]
    public void EmptyWindowNeverSettles()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-26T00:00:00Z");
        QChatSemanticSettleWindow window = new(new QChatSemanticSettleOptions
        {
            SettleDelay = TimeSpan.Zero,
            MaxWindowDuration = TimeSpan.Zero,
            MaxMessages = 1
        }, now);

        Assert.That(window.ShouldSettle(now.AddMinutes(1)), Is.False);
    }

    [Test]
    public void SnapshotPreservesWindowTimestampsAndMessageOrder()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-26T00:00:00Z");
        QChatSemanticSettleWindow window = new(new QChatSemanticSettleOptions(), now);

        window.AddMessage(CreateMessage("first", now.AddSeconds(1), messageId: 10));
        window.AddMessage(CreateMessage("second", now.AddSeconds(3), messageId: 11));

        QChatSemanticWindowSnapshot snapshot = window.Snapshot();

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.CreatedAt, Is.EqualTo(now));
            Assert.That(snapshot.LastUpdatedAt, Is.EqualTo(now.AddSeconds(3)));
            Assert.That(snapshot.Messages.Select(message => message.MessageId), Is.EqualTo(new long[] { 10, 11 }));
        });
    }

    [Test]
    public void MaxWindowDurationForcesIncompleteTrailingTextToSettle()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-26T00:00:00Z");
        QChatSemanticSettleWindow window = new(new QChatSemanticSettleOptions
        {
            SettleDelay = TimeSpan.FromMinutes(10),
            MaxWindowDuration = TimeSpan.FromSeconds(5),
            MaxMessages = 10
        }, now);

        window.AddMessage(CreateMessage("still incomplete,", now.AddSeconds(1)));

        Assert.That(window.ShouldSettle(now.AddSeconds(5)), Is.True);
    }
```

- [ ] **Step 3: Run the focused test**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter FullyQualifiedName~QChatSemanticSettleWindowTests
```

Expected: tests pass if the optional active-workspace feature compiles, or the run is blocked by an external build/restore issue. Record the exact result.

- [ ] **Step 4: Commit**

Run:

```powershell
git add -- Tests/Alife.Test.QChat/QChatSemanticSettleWindowTests.cs
git commit -m "Add QChat semantic settle invariant tests" --only -- Tests/Alife.Test.QChat/QChatSemanticSettleWindowTests.cs
```

Expected: commit contains only `Tests/Alife.Test.QChat/QChatSemanticSettleWindowTests.cs`.

---

### Task 4: Optional Voice Warmup Contract Tests

**Files:**
- Modify: `Tests/Alife.Test.QChat/QChatVoiceWarmupCoordinatorTests.cs`

- [ ] **Step 1: Confirm optional test file exists**

Run:

```powershell
Test-Path -LiteralPath Tests\Alife.Test.QChat\QChatVoiceWarmupCoordinatorTests.cs
```

Expected in this active workspace: `True`. If `False`, skip this task and keep voice warmup anchors optional.

- [ ] **Step 2: Add voice warmup contract tests**

Append these tests inside `QChatVoiceWarmupCoordinatorTests`:

```csharp
    [Test]
    public async Task WarmupAsync_MultipleProfilesTrackIndependentStatuses()
    {
        QChatVoiceProfile xiayu = CreateProfile("xiayu", 2905391496, "xiayu-zh");
        QChatVoiceProfile mixu = CreateProfile("mixu", 3340947887, "mixu-zh");
        FakeWarmupSpeechModel speechModel = new("warmup.wav");
        QChatVoiceWarmupCoordinator coordinator = new(
            speechModel,
            (endpoint, _) => Task.FromResult(endpoint.Port == 9880),
            retryDelayProvider: _ => TimeSpan.Zero);

        mixu.ApiBaseUrl = "http://127.0.0.1:9881";

        await coordinator.WarmupOnceAsync([xiayu, mixu], CancellationToken.None);

        QChatVoiceWarmupProfileStatus xiayuStatus = coordinator.GetStatus(xiayu.VoiceId);
        QChatVoiceWarmupProfileStatus mixuStatus = coordinator.GetStatus(mixu.VoiceId);

        Assert.Multiple(() =>
        {
            Assert.That(xiayuStatus.State, Is.EqualTo(QChatVoiceWarmupState.Ready));
            Assert.That(xiayuStatus.BotId, Is.EqualTo(2905391496));
            Assert.That(mixuStatus.State, Is.EqualTo(QChatVoiceWarmupState.EndpointUnreachable));
            Assert.That(mixuStatus.BotId, Is.EqualTo(3340947887));
            Assert.That(speechModel.Requests, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task StartAsync_RetriesUntilEndpointBecomesReachable()
    {
        QChatVoiceProfile profile = CreateProfile("xiayu", 2905391496, "xiayu-zh");
        int probes = 0;
        FakeWarmupSpeechModel speechModel = new("warmup.wav");
        QChatVoiceWarmupCoordinator coordinator = new(
            speechModel,
            (_, _) =>
            {
                probes++;
                return Task.FromResult(probes >= 2);
            },
            retryDelayProvider: _ => TimeSpan.Zero);

        await coordinator.StartAsync([profile], CancellationToken.None);
        await WaitUntilAsync(() => coordinator.GetStatus(profile.VoiceId).State == QChatVoiceWarmupState.Ready);
        await coordinator.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(probes, Is.GreaterThanOrEqualTo(2));
            Assert.That(speechModel.Requests, Is.EqualTo(1));
            Assert.That(coordinator.GetStatus(profile.VoiceId).State, Is.EqualTo(QChatVoiceWarmupState.Ready));
        });
    }

    [Test]
    public async Task StopAsync_CancelsRetryLoopWithoutAdditionalProbes()
    {
        QChatVoiceProfile profile = CreateProfile("xiayu", 2905391496, "xiayu-zh");
        int probes = 0;
        TaskCompletionSource<bool> firstProbe = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeWarmupSpeechModel speechModel = new("warmup.wav");
        QChatVoiceWarmupCoordinator coordinator = new(
            speechModel,
            (_, _) =>
            {
                probes++;
                firstProbe.TrySetResult(true);
                return Task.FromResult(false);
            },
            retryDelayProvider: _ => TimeSpan.FromMinutes(5));

        await coordinator.StartAsync([profile], CancellationToken.None);
        await firstProbe.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await coordinator.StopAsync();
        int probesAfterStop = probes;
        await Task.Delay(50);

        Assert.Multiple(() =>
        {
            Assert.That(probes, Is.EqualTo(probesAfterStop));
            Assert.That(coordinator.GetStatus(profile.VoiceId).State, Is.EqualTo(QChatVoiceWarmupState.EndpointUnreachable));
        });
    }

    static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));
        while (condition() == false)
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(10, timeout.Token);
        }
    }
```

- [ ] **Step 3: Run the focused test**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter FullyQualifiedName~QChatVoiceWarmupCoordinatorTests
```

Expected: tests pass if the optional active-workspace feature compiles, or the run is blocked by an external build/restore issue. Record the exact result.

- [ ] **Step 4: Commit**

Run:

```powershell
git add -- Tests/Alife.Test.QChat/QChatVoiceWarmupCoordinatorTests.cs
git commit -m "Add QChat voice warmup contract tests" --only -- Tests/Alife.Test.QChat/QChatVoiceWarmupCoordinatorTests.cs
```

Expected: commit contains only `Tests/Alife.Test.QChat/QChatVoiceWarmupCoordinatorTests.cs`.

---

### Task 5: Checker and Documentation Anchors

**Files:**
- Modify: `tools/check-qchat-engineering-map.ps1`
- Modify: `docs/qchat-harness-loop-prompt-engineering.md`

- [ ] **Step 1: Add required checker anchors**

Add these static checks to `tools/check-qchat-engineering-map.ps1`:

```powershell
Add-Check -Group "Harness" -Name "Prompt leak contract tests" -Path "Tests/Alife.Test.QChat/QChatPromptLeakContractTests.cs" -Patterns @("QChatPromptLeakContractTests", "InternalStateTextDoesNotBecomePrivateVisibleReply")
Add-Check -Group "Loop" -Name "Continuation invariant tests" -Path "Tests/Alife.Test.QChat/QChatContinuationPolicyTests.cs" -Patterns @("DeterministicTaskWithoutFeedbackStillBlocksModelDispatch", "FeedbackFlagAloneDoesNotSuppressNormalConversation")
```

- [ ] **Step 2: Add optional checker anchors**

Add these optional static checks to `tools/check-qchat-engineering-map.ps1`:

```powershell
Add-Check -Group "Loop" -Name "Semantic settle invariant tests" -Path "Tests/Alife.Test.QChat/QChatSemanticSettleWindowTests.cs" -Patterns @("EmptyWindowNeverSettles", "MaxWindowDurationForcesIncompleteTrailingTextToSettle") -Required $false
Add-Check -Group "Loop" -Name "Voice warmup contract tests" -Path "Tests/Alife.Test.QChat/QChatVoiceWarmupCoordinatorTests.cs" -Patterns @("WarmupAsync_MultipleProfilesTrackIndependentStatuses", "StartAsync_RetriesUntilEndpointBecomesReachable") -Required $false
```

- [ ] **Step 3: Update engineering map document**

Modify `docs/qchat-harness-loop-prompt-engineering.md` to include:

- `Tests/Alife.Test.QChat/QChatPromptLeakContractTests.cs` as required prompt leak contract coverage.
- `Tests/Alife.Test.QChat/QChatContinuationPolicyTests.cs` as required continuation invariant coverage.
- `Tests/Alife.Test.QChat/QChatSemanticSettleWindowTests.cs` as optional semantic settle contract coverage.
- `Tests/Alife.Test.QChat/QChatVoiceWarmupCoordinatorTests.cs` as optional voice warmup contract coverage.
- A note that static checker success is not full build/test/live service validation.

- [ ] **Step 4: Run static checker**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected: command exits `0` when required missing count is zero.

- [ ] **Step 5: Commit**

Run:

```powershell
git add -- tools/check-qchat-engineering-map.ps1 docs/qchat-harness-loop-prompt-engineering.md
git commit -m "Map QChat layered contract tests" --only -- tools/check-qchat-engineering-map.ps1 docs/qchat-harness-loop-prompt-engineering.md
```

Expected: commit contains only the checker and engineering map document.

---

### Task 6: Final Verification

**Files:**
- Verify all files changed by Tasks 1-5.

- [ ] **Step 1: Run focused QChat tests**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatPromptLeakContractTests|FullyQualifiedName~QChatContinuationPolicyTests|FullyQualifiedName~QChatSemanticSettleWindowTests|FullyQualifiedName~QChatVoiceWarmupCoordinatorTests"
```

Expected: tests pass, or the command is blocked by a clearly reported external NuGet/build issue. If blocked by `NU1301`, report the exact error and do not claim tests passed.

- [ ] **Step 2: Run static checker in active workspace**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected: exit `0`, with zero required missing anchors.

- [ ] **Step 3: Run incomplete marker scan**

Run:

```powershell
$markers = @("T" + "BD", "TO" + "DO", "PLACE" + "HOLDER", "\?" + "\?" + "\?")
foreach ($marker in $markers) {
    Select-String -Path `
        Tests/Alife.Test.QChat/QChatPromptLeakContractTests.cs,`
        Tests/Alife.Test.QChat/QChatContinuationPolicyTests.cs,`
        Tests/Alife.Test.QChat/QChatSemanticSettleWindowTests.cs,`
        Tests/Alife.Test.QChat/QChatVoiceWarmupCoordinatorTests.cs,`
        tools/check-qchat-engineering-map.ps1,`
        docs/qchat-harness-loop-prompt-engineering.md `
        -Pattern $marker
}
```

Expected: no matches.

- [ ] **Step 4: Verify clean worktree static checker behavior**

Run:

```powershell
git worktree add --detach D:\tmp\alife-layered-contract-clean-verify HEAD
powershell -ExecutionPolicy Bypass -File D:\tmp\alife-layered-contract-clean-verify\tools\check-qchat-engineering-map.ps1
git worktree remove --force D:\tmp\alife-layered-contract-clean-verify
```

Expected: clean checkout exits `0` when required missing count is zero. Optional entries may be present or missing.

- [ ] **Step 5: Final review**

Request final review focused on:

- Required/optional split.
- Scope compliance.
- Test determinism.
- No live service calls.
- No unrelated files committed.
