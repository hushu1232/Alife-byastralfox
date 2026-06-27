# Agent Web Research Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a QQ-friendly read-only web research pipeline that turns `@bot + keyword/question` into search, page selection, page reading, evidence extraction, and a short sourced answer.

**Architecture:** Add `AgentWebResearchService` in the message-filter layer so it can compose existing public search, public/browser read routing, and site experience policy without giving QQ direct browser control. Add a small QChat formatter and semantic routing so owner and mentioned group members can trigger search by natural language while existing browser-only capabilities remain owner-only.

**Tech Stack:** C#/.NET 9, existing `Alife.Function.MessageFilter` services, existing `Alife.Function.QChat` orchestrator and NUnit tests.

---

### Task 1: Research Service Models and Core Flow

**Files:**
- Create: `sources/Alife.Function/Alife.Function.MessageFilter/AgentWebResearchModels.cs`
- Create: `sources/Alife.Function/Alife.Function.MessageFilter/AgentWebResearchService.cs`
- Test: `Tests/Alife.Test.Framework/AgentWebResearchServiceTests.cs`

- [ ] **Step 1: Write failing tests**

Add tests that prove:

```csharp
AgentWebResearchService.SearchAndReadAsync("agent browser web access", actor, ct)
```

does the following:

- calls public search with a cleaned query,
- reads the top allowed result through `AgentWebAccessService`,
- returns a `AgentWebResearchResult` with `Answer`, `Evidence`, and `Sources`,
- returns a no-results response without fabricating when search has no results,
- refuses private/local/file URLs through the existing web access service.

- [ ] **Step 2: Run failing tests**

Run:

```powershell
dotnet test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --no-restore --filter "FullyQualifiedName~AgentWebResearchServiceTests"
```

Expected: compile failure because the service and models do not exist.

- [ ] **Step 3: Implement models and service**

Implement minimal records:

```csharp
public sealed record AgentWebResearchRequest(string Query, AgentWebAccessActor Actor, int MaxSources = 3);
public sealed record AgentWebResearchEvidence(string Title, string Url, string Summary, string SourceType);
public sealed record AgentWebResearchResult(bool Success, string Answer, IReadOnlyList<AgentWebResearchEvidence> Evidence, string? FailureReason = null);
```

Implement `AgentWebResearchService` to:

- reject blank queries,
- use existing `AgentPublicSearchService` / `AgentInternetService` public search flow,
- read the first few search result URLs with `AgentWebAccessService`,
- extract short evidence summaries from read results,
- compose a deterministic answer without calling the LLM.

- [ ] **Step 4: Verify tests pass**

Run the same focused test command and confirm pass.

### Task 2: QQ Semantic Trigger and Formatter

**Files:**
- Create or modify: `sources/Alife.Function/Alife.Function.QChat/QChatWebResearchFormatter.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatIntentClassifier.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
- Test: `Tests/Alife.Test.QChat/QChatWebResearchSemanticTriggerTests.cs`

- [ ] **Step 1: Write failing tests**

Add tests that prove:

- `@羽 查一下 agent browser` triggers research for mentioned group users,
- `@羽 搜一下 xxx` triggers research,
- unmentioned group chatter does not trigger research,
- owner private natural language can trigger research,
- group members do not receive owner-only browser snapshot capability,
- formatted QQ answer starts with conclusion, includes up to three compact bullet lines, and includes sources.

- [ ] **Step 2: Run failing tests**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatWebResearchSemanticTriggerTests"
```

Expected: compile failure or behavior failure because semantic research routing is missing.

- [ ] **Step 3: Implement semantic routing**

Use existing QChat intent classification patterns. Add a research intent only for:

- private owner messages,
- group messages that explicitly mention the bot,
- messages containing search semantics such as `查`, `搜`, `搜索`, `联网`, `资料`, `官方文档`, `对比`, or `是什么`.

Strip the trigger words into a search query and pass the actor identity to `AgentWebResearchService`.

- [ ] **Step 4: Implement QQ formatter**

Format research output as:

```text
结论：...
1. ...
2. ...
来源：title1 / title2
```

Keep it short and never expose provider names, policy labels, stack traces, or browser internal strategy.

- [ ] **Step 5: Verify tests pass**

Run the focused QChat test command and confirm pass.

### Task 3: Docs and Full Verification

**Files:**
- Modify: `docs/qchat-capability-matrix.md`
- Modify or create: `docs/agent-browser-web-research.md`

- [ ] **Step 1: Document behavior**

Document:

- `@bot + keyword/question` triggers read-only web research,
- group users can use public search research when mentioned,
- owner keeps browser snapshot/read privileges,
- browser interactions remain read-only and no login/click/download/form submission is allowed.

- [ ] **Step 2: Run focused verification**

Run:

```powershell
dotnet test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --no-restore --filter "FullyQualifiedName~AgentWebResearchServiceTests|FullyQualifiedName~AgentPublicSearchServiceTests|FullyQualifiedName~AgentWebAccessServiceTests|FullyQualifiedName~AgentWebAccessRouterTests"
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatWebResearchSemanticTriggerTests|FullyQualifiedName~QChatInternetCapabilityPolicyTests|FullyQualifiedName~QChatPublicInternetCommandPolicyTests|FullyQualifiedName~SemanticBrowser"
dotnet build --no-restore
```

Expected: all commands pass with zero errors.

### Task 4: Live QQ Smoke Diagnostics

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs`
- Modify: `Tests/Alife.Test.QChat/QChatDiagnosticsServiceTests.cs`
- Modify: `docs/agent-browser-web-research.md`

- [x] **Step 1: Add owner-only smoke checklist command**

Add `/qchat web smoke` to the diagnostics service. It returns a concise manual live-test checklist for owner private search, mentioned group search, non-owner denial, and `/qchat web doctor`.

- [x] **Step 2: Keep the command out of model execution**

The command stays inside the owner diagnostics command path, so it does not enter the model or public web research chain.

- [x] **Step 3: Verify with tests and build**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatDiagnosticsServiceTests|FullyQualifiedName~QChatOwnerCommandServiceTests|FullyQualifiedName~QChatPublicInternetCommandPolicyTests|FullyQualifiedName~PublicSearch|FullyQualifiedName~SemanticBrowser|FullyQualifiedName~WebResearch|FullyQualifiedName~QChatInternetCapabilityPolicyTests"
dotnet test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --no-restore --filter "FullyQualifiedName~AgentWebResearchServiceTests|FullyQualifiedName~AgentPublicSearchServiceTests|FullyQualifiedName~AgentWebAccessServiceTests|FullyQualifiedName~AgentWebAccessRouterTests|FullyQualifiedName~AgentBrowserSiteExperienceStoreTests"
dotnet build --no-restore
```

Expected: all commands pass with zero errors.

### Task 5: Conservative Owner Query Expansion

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.MessageFilter/AgentWebResearchService.cs`
- Modify: `Tests/Alife.Test.Framework/AgentWebResearchServiceTests.cs`
- Modify: `docs/agent-browser-web-research.md`

- [x] **Step 1: Add failing tests**

Add tests proving that owner research expands from `query` to `official docs query` when the original search only returns unsafe/private candidates, and that group-member research does not expand the query.

- [x] **Step 2: Implement minimal planner**

Keep the original query as the first search. Only when the original search has no usable public HTTP/HTTPS candidates and the actor is owner, try `official docs <query>`, `github <query>`, and `release notes <query>` in order.

- [x] **Step 3: Verify focused behavior**

Run:

```powershell
dotnet test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --no-restore --filter "FullyQualifiedName~AgentWebResearchServiceTests|FullyQualifiedName~AgentPublicSearchServiceTests|FullyQualifiedName~AgentWebAccessServiceTests|FullyQualifiedName~AgentWebAccessRouterTests|FullyQualifiedName~AgentBrowserSiteExperienceStoreTests"
```

Expected: all focused web research tests pass.

### Task 6: Site Experience Feedback Into Strategy

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.MessageFilter/AgentWebResearchService.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
- Modify: `Tests/Alife.Test.Framework/AgentWebResearchServiceTests.cs`
- Modify: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`
- Modify: `docs/agent-browser-web-research.md`
- Modify: `docs/browser-global-task-plan.md`

