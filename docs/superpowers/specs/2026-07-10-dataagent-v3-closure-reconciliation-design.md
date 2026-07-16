# DataAgent V3 Closure Reconciliation Design

**Date:** 2026-07-10

**Status:** Approved design; implementation not started

**Baseline:** `alife-byastralfox/master` at `9452f966`

**Scope:** DataAgent V3 version evidence, readiness, and roadmap reconciliation only

## Purpose

Close DataAgent V3 accurately and fail closed when any required V3 milestone is
missing. The repository currently contains the implemented V3 LangGraph
advisory, replay, evidence, and operator-gated path, but its final freeze has two
material gaps:

1. V3.10 was implemented and reviewed on an isolated branch but was never
   merged into `master`.
2. V3.28 accepts caller-provided counts and the checks present at its current
   construction point; it does not prove that every required V3 milestone is
   present, unique, and passed.

This design restores the V3.10 admission contract, introduces an explicit V3
closure ledger and manifest, and makes V3.28 validate the complete V3 evidence
chain before it can claim that V3 is frozen.

This work does not add ChatBI Console, change DataAgent's default result, start
LangGraph, modify QChat, or change any runtime account or process behavior.

## Verified Current State

The audit used the two documents created after 2026-06-24 under the user's
desktop introduction folder:

- `Alife-DataAgent-NL2SQL-设计说明.md`
- `Alife-DataAgent-NL2SQL-计划书与V3展望.md`

Those documents describe an early roadmap in which V3 meant a Vue-based ChatBI
Console. The repository later explicitly redefined V3 in the V2.17 handoff as a
controlled LangGraph adapter and advisory lane. The old documents were not
marked as superseded, leaving two incompatible meanings of V3.

The repository audit also established:

- Current V3-focused DataAgent tests pass: `60 passed / 0 failed / 0 skipped`.
- Current DataAgent readiness passes: `112 required passed / 0 missing` on the
  local V4.0 baseline used during the audit.
- The remote V4.1 baseline advances total readiness to `113 required` before
  this reconciliation.
- `GraphHandshakeFinalV3ReadinessFreezePresent` currently passes.
- Commit `4bea2eed` contains the reviewed V3.10 readiness contract work, but it
  is not an ancestor of `master` and exists only on the isolated V3.10 branch.
- V3.5 is regression hardening for V3.4 and V3.7 is reason-code hardening for
  V3.6; neither requires an artificial duplicate runtime readiness check.
- V3.8 and V3.9 readiness evidence is in the Governance portion of the current
  readiness construction and must be explicitly included in the final freeze.

The current tests prove that registered checks pass. They do not yet prove that
the registered set is complete.

## Product Decision: ChatBI Console Is Not Required

ChatBI Console is removed from the committed Alife roadmap.

It could provide a browser UI for data catalogs, QueryPlan and SQL previews,
tables, charts, audit records, and report publishing. Those benefits target
multi-user BI administration and portfolio demonstration rather than the
current product need: one local operator, two QQ accounts, and high local
runtime availability.

ChatBI Console does not improve host recovery, NapCat reconnection, account
isolation, message durability, voice or vision availability, or rolling account
restart. It would add a Vue/Node build chain, an API surface, another local
port, browser security concerns, and ongoing front-end compatibility work.

The authoritative markers are:

```text
chatbi_console_required=false
chatbi_console_current_scope=false
chatbi_console_blocks_v3_closure=false
chatbi_console_committed_future_version=false
```

ChatBI Console is not assigned to V4, V5, or another promised version. A future
project may reconsider it only after a concrete user need appears.

## Authoritative Version Model

The reconciled version meanings are:

```text
V1  Local, safe DataAgent NL2SQL core.
V2  Storage, orchestration, scenario context, diagnostics, and boundaries.
V3  LangGraph advisory contracts, replay, evidence, and operator gating.
V4  Real LangGraph manual shadow and later separately approved controlled use.
```

The 2026-06-27 ChatBI V3 text remains historical design context, not the active
completion authority. Version-controlled repository documentation is the
current authority.

## Scope

### In Scope

- Restore V3.10 as a current-mainline admission contract and static readiness
  gate.
- Add a machine-checkable V3.0-V3.28 closure ledger.
- Add a pure C# closure manifest and validator.
- Make V3.28 fail closed for missing, failed, duplicate, or unexpected evidence.
- Ensure V3.8 and V3.9 are included in the V3 freeze.
- Keep V4.0 and V4.1 outside the V3 frozen set.
- Reconcile the old ChatBI roadmap with the current V3 definition.
- Repair mojibake in the version-controlled 2026-06-27 DataAgent documents.
- Update readiness counts and all directly affected tests.

