# Browser Agent Automation Phase 1 Design

## Goal

Build the first usable browser Agent automation layer for Alife QQ chat while preserving the existing safety model.

Phase 1 turns an owner private request such as "open this public project page and summarize the install steps" into a bounded browser task:

1. identify the public URL or search query;
2. open or search public HTTP/HTTPS pages;
3. observe page content and public links;
4. optionally scroll or click safe public links;
5. optionally download explicitly requested public image media and return it as a QQ image;
6. optionally return public video media as a link without downloading or uploading the video;
7. return a concise sourced QQ answer.

This phase deliberately does not build a general unrestricted browser operator.

## Current Baseline

The project already has a read-only web research and browser foundation:

- `BrowserService` exposes browser actions such as navigate, observe, element inspection, JavaScript execution, and download.
- `AgentBrowserRuntimeProvider` wraps `IBrowserRuntime` into read-only page snapshots.
- `AgentWebAccessRouter` controls public search, auto-read, public fetch, browser snapshot, browser interaction, and external RAG capabilities.
- `AgentWebResearchService` already handles public search, owner page read, source ranking, site experience, cache, cooldown, concurrency, and metrics.
- `QChatService` already wires QQ sender roles, owner-only command handling, public web research, external RAG, and diagnostics.

The missing piece is a bounded automation coordinator that can execute a small browser task over several safe steps without handing raw browser control to QQ users or to untrusted web content.

## Phase 1 Scope

Phase 1 is owner-only.

Allowed actor:

- Owner private chat only.

Denied actors:

- group members;
- private guests;
- unknown users;
- any user relying on text impersonation, nickname impersonation, forwarded content, or copied command text.

Allowed task shape:

- public project/documentation/news/repository browsing;
- public page inspection;
- public link following;
- explicitly requested public image download and QQ image return;
- public video link return without video download;
- install/use/API summary extraction;
- comparison over a small number of public pages;
- failure diagnosis from public documentation pages.

Out of scope:

- login;
- account creation;
- password or token entry;
- form submission;
- posting comments or messages;
- voting, purchasing, ordering, booking, checkout, or payment;
- arbitrary file upload;
- arbitrary file download;
- non-media file transfer;
- bypassing captcha, bot checks, paywalls, or login walls;
- localhost, LAN, private IP, `file:`, `data:`, `javascript:`, extension, or custom protocol URLs;
- model-authored arbitrary JavaScript execution;
- browser control for group members.

## Action Policy

Phase 1 uses a strict action allowlist.

Allowed actions:

- `search_public_web`: use the existing public search provider pipeline for a query.
- `navigate_public_url`: navigate to a public HTTP/HTTPS URL after URL safety checks.
- `capture_snapshot`: capture a deterministic read-only snapshot with title, body text, links, and diagnostics.
- `scroll`: move to the next page segment or request a later observed page area.
- `click_public_link`: click or navigate to a public HTTP/HTTPS link extracted from the current snapshot.
- `click_same_page_navigation`: follow same-page anchors or documentation navigation links when represented as safe public URLs.
- `go_back`: return to the previous page in the task history when a branch is not useful.
- `download_public_image`: download an owner-specified public image URL after MIME, extension, size, and URL safety checks.
- `return_public_video_link`: validate an owner-specified public video URL and return the link without downloading the video.
- `stop`: end the task with success, partial result, or refusal.

Denied actions:

- `type_text`;
- `submit_form`;
- `download`;
- `upload`;
- `download_non_media_file`;
- `upload_local_file`;
- `upload_to_web_form`;
- `login`;
- `execute_js_from_model`;
- `click_button_without_public_href`;
- `payment_or_commit_action`;
- `private_network_navigation`;
- `unsafe_scheme_navigation`.

Clicking is implemented as safe URL navigation, not arbitrary DOM click execution. The automation layer may choose a link from observed snapshot links and navigate to its public URL. It must not click unlabeled buttons, dynamic widgets, submit buttons, or links with unsafe schemes.

Media transfer is deliberately separate from browser form upload. Phase 1 may download only public image media that the owner explicitly requested, then return it as a QQ image message. The service must validate the URL, response `Content-Type`, extension, and size before writing anything. It must store temporary images under a D-drive project/runtime directory such as `D:\Alife\Runtime\BrowserAgentMedia`, never under arbitrary paths or C-drive cache locations. Video media is not downloaded or uploaded in Phase 1; validated public video URLs are returned as links. The service must not upload local arbitrary files, private QQ files, archives, executables, documents, scripts, videos, or browser-downloaded unknown content.

