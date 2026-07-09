# DataAgent V3.26 Harness Replay Diff Gate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a C# harness replay diff gate that compares validated manual LangGraph advisory results against existing replay report evidence without giving the agent execution authority.

**Architecture:** Add an independent gate model beside the existing graph handshake replay/manual shadow files. The gate consumes `DataAgentGraphHandshakeReplayReport` and `DataAgentLangGraphManualShadowResult`, emits a compact advisory-only result, and is wired into DataAgent readiness as V3.26.

**Tech Stack:** .NET 9, NUnit, existing DataAgent graph handshake models, existing readiness marker script.

---

### Task 1: Gate Model and Formatter

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentHarnessReplayDiffGate.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentV326HarnessReplayDiffGateTests.cs`

- [ ] **Step 1: Write the failing tests**

Create tests for pass, rejected advisory fallback, replay default-result failure, reason mismatch operator gate, formatter safety, and V3.26 doc markers.

- [ ] **Step 2: Run RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --filter "DataAgentV326HarnessReplayDiffGateTests" -v:minimal
```

Expected: compile failure because `DataAgentHarnessReplayDiffGate` types do not exist.

- [ ] **Step 3: Implement minimal gate**

Create records:

```csharp
DataAgentHarnessReplayDiffGateInput
DataAgentHarnessReplayDiffGateResult
```

Create static classes:

```csharp
DataAgentHarnessReplayDiffGate
DataAgentHarnessReplayDiffGateFormatter
```

Gate rules:

```text
null report or advisory -> fallback_required=true
replay default_result_changed -> gate fails
manual shadow rejected -> gate fails and preserves reason
accepted advisory reason in replay StatusCounts -> gate passes
accepted advisory reason absent from replay StatusCounts -> operator_required=true and fallback_required=true
always default_result_changed=false, starts_runtime=false, installs_dependencies=false, calls_sidecar=false, stores_secrets=false, stores_sql=false, stores_hidden_context=false
```

- [ ] **Step 4: Run GREEN**

Run the same focused V3.26 test command. Expected: all V3.26 tests pass.

### Task 2: Documentation and Readiness

**Files:**
- Create: `docs/dataagent/dataagent-v3.26-harness-replay-diff-gate.md`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
- Modify: `tools/check-dataagent-readiness.ps1`
- Modify: readiness count tests under `Tests/Alife.Test.DataAgent`

- [ ] **Step 1: Add V3.26 doc**

Document markers:

```text
harness_replay_diff_gate=true
agent_advisory_contract=v3.24
real_langgraph_manual_shadow_provider=true
harness_execution_authority=true
csharp_validation_authority=true
agent_advisory_only=true
gate_only=true
operator_decides=true
default_result_changed=false
starts_runtime=false
installs_dependencies=false
calls_sidecar=false
stores_secrets=false
stores_sql=false
stores_hidden_context=false
```

- [ ] **Step 2: Add readiness check**

Add `GraphHandshakeHarnessReplayDiffGatePresent`, increase core required count from `91` to `92`, and total required count from `106` to `107`.

- [ ] **Step 3: Verify focused readiness**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --filter "DataAgentV326HarnessReplayDiffGateTests|DataAgentReadinessTests|DataAgentV210ReadinessTests|DataAgentV216ReadinessTests|DataAgentV30ReadinessTests" -v:minimal
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected: 0 failed; static readiness summary `107 required passed, 0 required missing`.

### Task 3: Full Verification and Commit

**Files:**
- All V3.26 files

- [ ] **Step 1: Run full DataAgent tests**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj -v:minimal
```

Expected: all non-live DataAgent tests pass; live PostgreSQL tests remain skipped.

- [ ] **Step 2: Run cleanup checks**

```powershell
git diff --check
rg -n -F -e 'Summary: 106 required' -e 'expectedRequired = 106' -e 'Has.Count.EqualTo(91)' Tests\Alife.Test.DataAgent tools\check-dataagent-readiness.ps1 sources\Alife.Function\Alife.Function.DataAgent
git status --short --branch
```

Expected: no stale readiness counts.

- [ ] **Step 3: Commit**

```powershell
git add Tests\Alife.Test.DataAgent sources\Alife.Function\Alife.Function.DataAgent tools\check-dataagent-readiness.ps1
git add -f docs\dataagent\dataagent-v3.26-harness-replay-diff-gate.md docs\superpowers\plans\2026-07-09-dataagent-v3-26-harness-replay-diff-gate.md
git commit -m "feat(dataagent): add v3.26 harness replay diff gate"
```
