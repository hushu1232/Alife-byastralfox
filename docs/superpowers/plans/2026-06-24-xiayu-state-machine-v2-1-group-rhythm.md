# XiaYu State Machine V2.1 Group Rhythm Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add XiaYu-only compact group rhythm and settled-turn speaker summary state, so group replies can feel aware of multi-speaker conversation pressure without storing raw text or affecting permissions.

**Architecture:** Extend the existing deterministic XiaYu self-state layer. `QChatService` already defers per-message XiaYu frames until model dispatch; V2.1 will enrich the settled frame with compact counts and let `XiaYuSelfStateMachine` update a bounded group rhythm enum. The output remains a private strategy prompt only and must not participate in permission, file, outbox, browser, desktop, or model routing gates.

**Tech Stack:** C#/.NET, NUnit, existing `Alife.Function.QChat` QChat settle-window pipeline, JSON persistence through `XiaYuSelfStateStore`.

---

## File Structure

- Modify `sources/Alife.Function/Alife.Function.QChat/XiaYuSelfStateMachine.cs`
  - Add `XiaYuGroupRhythm`.
  - Add compact group rhythm fields to `XiaYuGroupRelationshipState`.
  - Use `XiaYuEventFrame` turn metadata to update group rhythm.
  - Add compact rhythm fields to `XiaYuStatePromptFormatter`.
- Modify `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
  - Build settled-turn summary from merged frames in `BuildSettledXiaYuEventFrame`.
  - Keep raw text out of the frame and prompt.
- Modify `Tests/Alife.Test.QChat/XiaYuSelfStateMachineTests.cs`
  - Add unit tests for noisy, owner-centered, boundary-risk, quiet/normal rhythm, formatter compactness, and strategy-only behavior.
- Modify `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`
  - Add one integration test proving settle window passes compact speaker summary into the state prompt.
- Modify `docs/superpowers/plans/2026-06-24-xiayu-self-state-machine-v1.md`
  - Add a V2.1 completion note after verification.

---

## Task 1: State Core Group Rhythm

**Files:**
- Modify: `Tests/Alife.Test.QChat/XiaYuSelfStateMachineTests.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/XiaYuSelfStateMachine.cs`

- [x] **Step 1: Write failing tests**

Add tests:

```csharp
[Test]
public void GroupMultiSpeakerTurnBecomesNoisy()
{
    XiaYuSelfState state = XiaYuSelfState.CreateDefault("xiayu", Start);

    XiaYuStateTransition transition = XiaYuSelfStateMachine.Apply(
        state,
        new XiaYuEventFrame(
            XiaYuEventType.Message,
            QChatConversationKind.Group,
            QChatPersonaSpeakerRole.NonOwner,
            QChatSocialIntent.NormalChat,
            QChatBoundaryPressure.None,
            QChatPersonaResponseStance.NeutralBrief,
            QChatOwnerBoundaryRisk.None,
            PromptInjectionRisk: false,
            IsDirectlyAddressed: true,
            HasImage: false,
            SenderId: 2002,
            GroupId: 3001,
            TurnMessageCount: 4,
            TurnSpeakerCount: 3,
            TurnHasMultipleSpeakers: true));

    XiaYuGroupRelationshipState group = transition.State.GroupRelationships["3001"];
    Assert.Multiple(() =>
    {
        Assert.That(group.RecentRhythm, Is.EqualTo(XiaYuGroupRhythm.Noisy));
        Assert.That(group.LastTurnMessageCount, Is.EqualTo(4));
        Assert.That(group.LastTurnSpeakerCount, Is.EqualTo(3));
        Assert.That(group.LastTurnHadMultipleSpeakers, Is.True);
        Assert.That(transition.Strategy.AllowProactive, Is.False);
    });
}
```

Also add:

```csharp
GroupOwnerMentionBecomesOwnerCentered
GroupBoundaryRiskBecomesBoundaryRisk
QuietSingleFriendlyTurnStaysNormalOrQuiet
FormatterIncludesGroupRhythmButNoRawText
GroupRhythmDoesNotAllowProactiveOrPermissions
```

- [x] **Step 2: Run tests and verify they fail**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "GroupMultiSpeakerTurnBecomesNoisy|GroupOwnerMentionBecomesOwnerCentered|GroupBoundaryRiskBecomesBoundaryRisk|QuietSingleFriendlyTurnStaysNormalOrQuiet|FormatterIncludesGroupRhythmButNoRawText|GroupRhythmDoesNotAllowProactiveOrPermissions" --no-restore
```

Expected: compile failure because `XiaYuGroupRhythm` and new group rhythm properties do not exist.

- [x] **Step 3: Implement minimal state changes**

Add:

```csharp
public enum XiaYuGroupRhythm
{
    Quiet,
    Normal,
    Noisy,
    OwnerCentered,
    BoundaryRisk
}
```

Add fields to `XiaYuGroupRelationshipState`:

```csharp
public XiaYuGroupRhythm RecentRhythm { get; set; } = XiaYuGroupRhythm.Normal;
public int LastTurnMessageCount { get; set; } = 1;
public int LastTurnSpeakerCount { get; set; } = 1;
public bool LastTurnHadMultipleSpeakers { get; set; }
```