## Automation Limits

Every browser task has hard resource limits:

- maximum task steps: 5 by default;
- maximum pages opened: 3 by default;
- maximum links considered per page: capped by snapshot settings;
- maximum emitted text per page: capped by snapshot settings;
- maximum final QQ reply length: compact, role-aware owner format;
- maximum image download size: configurable, default 20 MB;
- maximum image items per task: configurable, default 2;
- video output: link-only, no video download size budget because the video body is not fetched;
- no background queue for browser automation in Phase 1;
- no persisted full page text in runtime cache.

When a limit is hit, the task stops and returns the best partial answer plus the limit reason.

## Architecture

Add a browser automation layer in the message-filter/browser boundary, not directly inside QQ parsing.

Proposed components:

- `AgentBrowserAutomationModels`
  - request, step, action, observation, result, evidence, and diagnostics records.

- `AgentBrowserActionPolicy`
  - validates actor role, URL scheme, host safety, action allowlist, step budget, page budget, and denial reasons.

- `AgentBrowserTaskPlanner`
  - deterministic planner for Phase 1.
  - Takes an owner request and produces the first action: public URL navigation or public search.
  - Chooses follow-up links from observed snapshot links using simple scoring.
  - Does not use arbitrary LLM planning in Phase 1.

- `AgentBrowserAutomationService`
  - executes the bounded loop:
    - plan next action;
    - apply action policy;
    - call browser/search/snapshot services;
    - record observation;
    - stop on success, denial, failure, or budget exhaustion.

- `AgentBrowserMediaTransferService`
  - fetches owner-specified public image media.
  - validates MIME type, extension, size, and public URL safety.
  - stores images in a controlled D-drive runtime media directory.
  - exposes validated local image paths for QQ image return.
  - validates public video links without downloading video bodies.

- `QChatBrowserAgentTriggerPolicy`
  - detects owner private browser automation requests.
  - Ensures non-owner requests do not call browser automation, do not reveal menus, and do not enter the model as a command chain.

- `QChatBrowserAgentFormatter`
  - formats the result for QQ:
    - conclusion first;
    - visited pages;
    - compact evidence;
    - source URLs;
    - short failure reason.

- `QChatBrowserAgentMediaOutput`
  - sends validated images as QQ image messages in owner private chat.
  - returns validated video URLs as text links.
  - does not call `IOneBotRuntime.UploadPrivateFile` for videos.

## Data Flow

Owner private QQ message:

```text
QChatService
-> QChatBrowserAgentTriggerPolicy
-> AgentBrowserAutomationService
-> AgentBrowserTaskPlanner
-> AgentBrowserActionPolicy
-> AgentPublicSearchService / AgentBrowserRuntimeProvider / AgentBrowserMediaTransferService
-> AgentBrowserSiteExperienceStore
-> QChatBrowserAgentFormatter
-> optional QChatBrowserAgentMediaOutput for images only
-> QQ reply
```

Non-owner or group message:

```text
QChatService
-> QChatBrowserAgentTriggerPolicy
-> denied before browser automation
-> no browser service call
-> no menu rendering
-> no model dispatch for command-like browser automation attempts
```

## Trigger Rules

Owner private messages can trigger browser automation when the text asks for browser-style work:

- "open this website and inspect it";
- "browse this project";
- "check the official site for install steps";
- "see whether this page has API docs";
- "open the README and summarize it";
- "check the official usage guide";
- "use the browser to look at this";
- direct public URL plus a browsing instruction.

Search-only requests continue to use the existing web research path unless the owner explicitly asks to open/browse a page.

Group members continue using public search or external RAG only. They cannot trigger browser automation by saying "I am the owner", changing nickname, forwarding owner text, or copying menu lines.

## Safety Boundaries

Browser automation is a high-trust capability. Phase 1 enforces:

- owner-only access;
- public HTTP/HTTPS only;
- URL validation before navigation;
- no login or credential entry;
- no form submission;
- no arbitrary downloads;
- no arbitrary uploads;
- no local-file uploads;
- image media return only after owner request and validation;
- video media is link-only after URL validation;
- no arbitrary JavaScript;
- no private network access;
- no capability escalation from web page content;
- untrusted external content wrapping for observations;
- diagnostics without page body dumps, API keys, cookies, or tokens.

