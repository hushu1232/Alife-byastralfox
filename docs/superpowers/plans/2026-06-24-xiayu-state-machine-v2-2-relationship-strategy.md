# XiaYu State Machine V2.2 Relationship Strategy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add compact XiaYu relationship memory summaries and map them into reply strategy hints, so friendly non-owner chat stays usable while repeated owner-boundary violations become colder and shorter.

**Architecture:** Extend the existing deterministic `XiaYuSelfStateMachine` instead of adding another prompt-only layer. Persist only bounded counters, enum-like labels, and strategy hints; never persist raw QQ text in relationship state. Keep `/qchat` command denial, file, browser, outbox, privacy, and hard safety gates unchanged.

**Tech Stack:** C#/.NET, NUnit, existing `Alife.Function.QChat` XiaYu self-state store and QChat settle-window path.

---

## Restriction Audit

Current QQ restrictions split into two categories:

- **Keep as hard gates:** non-owner `/qchat` command drop, diagnostics/menu suppression, file/desktop/browser high-risk gates, image URL privacy hiding, owner event outbox, blacklists, hard safety boundary, non-owner maintenance/status aliases.
- **Improve as social strategy:** ordinary non-owner friendly chat, bot-addressed group questions, owner-friendly mentions, noisy group rhythm, repeated user annoyance, and owner-boundary violations.

V2.2 must not make non-owners able to use owner commands. It should only make ordinary chat less over-restricted by giving the model a clearer `relationship_profile` and `strategy_hint`.

---

## File Structure

- Modify `sources/Alife.Function/Alife.Function.QChat/XiaYuSelfStateMachine.cs`
  - Add compact relationship profile labels to `XiaYuUserRelationshipState`.
  - Add compact long-term group trend labels to `XiaYuGroupRelationshipState`.
  - Add `StrategyHint` to `XiaYuReplyStrategy`.
  - Derive labels during relationship updates and decay.
  - Format compact user/group relationship hints without raw text.
- Modify `Tests/Alife.Test.QChat/XiaYuSelfStateMachineTests.cs`
  - Add tests for friendly known non-owner behavior.
  - Add tests for repeated boundary violator behavior.
  - Add tests for group trend summaries.
  - Add tests proving relationship strategy does not enable proactive or permissions.
- Modify `docs/superpowers/plans/2026-06-24-xiayu-self-state-machine-v1.md`
  - Add a V2.2 completion note after verification.

---

## Task 1: Relationship Profile Summary

**Files:**
- Modify: `Tests/Alife.Test.QChat/XiaYuSelfStateMachineTests.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/XiaYuSelfStateMachine.cs`

- [x] **Step 1: Write failing tests**

Add tests:

```csharp
[Test]
public void FriendlyKnownNonOwnerGetsWarmNeutralStrategyHint()
{
    XiaYuSelfState state = XiaYuSelfState.CreateDefault("xiayu", Start);
    XiaYuStateTransition transition = null!;

    for (int i = 0; i < 3; i++)
    {
        transition = XiaYuSelfStateMachine.Apply(
            state,
            new XiaYuEventFrame(
                XiaYuEventType.Message,
                QChatConversationKind.Group,
                QChatPersonaSpeakerRole.NonOwner,
                QChatSocialIntent.FriendlyChat,
                QChatBoundaryPressure.None,
                QChatPersonaResponseStance.NeutralBrief,
                QChatOwnerBoundaryRisk.None,
                PromptInjectionRisk: false,
                IsDirectlyAddressed: true,
                HasImage: false,
                MessageTone: XiaYuMessageTone.Friendly,
                SenderId: 2002,
                GroupId: 3001),
            Start.AddMinutes(i + 1));
        state = transition.State;
    }

    XiaYuUserRelationshipState user = transition.State.UserRelationships["2002"];
    Assert.Multiple(() =>
    {
        Assert.That(user.FamiliarityLevel, Is.EqualTo("known"));
        Assert.That(user.TrustLevel, Is.EqualTo("medium"));
        Assert.That(user.AnnoyanceLevel, Is.EqualTo("low"));
        Assert.That(user.HelpfulInteractionCount, Is.EqualTo(3));
        Assert.That(user.LastInteractionTone, Is.EqualTo("friendly"));
        Assert.That(transition.Strategy.Stance, Is.EqualTo(XiaYuReplyStance.Attentive));
        Assert.That(transition.Strategy.StrategyHint, Is.EqualTo("non_owner_friendly_brief"));
        Assert.That(transition.Strategy.AllowSharpReply, Is.False);
        Assert.That(transition.Strategy.AllowProactive, Is.False);
    });
}
```

