# Browser Agent Media Return Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans or inline TDD execution. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the QChat Browser Agent media output loop: validated public images may be returned as QQ images, while public videos are returned as links only.

**Status:** Completed on 2026-06-23.

**Architecture:** Keep browser automation read-only and owner-only. Extract media URLs from successful browser evidence, pass them through the existing `AgentBrowserMediaOutputService`, append video links to the browser text reply, and send validated image local paths as QQ image CQ messages. Do not add login, form submission, arbitrary downloads, upload, or model-generated JavaScript.

**Tech Stack:** C#/.NET 9, existing QChat service, existing `AgentBrowserMediaOutputService`, NUnit tests.

---

## File Structure

- Modify `sources/Alife.Function/Alife.Function.QChat/QChatBrowserAgentFormatter.cs`
  - Add media URL extraction helpers for image/video candidates in browser evidence.
  - Add media output formatting that uses `[CQ:image,file=...]` only for validated local image outputs and text links for videos.

- Modify `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
  - After browser automation succeeds, prepare media outputs from evidence URLs.
  - Send the normal text reply first.
  - Send at most `BrowserAgentMaxImageItems` validated QQ images after the text reply.
  - Never upload or download videos through QChat.

- Modify `Tests/Alife.Test.QChat/QChatBrowserAgentFormatterTests.cs`
  - Add tests for image CQ output and video-link-only formatting.

- Modify `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`
  - Add owner private test for image URL evidence returning a QQ image message after the text reply.
  - Add owner private test for video URL evidence returning a text link only and no QQ file/image upload.

- Modify docs:
  - `docs/agent-browser-web-research.md`
  - `docs/browser-global-task-plan.md`

---

## Tasks

- [x] Add failing formatter tests for image CQ and video-link-only output.
- [x] Implement formatter media helpers.
- [x] Add failing QChat service tests for owner private image and video media handling.
- [x] Implement QChat browser media preparation and sending.
- [x] Update diagnostics/docs to mark media return as connected.
- [x] Run focused QChat/browser tests and `dotnet build --no-restore`.
- [x] Upload through `D:\FOXD` after verification.

## Verification Evidence

- `dotnet test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --filter AgentBrowserMediaOutputServiceTests` passed: 13 passed, 0 failed.
- `dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "QChatBrowserAgentFormatterTests|QChatBrowserAgentTriggerPolicyTests|OwnerPrivateBrowserAgentRequestRunsAutomationWithoutModelDispatch|OwnerPrivateBrowserAgentImageUrlReturnsQqImageAfterTextReply|OwnerPrivateBrowserAgentVideoUrlReturnsLinkOnly|NonOwnerPrivateBrowserAgentRequestDoesNotRunAutomationOrModel|GroupBrowserAgentRequestDoesNotRunAutomation|TryHandleWebBrowserAgentReturnsOwnerOnlyPhaseOneSummary"` passed: 17 passed, 0 failed.
- `dotnet build --no-restore` passed with 0 warnings and 0 errors.
- GitHub upload through `D:\FOXD` verified remote commit `25802be220086d6fa55151e47339df017e7098d0` on `refs/heads/master`.

## Boundaries

- Non-owner private and group browser automation remains blocked before browser/provider/model dispatch.
- Public group search/RAG remains separate.
- Videos are returned as links only.
- Images are sent only after existing media service validation.
- Runtime media cache stays under controlled D-drive roots.
