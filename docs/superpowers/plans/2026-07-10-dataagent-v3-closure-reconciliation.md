# DataAgent V3 Closure Reconciliation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore the missing V3.10 admission contract and make V3.28 prove that the complete V3.0-V3.27 evidence chain exists, is unique, passes, and remains authority-bounded.

**Architecture:** Add one static V3.10 readiness contract, a versioned V3 closure ledger, and a pure C# manifest/validator. Feed the validated result into the existing V3.28 freeze, keep V4.0/V4.1 outside the frozen set, and reconcile the superseded ChatBI roadmap without changing DataAgent runtime behavior.

**Tech Stack:** .NET 9, C# records/static validators, NUnit, PowerShell readiness checks, Markdown machine markers, Git worktrees.

---

## Execution Preconditions

Use the existing isolated worktree and branch:

```text
Worktree: D:\Alife\.worktrees\dataagent-v3-closure-reconciliation
Branch:   dataagent-v3-closure-reconciliation
Baseline: 9452f966
```

Before implementation, verify:

```powershell
git status --short --branch
git log -1 --oneline
```

Expected:

```text
## dataagent-v3-closure-reconciliation
The branch contains the approved design and this implementation plan above
baseline 9452f966.
```

Do not re-create the worktree, cherry-pick the old V3.10 branch, switch to
`origin`, touch FOXD, or start any runtime service.

Use the user-local .NET 9 SDK for every build or test command:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe"
```

## File Structure

### New Files

- `docs/dataagent/dataagent-v3.10-langgraph-runtime-readiness-contract.md`
  - Restores the pre-runtime V3.10 admission contract on the current mainline.
- `docs/dataagent/dataagent-v3-closure-ledger.md`
  - Lists every V3.0-V3.28 milestone and its evidence kind.
- `docs/dataagent/dataagent-roadmap-reconciliation.md`
  - Makes the LangGraph V3 definition authoritative and removes ChatBI from the committed roadmap.
- `sources/Alife.Function/Alife.Function.DataAgent/DataAgentV3ClosureManifest.cs`
  - Owns expected milestones, required dynamic/static checks, parsers, closure validation, and result counts.
- `Tests/Alife.Test.DataAgent/DataAgentV310ReadinessTests.cs`
  - Proves the V3.10 document and static readiness gate.
- `Tests/Alife.Test.DataAgent/DataAgentV3ClosureManifestTests.cs`
  - Proves continuous milestones, evidence paths, required checks, count discipline, V4 exclusion, and roadmap reconciliation.

### Modified Files

- `sources/Alife.Function/Alife.Function.DataAgent/DataAgentV3FinalReadinessFreeze.cs`
  - Consumes a validated closure result and formats safe failure counts.
- `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs:3135`
  - Collects V3 closure evidence immediately before the V3.28 gate.
- `Tests/Alife.Test.DataAgent/DataAgentV328FinalReadinessFreezeTests.cs`
  - Replaces count-only fixtures with validated closure fixtures.
- `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs:19,305,496,561`
  - Keeps dynamic count at 98, updates static total to 114, and verifies the hardened freeze detail.
- `Tests/Alife.Test.DataAgent/DataAgentV210ReadinessTests.cs:36`
  - Updates the static total to 114.
- `Tests/Alife.Test.DataAgent/DataAgentV216ReadinessTests.cs:42`
  - Updates the static total to 114.
- `Tests/Alife.Test.DataAgent/DataAgentV30ReadinessTests.cs:52`
  - Updates the static total to 114.
- `tools/check-dataagent-readiness.ps1:198,265`
  - Adds the V3.10 gate, strengthens the V3.28 marker gate, and changes total 113 to 114.
- `docs/dataagent/dataagent-v3.28-final-readiness-freeze.md`
  - Changes the pre-freeze static baseline from 110 to 111 and documents closure failure counts.
- `docs/superpowers/specs/2026-06-27-dataagent-nl2sql-design.md`
  - Adds the roadmap reconciliation notice and repairs the five Chinese questions.
- `docs/superpowers/plans/2026-06-27-dataagent-nl2sql.md`
  - Adds the same historical notice and repairs the five Chinese questions.

The V4.0 and V4.1 tests are not changed unless a current assertion directly
references the old V3 frozen count. Verify with `rg` before editing them.

---

### Task 1: Restore the V3.10 Runtime Admission Contract

**Files:**
- Create: `Tests/Alife.Test.DataAgent/DataAgentV310ReadinessTests.cs`
- Create: `docs/dataagent/dataagent-v3.10-langgraph-runtime-readiness-contract.md`
- Modify: `tools/check-dataagent-readiness.ps1:202,265`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs:496,561`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV210ReadinessTests.cs:36`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV216ReadinessTests.cs:42`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV30ReadinessTests.cs:52`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV328FinalReadinessFreezeTests.cs:155`

- [ ] **Step 1: Add the failing V3.10 tests**