### Out of Scope

- ChatBI Console, Vue, Vite, Pinia, ECharts, Monaco, or a new web API.
- QChat behavior or visible output.
- QQ or NapCat connectivity.
- `ChatActivitySystem` account lifecycle.
- Voice, vision, browser, or LangGraph process supervision.
- Starting Python, installing dependencies, creating a virtual environment, or
  binding a port.
- SQL execution, checkpoint mutation, or state migration.
- Storage, Runtime, Outputs, local logs, screenshots, credentials, or account
  data.
- Any work in `D:\FOXD`, `D:\FOXD\alife-service`, or ASRRAL-FOX.

## Selected Architecture

Use a versioned closure ledger plus a pure C# manifest/validator integrated into
the existing DataAgent readiness path.

The design follows existing DataAgent patterns: immutable records, static
builders and validators, bounded formatters, deterministic tests, repository
documentation markers, and PowerShell static readiness.

### Components

#### V3.10 Runtime Admission Contract

Restore:

```text
docs/dataagent/dataagent-v3.10-langgraph-runtime-readiness-contract.md
Tests/Alife.Test.DataAgent/DataAgentV310ReadinessTests.cs
LangGraphRuntimeReadinessContractPresent
```

The contract permits only these loopback admission surfaces for the later
manual runtime:

```text
GET  /health
POST /handshake
POST /handshake-stream
```

Required boundary markers include:

```text
manual_only=true
advisory_only=true
loopback_only=true
starts_runtime=false
installs_dependencies=false
creates_venv=false
binds_port=false
supervises_process=false
no_sql_authority=true
no_checkpoint_mutation=true
no_visible_text=true
fallback_required=true
replay_parity_required=true
default_tests_live_runtime=false
```

The old branch must not be cherry-picked wholesale because its readiness count
expectations predate V3.11-V3.28 and V4.0-V4.1. Its approved contract content is
ported onto the current baseline instead.

#### V3 Closure Ledger

Add:

```text
docs/dataagent/dataagent-v3-closure-ledger.md
```

The ledger covers every version from V3.0 through V3.28 and records:

```text
version
purpose
evidence_kind
required_files
required_readiness_checks
required_markers
changes_default_runtime
grants_sidecar_authority
```

Allowed evidence kinds are:

```text
DynamicReadiness
StaticReadiness
ContractTest
RegressionHardening
OperatorArtifact
FinalFreeze
```

V3.5 and V3.7 remain legitimate hardening releases. The ledger links them to
their tests and surrounding readiness gates without inventing redundant runtime
checks.

#### V3 Closure Manifest and Validator

Add a focused source file such as:

```text
sources/Alife.Function/Alife.Function.DataAgent/
  DataAgentV3ClosureManifest.cs
```

The file owns small immutable types and a deterministic validator, for example:

```text
DataAgentV3ClosureManifest
DataAgentV3MilestoneEvidence
DataAgentV3ClosureValidator
DataAgentV3ClosureResult
```

The manifest owns the expected milestone and readiness-check sets. Callers do
not decide which V3 checks are required.

The validator verifies:

- all V3.0-V3.28 milestone entries are present exactly once;
- required evidence files and contract markers are present;
- every required V3 readiness check is present exactly once and passed;
- V3.8 and V3.9 governance checks are included;
- V3.10 static admission evidence is included;
- V3.27 operator evidence is present and passed;
- V4-only checks do not count toward V3 completeness;
- boundary flags preserve C# authority and unchanged default behavior.

It remains pure and deterministic. It does not start a runtime, call the
network, execute SQL, or read user/account data.

#### Hardened V3.28 Freeze

Modify:

```text
DataAgentV3FinalReadinessFreeze.cs
DataAgentReadiness.cs
```

The freeze consumes a validated closure result rather than treating
caller-provided counts as proof of completeness.

Counts are consistency evidence only. A numerically correct but incomplete set
must fail. The dynamic core count is derived from the actual frozen set where
possible. The static readiness count is verified through the static readiness
declaration and expected baseline, not accepted as an unsupported assertion.

Readiness construction must ensure the V3.8 and V3.9 evidence exists before the
V3.28 result is computed. V4.0 and V4.1 remain after the V3 freeze boundary.

## Evidence Flow

```text
V3 closure ledger
  + expected manifest
  + evidence files and markers
  + static readiness declaration
  + dynamic readiness results
               |
               v
      DataAgentV3ClosureValidator
               |
       +-------+-------+
       |               |
    complete        missing/failed/
       |            duplicate/unexpected
       v               v
 V3.28 PASS        V3.28 FAIL
       |               |
 V4 baseline       fallback_required=true
                   operator_required=true
```

