# QChat Web Search Experience Enhancement Design

## Goal

Improve the existing QChat web search pipeline from "usable" to "daily QQ usable" by reducing false triggers, producing better QQ-sized answers, preserving token controls, and keeping owner/member safety boundaries explicit.

## Scope

This is a first-phase enhancement. It improves the current public search and web research path. It does not build a full browser automation agent, does not add login handling, does not add form filling, and does not grant group members owner-level web reading.

## Current Chain

The current path is:

1. `QChatPublicInternetCommandPolicy.ParseMessage(...)` detects explicit `/search`, `/rag`, or semantic search text.
2. `QChatService` evaluates permissions and constructs `AgentWebResearchRequest`.
3. `AgentWebResearchService` searches, optionally reads public pages for owner requests, filters candidates, applies cache/cooldown/concurrency controls, and composes a research answer.
4. `QChatWebResearchFormatter.Format(...)` returns the final QQ message.

## Desired Behavior

### Triggering

Group member search must require an explicit bot mention. The bot should react to clear search language such as:

- `搜一下 <query>`
- `查一下 <query>`
- `帮我找 <query>`
- `联网查 <query>`
- `查最新 <query>`
- `找资料 <query>`
- `有没有公开信息 <query>`

Private owner messages can use the same semantic triggers without an `@` mention.

The bot must avoid search for ordinary chat, vague questions, and browser-tool meta talk. Examples that should not search:

- `你觉得这个怎么样`
- `你知道这个吗`
- `浏览器功能怎么样`
- `能不能以后联网`
- empty or whitespace-only query

### Permissions

Owner and group member can use public web search when enabled. Group members remain limited to public search evidence and snippets. Owner requests may use automatic page reading only when `EnableInternetAccess` allows it.

Non-owner private guests and unauthorized senders must not use public web search unless an existing policy explicitly allows them in the future.

### Output

`QChatWebResearchFormatter` should produce QQ-sized output:

- Put the conclusion first.
- Keep group-member replies short.
- Include at most two evidence bullets for group members.
- Include at most three evidence bullets for owners.
- Include at most two or three source links depending on role.
- Return short failure messages for cooldown, busy, no results, and search unavailable.
- Avoid dumping long page text into QQ.

### Token And Resource Controls

The existing resource controls remain:

- `PublicInternetSearchMaxResults`
- `PublicInternetQueryMaxChars`
- `PublicInternetUserCooldownSeconds`
- `PublicInternetGroupCooldownSeconds`
- `PublicInternetResultCacheSeconds`
- `PublicInternetMaxConcurrentResearch`

The enhanced formatter should also bound output length. The research service should continue to compact page content and avoid reading pages for group members.

### Diagnostics

Each web research attempt should emit diagnostics that make daily debugging possible:

- sender role
- message type
- query
- command kind
- permission decision reason
- cache/cooldown/busy result if available
- result success/failure
- evidence count
- whether owner page reading was enabled

Diagnostics must not include page bodies or secret tokens.

## Architecture

### `QChatPublicInternetCommandPolicy`

Owns trigger parsing and permission evaluation. It should remain deterministic and testable. Add stronger semantic trigger extraction without calling an LLM.

### `QChatWebResearchFormatter`

Owns QQ-specific output shaping. It should accept the research result and a small formatting context that includes sender role and message type. It should not perform network access or permission checks.

### `AgentWebResearchService`

Owns search/read/cache/cooldown/concurrency and evidence composition. It should expose enough metadata through the existing result or diagnostics to make formatting and observability better. The first phase can keep the result model stable unless a small metadata extension is necessary.

### `QChatService`

Owns integration. It should call the parser early enough to avoid model dispatch for clear web-search requests, then route through policy, web research service, formatter, and QQ reply. It must not route non-search chat into web research.

## Testing Strategy

Add focused tests for:

- group member `@bot 搜一下 ...` triggers public search;
- group member without `@bot` does not search;
- ordinary group chat does not search;
- private owner `查最新 ...` triggers freshness-aware search;
- group member cooldown blocks a second query and does not dispatch the model;
- formatter bounds answer length and sources;
- formatter returns short readable failure messages;
- owner can use richer evidence than group members when page reading is enabled.

## Non-Goals

- Full browser automation.
- Login/cookie handling.
- Form filling.
- CAPTCHA bypass.
- Large-scale crawling.
- Replacing the main LLM.
- Granting group members owner-level internet access.

## Self-Review

- Scope is limited to QChat public web search experience.
- Owner and group member permissions remain separate.
- Token controls are explicit.
- No browser-agent claims are made.
- No hard safety boundary is weakened.
