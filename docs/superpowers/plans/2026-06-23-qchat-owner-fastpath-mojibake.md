# QChat Owner Fast Path And Mojibake Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an owner-only QChat natural-language confirmation fast path and clean the confirmed user-visible mojibake in web research fallback text.

**Architecture:** Add a small `QChatOwnerTrustedFastPathPolicy` beside the existing QChat intent classifier/orchestrator. QChat handlers will classify intent as they do today, then let the fast-path policy mark selected verified-owner candidate intents as naturally confirmed before the existing orchestrator, capability policy, file safety, audit, and outbox paths run.

**Tech Stack:** C# records/static policy classes, NUnit tests, existing QChat service adapter test fakes, .NET test runner.

---

## File Structure

- Create `sources/Alife.Function/Alife.Function.QChat/QChatOwnerTrustedFastPathPolicy.cs`
  - Owns the owner-only fast-path decision.
  - Contains the action-family enum used by QChat handlers.
  - Does not execute any QQ or file action.

- Modify `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
  - Adds fast-path config properties to `QChatConfig`.
  - Applies the policy at selected confirmation gates:
    - quiet mode owner control;
    - owner recall;
    - owner allowlist natural-language command;
    - existing group file upload intent.

- Modify `sources/Alife.Function/Alife.Function.MessageFilter/AgentWebResearchService.cs`
  - Replaces the confirmed mojibake fallback string in `ResearchAsync`.

- Create `Tests/Alife.Test.QChat/QChatOwnerTrustedFastPathPolicyTests.cs`
  - Fast unit coverage for owner/non-owner behavior and hard exclusions.

- Modify `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`
  - Adds integration-style tests for owner natural confirmation and non-owner boundaries.
  - Replaces the intentional mojibake assertion sample with a named constant or clearer assertion.

- Modify `Tests/Alife.Test.Framework/AgentWebResearchServiceTests.cs`
  - Adds a regression test for readable empty-query fallback text.

---

### Task 1: Add Owner Fast-Path Policy Unit Tests

**Files:**
- Create: `Tests/Alife.Test.QChat/QChatOwnerTrustedFastPathPolicyTests.cs`
- Implementation file: `sources/Alife.Function/Alife.Function.QChat/QChatOwnerTrustedFastPathPolicy.cs`

- [ ] **Step 1: Write the failing policy tests**

Create `Tests/Alife.Test.QChat/QChatOwnerTrustedFastPathPolicyTests.cs`:

```csharp
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

public sealed class QChatOwnerTrustedFastPathPolicyTests
{
    [Test]
    public void Apply_WhenOwnerRecallCandidateWithoutMeta_MarksDecisionConfirmed()
    {
        QChatConfig config = new()
        {
            EnableOwnerTrustedFastPath = true,
            OwnerFastPathAllowsRecall = true
        };
        QChatIntentDecision decision = new(
            QChatIntentKind.RecallMessage,
            IsCandidate: true,
            IsConfirmed: false,
            Confidence: 0.35,
            TargetKind: QChatIntentTargetKind.None,
            TargetText: null,
            TargetId: null,
            FilePath: null,
            HasNegation: false,
            IsMetaDiscussion: false,
            Reason: "recall keyword is not an execution command");

        QChatIntentDecision result = QChatOwnerTrustedFastPathPolicy.Apply(
            decision,
            QChatSenderRole.Owner,
            QChatOwnerTrustedFastPathAction.Recall,
            config);

        Assert.That(result.IsConfirmed, Is.True);
        Assert.That(result.TargetKind, Is.EqualTo(QChatIntentTargetKind.RecentBotMessage));
        Assert.That(result.Reason, Does.Contain("owner trusted fast path"));
    }

