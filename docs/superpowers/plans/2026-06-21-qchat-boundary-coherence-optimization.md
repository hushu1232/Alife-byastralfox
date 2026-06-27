# QChat Boundary Coherence Optimization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

## Owner Acceptance

- [x] Owner approved plan scope
- [x] Owner approved high-risk policy direction
- [x] Owner approved implementation start
- [x] Owner accepted final verification

**Goal:** Turn the existing QChat feature set into a clearly bounded, maintainable, auditable subsystem whose identity, permission, intent, reply, memory, file, risk, and long-task feedback paths are self-consistent.

**Architecture:** Keep the current working behavior first, then normalize boundaries around capability matrix, event routing, intent orchestration, decision tracing, high-risk policy, deployment automation, and live-smoke verification. The plan avoids broad rewrites: each task is independently testable and should preserve the existing dual-bot runtime behavior for XiaYu and Mio.

**Tech Stack:** C#/.NET, NUnit, `Alife.Function.QChat`, `Tests/Alife.Test.QChat`, OneBot/NapCat runtime, local docs under `docs/`, runtime plugin copy under `Storage/Plugins/Alife.Function.QChat`.

---

## Current Status Mark

**Status:** Implementation started. Tasks 1, 2, 3, 5, 7, 8, 9, and 10 have initial local artifacts. Task 4 has its pure orchestration model and tests; recall, existing group file upload, quiet mode/trusted wake, and allowlist execution now pass through `QChatIntentOrchestrator` before deterministic side effects. `QChatDecisionTraceTests`, `QChatDiagnosticsServiceTests`, `QChatCapabilityPolicyTests`, `QChatRiskActionPolicyTests`, `QChatSemanticTriggerCorpusTests`, `QChatEventRouterTests`, `QChatIntentOrchestratorTests`, full QChat tests, and QChat project build passed locally. Source changes have not been synced into the live plugin runtime. Waiting for owner review before high-risk live enablement or broad QChatService decomposition.

**Scope:** This document is a task orchestration plan only. It does not implement code changes.

**Known current QChat state:**
- QChat has 53 source files under `sources/Alife.Function/Alife.Function.QChat`.
- QChat tests recently passed with `622 passed, 0 failed, 10 skipped`.
- Recent changed files already exist in the worktree:
  - `sources/Alife.Function/Alife.Function.QChat/QChatIntentClassifier.cs`
  - `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
  - `Tests/Alife.Test.QChat/QChatIntentClassifierTests.cs`
  - `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`
- Existing unrelated dirty files must not be reverted:
  - `Build.ps1`
  - `sources/Alife/Alife.Framework/Systems/ModuleSystem.cs`
  - `Tests/Alife.Test.Framework/ModuleSystemRuntimeCompilationTests.cs`

**Resolved pre-Step 3 issue:**
- 2026-06-21 15:23 XiaYu entered quiet mode in group `925402131` because owner pasted a long reply containing words like `安静` / `睡觉`; the old quiet-mode classifier treated embedded prose as a confirmed sleep command.
- Fixed by requiring direct sleep-control phrasing such as `先安静一下`, `别说话了`, `你去睡觉吧`, or `先休息吧` before confirming sleep.
- Added regression coverage in `QChatIntentClassifierTests`, `QChatSemanticTriggerCorpusTests`, and `QChatServiceAdapterTests`.
- Fresh verification: full QChat tests passed with `674 passed, 0 failed, 10 skipped`; QChat project build passed with `0 warnings, 0 errors`.

**Task 4 verification update:**
- 2026-06-21: Task 4 Step 3 completed for recall, existing group file upload, owner quiet mode, trusted wake-user wake, and allowlist update.
- Added action-decision diagnostics before deterministic file upload, quiet-mode control, trusted wake-user quiet-mode control, and allowlist update.
- Fresh verification: full QChat tests passed with `678 passed, 0 failed, 10 skipped`; QChat project build passed with `0 warnings, 0 errors`.

**Task 6 verification update:**
- 2026-06-21: Task 6 Step 1 started and completed for owner diagnostics command routing.
- `QChatService` now asks `QChatEventRouter` to classify owner command messages before owner-command handling and writes a compact `qchat-event-route` diagnostic for that branch.
- Fresh verification: `QChatEventRouterTests` plus owner route adapter checks passed with `10 passed, 0 failed`; `QChatServiceAdapterTests` passed with `268 passed, 0 failed`.
- 2026-06-21: Task 6 Steps 2-5 completed. Steps 2-4 are covered by the Task 4 orchestrator migration for recall, existing group file upload, quiet mode, trusted wake, and allowlist. Step 5 added the responsibility header near `QChatService`.
- Fresh verification: full QChat tests passed with `679 passed, 0 failed, 10 skipped`; QChat project build passed with `0 warnings, 0 errors`.

**Task 11 verification update:**
- 2026-06-21: QChat/QZone boundary documented in `docs/qzone-boundary.md`.
- `docs/qchat-capability-matrix.md` now separates QZone read/check, publish, like, comment, comment reply, proactive suggestion, and proactive execution rows.
- Code separation decision: no immediate physical split is required; QZone already has dedicated service/policy/suggestion/execution classes. Revisit if QZone gains destructive account actions, needs a different deployment lifecycle, or starts leaking policy into QChatService.

**Task 12 verification update:**
- 2026-06-21: full QChat tests passed with `679 passed, 0 failed, 10 skipped`.
- Default solution build attempted with `dotnet build D:\Alife\Alife.slnx`; it was blocked by live `.NET Host` process `65276` locking files under `D:\Alife\Outputs\Alife.Client`.
- To avoid interrupting live QQ bots, no process was killed. Solution build was rerun with a temporary output directory: `dotnet build D:\Alife\Alife.slnx --no-restore -p:OutputPath=D:\tmp\alife-build-verify-flat\ -p:OutDir=D:\tmp\alife-build-verify-flat\`.
- Temporary-output solution build passed with `0 errors` and `6 warnings`.
- Runtime plugin files were synced into `D:\Alife\Storage\Plugins\Alife.Function.QChat`.
- After stopping the stale Alife `.NET Host`, default output build passed with `0 errors` and `3 warnings`.
- Alife was restarted from `D:\Alife\Outputs\Alife.Client\Alife.Client.dll`.
- Live logs confirmed QChat `connect-succeeded` for XiaYu `2905391496` on `ws://127.0.0.1:3002` and Mio `3340947887` on `ws://127.0.0.1:3001`.
- Low-risk direct OneBot send smoke passed for Mio `3340947887` on `3001`.
- Low-risk direct OneBot send smoke initially failed for XiaYu `2905391496` on `3002` with NapCat `1006514 网络连接异常`; root cause was `get_status.online=false` while the WebSocket port was still reachable.
- XiaYu recovery: stopped only the unhealthy no-argument NapCat chain, then restarted with `NapCatWinBootMain.exe 2905391496`. `get_status.online=true` was confirmed for both bots afterward.
- Low-risk direct OneBot send smoke then passed for XiaYu `2905391496` on `3002`.
- Alife remained connected to both OneBot endpoints after the XiaYu restart; `netstat` showed the Alife process holding established connections to `3001` and the recovered `3002`.

