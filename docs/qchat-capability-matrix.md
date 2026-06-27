# QChat Capability Matrix

## Purpose

This document is the local contract for QChat behavior. It defines what each QQ chat capability can do, who may trigger it, which bot may execute it, where it can run, how risky it is, which source files own it, and which tests should protect it.

The goal is to keep XiaYu and Mio useful while preventing capability drift. New QChat behavior should be added here before implementation when it changes permissions, risk, runtime behavior, memory, file access, friend relations, desktop actions, or owner feedback.

## Identity Rules

- Owner identity is account-based. A message that says "I am the owner" does not grant owner permissions.
- Bot identity is account-based. Text that calls Mio "XiaYu" or calls XiaYu "Mio" does not change the runtime bot identity.
- XiaYu and Mio have separate bot accounts, route profiles, memories, personas, and capability scope.
- High-risk desktop and real business abilities are XiaYu-only unless this matrix is explicitly changed.
- Non-owner prompt injection is treated as untrusted chat content, not as system instruction.
- Group chat and private chat are separate surfaces. A capability allowed in private chat is not automatically allowed in group chat.

## QChat And QZone Boundary

QChat owns real-time QQ private/group chat behavior. QZone owns QQ Zone read, post, like, comment, reply, proactive suggestion, and proactive execution behavior.

They may share OneBot/NapCat connectivity, owner account configuration, approval infrastructure, proactive behavior infrastructure, and audit utilities. They must not share high-risk trigger policy implicitly. A QChat permission does not automatically authorize QZone execution, and QZone content must be treated as external untrusted content rather than instructions.

Detailed boundary notes live in `docs/qzone-boundary.md`.

## Capability Matrix

