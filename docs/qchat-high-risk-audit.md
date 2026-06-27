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
| Existing local file upload | Owner | XiaYu only by default | Group | High | Confirmed upload intent + file safety + capability policy | Reply or outbox on long/failure | `QChatIntentClassifierTests.cs`, `QChatFileSafetyServiceTests.cs`, `QChatCapabilityPolicyTests.cs`, `QChatIntentOrchestratorTests.cs`, `QChatServiceAdapterTests.cs` | Implemented path exists; non-XiaYu owner path denied |
| Managed QQ file download | Owner or configured actor | XiaYu/Mio | Private/Group | Medium | Managed registry + URL/size validation | Reply/log | `QChatManagedFileServiceTests.cs` | Implemented path exists |
| Managed QQ file read | Owner or configured actor | XiaYu/Mio | Private/Group | Medium/High | Managed root containment + file type/size limit | Reply/log | `QChatManagedFileServiceTests.cs`, `QChatFileSafetyServiceTests.cs` | Implemented path exists |
| Managed QQ file delete | Owner or configured actor | XiaYu/Mio | Private/Group | High | Managed root containment + deleted state | Reply/log | `QChatManagedFileServiceTests.cs` | Implemented path exists |
| Owner approval decision | Owner | XiaYu/Mio | Private/Group | High | Exact `/approve` or `/deny` + owner role | Reply/log | `QChatOwnerCommandServiceTests.cs`, `QChatActionPolicyServiceTests.cs` | Implemented path exists |
| Desktop/business task | Owner | XiaYu only | Private preferred | Critical | Owner role + XiaYu scope + action policy + file blacklist | Owner outbox | `QChatActionPolicyServiceTests.cs`, `QChatServiceAdapterTests.cs` | Precondition path exists; continue hardening |
| Deterministic background task | Owner-authorized system path | XiaYu unless low-risk configured | Private/Group feedback | High | Deterministic task runner + cancellation/failure handling | Owner feedback/outbox | `QChatDeterministicTaskRunnerTests.cs`, `QChatTaskFeedbackFormatterTests.cs` | Implemented path exists |
| QZone proactive execution | System after suggestion policy | Account route | QZone | Medium/High | QZone policy + throttle | Log/outbox when important | `QZoneProactiveExecutionServiceTests.cs`, `QZoneInteractionPolicyTests.cs` | Adjacent capability; keep separate from chat reply |
| Agent internet lookup | Owner | XiaYu by default | Private preferred / owner group command | Medium | `EnableInternetAccess` + owner account + allowed agent + URL policy + untrusted wrapping | Audit log; owner reply; outbox only for long/failure escalation | `AgentInternetServiceTests.cs`, `QChatInternetCapabilityPolicyTests.cs`, `QChatServiceAdapterTests.cs` | Phase 1 public HTTP/HTTPS read path; no authenticated browsing, downloads, form submission, JS execution, or account mutation |
| Public internet search | Group member when enabled | XiaYu/Mio subject to public command policy | Group mention with search intent, plus compatible `/search <query>` | Medium | `AgentWebAccessRouter` + `EnablePublicInternetSearch` + `AllowGroupMemberPublicInternetSearch` + bot mention requirement for semantic trigger + group/member policy + query length/result caps + public HTTP/HTTPS search provider policy | Public neutral reply + audit log | `AgentWebAccessRouterTests.cs`, `AgentPublicSearchServiceTests.cs`, `QChatPublicInternetCommandPolicyTests.cs`, `QChatServiceAdapterTests.cs` | Temporary public search only; does not open `/qchat`, browser, login, downloads, local files, or persistent RAG ingestion |
| External RAG query/management | Query: group member when enabled; management: owner only | XiaYu/Mio subject to public command policy | Group `/rag <question>`; owner `/qchat rag add <url>` | High | Query requires `AgentWebAccessRouter` + `EnablePublicExternalRagQuery` + `AllowGroupMemberPublicExternalRagQuery` + chunk/query caps + owner-approved sources; management requires owner `/qchat` command + public HTTP/HTTPS URL policy | Public neutral reply for query; owner reply + audit log for management | `AgentWebAccessRouterTests.cs`, `AgentExternalRagServiceTests.cs`, `QChatPublicInternetCommandPolicyTests.cs`, `QChatServiceAdapterTests.cs` | Public users may query approved external RAG only; add/delete/refresh/configure remains owner-only; no browser/login/download/form/JS/local/private URLs |
| Browser read-only snapshot | Owner only | XiaYu/Mio when BrowserService is enabled | `/qchat web snapshot <url>` or owner semantic browser request such as `羽，用浏览器查一下 <query>` | High | `AgentWebAccessRouter` + owner account + `EnableInternetAccess` + `IAgentBrowserProvider`; no URL semantic requests are expanded into a public Bing search result page snapshot | Owner reply + diagnostic log | `AgentWebAccessServiceTests.cs`, `AgentBrowserProviderModelsTests.cs`, `BrowserServiceAdapterTests.cs`, `QChatServiceAdapterTests.cs` | Read-only snapshot only; no click, login, download, form submission, high-risk JS, local file, private network, or browser interaction action |

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