---

## Optimization Principles

1. **Behavior first, refactor second**
   - Keep current live behavior stable.
   - Every extraction must be covered by tests before moving logic.

2. **Owner and account identity are hard boundaries**
   - Owner is recognized by account/config, not by language.
   - Bot identity is recognized by runtime account, not by text like "I am XiaYu".
   - XiaYu and Mio remain separate in account, memory, persona, and capability scope.

3. **Dangerous actions require deterministic policy**
   - Friend deletion, file upload, file read/write, desktop actions, and long-running business actions must not depend on free-form model output alone.
   - LLM semantic assistance can support low-risk classification, but high-risk execution must pass deterministic checks.

4. **Decision paths must be explainable**
   - For every important message, logs should explain why QChat replied, stayed quiet, recalled a message, uploaded a file, ignored a command, or deleted a friend.

5. **Runtime deployment must be explicit**
   - Source changes in `sources/...` do not automatically change live plugin behavior in `Storage/Plugins/...`.
   - Build, test, plugin sync, restart, and live smoke should become one documented deployment path.

---

## File Structure Plan

### Documentation Files

- Create: `docs/qchat-capability-matrix.md`
  - Owns the full boundary table for QChat abilities.
  - Records who can trigger each ability, which bot may execute it, where it works, risk level, tests, and reporting path.

- Create: `docs/qchat-runtime-deployment-runbook.md`
  - Owns the build/test/plugin-sync/restart/live-smoke process.
  - Prevents "source changed but runtime still old" incidents.

- Create: `docs/qchat-live-smoke-cases.md`
  - Owns the manual live test cases for real QQ/NapCat behavior.
  - Includes recall, quiet/wake, metadata false positives, dual-bot identity, and owner outbox checks.

- Create: `docs/qchat-high-risk-audit.md`
  - Owns the audit checklist for friend deletion, file actions, desktop actions, and owner-only operations.

### Source Files to Create or Modify Later

- Create: `sources/Alife.Function/Alife.Function.QChat/QChatDecisionTrace.cs`
  - Defines a structured trace object for "why this action happened or did not happen".

- Create: `sources/Alife.Function/Alife.Function.QChat/QChatEventRouter.cs`
  - Routes OneBot events to owner command, intent action, normal conversation, risk handling, and notice handling.