The compact freeze output contains only bounded booleans and counts. It must not
contain SQL, credentials, hidden context, user messages, QQ account details,
absolute local paths, or raw exception text.

Example failure packet:

```text
v3_final_readiness_freeze=false
missing_milestone_count=1
missing_required_check_count=1
failed_required_check_count=0
duplicate_required_check_count=0
unexpected_check_count=0
fallback_required=true
operator_required=true
default_result_changed=false
```

## Fail-Closed Rules

V3.28 fails when any of the following is true:

1. Any version from V3.0 through V3.27 is absent from the closure ledger.
2. The V3.10 contract document is absent.
3. `LangGraphRuntimeReadinessContractPresent` is absent or failed.
4. The V3.8 end-to-end chain gate is absent.
5. The V3.9 replay gate is absent.
6. The V3.24 advisory contract is absent.
7. The V3.25 manual shadow provider is absent.
8. The V3.26 replay diff gate is absent.
9. The V3.27 operator evidence pack is absent or failed.
10. Any required check is failed.
11. A required check or milestone appears more than once.
12. A declared evidence file does not exist.
13. A required marker is absent or has a conflicting value.
14. Any evidence grants sidecar SQL, checkpoint, Tool Broker, execution, state
    write, or visible-text authority.
15. The default DataAgent result is declared changed.
16. A V4-only check is used to fill a missing V3 requirement.
17. Static or dynamic counts disagree with the validated evidence set.
18. The ledger contains an unknown or skipped V3 version.

No failure may be hidden by lowering expected counts or changing a required
check to optional.

## Readiness Baselines

V3.10 adds one static admission check and no dynamic production check.

Expected baselines after reconciliation are:

```text
Before V3.28 freeze: 111 static required, 95 dynamic core
After V3.28 freeze:  112 static required, 96 dynamic core
After V4.0:          113 static required, 97 dynamic core
After V4.1:          114 static required, 98 dynamic core
```

The V3.28 document records the pre-freeze V3 baseline of `111/95`. Current
V4.1 readiness reports the total `114/98` after reconciliation.

## Documentation Reconciliation

Add:

```text
docs/dataagent/dataagent-roadmap-reconciliation.md
```

Modify the version-controlled 2026-06-27 design and plan documents to:

- retain their historical context;
- add a prominent reconciled/superseded notice;
- point to the authoritative roadmap reconciliation;
- mark ChatBI Console as not required and not committed;
- repair mojibake in the required-question examples.

The external desktop files were audited as historical inputs. They are not made
runtime or Git authorities, and absolute personal desktop paths are not added to
production code.

## Test Design

### Positive Coverage

A complete V3.0-V3.27 evidence chain, valid V3.10 contract, passed V3.27 operator
pack, and consistent counts produces:

```text
all_frozen_checks_passed=true
readiness_gates_frozen=true
fallback_required=false
operator_required=false
```

### Missing Milestones

Remove V3.5, V3.10, V3.18, and V3.27 in separate tests. Every case must fail
closed. This proves that hardening-only releases, the missing admission contract,
artifact releases, and the operator gate are all represented.

### Missing or Failed Checks

Separately remove or fail:

```text
GraphHandshakeBoundaryPresent
DataAgentEndToEndChainContractPresent
DataAgentReplayRunbookPresent
LangGraphRuntimeReadinessContractPresent
GraphHandshakeAgentAdvisoryContractPresent
GraphHandshakeOperatorEvidencePackPresent
```

Every case must fail.

### Duplicate and Fabricated Evidence

Tests cover:

- duplicate check names;
- unknown checks replacing a required check;
- correct counts with a missing critical check;
- a contract file with missing markers;
- a ledger entry whose evidence file does not exist;
- V4.0 or V4.1 checks attempting to fill a V3 gap.

### Authority Regression

Tests prove the reconciliation does not:

- start LangGraph or Python;
- install dependencies or bind a port;
- call a sidecar;
- execute SQL or mutate a checkpoint;
- change QChat text, QQ ingress, or the default DataAgent result.

### Verification Commands

Use the user-local .NET 9 SDK:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test `
  Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj `
  --filter "FullyQualifiedName~DataAgentV310ReadinessTests" `
  -v:minimal

& "C:\Users\hu shu\.dotnet\dotnet.exe" test `
  Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj `
  --filter "FullyQualifiedName~DataAgentV3ClosureManifestTests|FullyQualifiedName~DataAgentV328FinalReadinessFreezeTests" `
  -v:minimal

