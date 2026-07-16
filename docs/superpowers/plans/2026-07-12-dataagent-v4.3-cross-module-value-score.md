# DataAgent V4.3 Cross-Module Value Score Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Validate planner-only cross-module advisories and compute an operator-grounded deterministic value score that gates V4.4 production-shadow eligibility.

**Architecture:** A pure C# evaluator consumes a V4.2 packet, existing V3.14 manifests, explicit operator disposition, and bounded review timing. It derives component scores/status/eligibility without network, execution, state-write, or visible-text authority; formatter/artifact and readiness reuse existing safety patterns.

**Tech Stack:** .NET 9, C# records/static evaluator, NUnit 4, existing cross-module manifest validator and V4.2 packet.

---

## Files

- Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentV43CrossModuleValueScore.cs`.
- Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentV43CrossModuleValueArtifactWriter.cs`.
- Create `Tests/Alife.Test.DataAgent/DataAgentV43CrossModuleValueScoreTests.cs`.
- Create `docs/dataagent/dataagent-v4.3-cross-module-value-score.md`.
- Modify readiness source/tests/script and V3 post-version projection.

### Task 1: Deterministic evaluator, formatter, and artifact

- [ ] **Step 1: Write failing tests**

Test exact scores and status for adopted/useful/rejected/not-reviewed dispositions. Assert accepted packet 25, replay alignment 25, valid manifests 20, disposition 20/10/0, and proportional time reduction 0–10. Cover 80 `ProvenUseful`, 60 `Promising`, below-60 `Unproven`, invalid/rejected/fallback `Rejected`, and `ProductionShadowEligible` only for proven useful.

Add rejection tests for duplicate/unknown/unsafe capabilities, more than six capabilities, invalid manifests, negative or over-3,600,000 millisecond timing, time regression, and nonaccepted V4.2 packets. Assert no execution/write/visible-text authority.

- [ ] **Step 2: Run RED**

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj --no-restore --filter DataAgentV43CrossModuleValueScoreTests -v:minimal
```

Expected: compile failure because V4.3 types do not exist.

- [ ] **Step 3: Implement minimal evaluator**

Define disposition/status enums, input/result records, and evaluator constants. Resolve requested capabilities only from `DataAgentCrossModulePlannerManifestFactory.CreateDefault`, revalidate each manifest, calculate component scores exactly as the design specifies, derive status and eligibility, deduplicate stable reason codes, and fail closed with zero score.

- [ ] **Step 4: Add formatter/artifact tests then implementation**

Formatter emits only version, source baseline, capability tokens, component/total scores, status/disposition, eligibility, reason codes, and false authority fields. Writer persists only that formatter body to `dataagent-v4.3-cross-module-value-score.txt`; paths remain in-process only.

- [ ] **Step 5: Run GREEN and commit**

Run the focused command and commit source/tests with `feat(dataagent): add v4.3 cross-module value score`.

### Task 2: Readiness, documentation, and regression

- [ ] **Step 1: Write failing readiness tests**

Expect dynamic count 100 and static count 116, require `GraphHandshakeV43CrossModuleValueScorePresent`, add it to V4-only/post-V3 projection, and preserve V3 counts 111/95.

- [ ] **Step 2: Run RED**

Run V4.3, readiness, and V3 closure tests. Expected: missing gate/count failures only.

- [ ] **Step 3: Implement gate and document**

Add dynamic/static checks for source baseline V4.2, six allowlisted capabilities, four statuses/dispositions, 0–100 deterministic score, 80 eligibility threshold, no runtime call, and false execution/write/visible-text/default-change authorities. Create the runtime boundary document.

- [ ] **Step 4: Verify and commit**

Run focused tests, full DataAgent tests, static readiness, and `git diff --check`. Commit readiness/doc with `docs(dataagent): add v4.3 value readiness`.

## Completion boundary

V4.3 is complete only with exact score boundary tests, safe serialization, V3 freeze preservation, full DataAgent regression, and readiness passing. Continue to V4.4; V4.3 alone does not complete the active V4.5 goal.