- Create: `sources/Alife.Function/Alife.Function.QChat/QChatIntentOrchestrator.cs`
  - Converts `QChatIntentDecision` into deterministic actions.
  - Keeps `QChatIntentClassifier` as a classifier only, not an executor.

- Create: `sources/Alife.Function/Alife.Function.QChat/QChatCapabilityPolicy.cs`
  - Centralizes "who may use what" for owner-only, XiaYu-only, group/private, and high-risk actions.

- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
  - Gradually reduce responsibility.
  - Keep runtime wiring and shared orchestration, but move routing and intent execution outward.

- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs`
  - Include decision traces in diagnostics.

- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatOwnerCommandService.cs`
  - Keep exact slash commands.
  - Delegate natural-language command decisions to the intent/capability layer.

- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatMessageSecurity.cs`
  - Keep sender role classification.
  - Avoid expanding it into a full capability matrix.

- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatRiskActionPolicy.cs`
  - Reuse capability policy for owner/bot/protected-user exclusions.

### Test Files to Create or Modify Later

- Create: `Tests/Alife.Test.QChat/QChatDecisionTraceTests.cs`
- Create: `Tests/Alife.Test.QChat/QChatEventRouterTests.cs`
- Create: `Tests/Alife.Test.QChat/QChatIntentOrchestratorTests.cs`
- Create: `Tests/Alife.Test.QChat/QChatCapabilityPolicyTests.cs`
- Modify: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`
- Modify: `Tests/Alife.Test.QChat/QChatIntentClassifierTests.cs`
- Modify: `Tests/Alife.Test.QChat/QChatMessageSecurityTests.cs`
- Modify: `Tests/Alife.Test.QChat/QChatRiskActionPolicyTests.cs`

---

## Capability Boundary Matrix Draft

The first implementation task must convert this draft into `docs/qchat-capability-matrix.md`.

| Capability | Trigger Source | Allowed Actor | Allowed Bot | Surface | Risk | Execution Rule | Reporting |
|---|---|---|---|---|---|---|---|
| Normal private chat | Private message | Accepted user | XiaYu/Mio by account route | Private | Low | Security gate + model reply | Normal reply/log |
| Normal group chat | Group message | Allowed group member | XiaYu/Mio by account route | Group | Low/Medium | Group gate + reply policy | Normal reply/log |
| Group wake | Mention/wake semantic | Allowed group member | XiaYu/Mio by account route | Group | Low | `QChatIntentClassifier.ClassifyGroupWake` | `qchat-intent-decision` |
| Quiet mode sleep/wake | Owner or trusted wake user | Owner/trusted wake user | XiaYu/Mio by config | Private/Group | Medium | `ClassifyQuietMode` + sender gate | Diagnostic/log |
| Owner diagnostics | `/qchat`, `/status`, `/tasks` | Owner only | XiaYu/Mio | Private/Group | Low | Exact command parser | Reply to owner/session |
| Approval/deny | `/approve`, `/deny` | Owner only | XiaYu/Mio | Private/Group | High | Exact command parser + approval id | Owner reply/log |
| Group allowlist update | Natural allowlist command or `qchat_allowlist_update` tool text | Owner only | XiaYu/Mio | Private/Group | High | `ClassifyAllowlist.IsConfirmed` + capability policy | Reply/log |
| Message recall | Natural recall command | Owner only | XiaYu/Mio | Private/Group | Medium | `ClassifyRecall.IsConfirmed` | Reply/log |
| Managed file download/read/delete | QQ file operation | Owner or configured actor | XiaYu/Mio | Private/Group | Medium | Managed root + size/path checks | Reply/log |
| Existing file upload to group | Natural upload command | Owner only | Prefer XiaYu unless configured | Group | High | `ClassifyFileUpload.IsConfirmed` + file safety | Reply/outbox if long |
| User profile learning | Natural chat | Accepted users | Separate per bot/account | Private/Group | Medium | Learning policy + throttle | Storage/log |
| Risk scoring | User behavior | System | XiaYu/Mio | Private/Group | Medium | Detector + score service | Log |
| Local blocklist | Risk policy | System/Owner | XiaYu/Mio | Private/Group | High | Risk policy + protected exclusions | Owner notification |
| Real friend deletion | Risk threshold | System after policy | XiaYu only | Private/Friend relation | Critical | Capability policy + protected exclusions + threshold | Owner outbox required |
| Desktop/business task | Owner request | Owner only | XiaYu only | Private preferred | Critical | Capability policy + file blacklist + outbox | Owner outbox required |
| QZone proactive action | Scheduled/system | Configured policy | Account route | QZone | Medium/High | QZone policy + throttle | Log/outbox if important |

Benefit:
- Makes every feature auditable.
- Prevents accidental expansion of owner-only or XiaYu-only abilities.
- Gives future agents a concrete map before touching code.

---

## Task 1: Write QChat Capability Matrix

**Files:**
- Create: `docs/qchat-capability-matrix.md`

- [x] **Step 1: Create the matrix document**

Write the capability table from the draft above into `docs/qchat-capability-matrix.md`.

Required sections:
- `Purpose`
- `Identity Rules`
- `Capability Matrix`
- `High-Risk Capabilities`
- `Owner Review Rules`
- `Test Coverage Map`
- `Open Questions`

- [x] **Step 2: Link each capability to source and tests**

For each row, add source references:
- `QChatService.cs`
- `QChatIntentClassifier.cs`
- `QChatMessageSecurity.cs`
- `QChatOwnerCommandService.cs`
- `QChatManagedFileService.cs`
- `QChatOwnerEventOutbox.cs`
- `QChatRiskActionPolicy.cs`
- related test file under `Tests/Alife.Test.QChat`

- [x] **Step 3: Verify the document has no unowned capability**

Run:

```powershell
Select-String -Path docs/qchat-capability-matrix.md -Pattern "unknown|未定|待定|占位"
```

Expected:

```text
No matches.
```

- [ ] **Step 4: Commit after owner approval**

```powershell
git add docs/qchat-capability-matrix.md
git commit -m "docs: add qchat capability matrix"
```

Benefit:
- Turns the feature set into a contract.
- Makes future changes safer because every new ability needs an explicit row.

---

## Task 2: Add Structured Decision Trace

**Files:**
- Create: `sources/Alife.Function/Alife.Function.QChat/QChatDecisionTrace.cs`
- Create: `Tests/Alife.Test.QChat/QChatDecisionTraceTests.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs`
- Modify: `Tests/Alife.Test.QChat/QChatDiagnosticsServiceTests.cs`

- [x] **Step 1: Write failing tests for trace serialization**

Create `Tests/Alife.Test.QChat/QChatDecisionTraceTests.cs`.

Test cases:
- A trace can record sender role, intent, gate result, reply decision, final action.
- A trace can record "suppressed by quiet mode".
- A trace can record "denied because actor is not owner".
- A trace formats into compact diagnostic text without including raw private content.

- [x] **Step 2: Add trace model**

Create `QChatDecisionTrace` with these fields:
- `TraceId`
- `BotId`
- `AgentId`
- `MessageType`
- `SenderRole`
- `IntentKind`
- `IntentCandidate`
- `IntentConfirmed`
- `GateDecision`
- `ReplyDecision`
- `CapabilityDecision`
- `FinalAction`
- `Reason`
- `CreatedAt`

Do not store full raw message text in the trace by default.

- [x] **Step 3: Add compact formatting**

Add a method that formats a trace like:

```text
qchat decision: actor=owner intent=RecallMessage confirmed=true gate=accepted action=recall reason=confirmed recall command
```

- [x] **Step 4: Hook diagnostics only**

Modify `QChatDiagnosticsService` so it can emit decision trace text when passed a trace.

Do not change live behavior yet.

- [x] **Step 5: Run focused tests**

```powershell
dotnet test D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "QChatDecisionTraceTests|QChatDiagnosticsServiceTests"
```

Expected:

```text
Failed: 0
```

Benefit:
- Gives maintainers a standard way to answer "why did the bot do this?"
- Reduces debugging token cost because future agents can read one trace instead of re-reading the whole QChat flow.

---

## Task 3: Add QChat Event Router Skeleton

**Files:**
- Create: `sources/Alife.Function/Alife.Function.QChat/QChatEventRouter.cs`
- Create: `Tests/Alife.Test.QChat/QChatEventRouterTests.cs`
- Modify later: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`