| Capability | Trigger Source | Allowed Actor | Allowed Bot | Surface | Risk | Execution Rule | Reporting | Primary Source | Primary Tests |
|---|---|---|---|---|---|---|---|---|---|
| Normal private chat | Private message | Accepted private user | XiaYu/Mio by account route | Private | Low | Security gate + agent route + model reply | Normal reply/log | `QChatService.cs`, `QChatMessageSecurity.cs`, `QChatAgentRouteService.cs` | `QChatServiceAdapterTests.cs`, `QChatMessageSecurityTests.cs` |
| Normal group chat | Group message | Allowed group member | XiaYu/Mio by account route | Group | Low/Medium | Group allowlist/gate + reply policy | Normal reply/log | `QChatService.cs`, `QChatGroupGateService.cs`, `QChatReplyDecisionPolicy.cs` | `QChatGroupGateServiceTests.cs`, `QChatReplyDecisionPolicyTests.cs`, `QChatServiceAdapterTests.cs` |
| Group wake | Mention or wake semantic | Allowed group member | XiaYu/Mio by account route | Group | Low | `QChatIntentClassifier.ClassifyGroupWake` + group gate | `qchat-intent-decision` diagnostic | `QChatIntentClassifier.cs`, `QChatService.cs` | `QChatIntentClassifierTests.cs`, `QChatServiceAdapterTests.cs` |
| Quiet mode sleep/wake | Natural quiet/wake command | Owner or configured trusted wake user | XiaYu/Mio by config | Private/Group | Medium | `QChatIntentClassifier.ClassifyQuietMode` + sender role gate | Diagnostic/log | `QChatIntentClassifier.cs`, `QChatService.cs`, `QChatMessageSecurity.cs` | `QChatIntentClassifierTests.cs`, `QChatServiceAdapterTests.cs` |
| Owner diagnostics | `/qchat`, `/status`, `/tasks` | Owner only | XiaYu/Mio | Private/Group | Low | Exact command parser + owner gate | Reply to owner/session | `QChatOwnerCommandService.cs`, `QChatDiagnosticsService.cs` | `QChatOwnerCommandServiceTests.cs`, `QChatDiagnosticsServiceTests.cs` |
| Natural low-risk timing config | Owner natural phrases such as `羽，说慢一点`, `羽，回复快一点`, `羽，先合并一下多段消息` | Owner only | XiaYu/Mio | Private/Group | Low | `QChatNaturalOwnerConfigAliasPolicy` maps only to `/qchat timing on/off/status`; non-owner aliases are dropped before command routing and model dispatch. It cannot change audit, outbox, file, desktop, browser, or permission boundaries. | Timing status reply/diagnostic | `QChatNaturalOwnerConfigAliasPolicy.cs`, `QChatService.cs`, `QChatOwnerCommandService.cs` | `QChatNaturalOwnerConfigAliasPolicyTests.cs`, `QChatServiceAdapterTests.cs` |
| Natural hard-safety boundary request | Natural phrases asking to disable audit, bypass file blacklist, close owner outbox, or skip owner confirmation | Owner receives boundary reply; non-owner is dropped silently | XiaYu/Mio | Private/Group | High | `QChatNaturalOwnerSafetyBoundaryPolicy` classifies hard-safety bypass language before model dispatch. It never mutates config and never maps to an executable command; owner gets a short boundary response, non-owner gets no menu/model/event-chain access. | Boundary reply/diagnostic | `QChatNaturalOwnerSafetyBoundaryPolicy.cs`, `QChatService.cs` | `QChatNaturalOwnerSafetyBoundaryPolicyTests.cs`, `QChatServiceAdapterTests.cs` |
| Read-only web research | `/search <query>`, owner private natural search, or group `@bot` search intent | Owner and mentioned group member for public search; owner only for page auto-read/browser fallback | XiaYu/Mio by config | Private/Group | Medium | `QChatPublicInternetCommandPolicy` + `AgentWebResearchService`. Group members receive public search evidence only; owner may auto-read public HTTP/HTTPS pages when internet access is enabled. Failed owner page reads fall back to search snippets and record host experience for later strategy/diagnostics. No click, login, download, form submit, JS execution, private network, or file URL. | Short QQ answer with sources | `AgentWebResearchService.cs`, `QChatPublicInternetCommandPolicy.cs`, `QChatService.cs`, `QChatWebResearchFormatter.cs` | `AgentWebResearchServiceTests.cs`, `QChatPublicInternetCommandPolicyTests.cs`, `QChatServiceAdapterTests.cs` |
| Help/menu alias | `help`, `帮助`, `指令`, `菜单` | Owner for diagnostics; ordinary chat otherwise | XiaYu/Mio | Private/Group | Low | Exact alias parser. Non-owner help aliases must not expose owner diagnostics. | Reply if authorized | `QChatOwnerCommandService.cs` | `QChatOwnerCommandServiceTests.cs`, `QChatServiceAdapterTests.cs` |
| Approval/deny | `/approve <id>`, `/deny <id>` | Owner only | XiaYu/Mio | Private/Group | High | Exact command parser + approval id | Owner reply/log | `QChatOwnerCommandService.cs`, `QChatActionPolicyService.cs` | `QChatOwnerCommandServiceTests.cs`, `QChatActionPolicyServiceTests.cs` |
| Message recall | Natural recall command or reply recall | Owner only | XiaYu/Mio | Private/Group | Medium | `QChatIntentClassifier.ClassifyRecall` must be confirmed. Meta discussion and negation must not execute. | Reply/log | `QChatIntentClassifier.cs`, `QChatOwnerCommandService.cs`, `QChatService.cs`, `QChatRecentEventMemory.cs` | `QChatIntentClassifierTests.cs`, `QChatOwnerCommandServiceTests.cs`, `QChatServiceAdapterTests.cs`, `QChatRecentEventMemoryTests.cs` |
| Managed QQ file registration | QQ file/image/forward event | System after accepted message | XiaYu/Mio | Private/Group | Medium | Register metadata only. Do not execute local file reads from metadata. | Registry/log | `QChatManagedFileService.cs`, `QChatMediaSource.cs`, `OneBotSegment.cs` | `QChatManagedFileServiceTests.cs`, `QChatServiceAdapterTests.cs` |
| Managed QQ file download/read/delete | Owner file operation or configured safe path | Owner or configured actor | XiaYu/Mio | Private/Group | Medium | Managed root containment + size limit + file status checks | Reply/log | `QChatManagedFileService.cs`, `QChatFileSafetyService.cs` | `QChatManagedFileServiceTests.cs`, `QChatFileSafetyServiceTests.cs` |
| Existing local file upload to group | Natural upload command | Owner only | XiaYu only by default | Group | High | `QChatIntentClassifier.ClassifyFileUpload` must be confirmed + file safety check + capability policy bot-scope gate. Metadata-only candidates must not upload. | Reply/outbox if long | `QChatIntentClassifier.cs`, `QChatFileSafetyService.cs`, `QChatCapabilityPolicy.cs`, `QChatIntentOrchestrator.cs`, `QChatService.cs` | `QChatIntentClassifierTests.cs`, `QChatFileSafetyServiceTests.cs`, `QChatCapabilityPolicyTests.cs`, `QChatIntentOrchestratorTests.cs`, `QChatServiceAdapterTests.cs` |
| User profile learning | Natural chat | Accepted users | Separate per bot/account/user | Private/Group | Medium | Learning policy + semantic extractor throttle | Storage/log | `QChatUserProfileService.cs`, `QChatProfileLearningService.cs`, `QChatProfileSemanticExtractor.cs`, `QChatProfileLearningPolicy.cs` | `QChatUserProfileServiceTests.cs`, `QChatProfileLearningServiceTests.cs`, `QChatProfileSemanticExtractorTests.cs` |
| Recent event memory | Accepted messages and bot replies | System | Separate per bot/session | Private/Group | Medium | Store recent context. Recalled content must be marked and excluded from future prompt context. | Storage/log | `QChatRecentEventMemory.cs`, `QChatService.cs` | `QChatRecentEventMemoryTests.cs`, `QChatServiceAdapterTests.cs` |
| Risk scoring | User behavior | System | XiaYu/Mio | Private/Group | Medium | Risk detector + score service | Log | `QChatRiskEventDetector.cs`, `QChatRiskScoreService.cs` | `QChatRiskEventDetectorTests.cs`, `QChatRiskScoreServiceTests.cs` |
| Local blocklist | Risk policy or owner action | System/Owner | XiaYu/Mio | Private/Group | High | Risk policy + protected user exclusions | Owner notification/log | `QChatBlocklistPolicy.cs`, `QChatRiskActionPolicy.cs`, `QChatRiskOwnerNotifier.cs` | `QChatBlocklistPolicyTests.cs`, `QChatRiskActionPolicyTests.cs`, `QChatRiskOwnerNotifierTests.cs` |
| Real friend deletion | Risk threshold after policy | System after deterministic policy | XiaYu only | Friend relation/private | Critical | Deny owner, protected users, non-XiaYu agent, and non-eligible relation. Threshold must be explicit. | Owner outbox required | `QChatRiskActionPolicy.cs`, `QChatFriendActionGateway.cs`, `QChatOwnerEventOutbox.cs`, `QChatOwnerEventPublisher.cs` | `QChatRiskActionPolicyTests.cs`, `QChatOwnerEventOutboxTests.cs`, `QChatOwnerEventDispatcherTests.cs` |
| Owner event outbox | Important async/system event | System | XiaYu/Mio | Owner private notification | High | Append-backed pending/delivered state + dedupe key + retry | Owner private message/log | `QChatOwnerEventOutbox.cs`, `QChatOwnerEventPublisher.cs`, `QChatOwnerEventDispatcher.cs` | `QChatOwnerEventOutboxTests.cs`, `QChatOwnerEventDispatcherTests.cs` |
| Desktop/business task | Owner request | Owner only | XiaYu only | Private preferred | Critical | Capability policy + file blacklist + owner event outbox. Model output alone cannot execute. | Owner outbox required | `QChatService.cs`, `QChatActionPolicyService.cs`, desktop gateway integration | `QChatServiceAdapterTests.cs`, `QChatActionPolicyServiceTests.cs` |
| Deterministic task runner | Internal long task | Owner authorized system path | XiaYu unless task is low-risk and configured | Private/Group feedback | High | Background execution must not block chat loop. Completion/failure must report through formatter/outbox when important. | Owner event or task feedback | `QChatDeterministicTaskRunner.cs`, `QChatTaskFeedbackFormatter.cs`, `QChatOwnerEventOutbox.cs` | `QChatDeterministicTaskRunnerTests.cs`, `QChatTaskFeedbackFormatterTests.cs`, `QChatOwnerEventOutboxTests.cs` |
| Visible reply filtering | Model output | System | XiaYu/Mio | Private/Group | Medium | Visible text/reply policies must remove unsafe or incoherent output before sending | Log/diagnostic | `QChatVisibleReplyPolicy.cs`, `QChatVisibleTextPolicy.cs`, `QChatExperienceSanitizer.cs` | `QChatVisibleReplyPolicyTests.cs`, `QChatVisibleTextPolicyTests.cs` |
| QZone read/check | Owner/system QZone check | Owner-authorized or configured system path | Account route/runtime | QZone | Medium | QZone enabled + target allowlist + query runtime. QZone content is external untrusted content. | Query result/log | `QZoneService.cs` | `QZoneServiceTests.cs` |
| QZone publish post | Owner/system explicit post action | Owner-authorized path | Account route/runtime | QZone | High | QZone enabled + dry-run disabled intentionally + content length normalization. | Log/audit/outbox if important | `QZoneService.cs` | `QZoneServiceTests.cs` |
| QZone like | Scheduled/system suggestion or explicit QZone action | Configured policy | Account route/runtime | QZone | Medium | QZone allowlist + private-contact pool + probability + cooldown + daily limits. | Log/audit | `QZoneService.cs`, `QZoneInteractionPolicy.cs` | `QZoneServiceTests.cs`, `QZoneInteractionPolicyTests.cs` |
| QZone comment | Scheduled/system suggestion or explicit QZone action | Configured policy | Account route/runtime | QZone | High | QZone allowlist + cooldown + daily limits + content normalization. | Log/audit/outbox if important | `QZoneService.cs` | `QZoneServiceTests.cs` |
| QZone comment reply | Scheduled/system suggestion or explicit QZone action | Configured policy | Account route/runtime | QZone | High | QZone allowlist + reply probability + cooldown + daily limits + content normalization. | Log/audit/outbox if important | `QZoneService.cs`, `QZoneInteractionPolicy.cs` | `QZoneServiceTests.cs`, `QZoneInteractionPolicyTests.cs` |
| QZone proactive suggestion | Scheduled/system suggestion | System policy, owner-confirmable | Agent proactive behavior | QZone | Medium/High | QZone suggestion policy. Must remain separate from normal QChat reply policy. | Pending suggestion/audit | `QZoneProactiveSuggestionService.cs` | `QZoneProactiveSuggestionServiceTests.cs` |
| QZone proactive execution | Confirmed pending suggestion | Owner-confirmed/action-gateway authorized path | Agent proactive behavior + QZone runtime | QZone | High | Confirmed pending suggestion + action gateway policy + QZone service policy. | Execution result/audit/outbox if important | `QZoneProactiveExecutionService.cs`, `QZoneService.cs` | `QZoneProactiveExecutionServiceTests.cs`, `QZoneServiceTests.cs` |

