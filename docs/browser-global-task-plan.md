# Browser And Web Research Global Task Plan

## Operating Rule

After each priority is completed:

1. Run the relevant focused tests and `dotnet build --no-restore`.
2. Record the completion evidence in this document or the linked validation record.
3. Upload the tracked project snapshot to GitHub through the `D:\FOXD` workflow.
4. Report the completed priority and the next remaining priority to the owner.

Do not mark a priority complete when it only has unit tests but still needs a live QQ/NapCat condition that was not available. In that case, record the blocker explicitly.

## Priority 1: Real Link Validation

Status: partial completion; QQ message-level smoke is blocked by local runtime/session state.

Goal: prove the browser/web research feature works in the real runtime, not only in unit tests.

Tasks:

- Run `/qchat web smoke` checklist in a live QQ/NapCat session.
- Verify owner private natural search triggers web research.
- Verify group `@bot` search intent triggers public search evidence only.
- Verify non-owner private `/search` does not enter the model, expose menus, or trigger search.
- Verify real public search provider behavior in the current network.
- Verify real public page read behavior for representative sites.
- Record actual provider/read failures into validation notes.

Completion evidence required:

- QChat live health result.
- OneBot/NapCat availability result.
- At least one real public search smoke result.
- At least one real public HTTP/HTTPS read smoke result.
- If QQ/NapCat is unavailable, a blocker record plus successful provider/read smoke is acceptable only as partial completion, not full live completion.

Validation record:

- `docs/browser-live-validation-2026-06-23.md`

Current evidence:

- Real project-level public search/read smoke passed with `ALIFE_WEB_LIVE_SMOKE=1`.
- NapCat was running and `127.0.0.1:3001` was reachable.
- Mio OneBot action probe returned `good=true`, `online=false`.
- XiaYu endpoint `127.0.0.1:3002` was unreachable.
- Alife was not persistently reported as running by live health.

Remaining before full completion:

- Restore a healthy OneBot session for the target bot.
- Run the QQ message-level smoke checklist from `/qchat web smoke`.

## Priority 2: Site Experience Feedback Into Strategy

Status: completed on 2026-06-23.

Goal: make recorded site experience influence future web research decisions.

Tasks:

- Lower rank or skip hosts with recent login-wall failures. Done: `Blocked` site experience is removed from research candidates before owner auto-read.
- Lower rank or snippet-only hosts with repeated fetch failures. Done: anti-bot history avoids owner auto-read and uses compact search snippet evidence instead.
- Prefer official/docs/GitHub hosts when site history is clean. Done: source type ranking remains in place, and clean successful history receives a positive candidate score.
- Surface recent failures in `/qchat web doctor`. Done: browser doctor/status use `AgentBrowserSiteExperienceStore` recent records.
- Add tests for failure-driven ranking and fallback. Done: framework and QChat integration tests cover blocked-host skip and anti-bot snippet fallback.

Token saving effect:

- Known login-wall hosts are skipped before page reads, avoiding failed fetch/browser attempts and avoiding low-value snippets in final evidence.
- Known anti-bot hosts are not auto-read by the owner path; the bot uses the short search snippet instead.
- Candidate scoring favors sources with clean successful history, reducing wasted attempts on repeatedly failing domains.
- Evidence remains compact and deterministic, so the later QQ formatting path does not carry full external page text when it is unlikely to help.

Verification evidence:

- `dotnet test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --no-restore --filter "FullyQualifiedName~ResearchAsync_SkipsKnownLoginWallHostFromSiteExperience|FullyQualifiedName~ResearchAsync_UsesSearchSnippetForKnownAntiBotHostWithoutReadingPage"` passed.
- `dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~WebResearchOwnerPrivateSemanticSearchUsesInjectedSiteExperienceToSkipBlockedHost"` passed after wiring QChat research to the shared site experience store.
- `dotnet test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --no-restore --filter "FullyQualifiedName~AgentWebResearchServiceTests|FullyQualifiedName~AgentBrowserSiteExperienceStoreTests|FullyQualifiedName~AgentWebAccessServiceTests|FullyQualifiedName~AgentWebAccessRouterTests|FullyQualifiedName~AgentPublicSearchServiceTests"` passed: 57 passed, 0 failed.
- `dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~WebResearch|FullyQualifiedName~QChatPublicInternetCommandPolicyTests|FullyQualifiedName~QChatInternetCapabilityPolicyTests|FullyQualifiedName~QChatDiagnosticsServiceTests"` passed: 71 passed, 0 failed.
- `dotnet build --no-restore` passed with 0 warnings and 0 errors.