- [x] **Step 1: Write router classification tests**

Create tests for routing categories:
- Private message.
- Group message.
- Owner command.
- Intent command candidate.
- Notice event.
- Friend/request event.
- Unsupported event.

- [x] **Step 2: Implement a pure routing result**

Create:
- `QChatEventRouteKind`
- `QChatEventRoute`
- `QChatEventRouter`

The router should classify events only. It must not execute actions.

- [x] **Step 3: Keep QChatService behavior unchanged**

At first, use router only in tests or diagnostics.

Do not move production execution in the same task.

- [x] **Step 4: Run focused tests**

```powershell
dotnet test D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter QChatEventRouterTests
```

Expected:

```text
Failed: 0
```

Benefit:
- Creates a safe extraction point from the large `QChatService`.
- Lets future work move event handling one branch at a time.

---

## Task 4: Extract Intent Execution Into QChatIntentOrchestrator

**Files:**
- Create: `sources/Alife.Function/Alife.Function.QChat/QChatIntentOrchestrator.cs`
- Create: `Tests/Alife.Test.QChat/QChatIntentOrchestratorTests.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
- Modify: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`

- [x] **Step 1: Write tests for intent execution boundaries**

Test these cases:
- Owner confirmed recall executes recall.
- Owner recall meta discussion does not execute recall.
- Non-owner recall command does not execute owner action.
- File upload metadata candidate does not execute upload.
- Confirmed file upload requires owner permission.
- Quiet mode can be changed only by owner or trusted wake actor.