& "C:\Users\hu shu\.dotnet\dotnet.exe" test `
  Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj `
  --filter "FullyQualifiedName~DataAgentV3" `
  -v:minimal

& "C:\Users\hu shu\.dotnet\dotnet.exe" test `
  Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj `
  -v:minimal

powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1

& "C:\Users\hu shu\.dotnet\dotnet.exe" build Alife.slnx --no-restore -v:minimal

& "C:\Users\hu shu\.dotnet\dotnet.exe" test `
  Alife.slnx --no-restore --no-build -v:minimal

git diff --check
```

Required outcomes are `114 required passed / 0 missing`, dynamic core `98`, no
build errors, no failed tests, and no whitespace errors.

## Expected File Scope

### Add

```text
docs/dataagent/dataagent-v3.10-langgraph-runtime-readiness-contract.md
docs/dataagent/dataagent-v3-closure-ledger.md
docs/dataagent/dataagent-roadmap-reconciliation.md
sources/Alife.Function/Alife.Function.DataAgent/DataAgentV3ClosureManifest.cs
Tests/Alife.Test.DataAgent/DataAgentV310ReadinessTests.cs
Tests/Alife.Test.DataAgent/DataAgentV3ClosureManifestTests.cs
```

### Modify

```text
sources/Alife.Function/Alife.Function.DataAgent/DataAgentV3FinalReadinessFreeze.cs
sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs
Tests/Alife.Test.DataAgent/DataAgentV328FinalReadinessFreezeTests.cs
Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs
Tests/Alife.Test.DataAgent/DataAgentV210ReadinessTests.cs
Tests/Alife.Test.DataAgent/DataAgentV216ReadinessTests.cs
Tests/Alife.Test.DataAgent/DataAgentV30ReadinessTests.cs
Tests/Alife.Test.DataAgent/DataAgentV40RealLangGraphManualShadowIntegrationTests.cs
Tests/Alife.Test.DataAgent/DataAgentV41RealLangGraphManualShadowContextBudgetTests.cs
tools/check-dataagent-readiness.ps1
docs/dataagent/dataagent-v3.28-final-readiness-freeze.md
docs/superpowers/specs/2026-06-27-dataagent-nl2sql-design.md
docs/superpowers/plans/2026-06-27-dataagent-nl2sql.md
```

Only files whose assertions or documentation are directly affected should be
modified. The implementation plan must verify the exact set against the current
baseline rather than force every listed test file to change.

## Implementation and Commit Sequence

Work on branch:

```text
dataagent-v3-closure-reconciliation
```

in worktree:

```text
D:\Alife\.worktrees\dataagent-v3-closure-reconciliation
```

from baseline `9452f966`.

Expected implementation commits:

1. `docs(dataagent): restore v3.10 runtime admission contract`
2. `fix(dataagent): require complete v3 closure evidence`
3. `docs(dataagent): reconcile v3 roadmap and remove chatbi commitment`

The design and implementation plan commits precede these implementation
commits. Exact commit boundaries may combine inseparable tests with the code
they verify, but unrelated work must not enter the branch.

Push only to the `alife-byastralfox` remote and open a PR against
`hushu1232/Alife-byastralfox` master. Do not push to `origin` and do not touch
FOXD repositories.

## Rollback

This work has no data migration and no runtime activation.

- Reverting the V3.10 commit restores the prior static readiness count without
  changing QQ, NapCat, or DataAgent query execution.
- Reverting the freeze-hardening commit restores the previous V3.28 model while
  leaving V4.0 and V4.1 implementation code intact.
- Roadmap wording can be revised independently without reverting source.

A failing gate must be fixed at its evidence or implementation source. Deleting
tests, lowering expected counts, or making required checks optional is not an
acceptable rollback or repair.

## Acceptance Criteria

V3 is correctly closed only when all of the following are true:

1. V3.10 exists on the current mainline.
2. The V3.0-V3.28 closure ledger is continuous and machine-checked.
3. V3.28 fails for missing, failed, duplicate, fabricated, or V4-substituted
   evidence.
4. V3.8 and V3.9 are included in the final V3 evidence set.
5. ChatBI Console is removed from the required and committed roadmap.
6. The version-controlled 2026-06-27 documents point to the reconciled roadmap
   and no longer contain the identified mojibake.
7. DataAgent readiness reports `114 required passed / 0 missing`.
8. Dynamic core readiness remains `98` at the V4.1 baseline.
9. DataAgent tests and the full .NET 9 solution tests have zero failures.
10. The PR is reviewed, merged, and remote master is verified.
11. No runtime dependency, local state, credential, or unrelated repository
    change is introduced.

V3 closure does not make the whole Alife application production-ready. Local
dual-account production readiness remains a separate project covering host and
NapCat supervision, health probes, reconnect policy, rolling account restart,
on-demand voice/vision/browser/LangGraph recovery, backup and restore, Windows
restart drills, and 24/72-hour soak gates.
