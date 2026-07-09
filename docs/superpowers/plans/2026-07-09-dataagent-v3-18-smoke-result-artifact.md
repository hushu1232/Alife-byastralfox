# DataAgent V3.18 Smoke Result Artifact Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a local/manual smoke result artifact formatter for LangGraph sidecar smoke runs without storing secrets, SQL, hidden context, or making LangGraph part of the default DataAgent chain.

**Architecture:** Extend the manual smoke harness with sanitized artifact output and add a dedicated formatter script for already-captured smoke responses. The formatter produces a small local JSON report with stable booleans and redacted unsafe content. Readiness checks only file markers and does not call a live sidecar.

**Tech Stack:** PowerShell, .NET 9, NUnit, existing DataAgent readiness checks.

---

## Tasks

### Task 1: Formatter Contract Tests

**Files:**
- Create: `Tests/Alife.Test.DataAgent/DataAgentV318SmokeResultArtifactTests.cs`

- [ ] Write tests that assert `tools/format-dataagent-langgraph-smoke-result.ps1` exists, contains `artifact_formatter=true`, `manual_only=true`, `stores_secrets=false`, `stores_sql=false`, `stores_hidden_context=false`, `sanitizes_unsafe_text=true`, and omits runtime startup markers.
- [ ] Write tests that assert `docs/dataagent/dataagent-v3.18-smoke-result-artifact.md` declares the same safety boundary.
- [ ] Run focused tests and confirm they fail before implementation.

### Task 2: Formatter Script and Runbook

**Files:**
- Create: `tools/format-dataagent-langgraph-smoke-result.ps1`
- Create: `docs/dataagent/dataagent-v3.18-smoke-result-artifact.md`

- [ ] Implement a manual-only formatter accepting `-InputPath` and `-OutputPath`.
- [ ] Read an existing JSON response file, extract bounded fields, and emit a sanitized JSON artifact.
- [ ] Redact SQL-looking text, hidden-context markers, bearer/token/key/secret markers, and visible QChat text markers.
- [ ] Write V3.18 doc markers and operator notes.
- [ ] Run focused tests and confirm pass.

### Task 3: Readiness Integration

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
- Modify: `tools/check-dataagent-readiness.ps1`
- Modify readiness count tests under `Tests/Alife.Test.DataAgent`

- [ ] Add dynamic check `GraphHandshakeSmokeResultArtifactFormatterPresent`.
- [ ] Add static check with doc/script/test markers.
- [ ] Update dynamic readiness count from `83` to `84`.
- [ ] Update script required count from `98` to `99`.
- [ ] Run focused readiness tests and confirm pass.

### Task 4: Verification and Commit

**Files:**
- All above

- [ ] Run full `Alife.Test.DataAgent`.
- [ ] Run `git diff --check`.
- [ ] Scan stale `98` readiness counters.
- [ ] Commit as `feat(dataagent): add v3.18 smoke result artifact formatter`.