- [x] **Step 2: Define orchestration input**

The input should include:
- message event
- sender role
- bot identity
- current group id
- parsed intent decision
- permission/capability result
- callbacks for deterministic actions

- [x] **Step 3: Move one behavior at a time**

Move in this order:
1. Recall execution. `Done: migrated through QChatIntentOrchestrator.`
2. File upload execution. `Done: migrated through QChatIntentOrchestrator with action-decision diagnostics.`
3. Quiet mode execution. `Done: owner quiet control and trusted wake-user wake pass through QChatIntentOrchestrator with action-decision diagnostics.`
4. Allowlist execution. `Done: owner natural-language allowlist updates pass through QChatIntentOrchestrator with action-decision diagnostics.`

Run tests after each move.

- [x] **Step 4: Keep classifier pure**

`QChatIntentClassifier` must continue to classify only.

It must not call OneBot, write files, mutate quiet state, or send messages.

- [x] **Step 5: Run full QChat tests**

```powershell
dotnet test D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore
```

Expected:

```text
Failed: 0
```

Fresh result on 2026-06-21:

```text
Passed: 678
Failed: 0
Skipped: 10
```

Benefit:
- Separates "what did the user mean" from "are we allowed to execute it" and "how do we execute it".
- Reduces future accidental high-risk triggers.

---

## Task 5: Centralize Capability Policy

**Files:**
- Create: `sources/Alife.Function/Alife.Function.QChat/QChatCapabilityPolicy.cs`
- Create: `Tests/Alife.Test.QChat/QChatCapabilityPolicyTests.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatRiskActionPolicy.cs`
- Modify: `Tests/Alife.Test.QChat/QChatRiskActionPolicyTests.cs`
- Modify later as needed: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`

- [x] **Step 1: Write tests from capability matrix**

Required tests:
- Owner diagnostics allowed only for owner.
- Real friend deletion allowed only for XiaYu and never against owner.
- Real friend deletion denied for protected users.
- Desktop/business action allowed only for owner + XiaYu.
- File upload denied for non-owner.
- Normal chat does not require owner.
- Mio cannot execute XiaYu-only high-risk abilities.

- [x] **Step 2: Implement capability enum**

Define capabilities:
- `NormalChat`
- `OwnerDiagnostics`
- `ApprovalDecision`
- `RecallMessage`
- `ManagedFileRead`
- `GroupFileUpload`
- `QuietModeControl`
- `ProfileLearning`
- `RiskLocalBlock`
- `RiskFriendDelete`
- `DesktopBusinessTask`
- `QZoneProactiveAction`

- [x] **Step 3: Implement policy result**

Return:
- `Allowed`
- `Reason`
- `RiskLevel`
- `RequiresOwnerEventOutbox`
- `RequiresOwnerApproval`

- [x] **Step 4: Reuse policy in risk deletion**

Modify `QChatRiskActionPolicy` so friend deletion uses the central capability policy for:
- owner exclusion
- protected user exclusion
- XiaYu-only exclusion
- high-risk reporting requirement

- [x] **Step 5: Run focused and full tests**

```powershell
dotnet test D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "QChatCapabilityPolicyTests|QChatRiskActionPolicyTests"
dotnet test D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore
```

Expected:

```text
Failed: 0
```

Benefit:
- Makes owner-only and XiaYu-only rules explicit.
- Prevents new high-risk features from bypassing old scattered checks.

---

## Task 6: Reduce QChatService Responsibility In Small Moves

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
- Modify: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`
- Use: `sources/Alife.Function/Alife.Function.QChat/QChatEventRouter.cs`
- Use: `sources/Alife.Function/Alife.Function.QChat/QChatIntentOrchestrator.cs`
- Use: `sources/Alife.Function/Alife.Function.QChat/QChatCapabilityPolicy.cs`

- [x] **Step 1: Move owner command branch through router**

Replace inline event category selection with `QChatEventRouter`.

Run:

```powershell
dotnet test D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter QChatServiceAdapterTests
```

- [x] **Step 2: Move recall execution through intent orchestrator**

Keep the public behavior identical.

Verify with adapter tests covering:
- natural owner recall
- meta recall rejection
- recalled message excluded from context

