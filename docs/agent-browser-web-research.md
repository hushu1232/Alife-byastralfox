# Agent Browser Web Research

## Purpose

This feature turns ordinary QQ lookup requests into a read-only research flow:

```text
@bot 查一下 agent-browser web-access
```

The bot searches the public web, selects public HTTP/HTTPS results, builds short evidence, and replies with a compact sourced answer.

Normal Chinese examples:

```text
查一下 agent-browser web access
@bot 搜一下 dotnet 9 release notes
```

## Current Flow

```text
QChatPublicInternetCommandPolicy
-> AgentWebResearchService
-> AgentPublicSearchService
-> AgentWebAccessService for owner auto-read only
-> QChatWebResearchFormatter
-> QQ reply
```

## Trigger Rules

- `/search <query>` remains supported.
- Group members may trigger search only when they explicitly mention the bot and the message has a search intent.
- Owner private messages may trigger natural search, for example `查一下 <query>` or `搜一下 <query>`.
- Messages that explicitly ask for `浏览器` remain on the existing owner-only browser snapshot path.
- Ordinary unmentioned group chat does not trigger web research.

## Permission Boundary

- Group members: public search evidence only.
- Owner: public search plus `AutoRead` of public HTTP/HTTPS pages when `EnableInternetAccess=true`.
- Private guests: not authorized for public internet command execution.
- Browser snapshot and browser interaction remain owner-only.

## Safety Boundary

The research pipeline is read-only.

It does not:

- click buttons,
- log in,
- download files,
- submit forms,
- execute page JavaScript,
- access localhost/private network/file URLs,
- treat web content as system, owner, developer, or tool authorization.

Unsafe or unreadable search results are skipped. If no usable evidence remains, the bot says it did not find reliable public content instead of inventing an answer.

When owner-only page auto-read fails for an otherwise safe public result, the service falls back to the search title and snippet instead of discarding the result. This keeps answers useful when a site returns 403, rejects simple fetch, or is temporarily unavailable. The failed read is also recorded in `AgentBrowserSiteExperienceStore`, so later browser strategy and diagnostics can see hosts that hit login walls, anti-bot pages, or repeated fetch failures.

## Browser Snapshot Productization

Owner-only browser snapshots are still read-only. The productized snapshot path does not click, log in, download, submit forms, or perform browser interaction.

Snapshot extraction now gathers:

- page URL,
- page title,
- compact body text,
- public links with text and href,
- snapshot diagnostics.

The browser provider first runs one read-only DOM extraction script for `document.title`, `document.body.innerText`, and `a[href]` links. If structured body text is unavailable, it falls back to the existing `ObserveAsync(page)` path.

Snapshot diagnostics include:

- `snapshot_risk=none | login_wall | anti_bot | login_wall,anti_bot`
- `text_truncated=true original_chars=<n> emitted_chars=<n>` when large text is capped
- `links_total=<n> emitted=<n>` when link evidence is available

Login-wall and anti-bot detection is deterministic. Common sign-in/account-required text returns `login_required`; captcha, Cloudflare, "checking your browser", or human-verification pages return `anti_bot_challenge`. These pages are not treated as reliable research evidence.

Token-saving rules:

- One structured extraction replaces repeated element inspection.
- Large body text is capped before formatting.
- Links are capped by the snapshot element limit.
- The formatter records counts and truncation metadata instead of dumping full pages.
- No LLM summarizer is used for browser snapshots.

## Site Experience Strategy

`AgentBrowserSiteExperienceStore` is part of the research strategy, not only diagnostics.

- `Blocked` hosts, such as recent login-wall failures, are removed from the research candidate list before page reading.
- Hosts with anti-bot signals avoid owner auto-read and use the short search snippet instead.
- Hosts with recent successful reads receive a small ranking boost.
- Medium/high risk history lowers rank, so cleaner official/docs/GitHub sources are preferred when available.

This saves tokens and latency because the bot does not repeatedly fetch pages that are likely to fail, and it does not feed low-quality login/captcha/error content into the final answer. The fallback evidence is intentionally compact: title, URL, and search snippet only.

## Source Ranking

The research service keeps the user's original query intact, then ranks usable public search results before reading or summarizing them:

1. Official and documentation-style sources.
2. GitHub sources.
3. Other public web pages.

This avoids changing `/search` semantics while still making owner auto-read prefer more reliable pages when several results are available.

## Owner Query Expansion

Owner research keeps the original query as the first search. If that search produces no usable public HTTP/HTTPS candidate, the service tries a short fallback plan:

1. Intent-aware high-signal expansions, when applicable:
   - latest/version/news requests: `<query> latest release notes`
   - exact HTTP status or exception requests: quoted exact error phrase
   - known Chinese technical terms: compact English technical query
2. Generic fallback expansions:
   - `official docs <query>`
   - `github <query>`
   - `release notes <query>`

This is intentionally conservative. It only applies to owner research after the original result set is unusable. The service stops after the first expansion that produces usable candidates, so intent matches reduce broad fallback searches rather than adding unlimited queries. Group members stay on the original public-search query and do not get expanded owner search behavior.

Token-saving rules:

- No expansion is attempted while the original query has a usable public candidate.
- No LLM is used for query rewriting.
- Expansion candidates are deterministic and de-duplicated.
- Group member searches do not expand or auto-read pages.

## Output Shape

QQ output is intentionally short:

```text
结论：...
1. ...
2. ...
来源：Title https://example.com/page
```

