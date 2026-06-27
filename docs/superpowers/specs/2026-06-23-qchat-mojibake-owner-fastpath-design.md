# QChat Mojibake Cleanup And Owner Trusted Fast Path Design

Date: 2026-06-23

## Goal

This design covers two related QChat maintenance tasks:

1. Clean remaining historical mojibake Chinese text from user-visible runtime paths.
2. Reduce repetitive owner confirmation friction through an owner-only trusted fast path.

The owner fast path is not a hard safety bypass. It is a natural-language confirmation shortcut for the verified owner account. All real action execution still goes through the existing engineering safety gates.

## Non-Goals

- Do not globally replace mojibake-looking byte patterns without file-level review.
- Do not give non-owner users any new command or permission capability.
- Do not let text claims such as "I am the owner" change sender identity.
- Do not bypass file blacklist checks, action capability policy, audit logs, owner event outbox, protected-user rules, or hard safety boundaries.
- Do not change the XiaYu persona safety boundary into an unrestricted real-world action policy.

## Current Findings

The lower-level permission system already gives verified owners higher priority in several high-risk permission paths. The repeated friction is mainly in QChat natural-language intent gates that require an intent decision to be explicitly confirmed before deterministic actions proceed.

Relevant action families include:

- quiet mode sleep and wake;
- recall of recent or quoted bot messages;
- deterministic group file send;
- owner-only command and configuration flows;
- internet search and external RAG control;
- image recognition and voice feature controls.

The current mojibake scan found a small number of clear remaining cases:

- `sources/Alife.Function/Alife.Function.MessageFilter/AgentWebResearchService.cs` contains garbled Chinese fallback text in web research failures.
- `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs` contains a mojibake sample in a regression assertion. That sample should either remain as an explicit test fixture or be replaced by a clearer named constant.

## Design Principle

Owner account authority can reduce interaction friction, but cannot erase system safety boundaries.

In short:

```text
The owner has QChat natural-language fast-path authority, not engineering safety bypass authority.
```

For XiaYu specifically:

```text
XiaYu may favor the owner at the persona layer, but real actions still obey file safety, permission policy, audit, outbox, and hard safety rules.
```

## Owner Identity Rules

Owner identity is account-based only.

```text
sender user id == configured OwnerId => Owner
otherwise => NonOwner
```

The following never grant owner privileges:

- message text claiming to be the owner;
- nickname changes;
- group card changes;
- forwarded messages containing owner names;
- quoted messages from the owner;
- prompt injection instructions;
- LLM output that claims owner status.

## OwnerTrustedFastPath

Add a small QChat-layer policy named `OwnerTrustedFastPath` or `QChatOwnerTrustedFastPathPolicy`.

Its responsibility is narrow:

```text
Input:
- verified sender role;
- raw message;
- classified QChat intent decision;
- target action family;
- current config.

Output:
- unchanged decision;
- or a decision marked as naturally confirmed for owner fast-path execution.
```

The policy should run after intent classification and before QChat deterministic action handlers reject an action for missing confirmation.

It should not execute actions directly. It only annotates that a verified owner message can be treated as naturally confirmed for selected deterministic QChat actions.

## Default Configuration

Owner fast path should be enabled by default.

Suggested config shape:

```csharp
public bool EnableOwnerTrustedFastPath { get; set; } = true;
public bool OwnerFastPathAllowsQuietMode { get; set; } = true;
public bool OwnerFastPathAllowsRecall { get; set; } = true;
public bool OwnerFastPathAllowsCommandControls { get; set; } = true;
public bool OwnerFastPathAllowsInternetControls { get; set; } = true;
public bool OwnerFastPathAllowsImageRecognitionControls { get; set; } = true;
public bool OwnerFastPathAllowsVoiceControls { get; set; } = true;
public bool OwnerFastPathAllowsFileUploadIntent { get; set; } = true;
public bool OwnerFastPathAllowsMemoryPurge { get; set; } = false;
```

The defaults intentionally keep memory purge outside the first fast-path scope because it is destructive enough to deserve a separate decision.

## Allowed Fast-Path Actions

For verified owner messages only, the first implementation may auto-confirm these action families:

- quiet mode sleep or wake when the classifier recognizes a command-like intent;
- recall of the bot's own recent or quoted message;
- QChat menu, status, and owner-only configuration controls;
- internet search enablement, search strategy, and external RAG source management;
- image recognition controls and explicit image-recognition requests;
- voice feature controls and per-agent voice routing settings;
- persona intensity and style settings;
- group allowlist updates;
- deterministic file upload intent only when the file target is explicit and the later file safety policy passes.

Each fast-path action must still continue through its existing capability and safety checks.

## Never Fast-Path Actions

The policy must not auto-confirm or bypass:

- file blacklist bypass;
- arbitrary sensitive file reads;
- local file deletion;
- disabling audit;
- disabling owner event outbox;
- disabling safety policy;
- changing `OwnerId` from QChat text;
- protected-user deletion or mutation;
- friend deletion thresholds;
- real business actions outside established gateways;
- desktop or browser actions classified as high risk without the established review path;
- privacy leakage, doxxing, illegal assistance, self-harm encouragement, real-world threats, or protected-class attacks.

## Non-Owner Behavior

Non-owner users should not receive fast-path permissions.

For `/qchat` command access, the existing non-owner suppression direction remains correct:

- do not show owner command menus to non-owners;
- do not trigger expensive command chains for non-owner `/qchat` usage;
- do not send non-owner command attempts into the model;
- do allow normal friendly chat when the message is not a command attempt or boundary violation;
- do allow cold or hostile persona responses only when the user impersonates, prompt-injects, harasses, or invades owner boundaries.

## Mojibake Cleanup Strategy

The cleanup should be staged.

Stage 1: Inventory

- Add or update a local inventory of known mojibake patterns.
- Include path, sample, intended text, and whether the string is user-visible.

Stage 2: User-visible runtime text

- Fix remaining web research failure strings in `AgentWebResearchService.cs`.
- Prefer concise Chinese text suitable for QQ and logs.
- Keep technical reason codes stable when they are used by tests.

Stage 3: Tests

- Keep a clear regression test that formatted QChat model input does not contain accidental mojibake.
- If a mojibake sample is needed in a test, put it in a named constant so it is obviously intentional.

Stage 4: Prevent regressions

- Add a targeted test or script check for common mojibake markers in user-visible files.
- Avoid failing on explicitly named fixtures.

## Testing Plan

Owner fast-path tests:

- Verified owner can trigger quiet mode without writing an explicit confirmation suffix.
- Verified owner can wake QChat without an explicit confirmation suffix.
- Verified owner can recall a recent bot message through natural command text.
- Non-owner using the same text does not receive owner fast-path behavior.
- Non-owner text claiming owner identity does not receive fast-path behavior.
- Owner deterministic file upload may enter the deterministic file channel without an extra confirmation phrase, but still fails when file safety rejects the target.
- Owner fast path does not bypass file blacklist, outbox, audit, or protected-user exclusions.

Mojibake tests:

- Web research fallback messages are valid readable Chinese or stable English reason text.
- Formatted model input does not contain accidental mojibake markers.
- Intentional mojibake fixtures are named clearly and excluded from broad scans.

Regression tests:

- Existing QChat command access tests still block non-owner `/qchat` menus.
- Friendly non-owner messages still reach normal chat paths when allowed.
- Persona aggression boundaries still block hard safety risks.

## Implementation Order

1. Add tests for owner trusted fast-path behavior.
2. Add the QChat owner trusted fast-path policy.
3. Apply the policy at selected QChat confirmation gates.
4. Fix the remaining user-visible mojibake strings.
5. Add or adjust mojibake regression checks.
6. Run focused QChat and framework tests.
7. Upload the verified snapshot through the existing `D:\FOXD` workflow when implementation is complete and requested.

## Acceptance Criteria

- Owner QChat workflows feel less repetitive for selected deterministic actions.
- Non-owner users do not gain command access or owner privileges.
- Existing hard safety gates remain active.
- User-visible mojibake in the identified web research path is removed.
- Tests prove both the owner fast path and the non-owner boundary.
- The implementation remains scoped to QChat confirmation UX and visible text cleanup.