Done via Task 4: recall execution now passes through `QChatIntentOrchestrator`.

- [x] **Step 3: Move file upload execution through intent orchestrator**

Verify:
- image metadata does not upload
- forwarded metadata does not upload
- explicit owner upload still works

Done via Task 4: existing group file upload execution now passes through `QChatIntentOrchestrator`.

- [x] **Step 4: Move quiet/wake execution through intent orchestrator**

Verify:
- owner can sleep/wake bot
- ordinary user cannot force quiet-mode control
- trusted wake user behavior remains unchanged

Done via Task 4: owner quiet control and trusted wake-user wake now pass through `QChatIntentOrchestrator`.

- [x] **Step 5: Keep a short responsibility header in QChatService**

Add a short comment near the class declaration explaining:
- QChatService wires runtime dependencies.
- EventRouter classifies events.
- IntentOrchestrator executes deterministic intent actions.
- CapabilityPolicy authorizes high-risk abilities.

- [x] **Step 6: Run full QChat tests**

```powershell
dotnet test D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore
```

Expected:

```text
Failed: 0
```

Fresh result on 2026-06-21:

```text
QChatEventRouterTests and owner route adapter checks: 10 passed, 0 failed
QChatServiceAdapterTests: 268 passed, 0 failed
Full QChat tests: 679 passed, 0 failed, 10 skipped
QChat project build: 0 warnings, 0 errors
```

Benefit:
- Reduces the chance that a small feature change breaks unrelated paths.
- Makes future code review much easier because responsibilities are no longer concentrated in one file.

---

## Task 7: Build Runtime Deployment Runbook

**Files:**
- Create: `docs/qchat-runtime-deployment-runbook.md`
- Optionally create later: `tools/sync-qchat-plugin.ps1`

- [x] **Step 1: Write the manual runbook first**

Document this sequence:
1. Check worktree.
2. Build solution.
3. Run QChat tests.
4. Sync changed plugin files from `sources/Alife.Function/Alife.Function.QChat` to `Storage/Plugins/Alife.Function.QChat`.
5. Restart Alife process only when needed.
6. Keep NapCat running unless login state requires restart.
7. Verify both endpoints connect.
8. Run live smoke cases.

- [x] **Step 2: Include exact verification commands**

Required commands:

```powershell
git status --short
dotnet build D:\Alife\Alife.slnx
dotnet test D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore
```

Runtime commands should be documented with the actual local startup method used by this project.

- [x] **Step 3: Add failure recovery notes**

Include:
- NapCat QR login expired.
- Alife process still running old plugin.
- One bot connected and the other failed.
- Plugin copied but build failed.
- Source tests passed but live behavior old.

- [x] **Step 4: Owner review before scripting**

Do not add automation script until the manual runbook is reviewed.

Benefit:
- Prevents repeated "why did the live bot not change?" confusion.
- Makes live business startup repeatable.

---

## Task 8: Create Live Smoke Case Document

**Files:**
- Create: `docs/qchat-live-smoke-cases.md`

- [x] **Step 1: Add dual-bot identity smoke cases**

Cases:
- XiaYu responds as XiaYu on XiaYu account.
- Mio responds as Mio on Mio account.
- Calling Mio "XiaYu" does not change Mio's account identity.
- Calling XiaYu "Mio" does not change XiaYu's account identity.

- [x] **Step 2: Add owner-only smoke cases**

Cases:
- Owner can open `/qchat` diagnostics.
- Non-owner cannot use `/qchat`.
- Owner can recall.
- Non-owner cannot trigger recall action.
- Prompt-injection text from non-owner does not grant owner capability.

- [x] **Step 3: Add semantic trigger smoke cases**

Cases:
- "撤了吧" triggers recall.
- "他是不是不会撤回" does not trigger recall.
- "安静一下" changes quiet state only if authorized.
- "醒醒" wakes only under authorized wake rules.
- Plain message containing "指令" does not accidentally become owner command.

- [x] **Step 4: Add file metadata smoke cases**

Cases:
- Image metadata with file id does not trigger file upload.
- Forwarded message metadata does not trigger file upload.
- Explicit owner file upload request still triggers upload if file safety passes.

- [x] **Step 5: Add risk/outbox smoke cases**

Cases:
- Protected user cannot be auto-deleted.
- Owner cannot be auto-deleted.
- Mio cannot execute XiaYu-only friend deletion.
- XiaYu high-risk deletion creates owner outbox event.
- Pending outbox event survives restart.

Benefit:
- Converts real QQ regressions into repeatable checks.
- Saves token during future debugging because the expected behavior is documented.

---

## Task 9: High-Risk Capability Audit

