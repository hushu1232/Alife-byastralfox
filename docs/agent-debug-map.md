# Agent Debug Map

This file is a low-token index for chat-driven debugging. Use it before reading
large files or logs.

## How To Use

1. Convert the owner message into an issue packet.
2. Find the closest symptom below.
3. Use the listed symbols/tests as the first candidate set.
4. Use CodeGraph for structural paths.
5. Read only the focused regions needed to confirm the hypothesis.
6. If no candidate is unique, return a location failure report.

## Symptom Map

| Symptom | Issue Type | First Checks | Candidate Symbols | Tests |
| --- | --- | --- | --- | --- |
| QQ shows internal labels such as `[QQ`, `qchat-`, `route=`, `no-reply` | `qq-visible-output-leak` | QQ visible exits, pending poke text, recent diagnostics summary | `QChatService.SendTextOrMediaMessageAsync`, `QChatExperienceSanitizer`, `QChatVisibleReplyPolicy`, `QZoneService.Report`, `QZoneService.ReportQuery`, `QZoneService.ReportProactiveExecution` | `QChatServiceAdapterTests`, `QChatMessageSecurityTests`, `QZoneServiceTests` |
| Deterministic action failure becomes casual model chat | `deterministic-action-failure-chatified` | runner usage, continuation decision, task feedback | `QChatDeterministicTaskRunner.ExecuteAsync`, `QChatContinuationPolicy.Decide`, `QChatTaskFeedbackFormatter.Format`, `QFile`, `QImage`, `QVideo`, `QChatCrossSessionSend` | `QChatDeterministicTaskRunnerTests`, `QChatContinuationPolicyTests`, `QChatServiceAdapterTests` |
| File upload/download/delete state is unclear | `file-operation-risk` | managed file service, task feedback, owner session | `QChatManagedFileService`, `QChatFileSafetyService`, `QChatTaskFeedbackFormatter`, `ExecuteOneShotFileUploadTaskAsync` | `QChatManagedFileServiceTests`, `QChatFileSafetyServiceTests`, `QChatTaskFeedbackFormatterTests` |
| QZone action state leaks or is hard to trace | `qzone-action-risk` | QZone report functions and proactive executor | `QZoneService.Report`, `QZoneService.ReportQuery`, `QZoneService.ReportProactiveExecution`, `QZoneProactiveExecutionService.ExecuteAsync` | `QZoneServiceTests`, `QZoneProactiveExecutionServiceTests`, `QZoneProactiveSuggestionServiceTests` |
| Owner-only action is available to non-owner | `permission-bypass-risk` | permission request source and gateway decision | `AgentPermissionGate`, `AgentActionGatewayService.ExecuteAsync`, `QChatActionPolicyService.Evaluate`, owner command handlers in `QChatService` | `AgentPermissionGateTests`, `AgentApprovalServiceTests`, `QChatActionPolicyServiceTests`, `QChatServiceAdapterTests` |
| Reply goes to wrong session or mixes private/group output | `wrong-target-session` | reply session guard, fallback selection, cross-session policy | `QChatReplyTargetPolicy`, `GetCurrentReplySessionForGuard`, `TryBuildPlainTextFallbackResponse`, `QChatCrossSessionSend` | `QChatServiceAdapterTests`, `QChatReplyDecisionPolicyTests` |
| Model invents live runtime facts | `model-invented-runtime-state` | cognitive honesty protocol, QChat prompt, relation cache tools | `MessageFilterService.FormatChatMessage`, `QChatConfig.AppendChatPrompt`, `QChatRelationCacheService` | `MessageFilterContextComposerTests`, `QChatServiceAdapterTests`, `QChatRelationCacheServiceTests` |
| Memory stores internal labels or diagnostic noise | `memory-contamination` | memory sanitizer and memory write paths | `MemoryTextSanitizer`, `AutobiographicalMemoryService`, `MemoryStorage` | `MemoryTextSanitizerTests`, `AutobiographicalMemoryServiceTests`, `MemoryStorageConsistencyTests` |
| Agent reads too much context before locating issue | `token-overuse-during-debugging` | issue packet and debug map use | `docs/agent-debug-map.md`, CodeGraph tools, future issue-packet service | no code test yet; use review checklist |
| Persona hides uncertainty or changes engineering facts | `persona-fact-boundary-mix` | persona contract and final formatter | future persona-aware formatter, `QChatExperienceSanitizer`, `PromptStablePrefixService` | future persona contract tests |
| Desktop command leaks process/window details to non-owner | `desktop-permission-leak` | owner gate, desktop command handler, fake desktop reader | `TryHandleOwnerDesktopCommandAsync`, `DesktopControlService` | `QChatServiceAdapterTests`, `DesktopControlServiceTests` |
| Desktop command works from non-XiaYu bot | `desktop-bot-boundary-bypass` | bot route resolution, XiaYu allowlist, command handler | `BuildQChatMemoryStatusRoute`, `TryHandleOwnerDesktopCommandAsync`, `QChatAgentIdentityRegistry` | `QChatServiceAdapterTests`, `QChatAgentIdentityRegistryTests` |
| Desktop action executes without gateway | `desktop-action-bypass` | action gateway, owner gate, XiaYu gate, mutation-disabled policy | `DesktopActionGateway`, `DesktopCapabilityRegistry` | `DesktopActionGatewayTests`, `DesktopCapabilityRegistryTests` |

## Output Surface Index

QQ-visible or model-facing output should be checked here first:

- `QChatService.SendTextOrMediaMessageAsync`
- `QChatService.SendSingleMessageAsync`
- `QChatService.PublishQChatToolResultAsync`
- `QChatService.TryPoke`
- `QChatTaskFeedbackFormatter.Format`
- `QChatExperienceSanitizer.SanitizeOutgoing`
- `QChatVisibleReplyPolicy`
- `QZoneService.Report`
- `QZoneService.ReportQuery`
- `QZoneService.ReportProactiveExecution`
- owner notification delivery methods in `QChatService`

## Forbidden QQ-Visible Tokens

Use these as test seeds and review markers:

- `[QQ`
- `[QChat`
- `[Internal`
- `qchat-`
- `qzone-`
- `route=`
- `session=qq:`
- `managed_file_id=` unless owner explicitly requested file diagnostics
- `StopAfterTaskFeedback`
- `TaskFeedbackOnly`
- `NoReply`
- `no-reply`
- raw exception stack frames
- token, cookie, session key, or credential material

## First Commands

Focused QChat:

```powershell
dotnet test D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore
```

Focused Framework:

```powershell
dotnet test D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --no-restore
```

Full solution:

```powershell
dotnet test D:\Alife\Alife.slnx --no-restore
```

Whitespace:

```powershell
git diff --check
git diff --cached --check
```

## Location Failure Report Template

```text
Location status: not unique yet.
Known symptom:
- <short behavior>
Checked:
- <symbol/file>: <finding>
Likely candidates:
- <candidate>: <confidence and reason>
Missing evidence:
- <sample message, correlation id, log time, or failing test>
Next low-token step:
- <one action>
```
