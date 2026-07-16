# DataAgent V4.2 Operator Evidence Packet Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a bounded V4.2 operator evidence packet for accepted, rejected, and fallback LangGraph manual-shadow outcomes.

**Architecture:** A pure C# builder composes the existing V4.0 integration result and V4.1 context envelope, derives status and authority fields, and rejects unsafe advisory metadata. A formatter and artifact writer serialize only the validated packet; readiness checks prove presence without a live runtime.

**Tech Stack:** .NET 9, C# records/static builders, NUnit 4, existing DataAgent safety detector/readiness framework.

---

## Files

- Create `Sources/Alife.Function/Alife.Function.DataAgent/DataAgentV42OperatorEvidencePacket.cs` for model, builder, and formatter.
- Create `Sources/Alife.Function/Alife.Function.DataAgent/DataAgentV42OperatorEvidenceArtifactWriter.cs` for safe persistence.
- Create `Tests/Alife.Test.DataAgent/DataAgentV42OperatorEvidencePacketTests.cs` for contract and artifact tests.
- Create `docs/dataagent/dataagent-v4.2-operator-evidence-packet.md` for machine-readable boundaries.
- Modify `Sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`, readiness tests, and `tools/check-dataagent-readiness.ps1` for one V4.2 gate.

### Task 1: Packet status and safety contract

- [ ] **Step 1: Write failing tests**

Call `DataAgentV42OperatorEvidencePacketBuilder.Build` with accepted V4.0/V4.1 fixtures, `DataAgentV42AdvisoryKinds.DiagnosticSummary`, a bounded summary constant, and one logical replay reference. Assert accepted status, three pass fields, no fallback/default change/write authority, and deduplicated reason codes. Add tests for contract rejection, context rejection, runtime fallback, null inputs, invalid kind, summary over 320 characters, more than eight references, unsafe SQL/secret/path text, and unsafe source reason codes.

- [ ] **Step 2: Run RED**

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj --no-restore --filter DataAgentV42OperatorEvidencePacketTests -v:minimal
```

Expected: compile failure because V4.2 types do not exist.

- [ ] **Step 3: Implement the minimal model and builder**

Define `DataAgentV42OperatorEvidenceStatus`, `DataAgentV42OperatorEvidenceInput`, and `DataAgentV42OperatorEvidencePacket`. Implement `Build` so accepted requires both source objects accepted; fallback is derived from V4.0 fallback/runtime reason codes; all other failures are rejected. Use fixed advisory kinds, a 320-character summary cap, eight-reference cap, the existing unsafe detector, absolute-path rejection, safe-token validation, and fail-closed reason codes.

- [ ] **Step 4: Run GREEN**

Run the Step 2 command. Expected: all V4.2 packet tests pass.

- [ ] **Step 5: Commit**

```powershell
git add Sources/Alife.Function/Alife.Function.DataAgent/DataAgentV42OperatorEvidencePacket.cs Tests/Alife.Test.DataAgent/DataAgentV42OperatorEvidencePacketTests.cs
git commit -m 'feat(dataagent): add v4.2 operator evidence packet'
```

### Task 2: Safe formatter and artifact

- [ ] **Step 1: Write failing formatter and writer tests**

Assert the formatter emits the V4.2 contract, source baseline, derived status, bounded summary, logical refs, authority booleans, and safe reason codes. Assert it omits replay identifiers, context text, raw responses, SQL, secrets, and absolute paths. Assert the writer creates `dataagent-v4.2-operator-evidence-packet.txt`, returns missing-input reason codes, and writes exactly the safe formatter body.

- [ ] **Step 2: Run RED**

Run the focused test command from Task 1. Expected: compile failure because formatter/writer types are missing.

- [ ] **Step 3: Implement formatter and writer**

Add `DataAgentV42OperatorEvidencePacketFormatter.Format` using allowlisted fields only. Add `DataAgentV42OperatorEvidenceArtifactWriter.Write` with `FileName`, output-directory/result validation, directory creation, formatted body writing, and an in-process path result that is never serialized into the body.

- [ ] **Step 4: Run GREEN and commit**

Run focused tests, then:

```powershell
git add Sources/Alife.Function/Alife.Function.DataAgent/DataAgentV42OperatorEvidencePacket.cs Sources/Alife.Function/Alife.Function.DataAgent/DataAgentV42OperatorEvidenceArtifactWriter.cs Tests/Alife.Test.DataAgent/DataAgentV42OperatorEvidencePacketTests.cs
git commit -m 'feat(dataagent): persist safe v4.2 evidence packets'
```

### Task 3: Readiness and documentation

- [ ] **Step 1: Write failing readiness tests**

Require `GraphHandshakeV42OperatorEvidencePacketPresent` in dynamic readiness and the static script. Assert markers for V4.2, source V4.1, accepted/rejected/fallback, bounded summary/refs, C# authority, default-result unchanged, no live runtime, and no SQL/secret/hidden-context storage.

- [ ] **Step 2: Run RED**

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj --no-restore --filter 'DataAgentV42OperatorEvidencePacketTests|DataAgentReadinessTests' -v:minimal
```

Expected: readiness gate absent or expected counts mismatch.

- [ ] **Step 3: Add readiness gate and document**

Add the dynamic/static gate, update only the current V4 counts while preserving V3.28 frozen counts, and create `docs/dataagent/dataagent-v4.2-operator-evidence-packet.md` with machine markers and the non-authority boundary.

- [ ] **Step 4: Verify and commit**

Run focused tests, `powershell.exe -NoProfile -File tools/check-dataagent-readiness.ps1`, and `git diff --check`. Commit source/tests/readiness/doc with message `docs(dataagent): add v4.2 evidence readiness`.

## Completion boundary

V4.2 is complete only when packet, formatter, artifact, readiness, focused tests, DataAgent regression tests, and static readiness all pass. It does not satisfy the active V4.5 goal by itself; continue to V4.3 after verification.
