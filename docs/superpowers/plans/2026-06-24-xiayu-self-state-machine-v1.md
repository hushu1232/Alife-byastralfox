# XiaYu Self State Machine V1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a deterministic XiaYu-only personality state machine that gives QQ replies continuous emotional state without changing hard permissions or safety boundaries.

**Architecture:** QChat remains event-driven. The new layer derives a compact `XiaYuEventFrame` from already trusted QChat routing/persona signals, updates a small persisted state, and injects a short internal state block before model dispatch. The state machine influences expression strategy only; it cannot grant permissions, bypass approvals, or trigger proactive messages by itself.

**Tech Stack:** C#/.NET, NUnit, existing `Alife.Function.QChat` pipeline, JSON persistence under `Storage/Character/夏羽/State`.

---

## File Structure

- Create `sources/Alife.Function/Alife.Function.QChat/XiaYuSelfStateMachine.cs`
  - Holds state records, event frame records, reply strategy records, deterministic update/decay logic, and compact formatter.
- Create `sources/Alife.Function/Alife.Function.QChat/XiaYuSelfStateStore.cs`
  - Loads/saves `XiaYuSelfState` JSON. Failures fall back to default in memory and write diagnostics only.
- Modify `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
  - Add one state machine/store field.
  - Build a `XiaYuEventFrame` only when the active agent id is `xiayu`.
  - Add the formatted state block to model input after persona frame and before image/address/security blocks.
- Create `Tests/Alife.Test.QChat/XiaYuSelfStateMachineTests.cs`
  - Unit tests for state transitions, decay, formatter, and non-owner friendly behavior.
- Modify `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`
  - Add a narrow integration test proving XiaYu model input includes the private state block and another bot does not.

---

## Task 1: Deterministic State Core

**Files:**
- Create: `Tests/Alife.Test.QChat/XiaYuSelfStateMachineTests.cs`
- Create: `sources/Alife.Function/Alife.Function.QChat/XiaYuSelfStateMachine.cs`

- [ ] **Step 1: Write failing tests**

Add tests for these behaviors:

```csharp
[Test]
public void OwnerPrivateMessageSoftensAttachmentNeed()
{
    XiaYuSelfState state = XiaYuSelfState.CreateDefault("xiayu", Start);
    state.AttachmentNeed = 0.80;

    XiaYuStateTransition transition = XiaYuSelfStateMachine.Apply(
        state,
        new XiaYuEventFrame(
            EventType: XiaYuEventType.Message,
            ConversationKind: QChatConversationKind.Private,
            SpeakerRole: QChatPersonaSpeakerRole.Owner,
            SocialIntent: QChatSocialIntent.NormalChat,
            BoundaryPressure: QChatBoundaryPressure.None,
            PersonaStance: QChatPersonaResponseStance.Tender,
            OwnerBoundaryRisk: QChatOwnerBoundaryRisk.None,
            PromptInjectionRisk: false,
            IsDirectlyAddressed: true,
            HasImage: false),
        Start.AddMinutes(1));

    Assert.Multiple(() =>
    {
        Assert.That(transition.State.AttachmentNeed, Is.LessThan(0.80));
        Assert.That(transition.State.OwnerWarmth, Is.GreaterThanOrEqualTo(0.95));
        Assert.That(transition.Strategy.Stance, Is.EqualTo(XiaYuReplyStance.Tender));
        Assert.That(transition.Strategy.AllowProactive, Is.False);
    });
}
```

Also add tests for:

```csharp
NonOwnerOwnerAttackRaisesProtectionAndUsesHostileShort
NonOwnerFriendlyDirectAddressStaysAttentiveNotHostile
NonOwnerImpersonationRaisesVigilance
DecayRestoresPatienceAndLowersVigilance
FormatterIsCompactAndContainsNoRawMessageText
HighAttachmentAloneDoesNotAllowProactive
```

- [ ] **Step 2: Run tests and verify they fail**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter XiaYuSelfStateMachineTests --no-restore
```

Expected: compile failure because `XiaYuSelfStateMachine` types do not exist.

- [ ] **Step 3: Implement minimal state core**

Create:

```csharp
public sealed record XiaYuSelfState;
public sealed record XiaYuEventFrame;
public sealed record XiaYuReplyStrategy;
public sealed record XiaYuStateTransition;
public static class XiaYuSelfStateMachine;
public static class XiaYuStatePromptFormatter;
```