## Priority 3: Search Intelligence

Status: completed on 2026-06-23.

Goal: make search planning smarter without loosening safety boundaries.

Tasks:

- Expand owner queries based on intent, not only fixed fallback templates. Done: owner-only fallback now tries high-signal intent queries before generic `official docs`, `github`, and `release notes` fallbacks.
- Add freshness-aware query planning for latest/news/version asks. Done: latest/version/news style requests add a focused `latest release notes` query.
- Add exact-error query handling for bug reports. Done: HTTP status and exception/error terms are quoted before generic fallback.
- Add Chinese-to-English technical keyword expansion where useful. Done: browser/search/read/anti-bot/login-wall/RAG style Chinese terms can produce compact English technical queries.
- Keep group-member search conservative unless explicitly allowed later. Done: group members still use only the original query and never run owner query expansion.

Token saving effect:

- Intent expansions run only after the original owner search has no usable public HTTP/HTTPS candidate.
- The planner stops as soon as one expansion produces usable candidates, so successful intent plans avoid generic fallback searches.
- Latest/version questions use one focused `latest release notes` expansion instead of broad news-style browsing.
- Error questions quote the exact status/exception phrase, reducing irrelevant result pages and wasted page reads.
- Chinese technical questions use compact English terms only when matching known technical vocabulary, avoiding LLM-based query rewriting.
- Group members do not receive expansion, auto-read, or browser evidence, keeping group-triggered token and network cost bounded.

Verification evidence:

- `dotnet test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --no-restore --filter "FullyQualifiedName~ResearchAsync_OwnerUsesFreshnessAwareExpansionForLatestRequests|FullyQualifiedName~ResearchAsync_OwnerUsesExactErrorExpansionBeforeGenericFallback|FullyQualifiedName~ResearchAsync_OwnerUsesEnglishTechnicalExpansionForChineseBrowserTerms|FullyQualifiedName~ResearchAsync_GroupMemberDoesNotUseIntentExpansionForLatestRequests"` passed: 4 passed, 0 failed.
- `dotnet test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --no-restore --filter "FullyQualifiedName~AgentWebResearchServiceTests|FullyQualifiedName~AgentBrowserSiteExperienceStoreTests|FullyQualifiedName~AgentWebAccessServiceTests|FullyQualifiedName~AgentWebAccessRouterTests|FullyQualifiedName~AgentPublicSearchServiceTests"` passed: 61 passed, 0 failed.
- `dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~WebResearch|FullyQualifiedName~QChatPublicInternetCommandPolicyTests|FullyQualifiedName~QChatInternetCapabilityPolicyTests|FullyQualifiedName~QChatDiagnosticsServiceTests"` passed: 71 passed, 0 failed.
- `dotnet build --no-restore` passed with 0 warnings and 0 errors.

## Priority 4: External RAG Closure

Status: completed on 2026-06-23.

Goal: turn approved public research results into reusable external knowledge.

Tasks:

- Add owner-approved source ingestion. Done: `/qchat rag add <url>` remains owner-only and fetches public HTTP/HTTPS pages before storing them.
- Clean and chunk public page text. Done: HTML tags, script/style blocks, common boilerplate, and repeated whitespace are stripped before chunking.
- Deduplicate stored sources. Done: adding the same URL replaces the old source and removes stale chunks.
- Query stored public knowledge from QQ. Done: `/rag <question>` and semantic external RAG query paths return stored public knowledge without network or model dispatch.
- Keep add/delete/refresh/configuration owner-only. Done: `/qchat rag list` and `/qchat rag delete <id|url>` are owner-only; non-owner `/qchat` commands are dropped before reaching the RAG service.

Token saving effect:

- Source ingestion cleans noisy page text before chunking, so scripts, style blocks, cookie banners, navigation text, and repeated whitespace do not become reusable prompt context.
- Stored source listing returns only compact metadata: count, id, title, and URL. It never returns chunk text.
- RAG queries use `PublicExternalRagMaxChunks`, so QQ retrieval has a hard chunk cap.
- Add/delete/list management commands bypass the model and do not perform public search.
- Non-owner `/qchat rag ...` commands are dropped before service dispatch, menu rendering, and model execution.