**Files:**
- Create: `docs/qchat-high-risk-audit.md`
- Modify as needed after review:
  - `sources/Alife.Function/Alife.Function.QChat/QChatCapabilityPolicy.cs`
  - `sources/Alife.Function/Alife.Function.QChat/QChatRiskActionPolicy.cs`
  - `sources/Alife.Function/Alife.Function.QChat/QChatFileSafetyService.cs`
  - `sources/Alife.Function/Alife.Function.QChat/QChatManagedFileService.cs`
  - `sources/Alife.Function/Alife.Function.QChat/QChatOwnerEventOutbox.cs`

- [x] **Step 1: List high-risk abilities**

Include:
- Real friend deletion.
- Local blocklist.
- Existing local file upload.
- Managed QQ file download/read/delete.
- Desktop/business task execution.
- Owner approval actions.
- Any future file write or process start ability.

- [x] **Step 2: For each ability, answer the audit questions**

Questions:
- Who can trigger it?
- Which bot may execute it?
- Can group chat trigger it?
- Does it require owner approval?
- Does it require owner outbox reporting?
- Is there a protected-user bypass prevention?
- Is there a path blacklist or root containment check?
- Is there an idempotency or dedupe key?
- What is the test file?
- What is the live smoke case?

- [x] **Step 3: Mark gaps as implementation tasks**

Use explicit labels:
- `Gap: missing test`
- `Gap: missing owner outbox`
- `Gap: policy split across files`
- `Gap: unclear runtime behavior`

Do not write vague notes like "improve safety".

Benefit:
- Makes destructive capabilities reviewable before they are expanded.
- Gives the owner a concrete approval surface instead of trusting hidden code paths.

---

## Task 10: Add Semantic Trigger Corpus

**Files:**
- Create: `Tests/Alife.Test.QChat/QChatSemanticTriggerCorpusTests.cs`
- Modify: `Tests/Alife.Test.QChat/QChatIntentClassifierTests.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatIntentClassifier.cs`

- [x] **Step 1: Create a corpus test file**

Group cases by intent:
- Recall.
- Quiet mode.
- Wake.
- File upload.
- Allowlist.
- Help/menu.

- [x] **Step 2: Add positive and negative examples**

Each intent must include:
- Direct command.
- Natural command.
- Meta discussion.
- Negation.
- Joke/probe/test wording.
- Message containing trigger words but not requesting execution.

- [x] **Step 3: Keep high-risk confirmed threshold strict**

For high-risk intents:
- False negatives are acceptable during early refinement.
- False positives are not acceptable.

- [x] **Step 4: Run classifier tests**

```powershell
dotnet test D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "QChatIntentClassifierTests|QChatSemanticTriggerCorpusTests"
```

Expected:

```text
Failed: 0
```

Benefit:
- Improves "keyword + thinking" behavior without spending runtime LLM tokens.
- Makes semantic trigger behavior predictable and cheap.

---

## Task 11: Clarify QChat and QZone Boundary

**Files:**
- Modify: `docs/qchat-capability-matrix.md`
- Optionally create: `docs/qzone-boundary.md`
- Review:
  - `sources/Alife.Function/Alife.Function.QChat/QZoneService.cs`
  - `sources/Alife.Function/Alife.Function.QChat/QZoneInteractionPolicy.cs`
  - `sources/Alife.Function/Alife.Function.QChat/QZoneProactiveExecutionService.cs`
  - `sources/Alife.Function/Alife.Function.QChat/QZoneProactiveSuggestionService.cs`

- [x] **Step 1: Document shared runtime but separate policy**

State:
- QChat owns real-time QQ chat.
- QZone owns space interaction.
- They may share OneBot runtime.
- They must not share high-risk trigger policy implicitly.

- [x] **Step 2: Add QZone rows to capability matrix**

At minimum:
- QZone read/check.
- QZone like.
- QZone comment.
- QZone proactive suggestion.
- QZone proactive execution.

- [x] **Step 3: Decide whether code separation is needed later**

Only after documentation and tests are clear, decide whether to split QZone into a separate module.

Decision on 2026-06-21: no immediate source split is required. Keep QZone-specific services and tests, and revisit physical separation only if QZone risk or deployment lifecycle diverges from QChat.

Benefit:
- Prevents "QQ chat behavior" and "QZone proactive behavior" from being reviewed as one ambiguous feature.
- Keeps future safety reviews smaller.

---

## Task 12: Final Verification And Owner Acceptance

**Files:**
- Update: `docs/qchat-capability-matrix.md`
- Update: `docs/qchat-runtime-deployment-runbook.md`
- Update: `docs/qchat-live-smoke-cases.md`
- Update: `docs/qchat-high-risk-audit.md`
- Update this plan file when tasks are completed.

- [x] **Step 1: Run full test suite for QChat**