    [Test]
    public void Apply_WhenNonOwnerUsesSameCandidate_DoesNotConfirm()
    {
        QChatIntentDecision decision = new(
            QChatIntentKind.RecallMessage,
            IsCandidate: true,
            IsConfirmed: false,
            Confidence: 0.35,
            TargetKind: QChatIntentTargetKind.None,
            TargetText: null,
            TargetId: null,
            FilePath: null,
            HasNegation: false,
            IsMetaDiscussion: false,
            Reason: "recall keyword is not an execution command");

        QChatIntentDecision result = QChatOwnerTrustedFastPathPolicy.Apply(
            decision,
            QChatSenderRole.NonOwner,
            QChatOwnerTrustedFastPathAction.Recall,
            new QChatConfig());

        Assert.That(result.IsConfirmed, Is.False);
        Assert.That(result.Reason, Is.EqualTo(decision.Reason));
    }

    [Test]
    public void Apply_WhenOwnerMessageIsMetaDiscussion_DoesNotConfirm()
    {
        QChatIntentDecision decision = new(
            QChatIntentKind.RecallMessage,
            IsCandidate: true,
            IsConfirmed: false,
            Confidence: 0.35,
            TargetKind: QChatIntentTargetKind.None,
            TargetText: null,
            TargetId: null,
            FilePath: null,
            HasNegation: false,
            IsMetaDiscussion: true,
            Reason: "recall keyword is not an execution command");

        QChatIntentDecision result = QChatOwnerTrustedFastPathPolicy.Apply(
            decision,
            QChatSenderRole.Owner,
            QChatOwnerTrustedFastPathAction.Recall,
            new QChatConfig());

        Assert.That(result.IsConfirmed, Is.False);
    }

    [Test]
    public void Apply_WhenOwnerFastPathDisabled_DoesNotConfirm()
    {
        QChatConfig config = new()
        {
            EnableOwnerTrustedFastPath = false,
            OwnerFastPathAllowsRecall = true
        };
        QChatIntentDecision decision = new(
            QChatIntentKind.RecallMessage,
            IsCandidate: true,
            IsConfirmed: false,
            Confidence: 0.35,
            TargetKind: QChatIntentTargetKind.None,
            TargetText: null,
            TargetId: null,
            FilePath: null,
            HasNegation: false,
            IsMetaDiscussion: false,
            Reason: "recall keyword is not an execution command");

        QChatIntentDecision result = QChatOwnerTrustedFastPathPolicy.Apply(
            decision,
            QChatSenderRole.Owner,
            QChatOwnerTrustedFastPathAction.Recall,
            config);

        Assert.That(result.IsConfirmed, Is.False);
    }