Verification evidence:

- `dotnet test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --no-restore --filter "FullyQualifiedName~AddSource_CleansBoilerplateAndCompactsChunksToSaveTokens|FullyQualifiedName~ListSources_ReturnsCompactMetadataWithoutChunkText|FullyQualifiedName~DeleteSource_RemovesSourceAndChunksByUrl|FullyQualifiedName~DeleteSource_RejectsNonOwnerWrites"` passed: 4 passed, 0 failed.
- `dotnet test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --no-restore --filter "FullyQualifiedName~ListSources_ReturnsStoredSourcesWithoutFetching|FullyQualifiedName~DeleteSource_WhenOwner_RemovesStoredSourceAndAuditsSuccess|FullyQualifiedName~DeleteSource_WhenNonOwner_DoesNotDeleteOrFetch"` passed: 3 passed, 0 failed.
- `dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~OwnerCanListExternalRagSources|FullyQualifiedName~OwnerCanDeleteExternalRagSource|FullyQualifiedName~GroupMemberCannotDeleteExternalRagSourceViaQChat"` passed: 3 passed, 0 failed.
- `dotnet test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --no-restore --filter "FullyQualifiedName~AgentExternalRagServiceTests|FullyQualifiedName~AgentWebAccessServiceTests|FullyQualifiedName~AgentWebAccessRouterTests"` passed: 43 passed, 0 failed.
- `dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~Rag|FullyQualifiedName~ExternalRag|FullyQualifiedName~QChatPublicInternetCommandPolicyTests|FullyQualifiedName~QChatInternetCapabilityPolicyTests|FullyQualifiedName~QChatDiagnosticsServiceTests"` passed: 76 passed, 0 failed.
- `dotnet build --no-restore` passed with 0 warnings and 0 errors.

## Priority 5: Rate Limit, Cache, And Cost Control

Status: completed on 2026-06-23.

Goal: prevent group search abuse and make cost/latency observable.

Tasks:

- Add per-group and per-user cooldowns. Done: group-member web research uses `PublicInternetUserCooldownSeconds` and `PublicInternetGroupCooldownSeconds` before public search.
- Add short-term query result cache. Done: successful research results can be reused through `PublicInternetResultCacheSeconds`; identical cached queries return before cooldown, search, read, or model dispatch.
- Add concurrent web research cap. Done: `PublicInternetMaxConcurrentResearch` bounds non-cached research work and rejects excess requests with `web_research_busy`.
- Track search count, read count, page bytes, latency, and approximate summarization cost. Done: `AgentWebResearchControlState` tracks searches, owner reads, UTF-8 page bytes, total latency, estimated summary tokens, cache hits, rate-limit hits, and concurrency rejections.
- Add visible refusal or silence policy for over-limit group requests. Done: QChat sends a compact `web_research_rate_limited: cooldown` reply for over-limit group research and does not call search or the model.

Default QChat controls:

- `PublicInternetUserCooldownSeconds = 15`
- `PublicInternetGroupCooldownSeconds = 30`
- `PublicInternetResultCacheSeconds = 120`
- `PublicInternetMaxConcurrentResearch = 2`

Token saving effect:

- Repeated identical queries hit the short-term cache before cooldown checks, public search, page reads, or model dispatch.
- Different rapid group queries are rejected before provider calls, so group abuse does not consume search/read tokens or browser time.
- The concurrency cap returns immediately when the research worker pool is full instead of queueing expensive network work.
- Metrics let later diagnostics estimate cost from search count, read count, page bytes, latency, and approximate summary tokens without logging full page text.
- Owner auto-read remains bounded by the same cache/concurrency controls, while group members remain snippet-only.

Verification evidence:

- `dotnet test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --no-restore --filter "FullyQualifiedName~ResearchAsync_ReusesCachedResultBeforeSearchingAgain|FullyQualifiedName~ResearchAsync_GroupMemberCooldownRejectsDifferentQueryBeforeSearch|FullyQualifiedName~ResearchAsync_ConcurrentCapRejectsExtraRequestWithoutSearch|FullyQualifiedName~ResearchAsync_TracksSearchReadBytesLatencyAndApproximateSummaryCost"` passed: 4 passed, 0 failed.
- `dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~WebResearchGroupMentionCooldownDoesNotSearchOrDispatchModel"` passed: 1 passed, 0 failed.
- `dotnet test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --no-restore --filter "FullyQualifiedName~AgentWebResearchServiceTests|FullyQualifiedName~AgentBrowserSiteExperienceStoreTests|FullyQualifiedName~AgentWebAccessServiceTests|FullyQualifiedName~AgentWebAccessRouterTests|FullyQualifiedName~AgentPublicSearchServiceTests|FullyQualifiedName~AgentExternalRagServiceTests"` passed: 86 passed, 0 failed.
- `dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~WebResearch|FullyQualifiedName~Rag|FullyQualifiedName~ExternalRag|FullyQualifiedName~QChatPublicInternetCommandPolicyTests|FullyQualifiedName~QChatInternetCapabilityPolicyTests|FullyQualifiedName~QChatDiagnosticsServiceTests"` passed: 81 passed, 0 failed.
- `dotnet build --no-restore` passed with 0 warnings and 0 errors.

## Priority 6: Browser Snapshot Productization

Status: completed on 2026-06-23.

Goal: make owner-only read-only browser snapshots more reliable.

Tasks:

- Improve title/body/link extraction. Done: `AgentBrowserRuntimeProvider` now runs one read-only DOM extraction script for `document.title`, compact `document.body.innerText`, and public links, with `ObserveAsync` retained as fallback.
- Detect login walls and anti-bot pages. Done: snapshot analysis detects common sign-in/account-required and captcha/Cloudflare/human-verification signals; detected pages return `login_required` or `anti_bot_challenge`.
- Summarize large pages safely. Done: formatter records truncation metadata and emits capped text plus capped link evidence instead of dumping full page text.
- Keep snapshots as untrusted external context. Done: successful snapshots remain wrapped in `[UNTRUSTED EXTERNAL CONTEXT: browser-snapshot]` and include risk metadata inside that wrapper.
- Do not add click/login/download/form/JS interaction without a separate owner-approved high-risk design. Done: the productized snapshot path remains read-only; it navigates, reads DOM text/links, and observes fallback text only.

Token saving effect:

- One structured DOM extraction gathers title/body/links in a single browser script call, instead of requiring separate element inspection loops.
- Large page text is capped and marked with `text_truncated=true`, preventing full-page dumps into QQ/model context.
- Link output is capped by `maxElements`, and `links_total` preserves count without emitting every link.
- Login-wall and anti-bot pages are rejected with short reasons before they become reusable research evidence.
- No LLM summarizer is used; the safe summary is deterministic cleanup, truncation, and metadata.

Verification evidence:

- `dotnet test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --no-restore --filter "FullyQualifiedName~AgentBrowserProviderModelsTests"` passed: 5 passed, 0 failed.
- `dotnet test Tests\Alife.Test.Browser\Alife.Test.Browser.csproj --no-restore --filter "FullyQualifiedName~AgentBrowserRuntimeProvider|FullyQualifiedName~BrowserService_CanProvideAgentBrowserSnapshot"` passed: 5 passed, 0 failed.
- `dotnet test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --no-restore --filter "FullyQualifiedName~AgentBrowserProviderModelsTests|FullyQualifiedName~AgentBrowserSiteExperienceStoreTests|FullyQualifiedName~AgentWebAccessServiceTests|FullyQualifiedName~AgentWebAccessRouterTests|FullyQualifiedName~AgentWebResearchServiceTests"` passed: 56 passed, 0 failed.
- `dotnet test Tests\Alife.Test.Browser\Alife.Test.Browser.csproj --no-restore --filter "FullyQualifiedName~BrowserServiceAdapterTests"` passed: 10 passed, 0 failed.
- `dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~SemanticBrowser|FullyQualifiedName~BrowserSnapshot|FullyQualifiedName~QChatDiagnosticsServiceTests|FullyQualifiedName~QChatInternetCapabilityPolicyTests|FullyQualifiedName~QChatPublicInternetCommandPolicyTests"` passed: 74 passed, 0 failed.
- `dotnet build --no-restore` passed with 0 warnings and 0 errors.

## Priority 7: Engineering Cleanup And Upload Hygiene

Status: completed on 2026-06-23.

Goal: keep browser work maintainable and uploadable.

Tasks:

- Ensure all new web research files are tracked by git before upload. Done: audited representative browser/web research source, test, and doc files with `git ls-files`; added `docs/d-drive-storage.md` to the tracked snapshot.
- Keep generated/runtime data out of upload snapshots. Done: root-level `Outputs`, `Runtime`, `Storage`, `Models`, `bin`, `obj`, `.tmp`, `.codegraph`, and `.worktrees` were checked and are not tracked.
- Clean up historical garbled Chinese docs/tests in a separate task. Done: left existing mojibake untouched during browser work and recorded it as a separate cleanup task, not part of the browser capability changes.
- Keep D-drive storage preference for runtime/cache/temp data. Done: `docs/d-drive-storage.md` documents project-owned D-drive paths and optional user environment variables for NuGet, dotnet, temp, and Playwright cache relocation.
- Use `D:\FOXD` upload workflow after each completed priority. Done for Priorities 1 through 6; this priority also uses the same workflow after verification.

Upload hygiene evidence:

- `git ls-files docs/d-drive-storage.md sources/Alife.Function/Alife.Function.MessageFilter/AgentWebResearchControlState.cs sources/Alife.Function/Alife.Function.MessageFilter/AgentWebResearchService.cs sources/Alife.Function/Alife.Function.Browser/AgentBrowserRuntimeProvider.cs Tests/Alife.Test.Framework/AgentWebResearchServiceTests.cs Tests/Alife.Test.Browser/BrowserServiceAdapterTests.cs` listed every requested file.
- `git ls-files Outputs Runtime Storage Models bin obj .tmp .codegraph .worktrees` returned no tracked files.
- `git ls-files | Select-String -Pattern "^(Outputs|Runtime|Storage|Models|bin|obj|\.tmp|\.codegraph|\.worktrees)(/|$)"` returned no root-level generated/runtime tracked files.
- `git ls-files --others --exclude-standard` returned no untracked file paths; it only emitted the existing user-level warning about `C:\Users\hu shu/.config/git/ignore` permission.
- `dotnet build --no-restore` passed with 0 warnings and 0 errors.

Remaining follow-up outside the browser roadmap:

- Historical garbled Chinese text still exists in some docs/tests/source comments. Clean it in a separate encoding-focused pass with narrow tests, because broad text cleanup during browser feature work would create noisy diffs and higher regression risk.

## Priority 8: Browser Agent Automation Phase 1

Status: completed on 2026-06-23.

Goal: add owner-only bounded browser automation over public pages while keeping group-member web access on the lighter public search/RAG path.

Scope:

- Owner private chat may trigger browser automation through semantic browser-agent wording such as `browse https://example.com/docs`.
- Non-owner private users and all group messages must not trigger the browser automation provider, browser automation menu chain, or model fallback through browser-agent wording.
- Public search and external RAG for group members remain separate from browser automation.
- Runtime media cache should stay on controlled D-drive paths, not C drive.

Allowed actions:

- search public web;
- navigate public HTTP/HTTPS URLs;
- capture read-only browser snapshots;
- observe bounded text/links;
- follow safe public links;
- return validated public images as QQ images later;
- return public videos as links only.

Blocked actions:

- login;
- form submission;
- arbitrary download;
- video download or upload;
- local-file upload;
- arbitrary JavaScript;
- private network, localhost, loopback, `file:`, `data:`, or `javascript:` targets.

Completion evidence required:

- `AgentBrowserActionPolicyTests` pass.
- `AgentBrowserTaskPlannerTests` pass.
- `AgentBrowserAutomationServiceTests` pass.
- `AgentBrowserMediaOutputServiceTests` pass.
- QChat owner-private automation test passes.
- QChat non-owner and group denial tests pass.
- `/qchat web browser-agent` diagnostics text exists and contains `browser-agent=phase1`, `owner-only`, `no-login`, `image-ok`, and `video-link-only`.
- Focused build/test verification passes.
- GitHub upload through `D:\FOXD` uses the full `D:\Alife` source root.

Verification evidence:

- `dotnet test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --filter "AgentBrowserActionPolicyTests|AgentBrowserTaskPlannerTests|AgentBrowserAutomationServiceTests|AgentBrowserMediaOutputServiceTests|AgentWebAccessRouterTests|AgentBrowserProviderModelsTests"` passed: 100 passed, 0 failed.
- `dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "QChatBrowserAgentTriggerPolicyTests|QChatBrowserAgentFormatterTests|OwnerPrivateBrowserAgentRequestRunsAutomationWithoutModelDispatch|NonOwnerPrivateBrowserAgentRequestDoesNotRunAutomationOrModel|GroupBrowserAgentRequestDoesNotRunAutomation|QChatDiagnosticsServiceTests"` passed: 42 passed, 0 failed.
- `dotnet build --no-restore` passed with 0 warnings and 0 errors.
- GitHub upload through `D:\FOXD` verified remote commit `9a380b9cc98ff4f5c3ce5b03205603d451104a96` on `refs/heads/master`.