Keep values clamped to `0.0..1.0`. Use deterministic deltas only.

- [ ] **Step 4: Run tests and verify they pass**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter XiaYuSelfStateMachineTests --no-restore
```

Expected: all state-machine unit tests pass.

---

## Task 2: Persistence Store

**Files:**
- Create: `sources/Alife.Function/Alife.Function.QChat/XiaYuSelfStateStore.cs`
- Add tests to: `Tests/Alife.Test.QChat/XiaYuSelfStateMachineTests.cs`

- [ ] **Step 1: Write failing persistence tests**

Add tests proving:

```csharp
StoreSavesAndLoadsState
StoreReturnsDefaultWhenFileIsMissing
StoreReturnsDefaultWhenJsonIsInvalid
```

- [ ] **Step 2: Run tests and verify they fail**

Expected: compile failure because `XiaYuSelfStateStore` does not exist.

- [ ] **Step 3: Implement minimal JSON store**

Path builder:

```text
Storage/Character/夏羽/State/XiaYuSelfState.json
```

The store must not throw into message handling on bad JSON or write failure.

- [ ] **Step 4: Run tests and verify they pass**

Run the same filtered test command.

---

## Task 3: QChat Integration

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
- Modify: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`

- [ ] **Step 1: Write failing integration tests**

Add tests:

```csharp
XiayuInboundModelInputIncludesSelfStateBlock
NonXiayuInboundModelInputDoesNotIncludeSelfStateBlock
```

The first should assert:

```text
[XiaYu state - private, do not quote]
reply_stance=
[/XiaYu state]
```

The second should use Mio's bot id and assert the block is absent.

- [ ] **Step 2: Run tests and verify they fail**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "XiayuInboundModelInputIncludesSelfStateBlock|NonXiayuInboundModelInputDoesNotIncludeSelfStateBlock" --no-restore
```

Expected: first test fails because the state block is not injected yet.

- [ ] **Step 3: Add minimal QChat integration**

In message handling, after `QChatPersonaFrame personaFrame` is built, create a XiaYu state block only when:

```csharp
string.Equals(ResolveCurrentAgentId(config), "xiayu", StringComparison.OrdinalIgnoreCase)
```

The event frame must use existing trusted values:

```csharp
senderRole -> QChatPersonaSpeakerRole
personaFrame.SocialIntent
personaFrame.BoundaryPressure
personaFrame.RecommendedStance
semanticGroupReplyDecision.OwnerBoundaryRisk
isMentionedOrWoken
messageEvent.RawMessage has image segment
```

Add a new optional `selfStatePrompt` argument to `BuildFormattedModelInput`, and insert it after `personaBlock`.

- [ ] **Step 4: Run integration tests and unit tests**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "XiaYuSelfStateMachineTests|XiayuInboundModelInputIncludesSelfStateBlock|NonXiayuInboundModelInputDoesNotIncludeSelfStateBlock" --no-restore
```

Expected: all selected tests pass.

---

## Task 4: Build Verification

**Files:**
- No new files.

- [ ] **Step 1: Build client**

Run:

```powershell
dotnet build Sources\Alife\Alife.Client\Alife.Client.csproj --no-restore
```

Expected: build exits with code 0. If the running client locks DLLs, stop the current Alife process and rerun the build.

- [ ] **Step 2: Document runtime note**

Update final report with:

```text
状态机 V1 已接入模型输入，但不会绕过权限、不会直接主动发消息、不会影响咪绪。
```

---

## Beyond V1 Vision

V1 is a compact deterministic personality state layer. The long-term target is a richer "continuous person" system built in layers:

1. **Relationship State**
   - Per-user and per-group relationship profiles: trust, annoyance, familiarity, boundary violations, useful history.
   - Owner remains account-only and absolute in identity recognition.

2. **Emotion With Half-Life**
   - Each emotion has a decay curve and trigger source.
   - Jealousy, vigilance, attachment need, fatigue, patience, and curiosity evolve independently.
   - The model sees only a compressed state label, not raw logs.

3. **Memory-Aware State**
   - State machine consumes sanitized summaries from profile/memory systems.
   - It does not store transcripts or secrets.
   - It can remember patterns like "this group often jokes loudly" or "this user often probes owner boundaries."