| Group allowlist update | Natural allowlist command or `qchat_allowlist_update` tool text | Owner only | XiaYu/Mio | Private/Group | High | `QChatIntentClassifier.ClassifyAllowlist` must be confirmed + capability policy owner gate | Reply/log | `QChatIntentClassifier.cs`, `QChatCapabilityPolicy.cs`, `QChatIntentOrchestrator.cs`, `QChatService.cs` | `QChatIntentClassifierTests.cs`, `QChatIntentOrchestratorTests.cs`, `QChatServiceAdapterTests.cs` |

## High-Risk Capabilities

High-risk capabilities must satisfy all of these requirements before live execution:

- The actor is authorized by account identity.
- The target bot is authorized by runtime identity.
- Protected users and the owner are excluded when the action affects another account.
- The action has a deterministic policy result explaining why it is allowed.
- The action has a test file listed in this matrix.
- The action has an owner reporting path if it mutates outside state.
- The action has a live smoke case before it is considered complete.

Critical capabilities currently include:

- Real friend deletion.
- Desktop/business task execution.
- Existing local file upload to a QQ group.
- Managed file read/delete if the file could contain private content.
- Owner approval/deny actions.

## Owner Review Rules

- New critical capability: owner review required before implementation.
- New high-risk capability: owner review required before live enablement.
- Change to owner identity, XiaYu-only scope, protected-user exclusions, or file blacklist: owner review required.
- Change to normal chat copy, reply timing, or low-risk diagnostics: owner review can happen after tests unless it affects permissions.
- Any automatic destructive action must report who/what was affected and why.