## Priority 9: Browser Agent Media Return Phase 1

Status: completed on 2026-06-23.

Goal: complete the browser-agent media return loop for owner private QChat.

Completed behavior:

- QChat extracts public image/video URL candidates from successful browser-agent evidence.
- Public images pass through `AgentBrowserMediaOutputService` before QQ output.
- Validated images are sent as `[CQ:image,file=...]` messages after the browser text summary.
- Videos are returned as text links only.
- Browser-agent videos are not downloaded, not sent as `[CQ:video]`, and not uploaded through QQ file APIs.
- Media cache remains under `D:\Alife\Runtime\BrowserAgentMedia`.
- Non-owner private and group browser automation remain blocked by the Phase 1 trigger policy.

Verification evidence:

- `dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter QChatBrowserAgentFormatterTests` passed: 5 passed, 0 failed.
- `dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "OwnerPrivateBrowserAgentImageUrlReturnsQqImageAfterTextReply|OwnerPrivateBrowserAgentVideoUrlReturnsLinkOnly"` passed: 2 passed, 0 failed.

## Priority 10: Browser Agent Live Smoke Readiness

Status: completed on 2026-06-23 for deterministic checklist support. Real QQ/NapCat message-level smoke remains pending until the target bot session is healthy.

Goal: make browser-agent live validation repeatable without guessing or triggering unsafe browser actions.

Scope:

- Add an owner diagnostics checklist command for browser-agent live smoke readiness.
- Keep the checklist deterministic and compact; it must not run the browser, send QQ messages, call the model, or trigger provider chains by itself.
- Include owner private text, owner private image, owner private video, non-owner denial, and group denial checks.
- Keep images as validated QQ image return and videos as link-only.
- Keep cache and temporary media paths on D drive.

Command:

```text
/qchat web browser-agent smoke
```

Expected markers:

- `browser-agent-live-smoke`
- `status=manual`
- `live-smoke=pending`
- `owner-private-text`
- `owner-private-image`
- `owner-private-video`
- `non-owner-denied`
- `group-denied`
- `image-return=connected`
- `video-return=link-only`
- `media-cache=D:\Alife\Runtime\BrowserAgentMedia`
- `blocked=no-login no-form-submit no-video-download no-local-upload no-js no-private-network`

Completion evidence required:

- `QChatDiagnosticsServiceTests.TryHandleWebBrowserAgentSmokeReturnsLiveChecklist` passes.
- Existing browser-agent diagnostics still include `browser-agent=phase1`, `owner-only`, `image-return=connected`, and `video-return=link-only`.
- `dotnet build --no-restore` passes.
- GitHub upload through `D:\FOXD` verifies the new snapshot.
- Real QQ/NapCat smoke is recorded separately as pending or passed; do not claim live closure without a healthy OneBot session.

Verification evidence:

- `dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter TryHandleWebBrowserAgentSmokeReturnsLiveChecklist` passed: 1 passed, 0 failed.
- `dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter QChatDiagnosticsServiceTests` passed: 31 passed, 0 failed.
- `dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "QChatBrowserAgentFormatterTests|QChatBrowserAgentTriggerPolicyTests|OwnerPrivateBrowserAgentRequestRunsAutomationWithoutModelDispatch|OwnerPrivateBrowserAgentImageUrlReturnsQqImageAfterTextReply|OwnerPrivateBrowserAgentVideoUrlReturnsLinkOnly|NonOwnerPrivateBrowserAgentRequestDoesNotRunAutomationOrModel|GroupBrowserAgentRequestDoesNotRunAutomation|TryHandleWebBrowserAgentReturnsOwnerOnlyPhaseOneSummary|TryHandleWebBrowserAgentSmokeReturnsLiveChecklist"` passed: 18 passed, 0 failed.
- `dotnet build --no-restore` passed with 0 warnings and 0 errors.
- Real QQ/NapCat browser-agent live smoke remains pending and is tracked in `docs/browser-live-validation-2026-06-23.md`.