Create `DataAgentV310ReadinessTests.cs` with the reviewed V3.10 assertions,
updated for the current V4.1 total:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV310ReadinessTests
{
    [Test]
    public void StaticReadinessScriptIncludesLangGraphRuntimeReadinessContract()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string script = File.ReadAllText(Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1"));
        string declaration = FindReadinessCheckDeclaration(script, "LangGraphRuntimeReadinessContractPresent");

        Assert.Multiple(() =>
        {
            Assert.That(declaration, Is.Not.Empty);
            Assert.That(declaration, Does.Contain("docs/dataagent/dataagent-v3.10-langgraph-runtime-readiness-contract.md"));
            Assert.That(declaration, Does.Contain("manual_only=true"));
            Assert.That(declaration, Does.Contain("advisory_only=true"));
            Assert.That(declaration, Does.Contain("loopback_only=true"));
            Assert.That(declaration, Does.Contain("starts_runtime=false"));
            Assert.That(declaration, Does.Contain("installs_dependencies=false"));
            Assert.That(declaration, Does.Contain("creates_venv=false"));
            Assert.That(declaration, Does.Contain("binds_port=false"));
            Assert.That(declaration, Does.Contain("supervises_process=false"));
            Assert.That(declaration, Does.Contain("no_sql_authority=true"));
            Assert.That(declaration, Does.Contain("no_checkpoint_mutation=true"));
            Assert.That(declaration, Does.Contain("no_visible_text=true"));
            Assert.That(declaration, Does.Contain("fallback_required=true"));
            Assert.That(declaration, Does.Contain("replay_parity_required=true"));
            Assert.That(declaration, Does.Contain("default_tests_live_runtime=false"));
            Assert.That(script, Does.Contain("$expectedRequired = 114"));
        });
    }

    [Test]
    public void ContractDocumentDefinesPreRuntimeAdmissionBoundary()
    {
        string doc = ReadContractDocument();

        Assert.Multiple(() =>
        {
            Assert.That(doc, Does.Contain("V3.10 is not runtime integration"));
            Assert.That(doc, Does.Contain("GET /health"));
            Assert.That(doc, Does.Contain("POST /handshake"));
            Assert.That(doc, Does.Contain("POST /handshake-stream"));
            Assert.That(doc, Does.Contain("V3.11"));
            Assert.That(doc, Does.Contain("manual-only"));
            Assert.That(doc, Does.Contain("loopback-only"));
            Assert.That(doc, Does.Contain("default-disabled"));
            Assert.That(doc, Does.Contain("V3.12"));
            Assert.That(doc, Does.Contain("replay parity"));
            Assert.That(doc, Does.Contain("V4.0"));
            Assert.That(doc, Does.Contain("advisory mode"));
            Assert.That(doc, Does.Contain("C# remains the authority"));
        });
    }

    [Test]
    public void ContractDocumentForbidsRuntimeAndAuthorityExpansion()
    {
        string doc = ReadContractDocument();

        foreach (string marker in new[]
        {
            "manual_only=true", "advisory_only=true", "loopback_only=true",
            "starts_runtime=false", "installs_dependencies=false", "creates_venv=false",
            "binds_port=false", "supervises_process=false", "default_tests_live_runtime=false",
            "no_sql_authority=true", "no_checkpoint_mutation=true", "no_visible_text=true",
            "fallback_required=true", "replay_parity_required=true", "SQL execution",
            "checkpoint mutation", "Tool Broker route", "QChat visible text", "QQ ingress",
            "DataAgentGraphHandshakeValidator", "V3.9 replay"
        })
        {
            Assert.That(doc, Does.Contain(marker), marker);
        }
    }

    static string ReadContractDocument()
    {
        string root = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        return File.ReadAllText(Path.Combine(root, "docs", "dataagent", "dataagent-v3.10-langgraph-runtime-readiness-contract.md"));
    }

    static string FindReadinessCheckDeclaration(string script, string checkName)
    {
        string marker = $"-Name \"{checkName}\"";
        int nameIndex = script.IndexOf(marker, StringComparison.Ordinal);
        if (nameIndex < 0) return string.Empty;
        int start = script.LastIndexOf("New-Check", nameIndex, StringComparison.Ordinal);
        int next = script.IndexOf("New-Check", nameIndex + marker.Length, StringComparison.Ordinal);
        return start < 0 ? string.Empty : next < 0 ? script[start..] : script[start..next];
    }

    static string FindRepoRoot(string startDirectory)
    {
        for (DirectoryInfo? directory = new(startDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
```

- [ ] **Step 2: Run the focused test and verify RED**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test `
  Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj `
  --no-restore `
  --filter "FullyQualifiedName~DataAgentV310ReadinessTests" `
  -v:minimal
```

Expected: FAIL because the V3.10 document and static readiness declaration are
missing.

- [ ] **Step 3: Restore the V3.10 document**

Read the approved V3.10 blob with the following command and add that complete
document to the current branch. Do not cherry-pick its old readiness counts:

```powershell
git show 4bea2eed:docs/dataagent/dataagent-v3.10-langgraph-runtime-readiness-contract.md
```

The added document must preserve these exact sections and markers:

```markdown
# DataAgent V3.10 LangGraph Runtime Readiness Contract

V3.10 is not runtime integration. It defines the admission contract a real
LangGraph sidecar must satisfy before V3.11.

## Admission Surface

GET /health
POST /handshake
POST /handshake-stream

## Version Handoff

V3.11 is manual-only, loopback-only, and default-disabled.
V3.12 requires replay parity against the V3.9 replay baseline.
V4.0 is advisory mode only. C# remains the authority.

## Boundary

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

The complete blob includes prose explicitly forbidding SQL execution, checkpoint
mutation, Tool Broker route authority, QChat visible text, QQ ingress, file,
browser, desktop, evidence, audit, progress, and diagnostics write authority.

- [ ] **Step 4: Add the V3.10 static readiness declaration**

Insert after `DataAgentReplayRunbookPresent` in
`tools/check-dataagent-readiness.ps1`:

```powershell
New-Check -Group "Governance" -Name "LangGraphRuntimeReadinessContractPresent" -Passed (
    Test-FileMarker "docs/dataagent/dataagent-v3.10-langgraph-runtime-readiness-contract.md" @(
        "V3.10 is not runtime integration",
        "GET /health",
        "POST /handshake",
        "POST /handshake-stream",
        "V3.11",
        "manual-only",
        "loopback-only",
        "default-disabled",
        "V3.12",
        "replay parity",
        "V4.0",
        "advisory mode",
        "C# remains the authority",
        "DataAgentGraphHandshakeValidator",
        "SQL execution",
        "checkpoint mutation",
        "Tool Broker route",
        "QChat visible text",
        "QQ ingress",
        "manual_only=true",
        "advisory_only=true",
        "loopback_only=true",
        "starts_runtime=false",
        "installs_dependencies=false",
        "creates_venv=false",
        "binds_port=false",
        "supervises_process=false",
        "no_sql_authority=true",
        "no_checkpoint_mutation=true",
        "no_visible_text=true",
        "fallback_required=true",
        "replay_parity_required=true",
        "default_tests_live_runtime=false"
    )
) -Detail "V3.10 LangGraph runtime readiness contract markers manual_only=true advisory_only=true loopback_only=true starts_runtime=false installs_dependencies=false creates_venv=false binds_port=false supervises_process=false no_sql_authority=true no_checkpoint_mutation=true no_visible_text=true fallback_required=true replay_parity_required=true default_tests_live_runtime=false"
```

Change `$expectedRequired = 113` to `114`.

- [ ] **Step 5: Update all current static-count assertions**

Replace only the five current test references found by:

```powershell
rg -n '\$expectedRequired = 113|113 required passed' Tests\Alife.Test.DataAgent
```

with `114`. Do not change dynamic `Has.Count.EqualTo(98)` and do not change the
V3 frozen `110/95` markers in this task.

- [ ] **Step 6: Run V3.10 and readiness tests**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test `
  Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj `
  --no-restore `
  --filter "FullyQualifiedName~DataAgentV310ReadinessTests|FullyQualifiedName~DataAgentReadinessTests|FullyQualifiedName~DataAgentV210ReadinessTests|FullyQualifiedName~DataAgentV216ReadinessTests|FullyQualifiedName~DataAgentV30ReadinessTests|FullyQualifiedName~DataAgentV328FinalReadinessFreezeTests" `
  -v:minimal
```

Expected: PASS, with no dynamic count change.

- [ ] **Step 7: Run static readiness**

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected: `Summary: 114 required passed, 0 required missing`.

- [ ] **Step 8: Commit Task 1**

```powershell
git add docs/dataagent/dataagent-v3.10-langgraph-runtime-readiness-contract.md `
  Tests/Alife.Test.DataAgent/DataAgentV310ReadinessTests.cs `
  Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs `
  Tests/Alife.Test.DataAgent/DataAgentV210ReadinessTests.cs `
  Tests/Alife.Test.DataAgent/DataAgentV216ReadinessTests.cs `
  Tests/Alife.Test.DataAgent/DataAgentV30ReadinessTests.cs `
  Tests/Alife.Test.DataAgent/DataAgentV328FinalReadinessFreezeTests.cs `
  tools/check-dataagent-readiness.ps1
git commit -m "docs(dataagent): restore v3.10 runtime admission contract"
```

---

### Task 2: Add the Continuous V3 Closure Ledger

**Files:**
- Create: `docs/dataagent/dataagent-v3-closure-ledger.md`
- Create: `Tests/Alife.Test.DataAgent/DataAgentV3ClosureManifestTests.cs`

- [ ] **Step 1: Add a failing ledger-continuity test**

Create the test file with document-only tests first:

```csharp
using System.Text.RegularExpressions;
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed partial class DataAgentV3ClosureManifestTests
{
    const string InventoryStart = "[v3_closure_milestones]";
    const string InventoryEnd = "[/v3_closure_milestones]";
    const string MilestonePattern = @"^milestone=v3\.(0|[1-9]|1[0-9]|2[0-8])$";

    [Test]
    public void ClosureLedgerContainsEveryV3MilestoneExactlyOnce()
    {
        string ledger = File.ReadAllText(Path.Combine(FindRepoRoot(), "docs", "dataagent", "dataagent-v3-closure-ledger.md"));
        string[] versions = ParseMilestoneVersions(ledger);
        string[] expected = Enumerable.Range(0, 29).Select(index => $"v3.{index}").ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(versions, Is.EqualTo(expected));
            Assert.That(versions, Is.Unique);
        });
    }

    static string[] ParseMilestoneVersions(string ledger)
    {
        string[] lines = ledger.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        int[] starts = lines.Select((line, index) => (line, index))
            .Where(item => item.line == InventoryStart).Select(item => item.index).ToArray();
        int[] ends = lines.Select((line, index) => (line, index))
            .Where(item => item.line == InventoryEnd).Select(item => item.index).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(starts, Has.Exactly(1).Items);
            Assert.That(ends, Has.Exactly(1).Items);
        });
        Assert.That(ends.Single(), Is.GreaterThan(starts.Single()));

        string[] inventoryLines = lines[(starts.Single() + 1)..ends.Single()];
        foreach (string line in inventoryLines)
            Assert.That(Regex.IsMatch(line, MilestonePattern, RegexOptions.CultureInvariant), Is.True, line);

        return inventoryLines.Select(line => line["milestone=".Length..]).ToArray();
    }

    static string FindRepoRoot()
    {
        for (DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
```

- [ ] **Step 2: Run the ledger test and verify RED**

Expected: FAIL with `FileNotFoundException` for
`dataagent-v3-closure-ledger.md`.

- [ ] **Step 3: Create the closure ledger**

Create a Markdown table with one row per version and this exact machine block:

```text
[v3_closure_milestones]
milestone=v3.0
milestone=v3.1
milestone=v3.2
milestone=v3.3
milestone=v3.4
milestone=v3.5
milestone=v3.6
milestone=v3.7
milestone=v3.8
milestone=v3.9
milestone=v3.10
milestone=v3.11
milestone=v3.12
milestone=v3.13
milestone=v3.14
milestone=v3.15
milestone=v3.16
milestone=v3.17
milestone=v3.18
milestone=v3.19
milestone=v3.20
milestone=v3.21
milestone=v3.22
milestone=v3.23
milestone=v3.24
milestone=v3.25
milestone=v3.26
milestone=v3.27
milestone=v3.28
[/v3_closure_milestones]
```

Use these evidence kinds in the human-readable rows:

```text
v3.0-v3.3   DynamicReadiness
v3.4        StaticReadiness
v3.5        RegressionHardening
v3.6        DynamicReadiness
v3.7        RegressionHardening
v3.8-v3.9   DynamicReadiness
v3.10       StaticReadiness
v3.11-v3.17 DynamicReadiness
v3.18-v3.23 OperatorArtifact
v3.24-v3.26 DynamicReadiness
v3.27       OperatorArtifact
v3.28       FinalFreeze
```

Every row must state `changes_default_runtime=false` and
`grants_sidecar_authority=false`.

Use this exact version-to-evidence mapping in the table:

```text
v3.0  | Graph handshake boundary                 | docs/dataagent/dataagent-v3.0-graph-handshake-boundary.md                 | GraphHandshakeBoundaryPresent
v3.1  | Dev sidecar adapter                      | docs/dataagent/dataagent-v3.1-dev-sidecar-adapter.md                      | GraphHandshakeDevSidecarAdapterPresent
v3.2  | Progress bridge                          | docs/dataagent/dataagent-v3.2-sidecar-progress-bridge.md                  | GraphHandshakeDevSidecarProgressBridgePresent
v3.3  | NDJSON streaming                         | docs/dataagent/dataagent-v3.3-ndjson-streaming-transport.md               | GraphHandshakeDevSidecarStreamingTransportPresent
v3.4  | Manual live smoke                        | docs/dataagent/dataagent-v3.4-dev-sidecar-live-smoke-harness.md           | GraphHandshakeDevSidecarLiveSmokeHarnessPresent
v3.5  | Smoke contract regression                | Tests/Alife.Test.DataAgent/DataAgentGraphSidecarSmokeScriptContractTests.cs | inherited V3.4/V3.6 gates
v3.6  | Sidecar observability                     | sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeModels.cs | GraphHandshakeDevSidecarObservabilityContractPresent
v3.7  | Reason-code hardening                    | docs/superpowers/specs/2026-07-08-dataagent-v3.7-reason-code-stability-design.md | inherited V3.6 gate
v3.8  | End-to-end chain                         | Tests/Alife.Test.DataAgent/DataAgentEndToEndChainContractTests.cs         | DataAgentEndToEndChainContractPresent
v3.9  | Replay runbook                           | Tests/Alife.Test.DataAgent/DataAgentReplayRunbookTests.cs                 | DataAgentReplayRunbookPresent
v3.10 | Runtime admission contract               | docs/dataagent/dataagent-v3.10-langgraph-runtime-readiness-contract.md    | LangGraphRuntimeReadinessContractPresent
v3.11 | Real LangGraph skeleton                  | docs/dataagent/dataagent-v3.11-real-langgraph-sidecar-skeleton.md         | GraphHandshakeRealLangGraphSidecarSkeletonPresent
v3.12 | Replay parity                            | docs/dataagent/dataagent-v3.12-replay-parity-shadow-comparison.md         | GraphHandshakeReplayParityShadowComparisonPresent
v3.13 | Bounded diagnostics                      | docs/dataagent/dataagent-v3.13-bounded-diagnostics-explanation.md        | GraphHandshakeBoundedDiagnosticsExplanationPresent
v3.14 | Cross-module manifests                   | docs/dataagent/dataagent-v3.14-cross-module-planner-manifests.md          | GraphHandshakeCrossModulePlannerManifestsPresent
v3.15 | Authority fallback regression            | docs/dataagent/dataagent-v3.15-authority-fallback-regression.md           | GraphHandshakeAuthorityFallbackRegressionPresent
v3.16 | Live smoke readiness                     | docs/dataagent/dataagent-v3.16-langgraph-live-smoke-readiness.md          | GraphHandshakeLangGraphLiveSmokeReadinessPresent
v3.17 | Manual smoke harness                     | docs/dataagent/dataagent-v3.17-langgraph-manual-smoke.md                  | GraphHandshakeLangGraphManualSmokeHarnessPresent
v3.18 | Smoke artifact                           | docs/dataagent/dataagent-v3.18-smoke-result-artifact.md                   | GraphHandshakeSmokeResultArtifactFormatterPresent
v3.19 | Replay fixtures                          | docs/dataagent/dataagent-v3.19-replay-fixture-pack.md                     | GraphHandshakeReplayFixturePackPresent
v3.20 | Shadow replay report                     | docs/dataagent/dataagent-v3.20-shadow-replay-report.md                    | GraphHandshakeShadowReplayReportPresent
v3.21 | Replay report artifact                   | docs/dataagent/dataagent-v3.21-manual-replay-report-artifact.md           | GraphHandshakeManualReplayReportArtifactWriterPresent
v3.22 | Artifact index                           | docs/dataagent/dataagent-v3.22-manual-artifact-index.md                   | GraphHandshakeManualArtifactIndexPresent
v3.23 | Audit bundle                             | docs/dataagent/dataagent-v3.23-manual-audit-bundle.md                     | GraphHandshakeManualAuditBundlePresent
v3.24 | Agent advisory contract                  | docs/dataagent/dataagent-v3.24-agent-advisory-contract.md                 | GraphHandshakeAgentAdvisoryContractPresent
v3.25 | Manual shadow provider                   | docs/dataagent/dataagent-v3.25-real-langgraph-manual-shadow-provider.md   | GraphHandshakeRealLangGraphManualShadowProviderPresent
v3.26 | Replay diff gate                         | docs/dataagent/dataagent-v3.26-harness-replay-diff-gate.md                | GraphHandshakeHarnessReplayDiffGatePresent
v3.27 | Operator evidence pack                   | docs/dataagent/dataagent-v3.27-operator-evidence-pack.md                  | GraphHandshakeOperatorEvidencePackPresent
v3.28 | Final freeze                             | docs/dataagent/dataagent-v3.28-final-readiness-freeze.md                  | final freeze output
```

- [ ] **Step 4: Run the ledger test and verify GREEN**

Run the Task 2 test filter. Expected: PASS.

- [ ] **Step 5: Commit Task 2**

```powershell
git add docs/dataagent/dataagent-v3-closure-ledger.md `
  Tests/Alife.Test.DataAgent/DataAgentV3ClosureManifestTests.cs
git commit -m "docs(dataagent): add continuous v3 closure ledger"
```

---

### Task 3: Implement the Pure V3 Closure Manifest and Validator

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentV3ClosureManifest.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV3ClosureManifestTests.cs`

- [ ] **Step 1: Add failing manifest and fail-closed tests**

Extend the partial test class with these exact tests:

```csharp
[Test]
public void DefaultManifestCoversV30ThroughV328ExactlyOnce()
{
    IReadOnlyList<DataAgentV3MilestoneEvidence> manifest = DataAgentV3ClosureManifest.CreateDefault();
    string[] expected = Enumerable.Range(0, 29).Select(index => $"v3.{index}").ToArray();

    Assert.Multiple(() =>
    {
        Assert.That(manifest.Select(item => item.Version), Is.EqualTo(expected));
        Assert.That(manifest.Select(item => item.Version), Is.Unique);
        Assert.That(manifest.All(item => !item.ChangesDefaultRuntime), Is.True);
        Assert.That(manifest.All(item => !item.GrantsSidecarAuthority), Is.True);
        Assert.That(manifest.Single(item => item.Version == "v3.5").RequiredGateLabel, Is.EqualTo("inherited V3.4/V3.6 gates"));
        Assert.That(manifest.Single(item => item.Version == "v3.7").RequiredGateLabel, Is.EqualTo("inherited V3.6 gate"));
        Assert.That(manifest.Single(item => item.Version == "v3.28").RequiredGateLabel, Is.EqualTo("final freeze output"));
    });
}

[Test]
public void LedgerParserRejectsMissingOrDuplicateDelimitersAndOutOfBlockMarker()
{
    string ledger = ReadLedger();
    DataAgentV3LedgerParseResult missingStart = DataAgentV3ClosureManifest.ParseLedger(
        ledger.Replace("[v3_closure_milestones]", string.Empty, StringComparison.Ordinal));
    DataAgentV3LedgerParseResult missingEnd = DataAgentV3ClosureManifest.ParseLedger(
        ledger.Replace("[/v3_closure_milestones]", string.Empty, StringComparison.Ordinal));
    DataAgentV3LedgerParseResult duplicateStart = DataAgentV3ClosureManifest.ParseLedger(
        ledger.Replace(
            "[v3_closure_milestones]",
            $"[v3_closure_milestones]{Environment.NewLine}[v3_closure_milestones]",
            StringComparison.Ordinal));
    DataAgentV3LedgerParseResult duplicateEnd = DataAgentV3ClosureManifest.ParseLedger(
        ledger.Replace(
            "[/v3_closure_milestones]",
            $"[/v3_closure_milestones]{Environment.NewLine}[/v3_closure_milestones]",
            StringComparison.Ordinal));
    DataAgentV3LedgerParseResult outOfBlock = DataAgentV3ClosureManifest.ParseLedger(
        $"{ledger}{Environment.NewLine}milestone=v3.4");

    Assert.Multiple(() =>
    {
        Assert.That(missingStart.Errors, Is.Not.Empty);
        Assert.That(missingEnd.Errors, Is.Not.Empty);
        Assert.That(duplicateStart.Errors, Is.Not.Empty);
        Assert.That(duplicateEnd.Errors, Is.Not.Empty);
        Assert.That(outOfBlock.Errors, Does.Contain("out_of_block_milestone:milestone=v3.4"));
    });
}

[Test]
public void LedgerParserRejectsMalformedOutOfRangeWrongOrderAndCompactDuplicateRows()
{
    string ledger = ReadLedger();
    DataAgentV3LedgerParseResult malformed = DataAgentV3ClosureManifest.ParseLedger(
        ledger.Replace("milestone=v3.28", "milestone=v3.028", StringComparison.Ordinal));
    DataAgentV3LedgerParseResult outOfRange = DataAgentV3ClosureManifest.ParseLedger(
        ledger.Replace("milestone=v3.28", "milestone=v3.29", StringComparison.Ordinal));
    string wrongOrderText = ledger
        .Replace("milestone=v3.0", "milestone=temporary", StringComparison.Ordinal)
        .Replace("milestone=v3.1", "milestone=v3.0", StringComparison.Ordinal)
        .Replace("milestone=temporary", "milestone=v3.1", StringComparison.Ordinal);
    DataAgentV3LedgerParseResult wrongOrder = DataAgentV3ClosureManifest.ParseLedger(wrongOrderText);
    DataAgentV3LedgerParseResult compactDuplicate = DataAgentV3ClosureManifest.ParseLedger(
        AddCompactV34Duplicate(ledger));

    Assert.Multiple(() =>
    {
        Assert.That(malformed.Errors, Is.Not.Empty);
        Assert.That(outOfRange.Errors, Is.Not.Empty);
        Assert.That(wrongOrder.Errors, Does.Contain("milestone_order_or_membership_invalid"));
        Assert.That(compactDuplicate.Errors, Does.Contain("duplicate_closure_table_version"));
        Assert.That(compactDuplicate.Entries, Has.Count.EqualTo(30));
    });
}

[Test]
public void ValidatorAcceptsCompleteEvidenceWithoutV4Checks()
{
    DataAgentV3ClosureResult result = Validate(CompleteFixture());

    Assert.Multiple(() =>
    {
        Assert.That(result.Accepted, Is.True);
        Assert.That(result.StaticRequiredCheckCount, Is.EqualTo(111));
        Assert.That(result.FrozenCoreCheckCount, Is.EqualTo(95));
        Assert.That(result.OperatorEvidencePackPresent, Is.True);
        Assert.That(result.UnexpectedV4CheckNames, Is.Empty);
    });
}

[Test]
public void ValidatorRejectsLedgerManifestFieldParityDrift()
{
    ClosureFixture fixture = CompleteFixture();
    DataAgentV3LedgerEntry[] driftedEntries = fixture.Ledger.Entries.Select(item => item.Version switch
    {
        "v3.0" => item with { EvidenceKind = DataAgentV3EvidenceKind.StaticReadiness },
        "v3.1" => item with { Purpose = "drifted purpose" },
        "v3.2" => item with { EvidencePath = "docs/dataagent/drifted.md" },
        "v3.3" => item with { ChangesDefaultRuntime = true },
        "v3.4" => item with { GrantsSidecarAuthority = true },
        "v3.5" => item with { RequiredGateLabel = "drifted inherited gate" },
        "v3.7" => item with { RequiredGateLabel = "drifted inherited gate" },
        "v3.28" => item with { RequiredGateLabel = "drifted final label" },
        _ => item
    }).ToArray();

    DataAgentV3ClosureResult result = Validate(
        fixture,
        ledger: fixture.Ledger with { Entries = driftedEntries });

    Assert.Multiple(() =>
    {
        Assert.That(result.Accepted, Is.False);
        Assert.That(result.LedgerManifestParityMismatches, Does.Contain("v3.0:EvidenceKind"));
        Assert.That(result.LedgerManifestParityMismatches, Does.Contain("v3.1:Purpose"));
        Assert.That(result.LedgerManifestParityMismatches, Does.Contain("v3.2:EvidencePath"));
        Assert.That(result.LedgerManifestParityMismatches, Does.Contain("v3.3:ChangesDefaultRuntime"));
        Assert.That(result.LedgerManifestParityMismatches, Does.Contain("v3.4:GrantsSidecarAuthority"));
        Assert.That(result.LedgerManifestParityMismatches, Does.Contain("v3.5:RequiredGateLabel"));
        Assert.That(result.LedgerManifestParityMismatches, Does.Contain("v3.7:RequiredGateLabel"));
        Assert.That(result.LedgerManifestParityMismatches, Does.Contain("v3.28:RequiredGateLabel"));
    });
}

[Test]
public void ValidatorRejectsMissingV310MilestoneOrStaticContract()
{
    ClosureFixture missingMilestone = CompleteFixture();
    DataAgentV3ClosureResult missingMilestoneResult = Validate(
        missingMilestone,
        ledger: missingMilestone.Ledger with
        {
            MilestoneVersions = missingMilestone.Ledger.MilestoneVersions
                .Where(version => version != "v3.10").ToArray()
        });
    ClosureFixture missingStatic = CompleteFixture();
    DataAgentV3ClosureResult missingStaticResult = Validate(missingStatic, staticCheckNames: []);

    Assert.Multiple(() =>
    {
        Assert.That(missingMilestoneResult.Accepted, Is.False);
        Assert.That(missingMilestoneResult.MissingMilestoneVersions, Does.Contain("v3.10"));
        Assert.That(missingStaticResult.Accepted, Is.False);
        Assert.That(missingStaticResult.MissingRequiredCheckNames, Does.Contain("LangGraphRuntimeReadinessContractPresent"));
    });
}

[Test]
public void ValidatorRejectsMissingFailedOrDuplicateRequiredChecks()
{
    ClosureFixture missing = CompleteFixture();
    missing.Checks.RemoveAll(check => check.Name == "DataAgentEndToEndChainContractPresent");
    missing.Checks.Add(new DataAgentReadinessCheck("ReplacementBaselineCheck", true, "ready=true"));

    ClosureFixture failed = CompleteFixture();
    int failedIndex = failed.Checks.FindIndex(check => check.Name == "GraphHandshakeAgentAdvisoryContractPresent");
    failed.Checks[failedIndex] = failed.Checks[failedIndex] with { Passed = false };

    ClosureFixture duplicate = CompleteFixture();
    int baselineIndex = duplicate.Checks.FindLastIndex(check => check.Name.StartsWith("BaselineCheck", StringComparison.Ordinal));
    duplicate.Checks.RemoveAt(baselineIndex);
    duplicate.Checks.Add(new DataAgentReadinessCheck("DataAgentReplayRunbookPresent", true, "ready=true"));

    DataAgentV3ClosureResult missingResult = Validate(missing);
    DataAgentV3ClosureResult failedResult = Validate(failed);
    DataAgentV3ClosureResult duplicateResult = Validate(duplicate);

    Assert.Multiple(() =>
    {
        Assert.That(missingResult.MissingRequiredCheckNames, Does.Contain("DataAgentEndToEndChainContractPresent"));
        Assert.That(failedResult.FailedRequiredCheckNames, Does.Contain("GraphHandshakeAgentAdvisoryContractPresent"));
        Assert.That(duplicateResult.DuplicateRequiredCheckNames, Does.Contain("DataAgentReplayRunbookPresent"));
        Assert.That(missingResult.Accepted || failedResult.Accepted || duplicateResult.Accepted, Is.False);
    });
}

[Test]
public void ValidatorRejectsMissingEvidencePathAndCountDrift()
{
    ClosureFixture missingPath = CompleteFixture();
    HashSet<string> existingPaths = missingPath.ExistingEvidencePaths
        .Where(path => !path.EndsWith("dataagent-v3.10-langgraph-runtime-readiness-contract.md", StringComparison.Ordinal))
        .ToHashSet(StringComparer.Ordinal);
    DataAgentV3ClosureResult missingPathResult = Validate(missingPath, existingEvidencePaths: existingPaths);

    ClosureFixture coreDrift = CompleteFixture();
    int baselineIndex = coreDrift.Checks.FindLastIndex(check => check.Name.StartsWith("BaselineCheck", StringComparison.Ordinal));
    coreDrift.Checks.RemoveAt(baselineIndex);
    DataAgentV3ClosureResult coreDriftResult = Validate(coreDrift);
    DataAgentV3ClosureResult staticDriftResult = Validate(CompleteFixture(), staticRequiredCount: 110);

    Assert.Multiple(() =>
    {
        Assert.That(missingPathResult.MissingEvidencePaths, Has.Count.EqualTo(1));
        Assert.That(coreDriftResult.CoreCountMatches, Is.False);
        Assert.That(staticDriftResult.StaticCountMatches, Is.False);
        Assert.That(missingPathResult.Accepted || coreDriftResult.Accepted || staticDriftResult.Accepted, Is.False);
    });
}

[Test]
public void ValidatorRejectsV4SubstitutionAndAuthorityExpansion()
{
    ClosureFixture v4Substitution = CompleteFixture();
    int baselineIndex = v4Substitution.Checks.FindLastIndex(check => check.Name.StartsWith("BaselineCheck", StringComparison.Ordinal));
    v4Substitution.Checks.RemoveAt(baselineIndex);
    v4Substitution.Checks.Add(new DataAgentReadinessCheck(
        "GraphHandshakeRealLangGraphManualShadowIntegrationPresent", true, "ready=true"));
    DataAgentV3ClosureResult v4Result = Validate(v4Substitution);

    ClosureFixture authority = CompleteFixture();
    DataAgentV3MilestoneEvidence[] expandedManifest = authority.Manifest
        .Select(item => item.Version == "v3.0" ? item with { GrantsSidecarAuthority = true } : item)
        .ToArray();
    DataAgentV3ClosureResult authorityResult = Validate(authority, manifest: expandedManifest);

    Assert.Multiple(() =>
    {
        Assert.That(v4Result.UnexpectedV4CheckNames, Does.Contain("GraphHandshakeRealLangGraphManualShadowIntegrationPresent"));
        Assert.That(authorityResult.AuthorityExpansionCount, Is.EqualTo(1));
        Assert.That(v4Result.Accepted || authorityResult.Accepted, Is.False);
    });
}
```

Use this common fixture builder in the test class:

```csharp
static string ReadLedger() => File.ReadAllText(
    Path.Combine(FindRepoRoot(), "docs", "dataagent", "dataagent-v3-closure-ledger.md"));

static string AddCompactV34Duplicate(string ledger)
{
    string standardRow = ledger.Split('\n').Select(line => line.TrimEnd('\r'))
        .Single(line => line.StartsWith("| v3.4 |", StringComparison.Ordinal));
    string compactRow = standardRow
        .Replace(" | ", "|", StringComparison.Ordinal)
        .Replace("| ", "|", StringComparison.Ordinal)
        .Replace(" |", "|", StringComparison.Ordinal);
    return ledger.Replace(
        standardRow,
        $"{standardRow}{Environment.NewLine}{compactRow}",
        StringComparison.Ordinal);
}

static ClosureFixture CompleteFixture()
{
    IReadOnlyList<DataAgentV3MilestoneEvidence> manifest = DataAgentV3ClosureManifest.CreateDefault();
    DataAgentV3LedgerParseResult ledger = DataAgentV3ClosureManifest.ParseLedger(ReadLedger());
    List<DataAgentReadinessCheck> checks = manifest
        .SelectMany(item => item.RequiredDynamicCheckNames)
        .Distinct(StringComparer.Ordinal)
        .Select(name => new DataAgentReadinessCheck(
            name,
            true,
            name == "GraphHandshakeOperatorEvidencePackPresent"
                ? "operator_evidence_pack=true;operator_decides=true"
                : "ready=true"))
        .ToList();

    for (int index = checks.Count; index < DataAgentV3ClosureManifest.ExpectedFrozenCoreCount; index++)
        checks.Add(new DataAgentReadinessCheck($"BaselineCheck{index:D3}", true, "ready=true"));

    return new ClosureFixture(
        manifest,
        checks,
        ledger,
        manifest.SelectMany(item => item.RequiredStaticCheckNames).ToArray(),
        manifest.SelectMany(item => item.RequiredEvidencePaths).ToHashSet(StringComparer.Ordinal),
        DataAgentV3ClosureManifest.ExpectedFrozenStaticRequiredCount);
}

sealed record ClosureFixture(
    IReadOnlyList<DataAgentV3MilestoneEvidence> Manifest,
    List<DataAgentReadinessCheck> Checks,
    DataAgentV3LedgerParseResult Ledger,
    IReadOnlyList<string> StaticCheckNames,
    IReadOnlySet<string> ExistingEvidencePaths,
    int StaticRequiredCount);

static DataAgentV3ClosureResult Validate(
    ClosureFixture fixture,
    IReadOnlyCollection<DataAgentV3MilestoneEvidence>? manifest = null,
    DataAgentV3LedgerParseResult? ledger = null,
    IReadOnlyCollection<string>? staticCheckNames = null,
    IReadOnlySet<string>? existingEvidencePaths = null,
    int? staticRequiredCount = null) =>
    DataAgentV3ClosureValidator.Validate(
        manifest ?? fixture.Manifest,
        fixture.Checks,
        ledger ?? fixture.Ledger,
        staticCheckNames ?? fixture.StaticCheckNames,
        existingEvidencePaths ?? fixture.ExistingEvidencePaths,
        staticRequiredCount ?? fixture.StaticRequiredCount);
```

- [ ] **Step 2: Run tests and verify RED**

Expected: compile failure because the manifest, validator, result, enum, and
milestone types do not exist.

- [ ] **Step 3: Add the model and default manifest**

Create `DataAgentV3ClosureManifest.cs` with these public types and constants:

```csharp
using System.Text.RegularExpressions;

namespace Alife.Function.DataAgent;

public enum DataAgentV3EvidenceKind
{
    DynamicReadiness,
    StaticReadiness,
    ContractTest,
    RegressionHardening,
    OperatorArtifact,
    FinalFreeze
}

public sealed record DataAgentV3MilestoneEvidence(
    string Version,
    DataAgentV3EvidenceKind EvidenceKind,
    string Purpose,
    string EvidencePath,
    string RequiredGateLabel,
    IReadOnlyList<string> RequiredEvidencePaths,
    IReadOnlyList<string> RequiredDynamicCheckNames,
    IReadOnlyList<string> RequiredStaticCheckNames,
    bool ChangesDefaultRuntime,
    bool GrantsSidecarAuthority);

public sealed record DataAgentV3LedgerEntry(
    string Version,
    DataAgentV3EvidenceKind EvidenceKind,
    string Purpose,
    string EvidencePath,
    string RequiredGateLabel,
    bool ChangesDefaultRuntime,
    bool GrantsSidecarAuthority);

public sealed record DataAgentV3LedgerParseResult(
    IReadOnlyList<string> MilestoneVersions,
    IReadOnlyList<DataAgentV3LedgerEntry> Entries,
    IReadOnlyList<string> Errors);

public sealed record DataAgentV3ClosureResult(
    bool Accepted,
    int StaticRequiredCheckCount,
    int FrozenCoreCheckCount,
    IReadOnlyList<string> MissingMilestoneVersions,
    IReadOnlyList<string> DuplicateMilestoneVersions,
    IReadOnlyList<string> UnexpectedMilestoneVersions,
    IReadOnlyList<string> LedgerParseErrors,
    IReadOnlyList<string> LedgerManifestParityMismatches,
    IReadOnlyList<string> MissingEvidencePaths,
    IReadOnlyList<string> MissingRequiredCheckNames,
    IReadOnlyList<string> FailedRequiredCheckNames,
    IReadOnlyList<string> DuplicateRequiredCheckNames,
    IReadOnlyList<string> UnexpectedV4CheckNames,
    int AuthorityExpansionCount,
    bool OperatorEvidencePackPresent,
    bool StaticCountMatches,
    bool CoreCountMatches);

public static class DataAgentV3ClosureManifest
{
    public const int ExpectedFrozenStaticRequiredCount = 111;
    public const int ExpectedFrozenCoreCount = 95;

    public static IReadOnlyList<string> ExpectedVersions { get; } =
        Enumerable.Range(0, 29).Select(index => $"v3.{index}").ToArray();

    public static IReadOnlySet<string> V4OnlyCheckNames { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "GraphHandshakeRealLangGraphManualShadowIntegrationPresent",
            "GraphHandshakeRealLangGraphManualShadowContextBudgetPresent"
        };

    public static IReadOnlyList<DataAgentV3MilestoneEvidence> CreateDefault() =>
    [
        M("v3.0", DataAgentV3EvidenceKind.DynamicReadiness, "Graph handshake boundary", "docs/dataagent/dataagent-v3.0-graph-handshake-boundary.md", "GraphHandshakeBoundaryPresent", dynamicCheck: "GraphHandshakeBoundaryPresent"),
        M("v3.1", DataAgentV3EvidenceKind.DynamicReadiness, "Dev sidecar adapter", "docs/dataagent/dataagent-v3.1-dev-sidecar-adapter.md", "GraphHandshakeDevSidecarAdapterPresent", dynamicCheck: "GraphHandshakeDevSidecarAdapterPresent"),
        M("v3.2", DataAgentV3EvidenceKind.DynamicReadiness, "Progress bridge", "docs/dataagent/dataagent-v3.2-sidecar-progress-bridge.md", "GraphHandshakeDevSidecarProgressBridgePresent", dynamicCheck: "GraphHandshakeDevSidecarProgressBridgePresent"),
        M("v3.3", DataAgentV3EvidenceKind.DynamicReadiness, "NDJSON streaming", "docs/dataagent/dataagent-v3.3-ndjson-streaming-transport.md", "GraphHandshakeDevSidecarStreamingTransportPresent", dynamicCheck: "GraphHandshakeDevSidecarStreamingTransportPresent"),
        M("v3.4", DataAgentV3EvidenceKind.StaticReadiness, "Manual live smoke", "docs/dataagent/dataagent-v3.4-dev-sidecar-live-smoke-harness.md", "GraphHandshakeDevSidecarLiveSmokeHarnessPresent", staticCheck: "GraphHandshakeDevSidecarLiveSmokeHarnessPresent"),
        M("v3.5", DataAgentV3EvidenceKind.RegressionHardening, "Smoke contract regression", "Tests/Alife.Test.DataAgent/DataAgentGraphSidecarSmokeScriptContractTests.cs", "inherited V3.4/V3.6 gates"),
        M("v3.6", DataAgentV3EvidenceKind.DynamicReadiness, "Sidecar observability", "sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeModels.cs", "GraphHandshakeDevSidecarObservabilityContractPresent", dynamicCheck: "GraphHandshakeDevSidecarObservabilityContractPresent"),
        M("v3.7", DataAgentV3EvidenceKind.RegressionHardening, "Reason-code hardening", "docs/superpowers/specs/2026-07-08-dataagent-v3.7-reason-code-stability-design.md", "inherited V3.6 gate"),
        M("v3.8", DataAgentV3EvidenceKind.DynamicReadiness, "End-to-end chain", "Tests/Alife.Test.DataAgent/DataAgentEndToEndChainContractTests.cs", "DataAgentEndToEndChainContractPresent", dynamicCheck: "DataAgentEndToEndChainContractPresent"),
        M("v3.9", DataAgentV3EvidenceKind.DynamicReadiness, "Replay runbook", "Tests/Alife.Test.DataAgent/DataAgentReplayRunbookTests.cs", "DataAgentReplayRunbookPresent", dynamicCheck: "DataAgentReplayRunbookPresent"),
        M("v3.10", DataAgentV3EvidenceKind.StaticReadiness, "Runtime admission contract", "docs/dataagent/dataagent-v3.10-langgraph-runtime-readiness-contract.md", "LangGraphRuntimeReadinessContractPresent", staticCheck: "LangGraphRuntimeReadinessContractPresent"),
        M("v3.11", DataAgentV3EvidenceKind.DynamicReadiness, "Real LangGraph skeleton", "docs/dataagent/dataagent-v3.11-real-langgraph-sidecar-skeleton.md", "GraphHandshakeRealLangGraphSidecarSkeletonPresent", dynamicCheck: "GraphHandshakeRealLangGraphSidecarSkeletonPresent"),
        M("v3.12", DataAgentV3EvidenceKind.DynamicReadiness, "Replay parity", "docs/dataagent/dataagent-v3.12-replay-parity-shadow-comparison.md", "GraphHandshakeReplayParityShadowComparisonPresent", dynamicCheck: "GraphHandshakeReplayParityShadowComparisonPresent"),
        M("v3.13", DataAgentV3EvidenceKind.DynamicReadiness, "Bounded diagnostics", "docs/dataagent/dataagent-v3.13-bounded-diagnostics-explanation.md", "GraphHandshakeBoundedDiagnosticsExplanationPresent", dynamicCheck: "GraphHandshakeBoundedDiagnosticsExplanationPresent"),
        M("v3.14", DataAgentV3EvidenceKind.DynamicReadiness, "Cross-module manifests", "docs/dataagent/dataagent-v3.14-cross-module-planner-manifests.md", "GraphHandshakeCrossModulePlannerManifestsPresent", dynamicCheck: "GraphHandshakeCrossModulePlannerManifestsPresent"),
        M("v3.15", DataAgentV3EvidenceKind.DynamicReadiness, "Authority fallback regression", "docs/dataagent/dataagent-v3.15-authority-fallback-regression.md", "GraphHandshakeAuthorityFallbackRegressionPresent", dynamicCheck: "GraphHandshakeAuthorityFallbackRegressionPresent"),
        M("v3.16", DataAgentV3EvidenceKind.DynamicReadiness, "Live smoke readiness", "docs/dataagent/dataagent-v3.16-langgraph-live-smoke-readiness.md", "GraphHandshakeLangGraphLiveSmokeReadinessPresent", dynamicCheck: "GraphHandshakeLangGraphLiveSmokeReadinessPresent"),
        M("v3.17", DataAgentV3EvidenceKind.DynamicReadiness, "Manual smoke harness", "docs/dataagent/dataagent-v3.17-langgraph-manual-smoke.md", "GraphHandshakeLangGraphManualSmokeHarnessPresent", dynamicCheck: "GraphHandshakeLangGraphManualSmokeHarnessPresent"),
        M("v3.18", DataAgentV3EvidenceKind.OperatorArtifact, "Smoke artifact", "docs/dataagent/dataagent-v3.18-smoke-result-artifact.md", "GraphHandshakeSmokeResultArtifactFormatterPresent", dynamicCheck: "GraphHandshakeSmokeResultArtifactFormatterPresent"),
        M("v3.19", DataAgentV3EvidenceKind.OperatorArtifact, "Replay fixtures", "docs/dataagent/dataagent-v3.19-replay-fixture-pack.md", "GraphHandshakeReplayFixturePackPresent", dynamicCheck: "GraphHandshakeReplayFixturePackPresent"),
        M("v3.20", DataAgentV3EvidenceKind.OperatorArtifact, "Shadow replay report", "docs/dataagent/dataagent-v3.20-shadow-replay-report.md", "GraphHandshakeShadowReplayReportPresent", dynamicCheck: "GraphHandshakeShadowReplayReportPresent"),
        M("v3.21", DataAgentV3EvidenceKind.OperatorArtifact, "Replay report artifact", "docs/dataagent/dataagent-v3.21-manual-replay-report-artifact.md", "GraphHandshakeManualReplayReportArtifactWriterPresent", dynamicCheck: "GraphHandshakeManualReplayReportArtifactWriterPresent"),
        M("v3.22", DataAgentV3EvidenceKind.OperatorArtifact, "Artifact index", "docs/dataagent/dataagent-v3.22-manual-artifact-index.md", "GraphHandshakeManualArtifactIndexPresent", dynamicCheck: "GraphHandshakeManualArtifactIndexPresent"),
        M("v3.23", DataAgentV3EvidenceKind.OperatorArtifact, "Audit bundle", "docs/dataagent/dataagent-v3.23-manual-audit-bundle.md", "GraphHandshakeManualAuditBundlePresent", dynamicCheck: "GraphHandshakeManualAuditBundlePresent"),
        M("v3.24", DataAgentV3EvidenceKind.DynamicReadiness, "Agent advisory contract", "docs/dataagent/dataagent-v3.24-agent-advisory-contract.md", "GraphHandshakeAgentAdvisoryContractPresent", dynamicCheck: "GraphHandshakeAgentAdvisoryContractPresent"),
        M("v3.25", DataAgentV3EvidenceKind.DynamicReadiness, "Manual shadow provider", "docs/dataagent/dataagent-v3.25-real-langgraph-manual-shadow-provider.md", "GraphHandshakeRealLangGraphManualShadowProviderPresent", dynamicCheck: "GraphHandshakeRealLangGraphManualShadowProviderPresent"),
        M("v3.26", DataAgentV3EvidenceKind.DynamicReadiness, "Replay diff gate", "docs/dataagent/dataagent-v3.26-harness-replay-diff-gate.md", "GraphHandshakeHarnessReplayDiffGatePresent", dynamicCheck: "GraphHandshakeHarnessReplayDiffGatePresent"),
        M("v3.27", DataAgentV3EvidenceKind.OperatorArtifact, "Operator evidence pack", "docs/dataagent/dataagent-v3.27-operator-evidence-pack.md", "GraphHandshakeOperatorEvidencePackPresent", dynamicCheck: "GraphHandshakeOperatorEvidencePackPresent"),
        M("v3.28", DataAgentV3EvidenceKind.FinalFreeze, "Final freeze", "docs/dataagent/dataagent-v3.28-final-readiness-freeze.md", "final freeze output")
    ];

    public static DataAgentV3LedgerParseResult ParseLedger(string? text)
    {
        const string inventoryStart = "[v3_closure_milestones]";
        const string inventoryEnd = "[/v3_closure_milestones]";
        const string milestonePattern = @"^milestone=(v3\.(0|[1-9]|1[0-9]|2[0-8]))$";
        const string tableHeader = "| Version | Evidence kind | Purpose | Exact evidence path | Required check / gate | Runtime boundary | Sidecar authority boundary |";
        const string tableSeparator = "|---|---|---|---|---|---|---|";

        string[] lines = (text ?? string.Empty).Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        List<string> errors = [];
        List<string> versions = [];
        List<DataAgentV3LedgerEntry> entries = [];
        int[] starts = FindLineIndexes(lines, inventoryStart);
        int[] ends = FindLineIndexes(lines, inventoryEnd);

        if (starts.Length != 1) errors.Add($"inventory_start_count={starts.Length}");
        if (ends.Length != 1) errors.Add($"inventory_end_count={ends.Length}");
        if (starts.Length == 1 && ends.Length == 1)
        {
            if (ends[0] <= starts[0])
            {
                errors.Add("inventory_delimiter_order_invalid");
            }
            else
            {
                for (int index = starts[0] + 1; index < ends[0]; index++)
                {
                    Match match = Regex.Match(lines[index], milestonePattern, RegexOptions.CultureInvariant);
                    if (!match.Success) errors.Add($"invalid_milestone_line:{lines[index]}");
                    else versions.Add(match.Groups[1].Value);
                }

                for (int index = 0; index < lines.Length; index++)
                {
                    bool outsideInventory = index <= starts[0] || index >= ends[0];
                    if (outsideInventory && Regex.IsMatch(lines[index], milestonePattern, RegexOptions.CultureInvariant))
                        errors.Add($"out_of_block_milestone:{lines[index]}");
                }
            }
        }

        if (!versions.SequenceEqual(ExpectedVersions, StringComparer.Ordinal))
            errors.Add("milestone_order_or_membership_invalid");
        if (versions.GroupBy(version => version, StringComparer.Ordinal).Any(group => group.Count() != 1))
            errors.Add("duplicate_milestone_version");

        int[] headers = FindLineIndexes(lines, tableHeader);
        if (headers.Length != 1)
        {
            errors.Add($"closure_table_header_count={headers.Length}");
        }
        else if (headers[0] + 1 >= lines.Length || lines[headers[0] + 1] != tableSeparator)
        {
            errors.Add("closure_table_separator_invalid");
        }
        else
        {
            for (int index = headers[0] + 2; index < lines.Length; index++)
            {
                string row = lines[index].Trim();
                if (row.Length == 0) break;
                string[] columns = row.Split('|');
                if (columns.Length != 9 || columns[0].Length != 0 || columns[^1].Length != 0)
                {
                    errors.Add($"closure_table_column_count_invalid:{index}");
                    continue;
                }

                string[] values = columns[1..^1].Select(value => value.Trim()).ToArray();
                string kindText = UnwrapCodeSpan(values[1], errors, $"kind:{index}");
                if (!Enum.GetNames<DataAgentV3EvidenceKind>().Contains(kindText, StringComparer.Ordinal))
                {
                    errors.Add($"evidence_kind_invalid:{kindText}");
                    continue;
                }

                entries.Add(new DataAgentV3LedgerEntry(
                    values[0],
                    Enum.Parse<DataAgentV3EvidenceKind>(kindText),
                    values[2],
                    UnwrapCodeSpan(values[3], errors, $"path:{index}"),
                    UnwrapCodeSpan(values[4], errors, $"gate:{index}"),
                    ParseBoundary(values[5], "changes_default_runtime", errors, index),
                    ParseBoundary(values[6], "grants_sidecar_authority", errors, index)));
            }
        }

        string[] entryVersions = entries.Select(entry => entry.Version).ToArray();
        if (!entryVersions.SequenceEqual(ExpectedVersions, StringComparer.Ordinal))
            errors.Add("closure_table_order_or_membership_invalid");
        if (entryVersions.GroupBy(version => version, StringComparer.Ordinal).Any(group => group.Count() != 1))
            errors.Add("duplicate_closure_table_version");

        return new DataAgentV3LedgerParseResult(versions, entries, errors.Distinct(StringComparer.Ordinal).ToArray());
    }

    static int[] FindLineIndexes(string[] lines, string expected) =>
        lines.Select((line, index) => (line, index))
            .Where(item => item.line == expected)
            .Select(item => item.index)
            .ToArray();

    static string UnwrapCodeSpan(string value, List<string> errors, string field)
    {
        bool starts = value.StartsWith('`');
        bool ends = value.EndsWith('`');
        if (starts != ends || (starts && value.Length < 2))
        {
            errors.Add($"malformed_code_span:{field}");
            return string.Empty;
        }
        return starts ? value[1..^1] : value;
    }

    static bool ParseBoundary(string value, string name, List<string> errors, int row)
    {
        string unwrapped = UnwrapCodeSpan(value, errors, $"{name}:{row}");
        if (unwrapped == $"{name}=false") return false;
        if (unwrapped == $"{name}=true") return true;
        errors.Add($"boundary_invalid:{name}:{row}");
        return true;
    }

    public static IReadOnlyList<string> ParseStaticCheckNames(string script)
    {
        List<string> names = [];
        const string marker = "-Name \"";
        int offset = 0;
        while ((offset = (script ?? string.Empty).IndexOf(marker, offset, StringComparison.Ordinal)) >= 0)
        {
            int start = offset + marker.Length;
            int end = script.IndexOf('"', start);
            if (end < 0) break;
            names.Add(script[start..end]);
            offset = end + 1;
        }
        return names;
    }

    static DataAgentV3MilestoneEvidence M(
        string version,
        DataAgentV3EvidenceKind kind,
        string purpose,
        string evidencePath,
        string requiredGateLabel,
        string? dynamicCheck = null,
        string? staticCheck = null) =>
        new(
            version,
            kind,
            purpose,
            evidencePath,
            requiredGateLabel,
            [evidencePath],
            dynamicCheck == null ? [] : [dynamicCheck],
            staticCheck == null ? [] : [staticCheck],
            ChangesDefaultRuntime: false,
            GrantsSidecarAuthority: false);
}
```

- [ ] **Step 4: Implement the validator**

Append `DataAgentV3ClosureValidator` in the same file. Implement each result list
from exact set/group operations:

```csharp
public static class DataAgentV3ClosureValidator
{
    public static DataAgentV3ClosureResult Validate(
        IReadOnlyCollection<DataAgentV3MilestoneEvidence> manifest,
        IReadOnlyCollection<DataAgentReadinessCheck> dynamicChecks,
        DataAgentV3LedgerParseResult ledger,
        IReadOnlyCollection<string> staticCheckNames,
        IReadOnlySet<string> existingEvidencePaths,
        int staticRequiredCheckCount)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(dynamicChecks);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(staticCheckNames);
        ArgumentNullException.ThrowIfNull(existingEvidencePaths);

        string[] declaredVersions = manifest.Select(item => item.Version)
            .Concat(ledger.MilestoneVersions)
            .Concat(ledger.Entries.Select(item => item.Version))
            .ToArray();
        string[] missingVersions = DataAgentV3ClosureManifest.ExpectedVersions
            .Where(version =>
                manifest.Count(item => item.Version == version) != 1 ||
                ledger.MilestoneVersions.Count(item => item == version) != 1 ||
                ledger.Entries.Count(item => item.Version == version) != 1)
            .ToArray();
        string[] duplicateVersions = manifest.Select(item => item.Version).GroupBy(value => value, StringComparer.Ordinal)
            .Where(group => group.Count() > 1).Select(group => group.Key)
            .Concat(ledger.MilestoneVersions.GroupBy(value => value, StringComparer.Ordinal)
                .Where(group => group.Count() > 1).Select(group => group.Key))
            .Concat(ledger.Entries.Select(item => item.Version).GroupBy(value => value, StringComparer.Ordinal)
                .Where(group => group.Count() > 1).Select(group => group.Key))
            .Distinct(StringComparer.Ordinal).ToArray();
        string[] unexpectedVersions = declaredVersions
            .Where(version => !DataAgentV3ClosureManifest.ExpectedVersions.Contains(version, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal).ToArray();

        List<string> parityMismatches = [];
        foreach (string version in DataAgentV3ClosureManifest.ExpectedVersions)
        {
            DataAgentV3MilestoneEvidence[] manifestMatches = manifest.Where(item => item.Version == version).ToArray();
            DataAgentV3LedgerEntry[] ledgerMatches = ledger.Entries.Where(item => item.Version == version).ToArray();
            if (manifestMatches.Length != 1 || ledgerMatches.Length != 1) continue;
            DataAgentV3MilestoneEvidence manifestItem = manifestMatches[0];
            DataAgentV3LedgerEntry ledgerItem = ledgerMatches[0];

            if (manifestItem.Version != ledgerItem.Version) parityMismatches.Add($"{version}:Version");
            if (manifestItem.EvidenceKind != ledgerItem.EvidenceKind) parityMismatches.Add($"{version}:EvidenceKind");
            if (manifestItem.Purpose != ledgerItem.Purpose) parityMismatches.Add($"{version}:Purpose");
            if (manifestItem.EvidencePath != ledgerItem.EvidencePath) parityMismatches.Add($"{version}:EvidencePath");
            if (manifestItem.RequiredGateLabel != ledgerItem.RequiredGateLabel) parityMismatches.Add($"{version}:RequiredGateLabel");
            if (manifestItem.ChangesDefaultRuntime != ledgerItem.ChangesDefaultRuntime) parityMismatches.Add($"{version}:ChangesDefaultRuntime");
            if (manifestItem.GrantsSidecarAuthority != ledgerItem.GrantsSidecarAuthority) parityMismatches.Add($"{version}:GrantsSidecarAuthority");
        }

        string[] requiredPaths = manifest.SelectMany(item => item.RequiredEvidencePaths).Distinct(StringComparer.Ordinal).ToArray();
        string[] missingPaths = requiredPaths.Where(path => !existingEvidencePaths.Contains(path)).ToArray();

        string[] requiredDynamic = manifest.SelectMany(item => item.RequiredDynamicCheckNames).Distinct(StringComparer.Ordinal).ToArray();
        string[] requiredStatic = manifest.SelectMany(item => item.RequiredStaticCheckNames).Distinct(StringComparer.Ordinal).ToArray();
        string[] missingChecks = requiredDynamic.Where(name => dynamicChecks.Count(check => check.Name == name) == 0)
            .Concat(requiredStatic.Where(name => staticCheckNames.Count(item => item == name) == 0))
            .ToArray();
        string[] failedChecks = requiredDynamic.Where(name => dynamicChecks.Any(check => check.Name == name && !check.Passed)).ToArray();
        string[] duplicateChecks = requiredDynamic.Where(name => dynamicChecks.Count(check => check.Name == name) > 1)
            .Concat(requiredStatic.Where(name => staticCheckNames.Count(item => item == name) > 1))
            .ToArray();
        string[] unexpectedV4 = dynamicChecks.Where(check => DataAgentV3ClosureManifest.V4OnlyCheckNames.Contains(check.Name))
            .Select(check => check.Name).Distinct(StringComparer.Ordinal).ToArray();

        int authorityExpansionCount = manifest.Count(item => item.ChangesDefaultRuntime || item.GrantsSidecarAuthority);
        bool operatorPack = dynamicChecks.Any(check =>
            check.Name == "GraphHandshakeOperatorEvidencePackPresent" &&
            check.Passed &&
            check.Detail.Contains("operator_evidence_pack=true", StringComparison.Ordinal) &&
            check.Detail.Contains("operator_decides=true", StringComparison.Ordinal));
        bool staticCountMatches = staticRequiredCheckCount == DataAgentV3ClosureManifest.ExpectedFrozenStaticRequiredCount;
        bool coreCountMatches = dynamicChecks.Count == DataAgentV3ClosureManifest.ExpectedFrozenCoreCount;

        bool accepted =
            missingVersions.Length == 0 && duplicateVersions.Length == 0 && unexpectedVersions.Length == 0 &&
            ledger.Errors.Count == 0 && parityMismatches.Count == 0 &&
            missingPaths.Length == 0 && missingChecks.Length == 0 && failedChecks.Length == 0 &&
            duplicateChecks.Length == 0 && unexpectedV4.Length == 0 && authorityExpansionCount == 0 &&
            operatorPack && staticCountMatches && coreCountMatches;

        return new DataAgentV3ClosureResult(
            accepted,
            staticRequiredCheckCount,
            dynamicChecks.Count,
            missingVersions,
            duplicateVersions,
            unexpectedVersions,
            ledger.Errors,
            parityMismatches,
            missingPaths,
            missingChecks,
            failedChecks,
            duplicateChecks,
            unexpectedV4,
            authorityExpansionCount,
            operatorPack,
            staticCountMatches,
            coreCountMatches);
    }
}
```

Duplicate detection is per source: the manifest, milestone inventory, and
structured ledger table must each contain every expected version exactly once.
Acceptance also requires zero ledger parse errors and zero field-level parity
mismatches across all 29 manifest/table pairs.

- [ ] **Step 5: Run manifest tests and verify GREEN**

Run only `DataAgentV3ClosureManifestTests`. Expected: all tests pass.

- [ ] **Step 6: Commit Task 3**

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentV3ClosureManifest.cs `
  Tests/Alife.Test.DataAgent/DataAgentV3ClosureManifestTests.cs
git commit -m "feat(dataagent): validate complete v3 closure evidence"
```

---

### Task 4: Harden the V3.28 Freeze Model and Safe Packet

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentV3FinalReadinessFreeze.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV328FinalReadinessFreezeTests.cs`
- Modify: `docs/dataagent/dataagent-v3.28-final-readiness-freeze.md`

- [ ] **Step 1: Replace count-only tests with closure-result tests**

Update the test helper to return an accepted `DataAgentV3ClosureResult`, then
derive negative cases with record `with` expressions:

```csharp
static DataAgentV3ClosureResult AcceptedClosure() => new(
    Accepted: true,
    StaticRequiredCheckCount: 111,
    FrozenCoreCheckCount: 95,
    MissingMilestoneVersions: [],
    DuplicateMilestoneVersions: [],
    UnexpectedMilestoneVersions: [],
    LedgerParseErrors: [],
    LedgerManifestParityMismatches: [],
    MissingEvidencePaths: [],
    MissingRequiredCheckNames: [],
    FailedRequiredCheckNames: [],
    DuplicateRequiredCheckNames: [],
    UnexpectedV4CheckNames: [],
    AuthorityExpansionCount: 0,
    OperatorEvidencePackPresent: true,
    StaticCountMatches: true,
    CoreCountMatches: true);
```

Add assertions that an accepted result formats
`v3_final_readiness_freeze=true`, while a result with
`MissingRequiredCheckNames = ["DataAgentReplayRunbookPresent"]` formats:

```text
v3_final_readiness_freeze=false
missing_required_check_count=1
fallback_required=true
operator_required=true
```

Also assert the packet does not contain the missing check name, SQL, `bearer`,
absolute paths, or hidden context.

- [ ] **Step 2: Run V3.28 tests and verify RED**

Expected: compile failures because the builder still accepts raw checks/counts
and the freeze record has no failure-count properties.

- [ ] **Step 3: Change the freeze record and builder**

Add these integer properties to `DataAgentV3FinalReadinessFreeze`:

```csharp
int MissingMilestoneCount,
int MissingEvidencePathCount,
int MissingRequiredCheckCount,
int FailedRequiredCheckCount,
int DuplicateRequiredCheckCount,
int UnexpectedCheckCount,
```

Replace `Build(IReadOnlyCollection<DataAgentReadinessCheck>, int, int)` with:

```csharp
public static DataAgentV3FinalReadinessFreeze Build(DataAgentV3ClosureResult closure)
{
    ArgumentNullException.ThrowIfNull(closure);

    int unexpectedCount =
        closure.UnexpectedMilestoneVersions.Count +
        closure.UnexpectedV4CheckNames.Count +
        closure.LedgerParseErrors.Count +
        closure.LedgerManifestParityMismatches.Count +
        closure.AuthorityExpansionCount;

    return new DataAgentV3FinalReadinessFreeze(
        FreezeId,
        FinalV3Version,
        SourceVersions,
        closure.StaticRequiredCheckCount,
        closure.FrozenCoreCheckCount,
        closure.Accepted,
        closure.OperatorEvidencePackPresent,
        closure.Accepted,
        FallbackRequired: !closure.Accepted,
        OperatorRequired: !closure.Accepted,
        OperatorDecides: true,
        AgentAdvisoryOnly: true,
        HarnessExecutionAuthority: true,
        CSharpValidationAuthority: true,
        DefaultResultChanged: false,
        ManualOnly: true,
        StartsRuntime: false,
        InstallsDependencies: false,
        CallsSidecar: false,
        StoresSecrets: false,
        StoresSql: false,
        StoresHiddenContext: false,
        MissingMilestoneCount: closure.MissingMilestoneVersions.Count,
        MissingEvidencePathCount: closure.MissingEvidencePaths.Count,
        MissingRequiredCheckCount: closure.MissingRequiredCheckNames.Count,
        FailedRequiredCheckCount: closure.FailedRequiredCheckNames.Count,
        DuplicateRequiredCheckCount: closure.DuplicateRequiredCheckNames.Count,
        UnexpectedCheckCount: unexpectedCount);
}
```

- [ ] **Step 4: Update the safe formatter**

Replace the constant first line with:

```csharp
$"v3_final_readiness_freeze={LowerBool(freeze.AllFrozenChecksPassed)}"
```

Append only these counts, never names or paths:

```csharp
$"missing_milestone_count={freeze.MissingMilestoneCount}",
$"missing_evidence_path_count={freeze.MissingEvidencePathCount}",
$"missing_required_check_count={freeze.MissingRequiredCheckCount}",
$"failed_required_check_count={freeze.FailedRequiredCheckCount}",
$"duplicate_required_check_count={freeze.DuplicateRequiredCheckCount}",
$"unexpected_check_count={freeze.UnexpectedCheckCount}",
```

- [ ] **Step 5: Update the V3.28 document**

Change:

```text
frozen_required_check_count=110
```

to:

```text
frozen_required_check_count=111
```

Keep `frozen_core_check_count=95`. Add the six zero-valued failure-count markers
and explain that a missing name cannot be hidden by a correct count.

- [ ] **Step 6: Run V3.28 tests and verify GREEN**

Expected: all V3.28 unit/document tests pass.

- [ ] **Step 7: Commit Task 4**

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentV3FinalReadinessFreeze.cs `
  Tests/Alife.Test.DataAgent/DataAgentV328FinalReadinessFreezeTests.cs `
  docs/dataagent/dataagent-v3.28-final-readiness-freeze.md
git commit -m "fix(dataagent): fail closed on incomplete v3 freeze evidence"
```

---

### Task 5: Integrate Closure Validation into Dynamic and Static Readiness

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs:3135-3218`
- Modify: `tools/check-dataagent-readiness.ps1:198`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs:305-325`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV328FinalReadinessFreezeTests.cs`

- [ ] **Step 1: Add failing integration assertions**

In `DataAgentReadinessTests`, assert the V3.28 detail contains:

```csharp
Assert.That(detail, Does.Contain("frozen_required_check_count=111"));
Assert.That(detail, Does.Contain("frozen_core_check_count=95"));
Assert.That(detail, Does.Contain("missing_milestone_count=0"));
Assert.That(detail, Does.Contain("missing_evidence_path_count=0"));
Assert.That(detail, Does.Contain("missing_required_check_count=0"));
Assert.That(detail, Does.Contain("failed_required_check_count=0"));
Assert.That(detail, Does.Contain("duplicate_required_check_count=0"));
Assert.That(detail, Does.Contain("unexpected_check_count=0"));
```

Assert `checks` still has exactly 98 entries and contains exactly one each of
`DataAgentEndToEndChainContractPresent`, `DataAgentReplayRunbookPresent`, and
`GraphHandshakeFinalV3ReadinessFreezePresent`.

- [ ] **Step 2: Run integration tests and verify RED**

Expected: V3.28 readiness still calls the removed builder signature or emits old
count markers.

- [ ] **Step 3: Collect closure evidence before V3.28**

At the current V3.28 block, after locating `v328RepoRoot`, add:

```csharp
string v3LedgerPath = Path.Combine(v328RepoRoot, "docs", "dataagent", "dataagent-v3-closure-ledger.md");
string readinessScriptPath = Path.Combine(v328RepoRoot, "tools", "check-dataagent-readiness.ps1");
string v3Ledger = File.Exists(v3LedgerPath) ? File.ReadAllText(v3LedgerPath) : string.Empty;
string readinessScript = File.Exists(readinessScriptPath) ? File.ReadAllText(readinessScriptPath) : string.Empty;

IReadOnlyList<DataAgentV3MilestoneEvidence> v3Manifest = DataAgentV3ClosureManifest.CreateDefault();
DataAgentV3LedgerParseResult parsedV3Ledger = DataAgentV3ClosureManifest.ParseLedger(v3Ledger);
IReadOnlySet<string> existingV3EvidencePaths = v3Manifest
    .SelectMany(item => item.RequiredEvidencePaths)
    .Where(path => File.Exists(Path.Combine(v328RepoRoot, path.Replace('/', Path.DirectorySeparatorChar))))
    .ToHashSet(StringComparer.Ordinal);

DataAgentV3ClosureResult v3Closure = DataAgentV3ClosureValidator.Validate(
    v3Manifest,
    checks.ToArray(),
    parsedV3Ledger,
    DataAgentV3ClosureManifest.ParseStaticCheckNames(readinessScript),
    existingV3EvidencePaths,
    DataAgentV3ClosureManifest.ExpectedFrozenStaticRequiredCount);

DataAgentV3FinalReadinessFreeze v328Freeze =
    DataAgentV3FinalReadinessFreezeBuilder.Build(v3Closure);
```

Remove the old builder call with `110, 95`.

- [ ] **Step 4: Strengthen the dynamic V3.28 readiness condition**

Require:

```csharp
v3Closure.Accepted &&
v328Freeze.AllFrozenChecksPassed &&
v328Freeze.FrozenRequiredCheckCount == 111 &&
v328Freeze.FrozenCoreCheckCount == 95
```

Update packet marker checks to require the six zero failure counts. The pass
detail must emit the same bounded marker set. The fail detail may emit only
booleans and counts, not lists, paths, or raw document content.

- [ ] **Step 5: Strengthen the PowerShell V3.28 gate**

Extend `GraphHandshakeFinalV3ReadinessFreezePresent` so it also checks:

```powershell
Test-FileMarker "docs/dataagent/dataagent-v3-closure-ledger.md" @(
    "milestone=v3.0", "milestone=v3.5", "milestone=v3.7",
    "milestone=v3.10", "milestone=v3.27", "milestone=v3.28"
)
```

and verifies the new manifest source contains:

```text
ExpectedFrozenStaticRequiredCount = 111
ExpectedFrozenCoreCount = 95
DataAgentEndToEndChainContractPresent
DataAgentReplayRunbookPresent
LangGraphRuntimeReadinessContractPresent
GraphHandshakeOperatorEvidencePackPresent
GraphHandshakeRealLangGraphManualShadowIntegrationPresent
GraphHandshakeRealLangGraphManualShadowContextBudgetPresent
DataAgentV3LedgerParseResult
ParseLedger
LedgerManifestParityMismatches
```

Update V3.28 static markers from `110` to `111` and add the six failure-count
markers. In the source-marker assertion, replace the obsolete literal
`v3_final_readiness_freeze=true` with `v3_final_readiness_freeze=` because the
formatter now emits the actual acceptance boolean. Keep `$expectedRequired = 114`.

- [ ] **Step 6: Run focused readiness tests**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test `
  Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj `
  --no-restore `
  --filter "FullyQualifiedName~DataAgentV3ClosureManifestTests|FullyQualifiedName~DataAgentV328FinalReadinessFreezeTests|FullyQualifiedName~DataAgentReadinessTests" `
  -v:minimal
```

Expected: PASS, 98 dynamic checks.

- [ ] **Step 7: Run static readiness**

Expected: `114 required passed, 0 required missing`.

- [ ] **Step 8: Commit Task 5**

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs `
  tools/check-dataagent-readiness.ps1 `
  Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs `
  Tests/Alife.Test.DataAgent/DataAgentV328FinalReadinessFreezeTests.cs
git commit -m "fix(dataagent): require complete v3 closure readiness"
```

---

### Task 6: Reconcile the Roadmap and Repair Historical Documentation

**Files:**
- Create: `docs/dataagent/dataagent-roadmap-reconciliation.md`
- Modify: `docs/superpowers/specs/2026-06-27-dataagent-nl2sql-design.md`
- Modify: `docs/superpowers/plans/2026-06-27-dataagent-nl2sql.md`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV3ClosureManifestTests.cs`

- [ ] **Step 1: Add failing roadmap tests**

Add this test that reads all three documents:

```csharp
[Test]
public void ReconciledRoadmapRemovesChatBiCommitmentAndRepairsHistoricalQuestions()
{
    string root = FindRepoRoot();
    string reconciliation = File.ReadAllText(Path.Combine(root, "docs", "dataagent", "dataagent-roadmap-reconciliation.md"));
    string oldDesign = File.ReadAllText(Path.Combine(root, "docs", "superpowers", "specs", "2026-06-27-dataagent-nl2sql-design.md"));
    string oldPlan = File.ReadAllText(Path.Combine(root, "docs", "superpowers", "plans", "2026-06-27-dataagent-nl2sql.md"));

    Assert.Multiple(() =>
    {
        Assert.That(reconciliation, Does.Contain("chatbi_console_required=false"));
        Assert.That(reconciliation, Does.Contain("chatbi_console_current_scope=false"));
        Assert.That(reconciliation, Does.Contain("chatbi_console_blocks_v3_closure=false"));
        Assert.That(reconciliation, Does.Contain("chatbi_console_committed_future_version=false"));
        Assert.That(reconciliation, Does.Contain("V3 = LangGraph advisory"));
        Assert.That(oldDesign, Does.Contain("dataagent-roadmap-reconciliation.md"));
        Assert.That(oldPlan, Does.Contain("dataagent-roadmap-reconciliation.md"));
        Assert.That(oldDesign, Does.Contain("当前还有哪些 required gate 没通过？"));
        Assert.That(oldPlan, Does.Contain("最近一次测试通过、失败和跳过的数量是多少？"));
        Assert.That(oldDesign, Does.Not.Contain("褰撳墠"));
        Assert.That(oldPlan, Does.Not.Contain("褰撳墠"));
    });
}
```

- [ ] **Step 2: Run the roadmap test and verify RED**

Expected: missing reconciliation document and mojibake assertions fail.

- [ ] **Step 3: Create the authoritative reconciliation document**

Include these exact markers:

```text
V1 = local safe NL2SQL core
V2 = storage orchestration context diagnostics boundaries
V3 = LangGraph advisory replay evidence operator gating
V4 = real LangGraph manual shadow and separately approved controlled use
chatbi_console_required=false
chatbi_console_current_scope=false
chatbi_console_blocks_v3_closure=false
chatbi_console_committed_future_version=false
```

Explain that ChatBI can be reconsidered only through a new approved requirement,
not as inherited roadmap debt.

- [ ] **Step 4: Add historical notices to both 2026-06-27 documents**

Insert immediately after each title:

```markdown
> **Roadmap reconciliation (2026-07-10):** This document preserves the
> 2026-06-27 design history. Its ChatBI Console V3 outlook is not the current
> committed roadmap and does not block DataAgent V3 closure. See
> `docs/dataagent/dataagent-roadmap-reconciliation.md`.
```

Do not delete the historical ChatBI section; label it as superseded outlook.

- [ ] **Step 5: Replace the mojibake question block in both files**

Use exactly:

```text
当前还有哪些 required gate 没通过？
哪些 readiness check 与 QChat、视觉或 TTS 有关？
哪些测试证明 runtime readiness 是 required？
最近一次测试通过、失败和跳过的数量是多少？
哪些文档与 DataAgent/NL2SQL 计划有关？
```

- [ ] **Step 6: Run roadmap and manifest tests**

Expected: PASS.

- [ ] **Step 7: Commit Task 6**

The `docs/superpowers` paths are already tracked; do not use `git add -f` for
modifications to tracked files.

```powershell
git add docs/dataagent/dataagent-roadmap-reconciliation.md `
  docs/superpowers/specs/2026-06-27-dataagent-nl2sql-design.md `
  docs/superpowers/plans/2026-06-27-dataagent-nl2sql.md `
  Tests/Alife.Test.DataAgent/DataAgentV3ClosureManifestTests.cs
git commit -m "docs(dataagent): reconcile v3 roadmap and remove chatbi commitment"
```

---

### Task 7: Final Verification, Scope Audit, and Handoff

**Files:**
- Verify: all files changed on `dataagent-v3-closure-reconciliation`
- Verify: `Alife.slnx`
- Verify: `tools/check-dataagent-readiness.ps1`

- [ ] **Step 1: Run the V3.10 focused tests**

Expected: all pass.

- [ ] **Step 2: Run closure and V3.28 tests**

Expected: all pass, including missing/failed/duplicate/V4-substitution cases.

- [ ] **Step 3: Run all V3-named tests**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test `
  Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj `
  --no-restore `
  --filter "FullyQualifiedName~DataAgentV3" `
  -v:minimal
```

Expected: zero failures.

- [ ] **Step 4: Run all DataAgent tests**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test `
  Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj `
  --no-restore `
  -v:minimal
```

Expected: zero failures.

- [ ] **Step 5: Run DataAgent readiness**

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected:

```text
GraphHandshakeFinalV3ReadinessFreezePresent: PASS
GraphHandshakeRealLangGraphManualShadowIntegrationPresent: PASS
GraphHandshakeRealLangGraphManualShadowContextBudgetPresent: PASS
Summary: 114 required passed, 0 required missing
```

- [ ] **Step 6: Build the full .NET 9 solution**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" build Alife.slnx --no-restore -v:minimal
```

Expected: build succeeds with zero errors.

- [ ] **Step 7: Run the full .NET 9 solution tests**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test `
  Alife.slnx --no-restore --no-build -v:minimal
```

Expected: zero failed tests. Existing environment-gated skips remain skips.

- [ ] **Step 8: Run whitespace and incomplete-text checks**

```powershell
git diff --check master...HEAD
rg -n "T[B]D|T[O]DO|PLACE[H]OLDER|褰撳墠" `
  docs/dataagent/dataagent-v3.10-langgraph-runtime-readiness-contract.md `
  docs/dataagent/dataagent-v3-closure-ledger.md `
  docs/dataagent/dataagent-roadmap-reconciliation.md `
  docs/superpowers/specs/2026-06-27-dataagent-nl2sql-design.md `
  docs/superpowers/plans/2026-06-27-dataagent-nl2sql.md
```

Expected: `git diff --check` has no output. `rg` has no output and returns its
normal no-match status.

- [ ] **Step 9: Audit the final diff scope**

```powershell
git diff --stat master...HEAD
git status --short --branch
git log --oneline master..HEAD
```

Confirm:

- no QChat, NapCat, voice, vision, browser, Python sidecar runtime, Storage,
  Runtime, Outputs, credentials, screenshots, or logs changed;
- no Vue or Node project was added;
- no file under FOXD changed;
- only the design, plan, V3.10, closure validation, V3.28, readiness, tests, and
  reconciled documents are present.

- [ ] **Step 10: Request code review**

Use `superpowers:requesting-code-review` against `master...HEAD`. Address review
findings through `superpowers:receiving-code-review`, rerun affected tests, then
repeat Steps 3-9.

- [ ] **Step 11: Prepare integration choice**

Use `superpowers:finishing-a-development-branch`. If the user chooses a PR, push
only:

```powershell
git push -u alife-byastralfox dataagent-v3-closure-reconciliation
```

Create the PR against `hushu1232/Alife-byastralfox:master`. Do not push to
`origin`. After merge, verify remote master with:

```powershell
git ls-remote --heads alife-byastralfox master
```

Do not declare V3 correctly closed until the PR is merged, remote master is
verified, and all acceptance checks above remain green.

---

## Plan Self-Review

- **Spec coverage:** Tasks 1-6 cover V3.10 restoration, continuous ledger,
  manifest validation, V3.28 failure closure, readiness integration, ChatBI
  removal, and mojibake repair. Task 7 covers the full acceptance and Git scope.
- **Runtime boundary:** No task starts Python, LangGraph, Alife, NapCat, voice,
  vision, browser, or any network listener.
- **Count consistency:** V3 pre-freeze is `111/95`; V3.28 makes `112/96`; V4.0
  makes `113/97`; V4.1 makes `114/98`.
- **Type consistency:** `DataAgentV3ClosureManifest.CreateDefault`,
  `DataAgentV3ClosureManifest.ParseLedger`, `DataAgentV3LedgerParseResult`,
  `DataAgentV3ClosureValidator.Validate`, `DataAgentV3ClosureResult`, and
  `DataAgentV3FinalReadinessFreezeBuilder.Build(DataAgentV3ClosureResult)` use
  the same structured signatures throughout Tasks 3-5.
- **No hidden future commitment:** ChatBI has no assigned future version.
- **No incomplete markers:** Every implementation task names exact files, tests,
  commands, expected failures, minimal implementation, and commit boundary.