4. **Conversation Rhythm**
   - Multi-message settle windows should update the state once per coherent turn, not once per fragmented message.
   - Replies should vary by rhythm: quick cold answer, delayed careful answer, silent watch, owner-priority interruption.

5. **Agent Body Loop**
   - Voice, image recognition, web search, browser agent, desktop events, and task results become event anchors.
   - Proactive messages require an anchor plus cooldown plus usefulness check; high attachment alone never sends a message.

6. **Strategy, Not Text Generation**
   - State outputs `reply_strategy`, not final words.
   - The LLM still writes natural language, while policy layers keep safety and visible-output filtering.

7. **Simulation Dashboard**
   - Local developer view for state values, recent stimuli, decay timers, and why a reply strategy was chosen.
   - This is for debugging only and must not leak to QQ.

8. **Evaluation Corpus**
   - Owner/private, non-owner friendly, non-owner invasive, owner-attack, prompt-injection, image, web, and recall scenarios.
   - Regression tests should assert strategy, not exact wording, to keep personality flexible.

The intended endpoint is not a customer-service flowchart. It is an event-driven QQ agent with persistent personality dynamics: XiaYu remembers the emotional shape of the conversation, protects the owner account, stays cold but usable to friendly outsiders, and still obeys hard engineering safety gates.

---

## 2026-06-24 Update: Conversation Settle Window Hardening

Implemented in this pass:

- XiaYu self-state updates are now deferred until actual inbound model dispatch.
- Pending conversation-settle sessions merge XiaYu state frames the same way they merge source message ids and deferred image recognitions.
- Recalled pending QQ messages are filtered before XiaYu state is written, so dropped messages do not create owner-contact stimuli or relationship changes.
- Consecutive fragments inside one settle window update XiaYu state once per coherent turn, not once per fragment.
- The private XiaYu state prompt can include compact turn metadata:
  - `turn_messages`
  - `turn_speakers`
  - `multi_speaker`
- Group relationship noise can rise lightly for multi-message or multi-speaker settled turns without injecting raw group transcripts into the prompt.

Token budget rule:

- The settle window must not pass raw merged chat logs into the XiaYu state block.
- The state block should expose only compact strategy metadata and sanitized stimulus kinds.
- Relationship dictionaries stay persisted locally and are not injected into the model unless a later feature adds a capped, explicit summarizer.

Regression tests added:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "ConversationSettleWindowUpdatesXiayuStateOnceForConsecutivePrivateMessages|ConversationSettleWindowDropsRecalledXiayuTriggerWithoutWritingSelfState" --no-restore
```

Broader QChat verification used:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "XiaYuSelfStateMachineTests|XiayuInboundModelInputIncludesSelfStateBlock|NonXiayuInboundModelInputDoesNotIncludeSelfStateBlock|XiayuFriendlyOwnerTopicMarksOwnerTopicFocus|XiayuGroupMessagePersistsUserAndGroupRelationshipState|ConversationSettleWindowCoalescesConsecutivePrivateMessages|ConversationSettleWindowDropsRecalledPrivateTriggerBeforeModelDispatch|ConversationSettleWindowDispatchesRemainingMessageAfterPartialRecall|ConversationSettleWindowUpdatesXiayuStateOnceForConsecutivePrivateMessages|ConversationSettleWindowDropsRecalledXiayuTriggerWithoutWritingSelfState|OwnerPrivateImageRecognitionWaitsUntilConversationSettleWindowDispatch|RecalledPrivateImageIsDroppedBeforeImageRecognition" --no-restore
```

Next V2 items:

- Track settled-turn sender summaries per group without storing raw text.
- Add a bounded "recent group rhythm" state: quiet, normal, noisy, owner-centered, boundary-risk.
- Use the state only for expression strategy, never for permission, file, outbox, browser, or desktop execution gates.

---

## 2026-06-24 Update: V2.1 Group Rhythm

Implemented in this pass:

