# DataAgent V3.10 LangGraph Runtime Readiness Contract Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the V3.10 static readiness contract that defines when a real LangGraph sidecar may be introduced, without adding real runtime behavior.

**Architecture:** This is a static governance milestone. Add a DataAgent contract document, add deterministic tests that assert the contract and readiness markers, then update the static readiness script count from 93 to 94. Do not change production C# or Python runtime behavior unless a test proves a static marker cannot be expressed otherwise.

**Tech Stack:** C# NUnit tests, PowerShell readiness script, Markdown documentation, .NET 9 test project.

---

## File Structure

- Create `docs/dataagent/dataagent-v3.10-langgraph-runtime-readiness-contract.md`
  - Human-facing DataAgent contract for the next real LangGraph milestone.
  - Must state that V3.10 is not runtime integration.
  - Must define V3.11/V3.12/V4.0 handoff.

- Create `Tests/Alife.Test.DataAgent/DataAgentV310ReadinessTests.cs`
  - Focused NUnit tests for the V3.10 doc and static readiness marker.
  - No live runtime calls.

- Modify `tools/check-dataagent-readiness.ps1`
  - Add one required static readiness marker named `LangGraphRuntimeReadinessContractPresent`.
  - Increase `$expectedRequired` from `93` to `94`.

- Modify `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
  - Assert the default readiness script output contains `LangGraphRuntimeReadinessContractPresent`.
  - Assert `$expectedRequired = 94`.
  - Add a focused guard for the V3.10 static marker declaration if useful.

- Modify version guard tests that still assert `$expectedRequired = 93`
  - Known files from the V3.9 baseline include:
    - `Tests/Alife.Test.DataAgent/DataAgentV216ReadinessTests.cs`
    - `Tests/Alife.Test.DataAgent/DataAgentV30ReadinessTests.cs`
  - Search for all remaining `$expectedRequired = 93` literals and update static-count assertions to `94` if they guard the current script count.

Do not modify:

- `sources/Alife.Function/Alife.Function.QChat/**`
- `tools/dataagent-graph-sidecar/app.py`
- `tools/run-dataagent-graph-sidecar-smoke.ps1`
- `tools/replay-dataagent-chain.ps1`
- `tools/dataagent-replay/**`
- upload scripts
- Python dependency files

---

### Task 1: Add Failing V3.10 Readiness Tests

**Files:**
- Create: `Tests/Alife.Test.DataAgent/DataAgentV310ReadinessTests.cs`

- [ ] **Step 1: Create the focused test file**

Add this complete file:

```csharp
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
            Assert.That(declaration, Is.Not.Empty, "Missing readiness declaration for LangGraphRuntimeReadinessContractPresent.");
            Assert.That(declaration, Does.Contain("docs/dataagent/dataagent-v3.10-langgraph-runtime-readiness-contract.md"));
            Assert.That(declaration, Does.Contain("manual_only=true"));
            Assert.That(declaration, Does.Contain("advisory_only=true"));
            Assert.That(declaration, Does.Contain("loopback_only=true"));
            Assert.That(declaration, Does.Contain("starts_runtime=false"));
            Assert.That(declaration, Does.Contain("installs_dependencies=false"));
            Assert.That(declaration, Does.Contain("no_sql_authority=true"));
            Assert.That(declaration, Does.Contain("no_checkpoint_mutation=true"));
            Assert.That(declaration, Does.Contain("no_visible_text=true"));
            Assert.That(declaration, Does.Contain("fallback_required=true"));
            Assert.That(declaration, Does.Contain("replay_parity_required=true"));
            Assert.That(declaration, Does.Contain("default_tests_live_runtime=false"));
            Assert.That(script, Does.Contain("$expectedRequired = 94"));
        });
    }

    [Test]
    public void ContractDocumentDefinesPreRuntimeAdmissionBoundary()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string doc = File.ReadAllText(Path.Combine(
            repoRoot,
            "docs",
            "dataagent",
            "dataagent-v3.10-langgraph-runtime-readiness-contract.md"));

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
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string doc = File.ReadAllText(Path.Combine(
            repoRoot,
            "docs",
            "dataagent",
            "dataagent-v3.10-langgraph-runtime-readiness-contract.md"));

        Assert.Multiple(() =>
        {
            Assert.That(doc, Does.Contain("starts_runtime=false"));
            Assert.That(doc, Does.Contain("installs_dependencies=false"));
            Assert.That(doc, Does.Contain("creates_venv=false"));
            Assert.That(doc, Does.Contain("binds_port=false"));
            Assert.That(doc, Does.Contain("default_tests_live_runtime=false"));
            Assert.That(doc, Does.Contain("SQL execution"));
            Assert.That(doc, Does.Contain("checkpoint mutation"));
            Assert.That(doc, Does.Contain("Tool Broker route"));
            Assert.That(doc, Does.Contain("QChat visible text"));
            Assert.That(doc, Does.Contain("QQ ingress"));
            Assert.That(doc, Does.Contain("DataAgentGraphHandshakeValidator"));
            Assert.That(doc, Does.Contain("V3.9 replay"));
        });
    }

    static string FindReadinessCheckDeclaration(string script, string checkName)
    {
        string marker = $"-Name \"{checkName}\"";
        int nameIndex = script.IndexOf(marker, StringComparison.Ordinal);
        if (nameIndex < 0)
            return string.Empty;

        int start = script.LastIndexOf("New-Check", nameIndex, StringComparison.Ordinal);
        if (start < 0)
            return string.Empty;

        int next = script.IndexOf("New-Check", nameIndex + marker.Length, StringComparison.Ordinal);
        return next < 0
            ? script[start..]
            : script[start..next];
    }

    static string FindRepoRoot(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "docs")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tools")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
```

- [ ] **Step 2: Run the new tests to verify they fail**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentV310ReadinessTests" -v:minimal
```

Expected:

```text
Failed!  - Failed: 3
```

The failures should be:

- missing `LangGraphRuntimeReadinessContractPresent` declaration,
- missing V3.10 DataAgent doc,
- `$expectedRequired = 94` not present.

- [ ] **Step 3: Commit the failing tests**

Run:

```powershell
git add Tests\Alife.Test.DataAgent\DataAgentV310ReadinessTests.cs
git commit -m "Test DataAgent V3.10 LangGraph readiness contract"
```

---

### Task 2: Add The V3.10 DataAgent Contract Document

**Files:**
- Create: `docs/dataagent/dataagent-v3.10-langgraph-runtime-readiness-contract.md`
- Test: `Tests/Alife.Test.DataAgent/DataAgentV310ReadinessTests.cs`

- [ ] **Step 1: Add the DataAgent contract doc**

Create `docs/dataagent/dataagent-v3.10-langgraph-runtime-readiness-contract.md` with this content:

```markdown
# DataAgent V3.10 LangGraph Runtime Readiness Contract

V3.10 is not runtime integration. It defines the contract a real LangGraph sidecar must satisfy before it can be introduced behind the existing DataAgent graph sidecar boundary.

V3.10 does not add LangGraph runtime code, start Python, create a virtual environment, install dependencies, bind ports, call a live sidecar from default tests, or put LangGraph into the default DataAgent chain.

## Admission Surface

The future real LangGraph sidecar must satisfy the existing C# graph sidecar contract. The allowed endpoint surface is:

```text
GET /health
POST /handshake
POST /handshake-stream
```

The endpoint must be loopback-only for V3.11 and V3.12 manual runs:

```text
http://127.0.0.1:<port>
http://localhost:<port>
https://127.0.0.1:<port>
https://localhost:<port>
```

The response shape must remain compatible with:

- `DataAgentGraphHandshakeRequest`
- `DataAgentGraphHandshakeResponse`
- `DataAgentGraphHandshakeStreamEvent`
- `DataAgentGraphHandshakeValidator`
- `DataAgentGraphSidecarProgressBridge`
- `DataAgentGraphHandshakeDiagnosticsFormatter`

The runtime must not introduce an alternate JSON shape that bypasses C# validation.

## Authority Boundary

C# remains the authority. LangGraph may be advisory only.

Allowed advisory behavior:

- propose orchestration intent,
- request an existing C# safety service,
- return bounded trace or progress suggestions,
- report deterministic fallback.

Forbidden authority behavior:

- SQL execution,
- executable SQL generation authority,
- dataset, field, operator, or limit authorization,
- checkpoint mutation,
- Tool Broker route decisions,
- evidence writes,
- audit writes,
- progress writes,
- diagnostics writes,
- QChat visible text,
- QQ ingress,
- file, browser, desktop, plugin, or external RAG management authority.

Every sidecar response remains untrusted input. C# validates, executes, persists, records diagnostics, and owns any user-visible result.

## Runtime Lifecycle Boundary

V3.10 freezes these lifecycle markers:

```text
manual_only=true
advisory_only=true
loopback_only=true
starts_runtime=false
installs_dependencies=false
creates_venv=false
binds_port=false
default_tests_live_runtime=false
```

Default tests must not require Python, LangGraph, FastAPI, uvicorn, a live port, network access, QChat, QQ, NapCat, PostgreSQL, browser automation, or model calls.

## Version Handoff

```text
V3.10  LangGraph runtime readiness contract
       No real runtime.

V3.11  Real LangGraph sidecar skeleton
       Manual-only, loopback-only, default-disabled.

V3.12  Replay parity / shadow comparison
       Compare real LangGraph output against V3.9 replay fixture expectations.

V4.0   Advisory runtime integration
       LangGraph may influence suggestions only; C# remains the authority.
```

The earliest real LangGraph touch is V3.11.

The earliest replay parity milestone is V3.12. V3.12 must compare real sidecar output against the V3.9 replay baseline before advisory integration.

The earliest default DataAgent chain involvement is V4.0 advisory mode. Even then, LangGraph must not gain SQL, checkpoint, Tool Broker, QChat visible text, QQ ingress, file, browser, desktop, plugin, evidence, audit, progress, or diagnostics authority.

## Readiness Marker

The static readiness marker is:

```text
LangGraphRuntimeReadinessContractPresent
```

Expected detail:

```text
manual_only=true;advisory_only=true;loopback_only=true;starts_runtime=false;installs_dependencies=false;no_sql_authority=true;no_checkpoint_mutation=true;no_visible_text=true;fallback_required=true;replay_parity_required=true;default_tests_live_runtime=false
```

This marker proves the admission contract is present. It does not prove that a real LangGraph runtime exists.
```

- [ ] **Step 2: Run V3.10 tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentV310ReadinessTests" -v:minimal
```

Expected:

```text
Failed!
```

The document tests should pass. The readiness-script test should still fail because `LangGraphRuntimeReadinessContractPresent` and `$expectedRequired = 94` have not been added yet.

- [ ] **Step 3: Commit the contract doc**

Run:

```powershell
git add docs\dataagent\dataagent-v3.10-langgraph-runtime-readiness-contract.md
git commit -m "Document DataAgent V3.10 LangGraph readiness contract"
```

---

### Task 3: Add Static Readiness Marker And Count

**Files:**
- Modify: `tools/check-dataagent-readiness.ps1`
- Test: `Tests/Alife.Test.DataAgent/DataAgentV310ReadinessTests.cs`

- [ ] **Step 1: Add the readiness marker**

In `tools/check-dataagent-readiness.ps1`, add this `New-Check` in the `Governance` group immediately after `DataAgentReplayRunbookPresent`:

```powershell
    New-Check -Group "Governance" -Name "LangGraphRuntimeReadinessContractPresent" -Passed (Test-FileMarker "docs/dataagent/dataagent-v3.10-langgraph-runtime-readiness-contract.md" @("V3.10 is not runtime integration", "GET /health", "POST /handshake", "POST /handshake-stream", "V3.11", "manual-only", "loopback-only", "default-disabled", "V3.12", "replay parity", "V4.0", "advisory mode", "C# remains the authority", "DataAgentGraphHandshakeValidator", "SQL execution", "checkpoint mutation", "Tool Broker route", "QChat visible text", "QQ ingress", "starts_runtime=false", "installs_dependencies=false", "creates_venv=false", "binds_port=false", "default_tests_live_runtime=false")) -Detail "V3.10 LangGraph runtime readiness contract markers manual_only=true advisory_only=true loopback_only=true starts_runtime=false installs_dependencies=false no_sql_authority=true no_checkpoint_mutation=true no_visible_text=true fallback_required=true replay_parity_required=true default_tests_live_runtime=false"
```

- [ ] **Step 2: Increase the static expected count**

In `tools/check-dataagent-readiness.ps1`, change:

```powershell
$expectedRequired = 93
```

to:

```powershell
$expectedRequired = 94
```

- [ ] **Step 3: Run V3.10 tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentV310ReadinessTests" -v:minimal
```

Expected:

```text
Passed!  - Failed: 0
```

- [ ] **Step 4: Run the readiness script**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected:

```text
PASS     LangGraphRuntimeReadinessContractPresent
Summary: 94 required passed, 0 required missing
```

- [ ] **Step 5: Commit the readiness marker**

Run:

```powershell
git add tools\check-dataagent-readiness.ps1
git commit -m "Add DataAgent V3.10 LangGraph readiness marker"
```

---

### Task 4: Align Existing Readiness Tests With Count 94

**Files:**
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV216ReadinessTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV30ReadinessTests.cs`
- Search for any additional `Tests/Alife.Test.DataAgent/*.cs` file that contains `$expectedRequired = 93`

- [ ] **Step 1: Update `DataAgentReadinessTests` output assertions**

In `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`, find the assertion block in `ReadinessScriptDefaultModeExitsZeroAndPrintsSummary`. Add this assertion near the existing `DataAgentReplayRunbookPresent` assertion:

```csharp
            Assert.That(result.StandardOutput, Does.Contain("LangGraphRuntimeReadinessContractPresent"));
```

In the static script count assertion, change:

```csharp
            Assert.That(script, Does.Contain("$expectedRequired = 93"));
```

to:

```csharp
            Assert.That(script, Does.Contain("$expectedRequired = 94"));
```

- [ ] **Step 2: Add a declaration guard to `DataAgentReadinessTests`**

Add this test near `ReadinessScriptProtectsV39ReplayRunbookContract`:

```csharp
    [Test]
    public void ReadinessScriptProtectsV310LangGraphRuntimeReadinessContract()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1");
        string script = File.ReadAllText(scriptPath);

        string declaration = FindNewCheckDeclaration(script, "LangGraphRuntimeReadinessContractPresent");

        Assert.Multiple(() =>
        {
            Assert.That(declaration, Does.Contain("docs/dataagent/dataagent-v3.10-langgraph-runtime-readiness-contract.md"));
            Assert.That(declaration, Does.Contain("V3.10 is not runtime integration"));
            Assert.That(declaration, Does.Contain("GET /health"));
            Assert.That(declaration, Does.Contain("POST /handshake"));
            Assert.That(declaration, Does.Contain("POST /handshake-stream"));
            Assert.That(declaration, Does.Contain("manual_only=true"));
            Assert.That(declaration, Does.Contain("advisory_only=true"));
            Assert.That(declaration, Does.Contain("loopback_only=true"));
            Assert.That(declaration, Does.Contain("starts_runtime=false"));
            Assert.That(declaration, Does.Contain("installs_dependencies=false"));
            Assert.That(declaration, Does.Contain("no_sql_authority=true"));
            Assert.That(declaration, Does.Contain("no_checkpoint_mutation=true"));
            Assert.That(declaration, Does.Contain("no_visible_text=true"));
            Assert.That(declaration, Does.Contain("fallback_required=true"));
            Assert.That(declaration, Does.Contain("replay_parity_required=true"));
            Assert.That(declaration, Does.Contain("default_tests_live_runtime=false"));
        });
    }
```

- [ ] **Step 3: Update version guard tests**

In `Tests/Alife.Test.DataAgent/DataAgentV216ReadinessTests.cs`, change:

```csharp
            Assert.That(script, Does.Contain("$expectedRequired = 93"));
```

to:

```csharp
            Assert.That(script, Does.Contain("$expectedRequired = 94"));
```

In `Tests/Alife.Test.DataAgent/DataAgentV30ReadinessTests.cs`, change:

```csharp
            Assert.That(script, Does.Contain("$expectedRequired = 93"));
```

to:

```csharp
            Assert.That(script, Does.Contain("$expectedRequired = 94"));
```

- [ ] **Step 4: Search for stale static count assertions**

Run:

```powershell
rg -n "\$expectedRequired = 93" Tests\Alife.Test.DataAgent tools\check-dataagent-readiness.ps1
```

Expected:

```text
no output
```

- [ ] **Step 5: Run focused readiness tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentV310ReadinessTests|FullyQualifiedName~DataAgentReadinessTests|FullyQualifiedName~DataAgentV216ReadinessTests|FullyQualifiedName~DataAgentV30ReadinessTests" -v:minimal
```

Expected:

```text
Passed!  - Failed: 0
```

- [ ] **Step 6: Commit test alignment**

Run:

```powershell
git add Tests\Alife.Test.DataAgent\DataAgentReadinessTests.cs Tests\Alife.Test.DataAgent\DataAgentV216ReadinessTests.cs Tests\Alife.Test.DataAgent\DataAgentV30ReadinessTests.cs
git commit -m "Align DataAgent V3.10 readiness tests"
```

---

### Task 5: Final Verification

**Files:**
- No code changes expected.

- [ ] **Step 1: Run static readiness**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected:

```text
PASS     LangGraphRuntimeReadinessContractPresent
Summary: 94 required passed, 0 required missing
```

- [ ] **Step 2: Run focused DataAgent tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentV310ReadinessTests|FullyQualifiedName~DataAgentReadinessTests" -v:minimal
```

Expected:

```text
Passed!  - Failed: 0
```

- [ ] **Step 3: Run full DataAgent tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
```

Expected:

```text
Passed!  - Failed: 0
```

Skipped tests are acceptable only if they are existing environment-gated live tests.

- [ ] **Step 4: Run diff hygiene**

Run:

```powershell
git diff --check
```

Expected:

```text
no output
```

- [ ] **Step 5: Confirm no runtime files changed**

Run:

```powershell
git diff --name-only 523cee36136d997d9fc6b41450a42eddb4939ce2..HEAD
```

Expected changed files should be limited to:

```text
docs/dataagent/dataagent-v3.10-langgraph-runtime-readiness-contract.md
docs/superpowers/plans/2026-07-09-dataagent-v3.10-langgraph-runtime-readiness-contract.md
docs/superpowers/specs/2026-07-09-dataagent-v3.10-langgraph-runtime-readiness-contract-design.md
Tests/Alife.Test.DataAgent/DataAgentV310ReadinessTests.cs
Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs
Tests/Alife.Test.DataAgent/DataAgentV216ReadinessTests.cs
Tests/Alife.Test.DataAgent/DataAgentV30ReadinessTests.cs
tools/check-dataagent-readiness.ps1
```

Do not include:

```text
sources/Alife.Function/Alife.Function.QChat/**
tools/dataagent-graph-sidecar/app.py
tools/run-dataagent-graph-sidecar-smoke.ps1
tools/replay-dataagent-chain.ps1
tools/dataagent-replay/**
```

- [ ] **Step 6: Commit verification if verification artifacts changed**

No commit is expected for verification-only work. If a small test or doc fix was needed, commit it with:

```powershell
git add <changed-files>
git commit -m "Verify DataAgent V3.10 LangGraph readiness contract"
```

---

## Self-Review

- Spec coverage:
  - V3.10 no-runtime boundary is implemented by the DataAgent doc and static readiness marker.
  - V3.11/V3.12/V4.0 handoff is implemented by the doc and tested in `DataAgentV310ReadinessTests`.
  - Static readiness count moves from 93 to 94.
  - No dynamic readiness marker is added, matching the design's default recommendation.
  - No production C# or Python runtime file is required.

- Placeholder scan:
  - This plan contains no placeholder tokens or deferred-work markers.
  - Every code-changing step includes concrete code or exact replacements.

- Type and name consistency:
  - Marker name is consistently `LangGraphRuntimeReadinessContractPresent`.
  - Document path is consistently `docs/dataagent/dataagent-v3.10-langgraph-runtime-readiness-contract.md`.
  - Test file is consistently `Tests/Alife.Test.DataAgent/DataAgentV310ReadinessTests.cs`.