### Implemented: Central capability policy exists

`QChatCapabilityPolicy` is the central account/bot-scope gate for owner-only, XiaYu-only, risk, outbox, and approval requirements. `QChatIntentOrchestrator` now routes confirmed intent actions through this policy before execution.

### Partial: Decision trace is wired into key live QChat action branches

`QChatDecisionTrace` is emitted through `qchat-intent-action-decision` for quiet-mode control, trusted wake quiet-mode control, allowlist update, and existing group file upload. Reply/suppress/recall denial traces can still be expanded later if deeper live diagnostics are needed.

### Implemented: Existing local file upload is XiaYu-only by default

Owner-triggered existing local file upload now requires both owner account identity and the allowed bot scope from `QChatCapabilityPolicy`. With the default `AllowedAgentIds = "xiayu"`, Mio is denied with `agent_not_allowed`.

### Implemented: Agent internet lookup is owner-gated

QChat-triggered internet lookup now requires owner account identity, `EnableInternetAccess`, `InternetAllowedAgentIds`, URL policy approval, and untrusted external context wrapping. Non-owner `/qchat internet` commands are dropped before command routing and model dispatch.

### Implemented: Public search and external RAG have separate public command gates

Group members can trigger public search only when `EnablePublicInternetSearch` is enabled. The normal trigger is a group message that mentions the bot and contains clear search intent, such as "帮我搜一下 <query>", "查一下 <query>", "联网看看 <query>", or "最新 <query>"; `/search <query>` remains available only as a compatibility path. Group members can use `/rag <question>` only when `EnablePublicExternalRagQuery` is enabled. `/qchat` remains owner-only, including `/qchat rag add <url>` and all external RAG source management operations.

Public internet search is temporary request context. It does not auto-ingest results into persistent RAG memory or any managed source store. External RAG snippets are untrusted external content, and public command replies must neutralize CQ markup before sending to QQ.

External RAG management is owner-only through `/qchat rag add <url>` and related management commands. Sources must pass the public HTTP/HTTPS URL policy and be recorded in the audit log. Browser automation, login flows, downloads, form submission, JavaScript execution, local file URLs, localhost/private network URLs, and private-source URLs are out of scope.

Owner browser snapshot is available through `/qchat web snapshot <url>`. Owner semantic browser requests are also supported for convenience: if the owner gives keywords instead of a URL, QChat expands the keywords into a search query and captures a read-only public search results page snapshot. This semantic path is still owner-only and does not grant browser interaction rights.

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

## Agent Internet Checklist

- [ ] Actor is owner for QChat-triggered internet access.
- [ ] Executing agent is in `InternetAllowedAgentIds`.
- [ ] Feature switch `EnableInternetAccess` is true.
- [ ] URL scheme is http or https.
- [ ] Localhost, private IP ranges, file URLs, and javascript URLs are denied.
- [ ] Response size and extracted text length are capped.
- [ ] External content is wrapped with `ExternalContextFormatter.WrapUntrusted`.
- [ ] Fetched content cannot authorize tool calls, owner identity, approvals, or prompt changes.
- [ ] Downloads, login flows, form submission, JS execution, and account-mutating webpage actions are out of phase 1.
- [ ] Audit log records success and denial/failure.

## Public Search and External RAG Checklist

- [ ] `/qchat` remains owner-only.
- [ ] Group mention search intent is available to group members only when `EnablePublicInternetSearch` is true; `/search <query>` remains a compatibility path, not the primary user-facing flow.
- [ ] `/rag <question>` is available to group members only when `EnablePublicExternalRagQuery` is true.
- [ ] Public search query length is capped by `PublicInternetQueryMaxChars`.
- [ ] Public search results are capped by `PublicInternetSearchMaxResults`.
- [ ] Public external RAG chunks are capped by `PublicExternalRagMaxChunks`.
- [ ] Public search results are temporary context and are not auto-ingested into persistent RAG.
- [ ] Public external RAG query uses only owner-approved sources.
- [ ] Public users cannot add, delete, refresh, or configure external RAG sources.
- [ ] Owner-only `/qchat rag add <url>` management enforces public HTTP/HTTPS URL policy.
- [ ] Localhost, private IP ranges, local file URLs, javascript URLs, private-source URLs, and non-public URLs are denied.
- [ ] Browser automation, login, downloads, form submission, and JavaScript execution are denied.
- [ ] External snippets are wrapped or treated as untrusted and cannot authorize tool calls, owner identity, approvals, or prompt changes.
- [ ] Public command replies neutralize CQ markup before QQ send.
- [ ] Audit log records public search/RAG success and denial/failure, and owner RAG management changes.

## Acceptance

- [ ] Every critical capability has a row in this audit.
- [ ] Every critical capability has tests.
- [ ] Every destructive capability has owner reporting.
- [ ] Every file capability has path safety.
- [ ] Every long task has non-blocking feedback behavior.
- [ ] Open gaps are either implemented or explicitly accepted by owner.