Update `Clone()` and `UpdateRelationshipState()` to derive rhythm from existing structured frame metadata.

- [x] **Step 4: Run tests and verify they pass**

Run the same filtered command. Expected: all selected state-machine tests pass.

---

## Task 2: Formatter Compact Prompt

**Files:**
- Modify: `Tests/Alife.Test.QChat/XiaYuSelfStateMachineTests.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/XiaYuSelfStateMachine.cs`

- [x] **Step 1: Write failing formatter assertions**

Extend `FormatterIncludesGroupRhythmButNoRawText` to assert:

```csharp
Assert.That(prompt, Does.Contain("group_rhythm=owner_centered"));
Assert.That(prompt, Does.Contain("turn_messages=2"));
Assert.That(prompt, Does.Contain("turn_speakers=2"));
Assert.That(prompt, Does.Contain("multi_speaker=true"));
Assert.That(prompt, Does.Not.Contain("hello"));
Assert.That(prompt, Does.Not.Contain("raw"));
Assert.That(prompt.Split('\n'), Has.Length.LessThanOrEqualTo(12));
```

- [x] **Step 2: Run formatter test and verify it fails**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter FormatterIncludesGroupRhythmButNoRawText --no-restore
```

Expected: failure because rhythm fields are not formatted yet.

- [x] **Step 3: Add compact formatter output**

Add only compact metadata lines when `frame.ConversationKind == QChatConversationKind.Group` and `frame.GroupId > 0`.

- [x] **Step 4: Run formatter test and verify it passes**

Run the same filtered command. Expected: pass.

---

## Task 3: QChat Settled-Turn Integration

**Files:**
- Modify: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`

- [x] **Step 1: Write failing integration test**

Add:

```csharp
[Test]
public async Task ConversationSettleWindowPassesCompactSpeakerSummaryToXiayuState()
{
    // Use the existing fake runtime and settle-window test helpers.
    // Send two buffered group messages from different non-owner users to XiaYu.
    // Assert the captured model input contains:
    // group_rhythm=noisy
    // turn_messages=2
    // turn_speakers=2
    // multi_speaker=true
    // Assert it does not contain raw message text in the XiaYu state block.
}
```

- [x] **Step 2: Run integration test and verify it fails**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter ConversationSettleWindowPassesCompactSpeakerSummaryToXiayuState --no-restore
```

Expected: failure because settled prompt does not include group rhythm fields yet.

- [x] **Step 3: Implement settled-frame summary**

Update `BuildSettledXiaYuEventFrame` to preserve:

```csharp
TurnMessageCount = frames.Count;
TurnSpeakerCount = distinct positive SenderId count;
TurnHasMultipleSpeakers = TurnSpeakerCount > 1;
```

Ensure selected frame is still chosen by priority: boundary-risk, owner, latest.

- [x] **Step 4: Run integration test and verify it passes**

Run the same filtered command. Expected: pass.

---

## Task 4: Verification, Docs, Upload

**Files:**
- Modify: `docs/superpowers/plans/2026-06-24-xiayu-self-state-machine-v1.md`
- Add to git if still untracked:
  - `sources/Alife.Function/Alife.Function.QChat/XiaYuSelfStateMachine.cs`
  - `sources/Alife.Function/Alife.Function.QChat/XiaYuSelfStateStore.cs`
  - `Tests/Alife.Test.QChat/XiaYuSelfStateMachineTests.cs`

- [x] **Step 1: Run focused state-machine and QChat tests**

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "XiaYuSelfStateMachineTests|XiayuInboundModelInputIncludesSelfStateBlock|NonXiayuInboundModelInputDoesNotIncludeSelfStateBlock|XiayuFriendlyOwnerTopicMarksOwnerTopicFocus|XiayuGroupMessagePersistsUserAndGroupRelationshipState|ConversationSettleWindowPassesCompactSpeakerSummaryToXiayuState|ConversationSettleWindowUpdatesXiayuStateOnceForConsecutivePrivateMessages|ConversationSettleWindowDropsRecalledXiayuTriggerWithoutWritingSelfState" --no-restore
```

- [x] **Step 2: Run client build**

```powershell
dotnet build Sources\Alife\Alife.Client\Alife.Client.csproj --no-restore
```

- [x] **Step 3: Document V2.1 completion**

Append a dated note to the V1 plan stating:

```text
V2.1 adds compact group rhythm and settled-turn speaker summary. It stores no raw text and affects only reply strategy.
```

- [x] **Step 4: Track files and upload**

```powershell
git -C D:\Alife add -- sources/Alife.Function/Alife.Function.QChat/XiaYuSelfStateMachine.cs sources/Alife.Function/Alife.Function.QChat/XiaYuSelfStateStore.cs Tests/Alife.Test.QChat/XiaYuSelfStateMachineTests.cs docs/superpowers/plans/2026-06-24-xiayu-self-state-machine-v1.md docs/superpowers/plans/2026-06-24-xiayu-state-machine-v2-1-group-rhythm.md
powershell -ExecutionPolicy Bypass -File D:\Alife\tools\upload-alife-service-via-foxd.ps1
```

Expected: upload verifies a new `github/master` commit hash.
