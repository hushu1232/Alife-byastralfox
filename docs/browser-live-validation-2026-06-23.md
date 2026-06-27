# Browser Live Validation Record - 2026-06-23

## Scope

This record covers Priority 1 from `docs/browser-global-task-plan.md`: real link validation for browser/web research.

## Summary

Status: partial completion, QQ end-to-end blocked by runtime/session state.

Completed:

- Real public search provider access was verified outside the sandbox.
- Real public page read through `AgentWebResearchService` was verified outside the sandbox.
- A reusable opt-in live smoke test was added.
- NapCat/OneBot local health was checked.
- OneBot action calls to the reachable account were checked.

Blocked:

- Full QQ/NapCat message-level smoke was not completed because Alife was not persistently running in health checks, XiaYu's OneBot endpoint was unreachable, and the reachable Mio endpoint reported `online=false`.

## Commands And Evidence

### QChat Live Health

Command:

```powershell
powershell -NoLogo -ExecutionPolicy Bypass -File D:\Alife\tools\check-qchat-live-health.ps1 -ProjectRoot D:\Alife -StoragePath D:\Alife\Storage -OneBotHost 127.0.0.1 -OneBotPort 3001 -Json
```

Observed:

- `napCatRunning=true`
- `oneBot.reachable=true` for `127.0.0.1:3001`
- `alifeRunning=false`
- XiaYu endpoint `127.0.0.1:3002` was not reachable
- Mio endpoint `127.0.0.1:3001` was reachable

### OneBot Action Probe

Endpoint: `ws://127.0.0.1:3001`

Observed:

- `get_login_info` returned `user_id=3340947887`, nickname `雨宫 咪绪`
- `get_status` returned `good=true`, `online=false`

Interpretation:

The local OneBot WebSocket is reachable and can answer actions, but the QQ session is not fully online. This is not enough to claim real QQ message send/receive closure.

### XiaYu Endpoint Probe

Endpoint: `ws://127.0.0.1:3002`

Observed:

- Connection failed.

Interpretation:

XiaYu's configured OneBot endpoint was not available during this validation run.

### Real Public Web Probe

Outside sandbox network probing showed:

- Bing search endpoint returned HTTP 200.
- DuckDuckGo HTML endpoint returned HTTP 200.
- A direct PowerShell fetch to Microsoft Learn hit a connection/TLS error, but the project live smoke was still able to search and read the Microsoft Learn result through its .NET pipeline.

### Project Live Smoke

Command:

```powershell
$env:ALIFE_WEB_LIVE_SMOKE='1'
dotnet test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --no-restore --filter "FullyQualifiedName~AgentWebResearchLiveSmokeTests" --logger "console;verbosity=detailed"
```

Observed:

- Test passed.
- Query: `dotnet 9 release notes`
- Result: `reason=ok`
- Source: `What's new in .NET 9 | Microsoft Learn`
- URL: `https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-9/overview`
- Source type: `docs`
- Answer included a short conclusion and `来源`.

Interpretation:

The real public search and public read path is usable from the project code when network access is available.

## Added Test

`Tests/Alife.Test.Framework/AgentWebResearchLiveSmokeTests.cs`

Behavior:

- Skips by default.
- Runs only when `ALIFE_WEB_LIVE_SMOKE=1`.
- Uses real `DuckDuckGoHtmlSearchProvider`, `BingHtmlSearchProvider`, `AgentInternetService`, `AgentWebAccessService`, and `AgentWebResearchService`.
- Verifies that real search/read produces at least one evidence source and a sourced answer.

## Remaining Manual QQ Smoke

Run these after restoring the QQ session:

1. Owner private chat: `查一下 dotnet 9 release notes`
2. Group member in allowed group: `@bot 搜 dotnet 9 release notes`
3. Non-owner private chat: `/search dotnet 9`
4. Owner private chat: `/qchat web doctor`

Expected:

- Owner private search may auto-read public HTTP/HTTPS pages.
- Group member search receives public search evidence only.
- Non-owner private `/search` does not enter the model, reveal menus, or trigger web research.
- `/qchat web doctor` shows browser provider state, internet switch state, and recent site experience.

Do not mark Priority 1 fully complete until these QQ message-level checks are manually or automatically verified against a healthy OneBot session.

## Browser Agent Media Live Smoke - Pending

Scope:

This section tracks the browser-agent owner-only automation and media-return loop added after the original public web research live smoke.

Diagnostics command:

```text
/qchat web browser-agent smoke
```

Current status:

- `live-smoke=pending`
- The checklist command is deterministic diagnostics only.
- It does not run the browser, send QQ messages, call the model, or trigger provider chains by itself.
- Real QQ/NapCat message-level execution is still required before claiming full live closure.

Manual checks to run after the target bot OneBot session is healthy:

1. Owner private text task: `browse https://example.com/docs summarize`
   Expected: owner private browser-agent path runs bounded read-only automation and returns a compact sourced text reply.

2. Owner private image task: `browse https://example.com/gallery return image https://example.com/cat.png`
   Expected: a public image URL is validated, cached under `D:\Alife\Runtime\BrowserAgentMedia`, and returned as a QQ image after the text reply.

3. Owner private video task: `browse https://example.com/videos return video https://example.com/demo.mp4`
   Expected: the video target is returned as a text link only. The bot must not download the video, send `[CQ:video]`, or use QQ file upload APIs.

4. Non-owner private browser-agent wording: `browse https://example.com/docs`
   Expected: no browser automation, no provider call, no model fallback, and no owner menu rendering.

5. Group browser-agent wording with `@bot`
   Expected: no browser automation. Group public search/RAG remains separate and must not escalate into owner-only browser automation.

Blocked actions during the live smoke:

- login
- form submission
- video download
- local upload
- arbitrary JavaScript
- private-network, localhost, loopback, `file:`, `data:`, or `javascript:` URLs