The browser Agent may summarize what it saw, but external page text never becomes instruction authority.

## Failure Handling

The service returns short deterministic failure reasons:

- `browser_agent_owner_required`;
- `browser_agent_disabled`;
- `browser_agent_empty_task`;
- `browser_agent_unsafe_url`;
- `browser_agent_media_type_denied`;
- `browser_agent_media_too_large`;
- `browser_agent_media_download_failed`;
- `browser_agent_media_output_failed`;
- `browser_agent_action_denied`;
- `browser_agent_login_required`;
- `browser_agent_anti_bot_challenge`;
- `browser_agent_step_limit`;
- `browser_agent_no_reliable_evidence`;
- `browser_agent_runtime_unavailable`.

QQ output should be short and natural:

```text
Cannot open it. This page requires login, and I will not bypass that.
```

or:

```text
Conclusion: this project mainly provides a browser automation framework.
Checked: README, docs/getting-started
Sources: https://example.com/repo
```

## Diagnostics

Write compact diagnostics for:

- trigger decision;
- actor role;
- allowed/denied capability;
- each action kind;
- URL host only, when possible;
- step count;
- page count;
- final reason;
- site experience result.

Do not log:

- cookies;
- credentials;
- API keys;
- page body dumps;
- full screenshots;
- private QQ content beyond existing diagnostic norms.

## Token And Storage Control

The automation layer must be token-conservative:

- deterministic planner first, no LLM planning loop in Phase 1;
- capped snapshots;
- capped link lists;
- compact evidence;
- no full page text in final QQ reply;
- no unbounded media downloads;
- no non-media downloads;
- no video downloads;
- no C-drive media cache;
- no persisted page bodies on C drive;
- browser/cache/runtime outputs remain under existing D-drive project/runtime conventions.

## Tests Required

Unit tests:

- owner private browser request triggers automation;
- non-owner private browser request is denied before automation/model/menu;
- group member browser request is denied before automation;
- unsafe URLs are rejected;
- login-wall and anti-bot snapshots stop the task;
- step limit stops the loop;
- public link navigation is allowed;
- form/input/download/JS actions are denied;
- image download is allowed only for owner-specified public image URLs;
- videos are returned as links only and are not downloaded;
- non-media, oversized images, unsafe URLs, videos-as-files, and local-file upload are denied;
- validated network images can be returned as QQ images in owner private QQ;
- final formatter is compact and source-backed.

Integration-style tests with fakes:

- public URL -> snapshot -> result;
- search query -> candidate URL -> snapshot -> result;
- first page -> safe doc link -> second snapshot -> result;
- blocked host records site experience and returns safe failure.
- public image URL -> validated image download -> private QQ image return path;
- public video URL -> validated public URL -> private QQ link return path;
- PDF/ZIP/EXE/script URL -> denied before write/upload.

Live smoke, owner-run only:

- owner private: "open the official dotnet site and check .NET 9 install steps";
- owner private: "browse https://github.com/vercel-labs/agent-browser";
- owner private: "download this image https://example.com/cat.png";
- owner private: "show me this video https://example.com/demo.mp4";
- group mention with browser request: denied or downgraded to search, no browser automation;
- non-owner private browser request: no browser automation, no model chain.

## Completion Definition

Phase 1 is complete only when:

- owner private QQ can trigger a bounded browser task in tests;
- non-owner and group paths cannot trigger browser automation;
- browser actions are governed by explicit policy;
- media transfer actions are governed by explicit image-only-download and video-link-only policy;
- unsafe actions are denied with tests;
- final QQ output is concise and sourced;
- focused tests pass;
- the feature is documented as owner-only and Phase 1 limited.

Full browser Agent remains incomplete after Phase 1. Later phases may add controlled site search input, screenshot analysis, or owner-confirmed form workflows, but those require separate designs. Web-form upload, arbitrary local-file upload, and video upload remain out of scope even though owner-requested network images may be returned as QQ images.

## Self-Review

- Placeholder scan: no TBD/TODO placeholders remain.
- Scope check: this is focused on owner-only bounded browser automation, not a full browser operator.
- Safety check: high-risk actions are explicitly denied in Phase 1, image transfer is limited to owner-requested public image files, and videos are link-only.
- Consistency check: the design reuses existing search, snapshot, router, site experience, diagnostics, and QChat role boundaries.