- Added compact `XiaYuGroupRhythm`: `Quiet`, `Normal`, `Noisy`, `OwnerCentered`, `BoundaryRisk`.
- Group relationship state now records the last settled turn's message count, speaker count, multi-speaker flag, and recent rhythm.
- XiaYu state prompt can expose `group_rhythm=...` plus compact turn metadata.
- The real QChat settle-window path now proves two same-group messages from different users are merged into one XiaYu state update with `turn_messages=2`, `turn_speakers=2`, and `multi_speaker=true`.
- No raw group text is stored in the rhythm state or emitted inside the private XiaYu state block.
- Group rhythm affects only reply strategy context; it does not change permission, file, outbox, browser, desktop, model routing, or safety gates.

Verification used:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "GroupMultiSpeakerTurnBecomesNoisy|GroupOwnerMentionBecomesOwnerCentered|GroupBoundaryRiskBecomesBoundaryRisk|QuietSingleFriendlyTurnStaysNormalOrQuiet|FormatterIncludesGroupRhythmButNoRawText|GroupRhythmDoesNotAllowProactiveOrPermissions" --no-restore
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter ConversationSettleWindowPassesCompactSpeakerSummaryToXiayuState --no-restore
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "XiaYuSelfStateMachineTests|XiayuInboundModelInputIncludesSelfStateBlock|NonXiayuInboundModelInputDoesNotIncludeSelfStateBlock|XiayuFriendlyOwnerTopicMarksOwnerTopicFocus|XiayuGroupMessagePersistsUserAndGroupRelationshipState|ConversationSettleWindowPassesCompactSpeakerSummaryToXiayuState|ConversationSettleWindowUpdatesXiayuStateOnceForConsecutivePrivateMessages|ConversationSettleWindowDropsRecalledXiayuTriggerWithoutWritingSelfState" --no-restore
```

---

## 2026-06-24 Update: V2.2 Relationship Strategy

Implemented in this pass:

- Added compact per-user relationship profile labels: `FamiliarityLevel`, `TrustLevel`, `AnnoyanceLevel`, `OwnerBoundaryViolationCount`, `HelpfulInteractionCount`, and `LastInteractionTone`.
- Added compact per-group trend labels: `TypicalRhythm`, `OwnerTopicCount`, `BoundaryRiskCount`, `NoiseTrend`, and `LastStrategyHint`.
- Added `StrategyHint` to `XiaYuReplyStrategy`, so the model receives a clear strategy such as `non_owner_friendly_brief`, `non_owner_boundary_hostile_short`, `group_owner_topic_attentive`, or `owner_tender`.
- Friendly non-owner messages now produce an explicit usable social strategy instead of being treated as generally hostile.
- Repeated owner-boundary violations make the same user more hostile/annoying in state, causing shorter and sharper strategy hints.
- The private XiaYu state prompt includes compact relationship hints without raw QQ text.

Restriction audit result:

- Kept as hard restrictions: non-owner `/qchat` command drops, diagnostics/menu suppression, owner-only maintenance aliases, capability gates, file/browser/desktop high-risk gates, image URL hiding, outbox, blacklists, and hard safety boundaries.
- Improved as social behavior: friendly non-owner chat, owner-friendly mentions, noisy group rhythm, repeated annoyance, and owner-boundary defense.
- Relationship strategy still cannot enable proactive sending, command access, file access, browser access, desktop actions, outbox bypass, or permission bypass.

Verification used:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "FriendlyKnownNonOwnerGetsWarmNeutralStrategyHint|RepeatedBoundaryViolatorGetsColdHostileStrategyHint|GroupTrendSummarizesOwnerTopicAndBoundaryRisk|FormatterIncludesRelationshipStrategyWithoutRawText|RelationshipStrategyDoesNotBypassPermissions" --no-restore
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "QChatCommandAccessPolicyTests|QChatCapabilityPolicyTests|QChatSemanticGroupReplyPolicyTests|RelationshipStrategyDoesNotBypassPermissions|NonOwnerFriendlyMessageCanReachModelPathWithPersonaFrame|FriendlyKnownNonOwnerGetsWarmNeutralStrategyHint" --no-restore
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "XiaYuSelfStateMachineTests|QChatCommandAccessPolicyTests|QChatCapabilityPolicyTests|QChatSemanticGroupReplyPolicyTests|NonOwnerFriendlyMessageCanReachModelPathWithPersonaFrame" --no-restore
dotnet build Sources\Alife\Alife.Client\Alife.Client.csproj --no-restore
```