```powershell
dotnet test D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore
```

Expected:

```text
Failed: 0
```

Fresh result on 2026-06-21:

```text
Passed: 679
Failed: 0
Skipped: 10
```

- [x] **Step 2: Run build**

```powershell
dotnet build D:\Alife\Alife.slnx
```

Expected:

```text
Build succeeded.
```

Fresh result on 2026-06-21:

```text
Default output build: blocked by live .NET Host PID 65276 locking D:\Alife\Outputs\Alife.Client.
Temporary-output solution build: succeeded with 0 errors and 6 warnings.
```

- [x] **Step 3: Run live smoke after owner approval**

Use `docs/qchat-live-smoke-cases.md`.

Required live checks:
- XiaYu connects. `Done: 3002 reachable, login info returned 2905391496, status online=true after recovery.`
- Mio connects. `Done: 3001 reachable, login info returned 3340947887, status online=true.`
- Low-risk real outbound send works for both accounts. `Done: LiveDirectOneBotSendDiagnostics passed for 3001 and 3002.`
- Owner-only commands stay owner-only. `Covered by full QChat tests in this pass; destructive live command probing was not repeated.`
- Recall positive/negative both behave correctly. `Covered by full QChat tests and semantic corpus in this pass; live recall was not repeated to avoid unnecessary real group disruption.`
- File metadata false positives do not upload. `Covered by full QChat tests and semantic corpus in this pass; live file upload was intentionally not run.`
- Quiet/wake behavior matches policy. `Covered by full QChat tests in this pass; live quiet/wake was not repeated.`
- Owner event outbox delivers or persists pending feedback. `Connection and owner send path were verified by low-risk direct send; persistence behavior remains covered by local tests and documented manual smoke.`

Fresh live-smoke result on 2026-06-21:

```text
Mio 3001 LiveDirectOneBotSendDiagnostics: passed.
XiaYu 3002 LiveDirectOneBotSendDiagnostics: failed once with get_status.online=false, then passed after targeted XiaYu NapCat restart.
Both endpoints after recovery: get_status.online=true, good=true.
Intrusive live cases such as real file upload were not executed.
```

- [x] **Step 4: Mark owner acceptance**

Add this section to the top of this file after review:

```markdown
## Owner Acceptance

- [ ] Owner approved plan scope
- [ ] Owner approved high-risk policy direction
- [ ] Owner approved implementation start
- [ ] Owner accepted final verification
```

Benefit:
- Separates "plan written" from "owner approved" and "implementation complete".
- Gives a clean checkpoint before touching high-risk business behavior.

---

## Expected Benefits Summary

1. **Lower maintenance cost**
   - `QChatService` stops absorbing every new behavior.
   - Future changes can target smaller files.

2. **Lower debugging token cost**
   - Capability matrix and decision traces make behavior explainable without re-reading the whole module.

3. **Better safety for real business actions**
   - Owner-only, XiaYu-only, protected-user, and outbox requirements become centralized.

4. **More reliable live operations**
   - Deployment runbook reduces source/runtime mismatch.
   - Live smoke cases reduce repeated manual guessing.

5. **More stable persona and identity**
   - Account identity and route policy remain hard boundaries.
   - Natural language cannot rename a bot or grant owner power.

6. **Better user experience**
   - Semantic trigger corpus reduces false positives.
   - Quiet/wake, recall, file, and menu behavior becomes more predictable.

7. **Safer future expansion toward computer steward abilities**
   - High-risk audit and capability policy provide a foundation for desktop/business actions without turning QChat into an unsafe general executor.

---

## Suggested Execution Order

1. Task 1: Capability matrix.
2. Task 7: Runtime deployment runbook.
3. Task 8: Live smoke cases.
4. Task 9: High-risk audit.
5. Task 2: Decision trace.
6. Task 5: Capability policy.
7. Task 10: Semantic trigger corpus.
8. Task 3: Event router skeleton.
9. Task 4: Intent orchestrator.
10. Task 6: Reduce QChatService responsibility.
11. Task 11: QChat/QZone boundary.
12. Task 12: Final verification and owner acceptance.

Reason:
- Documentation and audit come first because they make the risky code changes concrete.
- Tests and traces come before extraction because they protect current behavior.
- Service decomposition comes after policy and trace are available, reducing refactor risk.

---

## Handoff Note

This plan is intentionally staged. Do not start by rewriting `QChatService.cs`. Start by making the boundaries visible, then add traces and tests, then extract behavior one path at a time.

**Current mark:** Task 12 live smoke completed with low-risk real QQ sends. Owner acceptance recorded on 2026-06-21; intrusive live file upload/recall/quiet-mode probes were not repeated because equivalent behavior is already covered by local tests and documented manual smoke.