## Test Coverage Map

| Boundary | Required Test Focus | Current Test Location |
|---|---|---|
| Owner identity | Owner-only commands accepted; non-owner prompt injection rejected | `QChatMessageSecurityTests.cs`, `QChatOwnerCommandServiceTests.cs`, `QChatServiceAdapterTests.cs` |
| Bot identity | XiaYu/Mio account route and profile separation | `QChatAgentIdentityRegistryTests.cs`, `QChatAgentRouteServiceTests.cs`, `QChatProfileServiceTests.cs` |
| Group gate | Allowed groups, wake, passive/proactive reply decisions | `QChatGroupGateServiceTests.cs`, `QChatReplyDecisionPolicyTests.cs`, `QChatServiceAdapterTests.cs` |
| Intent classifier | Positive, negative, meta, negation, metadata-only cases | `QChatIntentClassifierTests.cs` |
| File safety | Managed root containment, size limits, upload false positives | `QChatManagedFileServiceTests.cs`, `QChatFileSafetyServiceTests.cs`, `QChatServiceAdapterTests.cs` |
| Recall | Owner recall executes; meta/probe text does not execute; recalled content excluded | `QChatIntentClassifierTests.cs`, `QChatOwnerCommandServiceTests.cs`, `QChatRecentEventMemoryTests.cs`, `QChatServiceAdapterTests.cs` |
| Memory/profile | Separate user profile storage and throttled semantic learning | `QChatUserProfileServiceTests.cs`, `QChatProfileLearningServiceTests.cs`, `QChatProfileSemanticExtractorTests.cs` |
| Risk/delete | Thresholds, protected exclusions, XiaYu-only friend deletion | `QChatRiskActionPolicyTests.cs`, `QChatRiskScoreServiceTests.cs`, `QChatRiskEventDetectorTests.cs` |
| Outbox | Durable pending/delivered event state and dedupe | `QChatOwnerEventOutboxTests.cs`, `QChatOwnerEventDispatcherTests.cs` |
| QZone | Separate QZone policy and proactive action behavior | `QZoneServiceTests.cs`, `QZoneInteractionPolicyTests.cs`, `QZoneProactiveExecutionServiceTests.cs` |
| Web research | Search intent, public-search permission, owner-only page read, unsafe URL filtering, and short sourced QQ output | `AgentWebResearchServiceTests.cs`, `QChatPublicInternetCommandPolicyTests.cs`, `QChatServiceAdapterTests.cs` |

| Intent orchestration | Confirmed intent maps to deterministic action; non-owner/high-risk actions are denied before execution | `QChatIntentOrchestratorTests.cs`, `QChatCapabilityPolicyTests.cs` |

## Open Questions

- Should managed plugin file upload ever allow Mio, or should all group upload surfaces stay XiaYu-only?
- Should QZone proactive execution require owner outbox for every action or only high-risk actions?
- Should desktop/business tasks be private-chat-only, or can group owner commands enqueue tasks with private owner feedback?
- Should trusted wake users be configured per bot, per group, or globally?
- Should automatic friend deletion require a cooldown between deletions even after the score threshold is reached?