- [x] **Step 1: Add failing tests**

Add tests proving:

- research skips a known login-wall `Blocked` host and reads the next public result,
- research uses search snippet evidence for known anti-bot hosts without owner auto-read,
- QChat passes the injected `AgentBrowserSiteExperienceStore` into the research service, so blocked hosts do not leak back into QQ evidence.

- [x] **Step 2: Implement site experience-aware candidate strategy**

`AgentWebResearchService` now accepts an optional `AgentBrowserSiteExperienceStore`.

Behavior:

- `PreferredStrategy=Blocked` removes the host from candidates,
- anti-bot history switches owner evidence to snippet-only,
- recent success boosts candidate score,
- medium/high risk history lowers candidate score.

- [x] **Step 3: Wire QChat to the same store**

`QChatService` now creates `AgentWebResearchService` with `BrowserSiteExperienceStore`, matching the store already used by owner auto-read and browser snapshot paths.

- [x] **Step 4: Document token savings**

Documented the strategy in `docs/agent-browser-web-research.md` and `docs/browser-global-task-plan.md`.

- [x] **Step 5: Verify and upload**

Run focused Framework/QChat web research tests and `dotnet build --no-restore`, then upload through the `D:\FOXD` workflow.

Verification on 2026-06-23:

- Framework focused tests passed: 57 passed, 0 failed.
- QChat focused tests passed: 71 passed, 0 failed.
- `dotnet build --no-restore` passed with 0 warnings and 0 errors.