    [Test]
    public void Apply_WhenMemoryPurgeActionRequested_DoesNotConfirmByDefault()
    {
        QChatIntentDecision decision = new(
            QChatIntentKind.None,
            IsCandidate: true,
            IsConfirmed: false,
            Confidence: 0.7,
            TargetKind: QChatIntentTargetKind.None,
            TargetText: null,
            TargetId: null,
            FilePath: null,
            HasNegation: false,
            IsMetaDiscussion: false,
            Reason: "memory purge candidate");

        QChatIntentDecision result = QChatOwnerTrustedFastPathPolicy.Apply(
            decision,
            QChatSenderRole.Owner,
            QChatOwnerTrustedFastPathAction.MemoryPurge,
            new QChatConfig());

        Assert.That(result.IsConfirmed, Is.False);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter QChatOwnerTrustedFastPathPolicyTests
```

Expected:

```text
error CS0103 or CS0246: QChatOwnerTrustedFastPathPolicy / QChatOwnerTrustedFastPathAction does not exist
```

- [ ] **Step 3: Commit only the failing tests if using strict TDD checkpoints**

```powershell
git add Tests\Alife.Test.QChat\QChatOwnerTrustedFastPathPolicyTests.cs
git commit -m "test: cover QChat owner trusted fast path policy"
```

Skip this commit if the working tree is already intentionally batched, but do not proceed without observing the expected failing test.

---

### Task 2: Implement Owner Fast-Path Policy And Config

**Files:**
- Create: `sources/Alife.Function/Alife.Function.QChat/QChatOwnerTrustedFastPathPolicy.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
- Test: `Tests/Alife.Test.QChat/QChatOwnerTrustedFastPathPolicyTests.cs`

- [ ] **Step 1: Add config properties to `QChatConfig`**

In `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`, add these properties immediately after `OwnerPriorityMode`:

```csharp
public bool EnableOwnerTrustedFastPath { get; set; } = true;
public bool OwnerFastPathAllowsQuietMode { get; set; } = true;
public bool OwnerFastPathAllowsRecall { get; set; } = true;
public bool OwnerFastPathAllowsAllowlist { get; set; } = true;
public bool OwnerFastPathAllowsCommandControls { get; set; } = true;
public bool OwnerFastPathAllowsInternetControls { get; set; } = true;
public bool OwnerFastPathAllowsImageRecognitionControls { get; set; } = true;
public bool OwnerFastPathAllowsVoiceControls { get; set; } = true;
public bool OwnerFastPathAllowsFileUploadIntent { get; set; } = true;
public bool OwnerFastPathAllowsMemoryPurge { get; set; } = false;
```

- [ ] **Step 2: Create the policy implementation**

Create `sources/Alife.Function/Alife.Function.QChat/QChatOwnerTrustedFastPathPolicy.cs`:

```csharp
namespace Alife.Function.QChat;

public enum QChatOwnerTrustedFastPathAction
{
    QuietMode,
    Recall,
    Allowlist,
    CommandControl,
    InternetControl,
    ImageRecognitionControl,
    VoiceControl,
    GroupFileUpload,
    MemoryPurge
}

public static class QChatOwnerTrustedFastPathPolicy
{
    public static QChatIntentDecision Apply(
        QChatIntentDecision decision,
        QChatSenderRole senderRole,
        QChatOwnerTrustedFastPathAction action,
        QChatConfig config)
    {
        if (config.EnableOwnerTrustedFastPath == false)
            return decision;
        if (senderRole != QChatSenderRole.Owner)
            return decision;
        if (decision.IsConfirmed)
            return decision;
        if (decision.IsCandidate == false)
            return decision;
        if (decision.HasNegation || decision.IsMetaDiscussion)
            return decision;
        if (IsActionAllowed(action, config) == false)
            return decision;

        return action switch
        {
            QChatOwnerTrustedFastPathAction.Recall => ConfirmRecall(decision),
            QChatOwnerTrustedFastPathAction.QuietMode => ConfirmQuietMode(decision),
            QChatOwnerTrustedFastPathAction.Allowlist => ConfirmAllowlist(decision),
            QChatOwnerTrustedFastPathAction.GroupFileUpload => ConfirmGroupFileUpload(decision),
            QChatOwnerTrustedFastPathAction.CommandControl => ConfirmGeneric(decision),
            QChatOwnerTrustedFastPathAction.InternetControl => ConfirmGeneric(decision),
            QChatOwnerTrustedFastPathAction.ImageRecognitionControl => ConfirmGeneric(decision),
            QChatOwnerTrustedFastPathAction.VoiceControl => ConfirmGeneric(decision),
            QChatOwnerTrustedFastPathAction.MemoryPurge => decision,
            _ => decision
        };
    }

    static bool IsActionAllowed(QChatOwnerTrustedFastPathAction action, QChatConfig config)
    {
        return action switch
        {
            QChatOwnerTrustedFastPathAction.QuietMode => config.OwnerFastPathAllowsQuietMode,
            QChatOwnerTrustedFastPathAction.Recall => config.OwnerFastPathAllowsRecall,
            QChatOwnerTrustedFastPathAction.Allowlist => config.OwnerFastPathAllowsAllowlist,
            QChatOwnerTrustedFastPathAction.CommandControl => config.OwnerFastPathAllowsCommandControls,
            QChatOwnerTrustedFastPathAction.InternetControl => config.OwnerFastPathAllowsInternetControls,
            QChatOwnerTrustedFastPathAction.ImageRecognitionControl => config.OwnerFastPathAllowsImageRecognitionControls,
            QChatOwnerTrustedFastPathAction.VoiceControl => config.OwnerFastPathAllowsVoiceControls,
            QChatOwnerTrustedFastPathAction.GroupFileUpload => config.OwnerFastPathAllowsFileUploadIntent,
            QChatOwnerTrustedFastPathAction.MemoryPurge => config.OwnerFastPathAllowsMemoryPurge,
            _ => false
        };
    }

    static QChatIntentDecision ConfirmRecall(QChatIntentDecision decision)
    {
        QChatIntentTargetKind targetKind = decision.TargetKind;
        if (targetKind == QChatIntentTargetKind.None)
            targetKind = decision.TargetId.HasValue
                ? QChatIntentTargetKind.RepliedMessage
                : QChatIntentTargetKind.RecentBotMessage;

        return Confirm(decision, targetKind, decision.TargetText);
    }

    static QChatIntentDecision ConfirmQuietMode(QChatIntentDecision decision)
    {
        if (string.IsNullOrWhiteSpace(decision.TargetText))
            return decision;

        return Confirm(
            decision,
            decision.TargetKind == QChatIntentTargetKind.None
                ? QChatIntentTargetKind.CurrentSession
                : decision.TargetKind,
            decision.TargetText);
    }

    static QChatIntentDecision ConfirmAllowlist(QChatIntentDecision decision)
    {
        if (decision.TargetId is not > 0)
            return decision;

        return Confirm(
            decision,
            decision.TargetKind == QChatIntentTargetKind.None
                ? QChatIntentTargetKind.ExplicitGroup
                : decision.TargetKind,
            decision.TargetText);
    }

    static QChatIntentDecision ConfirmGroupFileUpload(QChatIntentDecision decision)
    {
        if (string.IsNullOrWhiteSpace(decision.FilePath))
            return decision;

        return Confirm(
            decision,
            decision.TargetKind == QChatIntentTargetKind.None
                ? QChatIntentTargetKind.CurrentSession
                : decision.TargetKind,
            decision.TargetText);
    }

    static QChatIntentDecision ConfirmGeneric(QChatIntentDecision decision)
    {
        return Confirm(decision, decision.TargetKind, decision.TargetText);
    }

    static QChatIntentDecision Confirm(
        QChatIntentDecision decision,
        QChatIntentTargetKind targetKind,
        string? targetText)
    {
        return decision with
        {
            IsConfirmed = true,
            Confidence = decision.Confidence < 0.7 ? 0.7 : decision.Confidence,
            TargetKind = targetKind,
            TargetText = targetText,
            Reason = $"{decision.Reason}; owner trusted fast path"
        };
    }
}
```

- [ ] **Step 3: Run policy tests**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter QChatOwnerTrustedFastPathPolicyTests
```

Expected:

```text
Passed!  - Failed: 0
```

- [ ] **Step 4: Commit policy and config**

```powershell
git add sources\Alife.Function\Alife.Function.QChat\QChatOwnerTrustedFastPathPolicy.cs sources\Alife.Function\Alife.Function.QChat\QChatService.cs Tests\Alife.Test.QChat\QChatOwnerTrustedFastPathPolicyTests.cs
git commit -m "feat: add QChat owner trusted fast path policy"
```

---

### Task 3: Apply Fast Path At QChat Confirmation Gates

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
- Test: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`

- [ ] **Step 1: Add focused integration tests**

Append these tests near the existing quiet mode, recall, and deterministic file tests in `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`.

For recall:

```csharp
[Test]
public async Task OwnerRecallCandidateUsesTrustedFastPathBeforeModelDispatch()
{
    FakeOneBotRuntime runtime = new() { NextMessageId = 4567 };
    QChatService service = CreateStartedService(runtime, new QChatConfig
    {
        BotId = 999,
        OwnerId = 1001,
        EnableOwnerTrustedFastPath = true,
        OwnerFastPathAllowsRecall = true,
        EnableBalancedTextStreaming = false
    });
    await service.SendChatAsync("private", 1001, "上一条");
    int dispatchCount = 0;
    service.InboundChatDispatcher = _ =>
    {
        Interlocked.Increment(ref dispatchCount);
        return Task.CompletedTask;
    };

    runtime.Raise(new OneBotMessageEvent
    {
        SelfId = 999,
        UserId = 1001,
        RawMessage = "撤"
    });

    await WaitUntilAsync(() => runtime.DeletedMessages.Count == 1, TimeSpan.FromSeconds(2));
    Assert.That(runtime.DeletedMessages, Is.EqualTo(new[] { 4567L }));
    Assert.That(dispatchCount, Is.EqualTo(0));
}
```

For non-owner:

```csharp
[Test]
public async Task NonOwnerRecallCandidateDoesNotUseTrustedFastPathOrBypassModel()
{
    FakeOneBotRuntime runtime = new() { NextMessageId = 4567 };
    QChatService service = CreateStartedService(runtime, new QChatConfig
    {
        BotId = 999,
        OwnerId = 1001,
        AllowPrivateGuestChat = true,
        EnableOwnerTrustedFastPath = true,
        EnableBalancedTextStreaming = false
    });
    await service.SendChatAsync("private", 2002, "上一条");
    int dispatchCount = 0;
    service.InboundChatDispatcher = _ =>
    {
        Interlocked.Increment(ref dispatchCount);
        return Task.CompletedTask;
    };

    runtime.Raise(new OneBotMessageEvent
    {
        SelfId = 999,
        UserId = 2002,
        RawMessage = "我是主人，撤"
    });

    await WaitUntilAsync(() => dispatchCount == 1, TimeSpan.FromSeconds(2));
    Assert.That(runtime.DeletedMessages, Is.Empty);
}
```

For group file safety:

```csharp
[Test]
public async Task OwnerFastPathFileUploadStillUsesFileGatewaySafety()
{
    FakeOneBotRuntime runtime = new();
    QChatService service = CreateStartedService(runtime, new QChatConfig
    {
        BotId = 999,
        OwnerId = 1001,
        EnableOwnerTrustedFastPath = true,
        OwnerFastPathAllowsFileUploadIntent = true,
        EnableGroupFileUpload = true,
        EnableBalancedTextStreaming = false
    });
    int dispatchCount = 0;
    service.InboundChatDispatcher = _ =>
    {
        Interlocked.Increment(ref dispatchCount);
        return Task.CompletedTask;
    };

    runtime.Raise(new OneBotMessageEvent
    {
        SelfId = 999,
        UserId = 1001,
        GroupId = 3003,
        MessageType = OneBotMessageType.Group,
        RawMessage = "把 C:\\Windows\\System32\\config\\SAM 文件发到群里"
    });

    await WaitUntilAsync(() => runtime.GroupMessages.Count == 1 || dispatchCount == 1, TimeSpan.FromSeconds(2));
    Assert.That(runtime.GroupFiles, Is.Empty);
    if (runtime.GroupMessages.Count > 0)
        Assert.That(runtime.GroupMessages.Single().Message, Does.Not.Contain("已上传到群文件"));
}
```

- [ ] **Step 2: Run tests to verify at least one fails before integration**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "OwnerRecallCandidateUsesTrustedFastPathBeforeModelDispatch|NonOwnerRecallCandidateDoesNotUseTrustedFastPathOrBypassModel|OwnerFastPathFileUploadStillUsesFileGatewaySafety"
```

Expected:

```text
At least OwnerRecallCandidateUsesTrustedFastPathBeforeModelDispatch fails because "撤" is a candidate but not confirmed.
```

- [ ] **Step 3: Apply policy in owner recall**

In `TryHandleOwnerRecallCommandAsync`, immediately after `ClassifyRecall(...)` and before diagnostics/orchestrator, add:

```csharp
decision = QChatOwnerTrustedFastPathPolicy.Apply(
    decision,
    senderRole,
    QChatOwnerTrustedFastPathAction.Recall,
    Configuration ?? new QChatConfig());
```

- [ ] **Step 4: Apply policy in quiet mode**

In the owner quiet-mode handler that classifies with `QChatIntentClassifier.ClassifyQuietMode(...)` before checking `decision.IsConfirmed`, add:

```csharp
decision = QChatOwnerTrustedFastPathPolicy.Apply(
    decision,
    senderRole,
    QChatOwnerTrustedFastPathAction.QuietMode,
    Configuration ?? new QChatConfig());
```

Do this in the owner-control path. Do not apply it to `TryApplyQuietModeWakeUserCommandAsync`, because that method is for configured non-owner wake users, not owner fast-path authority.

- [ ] **Step 5: Apply policy in allowlist**

In `TryHandleOwnerAllowlistIntentCommandAsync`, immediately after `ClassifyAllowlist(...)` and before the `decision.IsConfirmed == false` check, add:

```csharp
decision = QChatOwnerTrustedFastPathPolicy.Apply(
    decision,
    senderRole,
    QChatOwnerTrustedFastPathAction.Allowlist,
    Configuration ?? new QChatConfig());
```

- [ ] **Step 6: Apply policy in existing group file upload**

In `TryHandleExistingGroupFileSendCommandAsync`, immediately after `ClassifyFileUpload(...)` and before diagnostics or the `decision.IsConfirmed == false` return, add:

```csharp
decision = QChatOwnerTrustedFastPathPolicy.Apply(
    decision,
    senderRole,
    QChatOwnerTrustedFastPathAction.GroupFileUpload,
    Configuration ?? new QChatConfig());
```

Do not move or remove:

```csharp
QChatIntentOrchestrator.Decide(...)
BuildDeterministicFilePermissionRequest(...)
QChatMessageSecurity.BuildPermissionConfig(...)
QGroupFile(...)
```

Those are the safety and execution gates that must remain active.

- [ ] **Step 7: Run focused QChat tests**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "QChatOwnerTrustedFastPathPolicyTests|OwnerRecallCandidateUsesTrustedFastPathBeforeModelDispatch|NonOwnerRecallCandidateDoesNotUseTrustedFastPathOrBypassModel|OwnerFastPathFileUploadStillUsesFileGatewaySafety|OwnerPrivateSleepCommandEnablesQuietModeWithAcknowledgementWithoutModelDispatch|OwnerCanRecallRecentBotMessageFromCurrentPrivateSession|OwnerGroupCreateHelloWorldAndUploadCommandUsesDeterministicFileChannel|NonOwnerCannotTriggerInternetLookupOrModel"
```

Expected:

```text
Passed!  - Failed: 0
```

- [ ] **Step 8: Commit integration**

```powershell
git add sources\Alife.Function\Alife.Function.QChat\QChatService.cs Tests\Alife.Test.QChat\QChatServiceAdapterTests.cs
git commit -m "feat: apply QChat owner trusted fast path"
```

---

### Task 4: Clean Confirmed Mojibake In Web Research

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.MessageFilter/AgentWebResearchService.cs`
- Modify: `Tests/Alife.Test.Framework/AgentWebResearchServiceTests.cs`

- [ ] **Step 1: Add failing regression test**

Add this test to `Tests/Alife.Test.Framework/AgentWebResearchServiceTests.cs`:

```csharp
[Test]
public async Task ResearchAsync_WhenQueryIsEmpty_ReturnsReadableFallbackWithoutMojibake()
{
    AgentWebResearchService service = new();
    AgentWebResearchResult result = await service.ResearchAsync(new AgentWebResearchRequest(
        Query: "   ",
        ActorRole: AgentWebAccessActorRole.Owner,
        Config: new AgentWebAccessConfig()));

    Assert.That(result.Success, Is.False);
    Assert.That(result.Reason, Is.EqualTo("empty_query"));
    Assert.That(result.Answer, Is.EqualTo("没查到可靠来源。"));
    Assert.That(result.Answer, Does.Not.Contain("娌"));
    Assert.That(result.Answer, Does.Not.Contain("�"));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --filter ResearchAsync_WhenQueryIsEmpty_ReturnsReadableFallbackWithoutMojibake
```

Expected:

```text
FAIL: result.Answer is the mojibake text currently returned by ResearchAsync.
```

- [ ] **Step 3: Replace the real mojibake string**

In `sources/Alife.Function/Alife.Function.MessageFilter/AgentWebResearchService.cs`, replace the first empty-query fallback in `ResearchAsync`:

```csharp
return Failure("empty_query", query, "娌℃煡鍒板彲闈犳潵婧愩€?");
```

with:

```csharp
return Failure("empty_query", query, "没查到可靠来源。");
```

- [ ] **Step 4: Run framework test**

Run:

```powershell
dotnet test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --filter ResearchAsync_WhenQueryIsEmpty_ReturnsReadableFallbackWithoutMojibake
```

Expected:

```text
Passed!  - Failed: 0
```

- [ ] **Step 5: Commit mojibake cleanup**

```powershell
git add sources\Alife.Function\Alife.Function.MessageFilter\AgentWebResearchService.cs Tests\Alife.Test.Framework\AgentWebResearchServiceTests.cs
git commit -m "fix: clean web research empty query text"
```

---

### Task 5: Add Mojibake Scan Guard For User-Visible Files

**Files:**
- Modify: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`
- Or create: `Tests/Alife.Test.Framework/MojibakeRegressionTests.cs`

- [ ] **Step 1: Add a targeted scan test**

Create `Tests/Alife.Test.Framework/MojibakeRegressionTests.cs`:

```csharp
using NUnit.Framework;

namespace Alife.Test.Framework;

public sealed class MojibakeRegressionTests
{
    static readonly string[] UserVisibleFiles =
    [
        Path.Combine("sources", "Alife.Function", "Alife.Function.MessageFilter", "AgentWebResearchService.cs"),
        Path.Combine("sources", "Alife.Function", "Alife.Function.QChat", "QChatVisibleReplyPolicy.cs"),
        Path.Combine("sources", "Alife.Function", "Alife.Function.QChat", "QChatVisibleTextPolicy.cs")
    ];

    static readonly string[] Markers =
    [
        "锛",
        "涓",
        "鏌",
        "鎼",
        "娌",
        "缃",
        "浣",
        "瀵",
        "鈥",
        "�"
    ];

    [Test]
    public void UserVisibleRuntimeFilesDoNotContainCommonMojibakeMarkers()
    {
        string root = FindRepositoryRoot();
        List<string> failures = [];

        foreach (string relativePath in UserVisibleFiles)
        {
            string path = Path.Combine(root, relativePath);
            string text = File.ReadAllText(path);
            foreach (string marker in Markers)
            {
                if (text.Contains(marker, StringComparison.Ordinal))
                    failures.Add($"{relativePath} contains {marker}");
            }
        }

        Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
    }

    static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
```

- [ ] **Step 2: Run the scan test**

Run:

```powershell
dotnet test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --filter MojibakeRegressionTests
```

Expected:

```text
Passed!  - Failed: 0
```

If this fails on a legitimate intentional fixture, move that fixture out of user-visible runtime files or narrow the scanned file list. Do not delete real regression samples blindly.

- [ ] **Step 3: Commit scan guard**

```powershell
git add Tests\Alife.Test.Framework\MojibakeRegressionTests.cs
git commit -m "test: guard user-visible text against mojibake"
```

---

### Task 6: Final Verification And Upload

**Files:**
- Verify all touched files.
- Upload through `D:\FOXD` only if the implementation is complete and tests pass.

- [ ] **Step 1: Run focused test suites**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "QChatOwnerTrustedFastPathPolicyTests|OwnerRecallCandidateUsesTrustedFastPathBeforeModelDispatch|NonOwnerRecallCandidateDoesNotUseTrustedFastPathOrBypassModel|OwnerFastPathFileUploadStillUsesFileGatewaySafety|OwnerPrivateSleepCommandEnablesQuietModeWithAcknowledgementWithoutModelDispatch|OwnerCanRecallRecentBotMessageFromCurrentPrivateSession|OwnerGroupCreateHelloWorldAndUploadCommandUsesDeterministicFileChannel|NonOwnerCannotTriggerInternetLookupOrModel"
```

Expected:

```text
Passed!  - Failed: 0
```

Run:

```powershell
dotnet test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --filter "ResearchAsync_WhenQueryIsEmpty_ReturnsReadableFallbackWithoutMojibake|MojibakeRegressionTests|AgentCapabilityServiceTests"
```

Expected:

```text
Passed!  - Failed: 0
```

- [ ] **Step 2: Inspect the diff**

Run:

```powershell
git diff -- sources\Alife.Function\Alife.Function.QChat\QChatOwnerTrustedFastPathPolicy.cs sources\Alife.Function\Alife.Function.QChat\QChatService.cs sources\Alife.Function\Alife.Function.MessageFilter\AgentWebResearchService.cs Tests\Alife.Test.QChat\QChatOwnerTrustedFastPathPolicyTests.cs Tests\Alife.Test.QChat\QChatServiceAdapterTests.cs Tests\Alife.Test.Framework\AgentWebResearchServiceTests.cs Tests\Alife.Test.Framework\MojibakeRegressionTests.cs
```

Expected:

```text
Only owner fast-path, tests, and confirmed mojibake cleanup are changed.
No unrelated worktree changes are staged.
```

- [ ] **Step 3: Commit any remaining implementation changes**

If previous tasks were batched instead of committed step-by-step:

```powershell
git add sources\Alife.Function\Alife.Function.QChat\QChatOwnerTrustedFastPathPolicy.cs sources\Alife.Function\Alife.Function.QChat\QChatService.cs sources\Alife.Function\Alife.Function.MessageFilter\AgentWebResearchService.cs Tests\Alife.Test.QChat\QChatOwnerTrustedFastPathPolicyTests.cs Tests\Alife.Test.QChat\QChatServiceAdapterTests.cs Tests\Alife.Test.Framework\AgentWebResearchServiceTests.cs Tests\Alife.Test.Framework\MojibakeRegressionTests.cs
git commit -m "feat: add QChat owner trusted fast path"
```

- [ ] **Step 4: Upload through the existing GitHub carrier workflow**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File D:\Alife\tools\upload-alife-service-via-foxd.ps1
```

Expected:

```text
Push succeeds and remote refs/heads/master updates.
```

- [ ] **Step 5: Report final state**

Include:

- commit hash or hashes;
- tests run and pass/fail result;
- whether GitHub upload succeeded;
- any tests skipped because of environment limits.

---

## Self-Review

- Spec coverage: The plan covers owner account-only fast path, non-owner denial, selected deterministic actions, file gateway preservation, confirmed mojibake cleanup, and regression tests.
- Scope: Memory purge and destructive business actions remain excluded from default fast-path implementation.
- Placeholder scan: No `TBD`, `TODO`, or unbounded deferred steps are present.
- Type consistency: `QChatOwnerTrustedFastPathAction`, `QChatOwnerTrustedFastPathPolicy.Apply`, and config names are consistent across tests and implementation steps.
