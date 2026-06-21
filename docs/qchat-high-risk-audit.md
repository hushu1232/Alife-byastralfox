# QChat High-Risk Capability Audit

## Purpose

This audit defines the review surface for QChat actions that can mutate external state, expose private data, affect QQ relationships, read local files, upload files, or control the computer.

High-risk behavior should not be expanded until this document has a row for the capability and the corresponding tests exist.

## Audit Rules

- Owner-only means owner account only, not text claiming owner status.
- XiaYu-only means runtime bot identity only, not text calling another bot XiaYu.
- Model output is never enough to execute a destructive action.
- Critical actions must have deterministic policy, test coverage, and owner reporting.
- File actions must have root containment, blacklist, or explicit allow policy.
- Long-running or disconnect-prone actions must report through owner event outbox when important.

## Audit Table

| Capability | Actor | Bot Scope | Surface | Risk | Required Gate | Required Report | Test Owner | Current State |
|---|---|---|---|---|---|---|---|---|
| Real friend deletion | System after risk policy | XiaYu only | Friend relation/private | Critical | Owner/protected exclusion + score threshold + bot scope | Owner outbox | `QChatRiskActionPolicyTests.cs`, `QChatOwnerEventOutboxTests.cs` | Implemented path exists; keep under audit |
| Local blocklist | System or owner | XiaYu/Mio | Private/Group | High | Risk policy + protected exclusion | Owner notification/log | `QChatBlocklistPolicyTests.cs`, `QChatRiskActionPolicyTests.cs` | Implemented path exists |
| Existing local file upload | Owner | Prefer XiaYu unless changed | Group | High | Confirmed upload intent + file safety | Reply or outbox on long/failure | `QChatIntentClassifierTests.cs`, `QChatFileSafetyServiceTests.cs`, `QChatServiceAdapterTests.cs` | Implemented path exists; XiaYu-only decision still open |
| Managed QQ file download | Owner or configured actor | XiaYu/Mio | Private/Group | Medium | Managed registry + URL/size validation | Reply/log | `QChatManagedFileServiceTests.cs` | Implemented path exists |
| Managed QQ file read | Owner or configured actor | XiaYu/Mio | Private/Group | Medium/High | Managed root containment + file type/size limit | Reply/log | `QChatManagedFileServiceTests.cs`, `QChatFileSafetyServiceTests.cs` | Implemented path exists |
| Managed QQ file delete | Owner or configured actor | XiaYu/Mio | Private/Group | High | Managed root containment + deleted state | Reply/log | `QChatManagedFileServiceTests.cs` | Implemented path exists |
| Owner approval decision | Owner | XiaYu/Mio | Private/Group | High | Exact `/approve` or `/deny` + owner role | Reply/log | `QChatOwnerCommandServiceTests.cs`, `QChatActionPolicyServiceTests.cs` | Implemented path exists |
| Desktop/business task | Owner | XiaYu only | Private preferred | Critical | Owner role + XiaYu scope + action policy + file blacklist | Owner outbox | `QChatActionPolicyServiceTests.cs`, `QChatServiceAdapterTests.cs` | Precondition path exists; continue hardening |
| Deterministic background task | Owner-authorized system path | XiaYu unless low-risk configured | Private/Group feedback | High | Deterministic task runner + cancellation/failure handling | Owner feedback/outbox | `QChatDeterministicTaskRunnerTests.cs`, `QChatTaskFeedbackFormatterTests.cs` | Implemented path exists |
| QZone proactive execution | System after suggestion policy | Account route | QZone | Medium/High | QZone policy + throttle | Log/outbox when important | `QZoneProactiveExecutionServiceTests.cs`, `QZoneInteractionPolicyTests.cs` | Adjacent capability; keep separate from chat reply |

## Required Questions Per Capability

Every high-risk capability must answer:

- Who can trigger it?
- Which bot may execute it?
- Can group chat trigger it?
- Does it require owner approval?
- Does it require owner outbox reporting?
- Can it affect the owner?
- Can it affect protected users?
- Does it read local files?
- Does it write local files?
- Does it upload anything outside the machine?
- Does it mutate QQ social state?
- Does it survive disconnects or restarts?
- What exact tests cover it?
- What live smoke case covers it?

## Current Gaps

### Gap: Central capability policy does not exist yet

Policy concepts are currently split across message security, risk policy, QChat service branches, and action policy. This should be normalized by `QChatCapabilityPolicy`.

Planned task:

- `docs/superpowers/plans/2026-06-21-qchat-boundary-coherence-optimization.md` Task 5.

### Gap: Decision trace is not yet wired into live QChat branches

`QChatDecisionTrace` provides the structured format, but live branches still need to emit traces around reply, suppress, recall, upload, and deny decisions.

Planned tasks:

- Task 2 for trace model.
- Task 6 for `QChatService` branch integration.

### Gap: Existing local file upload bot scope needs final owner decision

The capability matrix currently says "Prefer XiaYu unless explicitly configured". For stricter safety, this can become XiaYu-only.

Decision needed:

- Keep XiaYu-preferred.
- Change to XiaYu-only.
- Allow both bots only for managed plugin files.

### Gap: QZone and QChat share module space

QZone is adjacent to QChat and shares OneBot runtime concepts, but it has different safety and timing behavior.

Planned task:

- Task 11 clarifies QChat/QZone boundary.

### Gap: Desktop/business tasks need end-to-end live smoke

The safety model exists, but real steward-like behavior should not be considered complete until a controlled live smoke proves:

- Owner-only trigger.
- XiaYu-only execution.
- File blacklist respected.
- Long task does not block QQ chat.
- Completion/failure reaches owner outbox.

## Friend Deletion Safety Checklist

- [ ] Target is not owner.
- [ ] Target is not protected.
- [ ] Executing bot is XiaYu.
- [ ] Risk score meets configured threshold.
- [ ] Risk event history explains the score.
- [ ] Deletion action is idempotent or safely handles already-deleted target.
- [ ] Owner outbox event includes target id and reason.
- [ ] Local block state is updated consistently.
- [ ] Test covers denied owner.
- [ ] Test covers denied protected user.
- [ ] Test covers denied non-XiaYu.
- [ ] Test covers allowed eligible user.

## File Action Safety Checklist

- [ ] Path is under managed root, or passes explicit file safety policy.
- [ ] Path does not match read blacklist.
- [ ] File size is within limit.
- [ ] Upload intent is confirmed from user-authored text, not metadata.
- [ ] Group target is explicit or current session is safe.
- [ ] Failure message does not leak unnecessary local path details to non-owner.
- [ ] Test covers image metadata false positive.
- [ ] Test covers forwarded metadata false positive.
- [ ] Test covers path traversal denial.

## Desktop/Business Task Safety Checklist

- [ ] Actor is owner.
- [ ] Bot is XiaYu.
- [ ] Task is represented as a deterministic action or approved draft.
- [ ] Task cannot bypass file blacklist.
- [ ] Task cannot modify blocked paths.
- [ ] Task reports start, completion, and failure when long-running.
- [ ] Task does not block QQ chat loop.
- [ ] Task has cancellation or timeout behavior.
- [ ] Test covers non-owner denial.
- [ ] Test covers Mio denial.
- [ ] Test covers owner XiaYu allowed path.

## Acceptance

- [ ] Every critical capability has a row in this audit.
- [ ] Every critical capability has tests.
- [ ] Every destructive capability has owner reporting.
- [ ] Every file capability has path safety.
- [ ] Every long task has non-blocking feedback behavior.
- [ ] Open gaps are either implemented or explicitly accepted by owner.