The formatter avoids exposing internal provider names, routing reasons, policy labels, stack traces, or browser strategy details.

## External RAG

External RAG stores owner-approved public pages as reusable public knowledge. It is separate from live web search:

- `/qchat rag add <url>`: owner-only, fetches a public HTTP/HTTPS URL and stores cleaned chunks.
- `/qchat rag list`: owner-only, lists compact source metadata only.
- `/qchat rag delete <id|url>`: owner-only, deletes one stored source and its chunks.
- `/rag <question>`: query stored public knowledge when external RAG querying is enabled.

Token-saving rules:

- Stored content is cleaned before chunking: script/style blocks, HTML tags, common boilerplate, and repeated whitespace are removed.
- Listing sources never returns chunk text.
- Query output is capped by `PublicExternalRagMaxChunks`.
- Add/list/delete management commands do not call the model and do not perform public search.
- Non-owner `/qchat rag ...` management commands are dropped before the RAG service is called.

## Rate Limit, Cache, And Cost Control

QChat uses a shared in-memory `AgentWebResearchControlState` for live web research. It does not store page text on C drive and does not persist runtime cache across restart.

Default QChat settings:

- `PublicInternetUserCooldownSeconds = 15`
- `PublicInternetGroupCooldownSeconds = 30`
- `PublicInternetResultCacheSeconds = 120`
- `PublicInternetMaxConcurrentResearch = 2`

Behavior:

- Repeating the same query inside the cache window returns the cached sourced answer before public search, page read, cooldown refusal, or model dispatch.
- A group member sending a different search too quickly receives a compact `web_research_rate_limited: cooldown` reply.
- When too many non-cached research jobs run at once, the service returns `web_research_busy: try again later` instead of queueing more network work.
- Owner research and group research share the cache/concurrency controls, but only group members are subject to the per-user/per-group cooldown.

Tracked metrics:

- public search call count,
- owner page read count,
- UTF-8 page bytes read,
- total research latency,
- approximate summary tokens,
- cache hits,
- rate-limit hits,
- concurrency rejections.

Token-saving rules:

- Cache lookup happens before cooldown and before provider calls.
- Group cooldown happens before public search and before model dispatch.
- Concurrency rejection happens before public search and page read.
- Metrics are counters only; they do not store full public page text.

## Live Smoke Checklist

Owner-only diagnostics expose the same checklist through:

```text
/qchat web smoke
```

Run these checks against a live QQ/NapCat session before treating the feature as production-ready:

1. Owner private chat: `查一下 dotnet 9 release notes`
   Expected: the owner path may auto-read public HTTP/HTTPS pages and reply with a short conclusion plus sources.

2. Group member in a group: `@bot 搜 dotnet 9 release notes`
   Expected: group members receive public search evidence only. This must not trigger owner-only browser snapshot or browser interaction.

3. Non-owner private chat: `/search dotnet 9`
   Expected: the request must not enter the model, reveal owner menus, or trigger the web research event chain.

4. Owner private chat: `/qchat web doctor`
   Expected: diagnostics show browser provider state, internet switch state, and recent site experience.

The smoke run must not trigger clicking, login, downloads, form submission, JavaScript execution, private-network access, or `file:` URLs.

## Browser Agent Automation Phase 1

Status: completed on 2026-06-23 for Phase 1 bounded automation.

Phase 1 adds a bounded owner-only browser Agent for private QQ chat. It is separate from group-member public search/RAG. Group members may use the public web research path when enabled, but they cannot trigger the browser automation provider, menu chain, or model fallback through browser-agent wording.

Allowed Phase 1 actions:

- public web search;
- public HTTP/HTTPS navigation;
- read-only browser snapshots;
- bounded scroll/page observation;
- safe public link navigation;
- public image return as QQ image after URL, DNS, MIME, magic-byte, size, and D-drive cache validation;
- public video return as link only;
- compact sourced QQ output.

Blocked Phase 1 actions:

- login;
- form submission;
- arbitrary downloads;
- video download or QQ file upload;
- local-file upload;
- arbitrary JavaScript from model output;
- private-network, localhost, loopback, `file:`, `data:`, or `javascript:` targets;
- browser automation for non-owner private users or group messages.

Media return rule:

- Images may be fetched only from validated public URLs and stored under controlled D-drive cache roots before QQ image return.
- QChat sends validated images as `[CQ:image,file=...]` messages after the text browser summary.
- Videos are never downloaded by the bot. A validated public video target is returned as a text link only.
- QChat never uses QQ file upload or video upload for browser-agent video results.

Diagnostics:

```text
/qchat web browser-agent
```

Expected machine-readable markers include `browser-agent=phase1`, `owner-only`, `no-login`, `image-return=connected`, `image-ok`, `video-return=link-only`, and `video-link-only`.

Browser-agent live smoke checklist:

```text
/qchat web browser-agent smoke
```

This command is a readiness checklist only. It does not run the browser, send QQ messages, or call the model by itself. It returns compact machine-readable markers for manual live QQ/NapCat verification:

- `browser-agent-live-smoke`
- `live-smoke=pending`
- `owner-private-text`
- `owner-private-image`
- `owner-private-video`
- `non-owner-denied`
- `group-denied`
- `image-return=connected`
- `video-return=link-only`
- `media-cache=D:\Alife\Runtime\BrowserAgentMedia`

Do not mark browser-agent live QQ closure complete until the owner private text/image/video cases are observed in real QQ, and the non-owner private plus group cases are observed to stop before browser automation, provider calls, model fallback, and menu rendering.
