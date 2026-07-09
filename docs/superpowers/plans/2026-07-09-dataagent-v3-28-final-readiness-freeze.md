# DataAgent V3.28 Final Readiness Freeze Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a final V3 readiness freeze that proves V3.0-V3.27 are bounded, evidence-backed, and operator-gated without adding runtime agent authority.

**Architecture:** Add a pure in-memory freeze summary model, builder, and formatter near the existing V3.27 operator evidence pack. Integrate it as the final DataAgent readiness check after all existing dynamic checks, and as one static readiness script marker.

**Tech Stack:** C#/.NET 9, NUnit, PowerShell static readiness script, CodeGraph.

---

## File Structure

- Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentV3FinalReadinessFreeze.cs`
  - Defines `DataAgentV3FinalReadinessFreeze`, `DataAgentV3FinalReadinessFreezeBuilder`, and `DataAgentV3FinalReadinessFreezeFormatter`.
- Create `Tests/Alife.Test.DataAgent/DataAgentV328FinalReadinessFreezeTests.cs`
  - Tests freeze behavior, fail-closed readiness, formatter safety, and documentation markers.
- Create `docs/dataagent/dataagent-v3.28-final-readiness-freeze.md`
  - Declares V3.28 as final V3 readiness freeze and preserves the agent/harness/operator boundary.
- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
  - Adds `GraphHandshakeFinalV3ReadinessFreezePresent` after existing dynamic checks.
- Modify `tools/check-dataagent-readiness.ps1`
  - Adds static `GraphHandshakeFinalV3ReadinessFreezePresent`.
  - Updates `$expectedRequired` from `108` to `109`.
- Modify readiness tests under `Tests/Alife.Test.DataAgent`
  - Updates dynamic core count from `93` to `94`.
  - Updates static required count expectations from `108` to `109`.
  - Asserts the new final freeze check appears.

## Tasks

### Task 1: RED Tests

- [ ] Create `Tests/Alife.Test.DataAgent/DataAgentV328FinalReadinessFreezeTests.cs`.
- [ ] Add tests that reference missing `DataAgentV3FinalReadinessFreeze*` types and missing V3.28 docs.
- [ ] Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --filter "DataAgentV328FinalReadinessFreezeTests" -v:minimal
```

Expected: compile failure for missing V3.28 types or missing document assertion once types exist.

### Task 2: Minimal Production Code

- [ ] Create `DataAgentV3FinalReadinessFreeze.cs`.
- [ ] Keep it pure C# only: no runtime start, no dependency install, no sidecar/network call, no SQL/state/secret/hidden-context persistence.
- [ ] Formatter must redact unsafe tokens and emit stable markers:

```text
v3_final_readiness_freeze=true
final_v3_version=v3.28
source_versions=v3.0-v3.27
frozen_required_check_count=108
frozen_core_check_count=93
operator_decides=true
agent_advisory_only=true
harness_execution_authority=true
csharp_validation_authority=true
default_result_changed=false
manual_only=true
starts_runtime=false
installs_dependencies=false
calls_sidecar=false
stores_secrets=false
stores_sql=false
stores_hidden_context=false
```

### Task 3: Docs

- [ ] Create `docs/dataagent/dataagent-v3.28-final-readiness-freeze.md`.
- [ ] Include the boundary:

```text
Agent suggests.
Harness executes.
C# validates.
Artifact records.
Readiness gates.
Operator decides.
```

- [ ] State that V3.28 is the last V3.X version and does not introduce execution authority.

### Task 4: Readiness Integration

- [ ] Add dynamic `GraphHandshakeFinalV3ReadinessFreezePresent` at the end of `DataAgentReadiness.CheckCore`, after the existing checks have been accumulated.
- [ ] Add static readiness check in `tools/check-dataagent-readiness.ps1`.
- [ ] Update required count `108 -> 109`.
- [ ] Update dynamic core count `93 -> 94`.

### Task 5: Verification

- [ ] Run targeted V3.28 tests.
- [ ] Run readiness-related tests:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --filter "DataAgentV328FinalReadinessFreezeTests|DataAgentReadinessTests|DataAgentV210ReadinessTests|DataAgentV216ReadinessTests|DataAgentV30ReadinessTests" -v:minimal
```

- [ ] Run static readiness:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected: `Summary: 109 required passed, 0 required missing`.

- [ ] Run full DataAgent tests:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj -v:minimal
```

- [ ] Run `git diff --check`, stale marker search, `codegraph sync .`, and `codegraph status .`.

### Task 6: Commit

- [ ] Stage code, tests, readiness script, and force-add ignored docs.
- [ ] Commit:

```powershell
git commit -m "feat(dataagent): add v3.28 final readiness freeze"
```
