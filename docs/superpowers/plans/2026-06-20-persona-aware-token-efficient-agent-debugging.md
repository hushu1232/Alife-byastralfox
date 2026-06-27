# Persona-Aware Token-Efficient Agent Debugging Plan

This is a future implementation plan. Do not start code changes from this file
until the owner explicitly asks to implement it.

## Objective

Make chat-driven code work reliable, token-efficient, and persona-consistent.

The agent should convert owner chat into an issue packet, use a debug map and
CodeGraph before broad reads, preserve a compact persona style, and edit code
only after evidence or a failing test identifies a path.

## Current Context

- `docs/superpowers/specs/2026-06-20-astralfox-engineering-standards.md`
  defines deterministic action, QQ-visible output, event, safety, and testing
  standards.
- `docs/superpowers/specs/2026-06-20-persona-aware-token-efficient-agent-debugging.md`
  defines the new debugging design.
- `docs/agent-debug-map.md` maps common symptoms to candidate symbols and tests.
- QChat deterministic file, text, image, and video send failures are already
  moving toward deterministic runner patterns.
- QZone report feedback remains a known follow-up risk area, but this plan does
  not implement it yet.

## Scope

Future implementation should add:

- issue-packet helper or template;
- low-token location report format;
- tests for visible-output internal label rejection;
- QZone report feedback layering;
- optional diagnostic summary helper for recent logs;
- persona-aware engineering reply templates.

## Non-Goals

- Do not build a full autonomous code agent framework.
- Do not replace existing QChat/QZone services wholesale.
- Do not store raw owner/private QQ logs in long-term memory.
- Do not make persona text responsible for permissions, safety, or correctness.
- Do not perform live QQ/NapCat validation until local behavior is covered.

## Implementation Tasks

### Task 1: Issue Packet Template

Files likely affected:

- new docs or source helper under the agent/message-filter boundary;
- tests under `Tests/Alife.Test.Framework` if implemented in code.

Steps:

1. Add a minimal issue packet record or documented template.
2. Cover common issue types from `docs/agent-debug-map.md`.
3. Add tests that classification preserves goal and constraints without storing
   broad chat history.

### Task 2: Location Failure Report

Steps:

1. Define a small formatter for unknown-location reports.
2. Include checked scope, candidate list, missing evidence, and next step.
3. Test that reports do not claim a unique location when confidence is missing.

### Task 3: QZone Feedback Layering

Candidate symbols:

- `QZoneService.Report`
- `QZoneService.ReportQuery`
- `QZoneService.ReportProactiveExecution`

Steps:

1. Add failing tests proving `[QQ Zone ...]` does not enter pending poke/model
   feedback.
2. Replace raw bracketed feedback with diagnostic events and sanitized summaries.
3. Keep `QZoneActionResult`, `QZoneQueryResult`, and
   `QZoneProactiveExecutionResult` structured for callers.

### Task 4: Visible Output Seeds

Steps:

1. Add tests for the forbidden visible tokens in `docs/agent-debug-map.md`.
2. Route checks through the existing visible text policy or sanitizer.
3. Avoid broad prompt-only fixes.

### Task 5: Token-Efficient Diagnostics

Steps:

1. Add a small diagnostic summary helper if recent logs become too large to read.
2. Summarize by event id, count, last safe error summary, and correlation id.
3. Read raw log entries only after a correlation id is known.

### Task 6: Persona-Aware Engineering Replies

Steps:

1. Add a compact persona contract surface for owner-facing engineering updates.
2. Keep facts and verification results outside persona control.
3. Test that uncertainty and failures are stated plainly.

## Verification

Run after implementation:

```powershell
dotnet test D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore
dotnet test D:\Alife\Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --no-restore
dotnet test D:\Alife\Alife.slnx --no-restore
git diff --check
git diff --cached --check
```

Live QQ/NapCat validation should be a separate checkpoint after local tests pass.

## Acceptance Criteria

- The owner can describe a code problem without naming files.
- The agent first creates or implies an issue packet.
- The agent uses `docs/agent-debug-map.md` and CodeGraph before broad reads.
- Unknown locations produce a location failure report instead of guessed edits.
- Persona remains visible in compact owner-facing updates.
- Persona does not alter facts, permissions, or test results.
- Internal labels are not routed into QQ-visible text or raw model feedback.
- Future QZone report work starts from failing tests.