Also add:

```csharp
RepeatedBoundaryViolatorGetsColdHostileStrategyHint
GroupTrendSummarizesOwnerTopicAndBoundaryRisk
FormatterIncludesRelationshipStrategyWithoutRawText
RelationshipStrategyDoesNotBypassPermissions
```

- [x] **Step 2: Run tests and verify they fail**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "FriendlyKnownNonOwnerGetsWarmNeutralStrategyHint|RepeatedBoundaryViolatorGetsColdHostileStrategyHint|GroupTrendSummarizesOwnerTopicAndBoundaryRisk|FormatterIncludesRelationshipStrategyWithoutRawText|RelationshipStrategyDoesNotBypassPermissions" --no-restore
```

Expected: compile failure because profile label fields and `StrategyHint` do not exist yet.

- [x] **Step 3: Implement minimal state fields**

Add to `XiaYuUserRelationshipState`:

```csharp
public string FamiliarityLevel { get; set; } = "stranger";
public string TrustLevel { get; set; } = "low";
public string AnnoyanceLevel { get; set; } = "low";
public int OwnerBoundaryViolationCount { get; set; }
public int HelpfulInteractionCount { get; set; }
public string LastInteractionTone { get; set; } = "unknown";
```

Add to `XiaYuGroupRelationshipState`:

```csharp
public string TypicalRhythm { get; set; } = "normal";
public int OwnerTopicCount { get; set; }
public int BoundaryRiskCount { get; set; }
public string NoiseTrend { get; set; } = "normal";
public string LastStrategyHint { get; set; } = "group_watch";
```

Update `Clone()` for both classes.

- [x] **Step 4: Derive relationship labels**

Update `UpdateRelationshipState`:

- Friendly/practical non-owner:
  - increment `HelpfulInteractionCount`
  - `LastInteractionTone = "friendly"`
  - derive `FamiliarityLevel` from helpful + friendly interactions
- Owner-boundary threat:
  - increment `OwnerBoundaryViolationCount`
  - `LastInteractionTone = "boundary_risk"`
  - derive high annoyance
- Group owner topic:
  - increment `OwnerTopicCount`
- Group boundary risk:
  - increment `BoundaryRiskCount`
- Group noise:
  - derive `TypicalRhythm` and `NoiseTrend` from compact counters and `NoiseLevel`

- [x] **Step 5: Run tests and verify they pass**

Run the same filtered command. Expected: all selected relationship tests pass.

---

## Task 2: Strategy Hint Mapping

**Files:**
- Modify: `Tests/Alife.Test.QChat/XiaYuSelfStateMachineTests.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/XiaYuSelfStateMachine.cs`

- [x] **Step 1: Write failing strategy assertions**

Assert:

```csharp
Assert.That(transition.Strategy.StrategyHint, Is.EqualTo("non_owner_friendly_brief"));
Assert.That(transition.Strategy.StrategyHint, Is.EqualTo("non_owner_boundary_hostile_short"));
Assert.That(transition.Strategy.StrategyHint, Is.EqualTo("group_owner_defense"));
```

- [x] **Step 2: Run tests and verify they fail**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "FriendlyKnownNonOwnerGetsWarmNeutralStrategyHint|RepeatedBoundaryViolatorGetsColdHostileStrategyHint|RelationshipStrategyDoesNotBypassPermissions" --no-restore
```

- [x] **Step 3: Add `StrategyHint`**

Change `XiaYuReplyStrategy` to:

```csharp
public sealed record XiaYuReplyStrategy(
    XiaYuReplyStance Stance,
    string Length,
    string OwnerBias,
    string NonOwnerPatience,
    bool AllowSharpReply,
    bool AllowProactive,
    string StrategyHint = "default");
```

Map hints in `BuildReplyStrategy`:

- owner: `owner_tender`
- owner-boundary threat: `non_owner_boundary_hostile_short`
- friendly known non-owner: `non_owner_friendly_brief`
- owner-friendly mention: `group_owner_topic_attentive`
- protective pushback: `group_owner_defense`
- normal cold outsider: `non_owner_cold_brief`
- timer: `silent_timer`

- [x] **Step 4: Run tests and verify they pass**

Run the same filtered command. Expected: pass.

---

## Task 3: Prompt Compactness And No Raw Text

**Files:**
- Modify: `Tests/Alife.Test.QChat/XiaYuSelfStateMachineTests.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/XiaYuSelfStateMachine.cs`

- [x] **Step 1: Write prompt assertions**

Add to formatter test:

```csharp
Assert.That(prompt, Does.Contain("strategy_hint=non_owner_friendly_brief"));
Assert.That(prompt, Does.Contain("user_profile=known"));
Assert.That(prompt, Does.Contain("user_trust=medium"));
Assert.That(prompt, Does.Contain("user_annoyance=low"));
Assert.That(prompt, Does.Contain("group_trend=owner_centered"));
Assert.That(prompt, Does.Not.Contain("raw"));
Assert.That(prompt, Does.Not.Contain("message_text"));
Assert.That(prompt.Split('\n'), Has.Length.LessThanOrEqualTo(12));
```

- [x] **Step 2: Implement compact formatter lines**

Keep all additions compact:

```text
reply_stance=... strategy_hint=...
user_profile=known user_trust=medium user_annoyance=low last_tone=friendly
group_rhythm=owner_centered group_trend=owner_centered noise_trend=normal
```

- [x] **Step 3: Run formatter test**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter FormatterIncludesRelationshipStrategyWithoutRawText --no-restore
```

Expected: pass.

---

## Task 4: Restriction Regression

**Files:**
- Modify: `Tests/Alife.Test.QChat/XiaYuSelfStateMachineTests.cs`
- Existing tests to keep green:
  - `QChatCommandAccessPolicyTests`
  - `QChatCapabilityPolicyTests`
  - `QChatSemanticGroupReplyPolicyTests`

- [x] **Step 1: Verify hard restrictions remain**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "QChatCommandAccessPolicyTests|QChatCapabilityPolicyTests|QChatSemanticGroupReplyPolicyTests|RelationshipStrategyDoesNotBypassPermissions" --no-restore
```

Expected: pass. This proves relationship/personality strategy does not grant non-owner command or capability access.

- [x] **Step 2: Verify friendly non-owner chat remains usable**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "NonOwnerFriendlyMessageCanReachModelPathWithPersonaFrame|FriendlyKnownNonOwnerGetsWarmNeutralStrategyHint" --no-restore
```

Expected: pass.

---

## Task 5: Verification, Docs, Upload

**Files:**
- Modify: `docs/superpowers/plans/2026-06-24-xiayu-self-state-machine-v1.md`
- Modify: `docs/superpowers/plans/2026-06-24-xiayu-state-machine-v2-2-relationship-strategy.md`

- [x] **Step 1: Run focused state-machine tests**

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "XiaYuSelfStateMachineTests|QChatCommandAccessPolicyTests|QChatCapabilityPolicyTests|QChatSemanticGroupReplyPolicyTests|NonOwnerFriendlyMessageCanReachModelPathWithPersonaFrame" --no-restore
```

- [x] **Step 2: Build client**

```powershell
dotnet build Sources\Alife\Alife.Client\Alife.Client.csproj --no-restore
```

- [x] **Step 3: Document V2.2 completion**

Append a dated note to the V1 state-machine document:

```text
V2.2 adds compact relationship profiles and reply strategy hints. It loosens ordinary social expression by making friendly non-owner chat explicit, while preserving all hard command/capability restrictions.
```

- [x] **Step 4: Track files and upload**

```powershell
git -C D:\Alife add -- sources/Alife.Function/Alife.Function.QChat/XiaYuSelfStateMachine.cs Tests/Alife.Test.QChat/XiaYuSelfStateMachineTests.cs docs/superpowers/plans/2026-06-24-xiayu-self-state-machine-v1.md docs/superpowers/plans/2026-06-24-xiayu-state-machine-v2-2-relationship-strategy.md
powershell -ExecutionPolicy Bypass -File D:\Alife\tools\upload-alife-service-via-foxd.ps1
```

Expected: upload verifies a new `github/master` commit hash.