### Task 7: Token-Saving Search Intelligence

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.MessageFilter/AgentWebResearchService.cs`
- Modify: `Tests/Alife.Test.Framework/AgentWebResearchServiceTests.cs`
- Modify: `docs/agent-browser-web-research.md`
- Modify: `docs/browser-global-task-plan.md`

- [x] **Step 1: Add failing tests**

Add tests proving:

- latest/version owner requests use a focused `latest release notes` fallback,
- exact HTTP status/error requests quote the error before generic fallback,
- known Chinese technical terms can become compact English technical queries,
- group members never get owner query expansion.

- [x] **Step 2: Implement deterministic query planning**

Owner expansion now runs only when the original search produces no usable public candidates. The planner tries intent-aware queries before generic fallbacks and de-duplicates planned queries.

- [x] **Step 3: Keep token cost bounded**

Each behavior saves tokens by replacing broad fallback with one high-signal query and stopping as soon as usable candidates are found. No LLM-based query rewrite is used. Group member searches stay single-query and snippet-only.

- [x] **Step 4: Verify and upload**

Run focused Framework/QChat web research tests and `dotnet build --no-restore`, then upload through the `D:\FOXD` workflow.

Verification on 2026-06-23:

- Framework focused tests passed: 61 passed, 0 failed.
- QChat focused tests passed: 71 passed, 0 failed.
- `dotnet build --no-restore` passed with 0 warnings and 0 errors.

### Task 8: External RAG Closure

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.MessageFilter/AgentExternalRagStore.cs`
- Modify: `sources/Alife.Function/Alife.Function.MessageFilter/AgentExternalRagService.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs`
- Modify: `Tests/Alife.Test.Framework/AgentExternalRagServiceTests.cs`
- Modify: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`
- Modify: `docs/agent-browser-web-research.md`
- Modify: `docs/browser-global-task-plan.md`

- [x] **Step 1: Add failing tests**

Add tests proving:

- public RAG content is cleaned and compacted before chunking,
- source list returns metadata without chunk text,
- delete removes source and chunks,
- service list/delete do not fetch public pages,
- owner can list/delete through QQ,
- group members cannot trigger `/qchat rag delete`.

- [x] **Step 2: Implement store/service closure**

`AgentExternalRagStore` now supports `ListSources` and owner-only `DeleteSource`. Stored content is cleaned before chunking. `AgentExternalRagService` exposes list/delete and audits delete operations.

- [x] **Step 3: Implement QChat management closure**

Owner-only `/qchat rag list` and `/qchat rag delete <id|url>` now run deterministically without model dispatch. The RAG menu documents add/list/delete/status.

- [x] **Step 4: Keep token cost bounded**

Source cleanup removes noisy page text before storage. List replies return only metadata. Query replies stay chunk-capped. Non-owner `/qchat rag ...` commands are dropped before service dispatch.

- [x] **Step 5: Verify and upload**

Run focused Framework/QChat RAG tests and `dotnet build --no-restore`, then upload through the `D:\FOXD` workflow.

Verification on 2026-06-23:

- Framework focused tests passed: 43 passed, 0 failed.
- QChat focused tests passed: 76 passed, 0 failed.
- `dotnet build --no-restore` passed with 0 warnings and 0 errors.

### Task 9: Rate Limit, Cache, And Cost Control

**Files:**
- Create: `sources/Alife.Function/Alife.Function.MessageFilter/AgentWebResearchControlState.cs`
- Modify: `sources/Alife.Function/Alife.Function.MessageFilter/AgentWebResearchModels.cs`
- Modify: `sources/Alife.Function/Alife.Function.MessageFilter/AgentWebResearchService.cs`
- Modify: `sources/Alife.Function/Alife.Function.MessageFilter/AgentWebAccessRouter.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
- Modify: `Tests/Alife.Test.Framework/AgentWebResearchServiceTests.cs`
- Modify: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`
- Modify: `docs/agent-browser-web-research.md`
- Modify: `docs/browser-global-task-plan.md`

- [x] **Step 1: Add failing tests**

Add tests proving:

- identical queries reuse cached results and do not call public search twice,
- rapid different group queries are rejected before public search,
- the concurrent cap rejects an extra request without provider calls,
- metrics count search, read, bytes, latency, and approximate summary tokens,
- QChat group mention cooldown replies without entering search or model dispatch.

- [x] **Step 2: Implement shared control state**

`AgentWebResearchControlState` now owns:

- short-term successful-result cache,
- group-member per-user and per-group cooldown,
- non-cached concurrent research cap,
- counters for search/read/bytes/latency/estimated summary tokens/cache hits/rejections.

- [x] **Step 3: Wire QChat defaults**

`QChatConfig` now exposes:

- `PublicInternetUserCooldownSeconds = 15`
- `PublicInternetGroupCooldownSeconds = 30`
- `PublicInternetResultCacheSeconds = 120`
- `PublicInternetMaxConcurrentResearch = 2`

`QChatService` passes QQ `UserId` and `GroupId` into `AgentWebResearchRequest`, and reuses one in-memory control state per service instance.

- [x] **Step 4: Keep token cost bounded**

The service checks cache before cooldown/search/read/model dispatch, rejects rapid group abuse before public search, and rejects concurrent overflow before starting network work. Metrics stay counter-only and do not retain full page text.

- [x] **Step 5: Verify and upload**

Run focused Framework/QChat web research tests and `dotnet build --no-restore`, then upload through the `D:\FOXD` workflow.

Verification on 2026-06-23:

- Framework focused tests passed: 86 passed, 0 failed.
- QChat focused tests passed: 81 passed, 0 failed.
- `dotnet build --no-restore` passed with 0 warnings and 0 errors.

### Task 10: Browser Snapshot Productization

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.MessageFilter/AgentBrowserProviderModels.cs`
- Modify: `sources/Alife.Function/Alife.Function.Browser/AgentBrowserRuntimeProvider.cs`
- Modify: `Tests/Alife.Test.Framework/AgentBrowserProviderModelsTests.cs`
- Modify: `Tests/Alife.Test.Browser/BrowserServiceAdapterTests.cs`
- Modify: `docs/agent-browser-web-research.md`
- Modify: `docs/browser-global-task-plan.md`

- [x] **Step 1: Add failing tests**

Add tests proving:

- browser snapshot formatting emits truncation/link/risk diagnostics inside untrusted context,
- structured DOM extraction returns title, body text, and links,
- login-wall pages return `login_required`,
- anti-bot pages return `anti_bot_challenge`.

- [x] **Step 2: Implement structured read-only extraction**

`AgentBrowserRuntimeProvider` now runs one read-only DOM extraction script for `document.title`, `document.body.innerText`, and `a[href]` links. If structured body text is unavailable, it falls back to `ObserveAsync(page)`.

- [x] **Step 3: Add snapshot diagnostics**

`AgentBrowserSnapshotDiagnostics` records login-wall detection, anti-bot detection, truncation, original text length, and total link count. The formatter emits these as compact metadata.

- [x] **Step 4: Keep token cost bounded**

Large text is capped before formatting, links are capped by `maxElements`, and diagnostics preserve counts without dumping full pages. No LLM summarizer is used.

- [x] **Step 5: Preserve hard browser boundary**

The productized snapshot path remains read-only. It does not add clicking, login, download, form submission, or interactive browser control.

Verification on 2026-06-23:

- Framework browser provider model tests passed: 5 passed, 0 failed.
- Browser runtime provider tests passed: 5 passed, 0 failed.
- Framework focused browser/web tests passed: 56 passed, 0 failed.
- Browser adapter tests passed: 10 passed, 0 failed.
- QChat focused browser/diagnostics/policy tests passed: 74 passed, 0 failed.
- `dotnet build --no-restore` passed with 0 warnings and 0 errors.

### Task 11: Engineering Cleanup And Upload Hygiene

**Files:**
- Modify: `docs/browser-global-task-plan.md`
- Track: `docs/d-drive-storage.md`

- [x] **Step 1: Audit tracked browser roadmap files**

Confirmed representative browser/web research source, test, and documentation files are tracked by `git ls-files`, including:

- `sources/Alife.Function/Alife.Function.MessageFilter/AgentWebResearchControlState.cs`
- `sources/Alife.Function/Alife.Function.MessageFilter/AgentWebResearchService.cs`
- `sources/Alife.Function/Alife.Function.Browser/AgentBrowserRuntimeProvider.cs`
- `Tests/Alife.Test.Framework/AgentWebResearchServiceTests.cs`
- `Tests/Alife.Test.Browser/BrowserServiceAdapterTests.cs`
- `docs/d-drive-storage.md`

- [x] **Step 2: Audit generated/runtime exclusions**

Confirmed these root-level generated/runtime directories are not tracked:

- `Outputs`
- `Runtime`
- `Storage`
- `Models`
- `bin`
- `obj`
- `.tmp`
- `.codegraph`
- `.worktrees`

- [x] **Step 3: Preserve D-drive storage preference**

`docs/d-drive-storage.md` documents D-drive project paths and optional environment variables for NuGet, dotnet, temp, and Playwright caches.

- [x] **Step 4: Keep encoding cleanup separate**

Historical garbled Chinese text remains a separate cleanup task. It was not changed during browser roadmap work to avoid noisy diffs and accidental behavior changes.

- [x] **Step 5: Verify and upload**

Run focused documentation/upload-hygiene audits, `dotnet build --no-restore`, then upload through the `D:\FOXD` workflow.

Verification on 2026-06-23:

- Representative browser/web research files were listed by `git ls-files`.
- Root-level generated/runtime directories were absent from `git ls-files`.
- `git ls-files --others --exclude-standard` returned no untracked paths, only the existing user-level ignore permission warning.
- `dotnet build --no-restore` passed with 0 warnings and 0 errors.

### Self-Review

- Spec coverage: covers keyword trigger, public search, page read, evidence summary, QQ formatting, permissions, and docs.
- Placeholder scan: no TBD/TODO placeholders.
- Type consistency: service and model names use `AgentWebResearch*`; QChat formatter uses `QChatWebResearchFormatter`.
