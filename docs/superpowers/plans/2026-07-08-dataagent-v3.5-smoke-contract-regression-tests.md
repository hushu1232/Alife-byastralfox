# DataAgent V3.5 Smoke Contract Regression Tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add deterministic no-network regression tests that prove the V3.4 PowerShell smoke harness rejects malformed handshake and NDJSON responses.

**Architecture:** The implementation adds one NUnit test file that launches PowerShell with a generated in-memory harness. The harness parses `tools/run-dataagent-graph-sidecar-smoke.ps1`, loads only function declarations, and calls `New-SmokeHandshakeRequest`, `Test-HandshakeResponse`, and `Test-NdjsonStream` directly without executing the script main flow or making HTTP requests.

**Tech Stack:** NUnit, C# process execution, Windows PowerShell 5-compatible harness, existing DataAgent .NET 9 test project, existing V3.4 PowerShell smoke script.

---

## File Structure

- Create: `Tests/Alife.Test.DataAgent/DataAgentGraphSidecarSmokeScriptContractTests.cs`
  - Owns all V3.5 no-network smoke-script contract regression tests.
  - Runs PowerShell in a subprocess with `-NoLogo`, `-NoProfile`, `-ExecutionPolicy Bypass`, and `-Command`.
  - Loads only function definitions from `tools/run-dataagent-graph-sidecar-smoke.ps1`.
  - Asserts valid in-memory response acceptance.
  - Asserts malformed in-memory response rejection.
  - Asserts malformed NDJSON final response rejection.
  - Includes static self-boundary checks over its own source.
- Do not modify: `tools/run-dataagent-graph-sidecar-smoke.ps1`
  - V3.5 is regression coverage for the existing hardened V3.4 script.
  - Modify this script only if the new tests reveal drift from the current accepted contract.
- Do not modify: `tools/check-dataagent-readiness.ps1`
  - Required count stays `90`.
- Do not modify: `tools/check-qchat-engineering-map.ps1`
  - Required count stays `63`.
- Do not modify: `sources/Alife.Function/Alife.Function.QChat/**`
  - QChat production source boundary remains unchanged.

---

### Task 1: Add No-Network Smoke Contract Regression Tests

**Files:**
- Create: `Tests/Alife.Test.DataAgent/DataAgentGraphSidecarSmokeScriptContractTests.cs`

- [ ] **Step 1: Create the test file with the PowerShell harness**

Create `Tests/Alife.Test.DataAgent/DataAgentGraphSidecarSmokeScriptContractTests.cs` with this content:

```csharp
using System.Diagnostics;
using System.Text;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentGraphSidecarSmokeScriptContractTests
{
    static readonly string[] ExpectedHarnessPassLines =
    [
        "PASS valid response accepted",
        "PASS rejects sql.execute requested tool",
        "PASS rejects unknown requested tool",
        "PASS rejects empty requested tool list",
        "PASS rejects SelectedNodes object entry",
        "PASS rejects SelectedNodes unknown node",
        "PASS rejects NodeProgress unknown node",
        "PASS rejects scalar Facts",
        "PASS rejects array Facts",
        "PASS rejects Facts reserved source key",
        "PASS rejects Facts non-string value",
        "PASS rejects NDJSON final response sql.execute",
        "PASS accepts null Facts"
    ];

    [Test]
    public void SmokeScriptRejectsMalformedHandshakeAndStreamContractsWithoutNetwork()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "run-dataagent-graph-sidecar-smoke.ps1");
        string harness = BuildPowerShellHarness(scriptPath);

        ScriptResult result = RunPowerShell(harness);
        string combinedOutput = result.StandardOutput + Environment.NewLine + result.StandardError;

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(0), combinedOutput);
            foreach (string expectedLine in ExpectedHarnessPassLines)
            {
                Assert.That(result.StandardOutput, Does.Contain(expectedLine), combinedOutput);
            }
        });
    }

    [Test]
    public void SmokeContractRegressionTestDoesNotInvokeLiveRuntime()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string source = File.ReadAllText(Path.Combine(repoRoot, "Tests", "Alife.Test.DataAgent", "DataAgentGraphSidecarSmokeScriptContractTests.cs"));
        string[] forbiddenRuntimeMarkers =
        [
            "Invoke-" + "WebRequest",
            "Start-" + "Process",
            "uvicorn " + "app:app",
            "text/" + "event-stream",
            "Event" + "Source"
        ];

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("Parser.ParseInput"));
            Assert.That(source, Does.Contain("FunctionDefinitionAst"));
            Assert.That(source, Does.Contain("Test-HandshakeResponse"));
            Assert.That(source, Does.Contain("Test-NdjsonStream"));
            Assert.That(source, Does.Not.Contain("127.0.0.1:" + "8765"));
            foreach (string marker in forbiddenRuntimeMarkers)
            {
                Assert.That(source, Does.Not.Contain(marker));
            }
        });
    }

    static string BuildPowerShellHarness(string scriptPath)
    {
        string escapedScriptPath = scriptPath.Replace("'", "''", StringComparison.Ordinal);
        return $$"""
$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$scriptPath = '{{escapedScriptPath}}'
$source = Get-Content -LiteralPath $scriptPath -Raw
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors -and $parseErrors.Count -gt 0) {
    throw ("Smoke script parse errors: {0}" -f ($parseErrors | ForEach-Object { $_.Message } | Out-String))
}

$functions = $ast.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst]
}, $true)

foreach ($function in $functions) {
    Invoke-Expression $function.Extent.Text
}

function ConvertTo-DataJson {
    param($Value)
    return ($Value | ConvertTo-Json -Depth 32 -Compress)
}

function ConvertFrom-DataJson {
    param([string]$Json)
    return ($Json | ConvertFrom-Json)
}

function New-ValidResponse {
    param($Request)

    [ordered]@{
        RequestId = $Request.RequestId
        Accepted = $true
        NoSqlAuthority = $true
        ReadOnly = $true
        FallbackRequired = $false
        RequestsCheckpointMutation = $false
        RequestsVisibleText = $false
        SelectedNodes = @(
            "scenario_knowledge",
            "query_planner",
            "diagnostics_router"
        )
        NodeProgress = @(
            [ordered]@{
                NodeName = "scenario_knowledge"
                Status = "Completed"
                ReasonCode = "scenario_ready"
                Facts = [ordered]@{ stage = "scenario" }
            },
            [ordered]@{
                NodeName = "query_planner"
                Status = "Completed"
                ReasonCode = "planner_ready"
                Facts = [ordered]@{ stage = "planner" }
            },
            [ordered]@{
                NodeName = "diagnostics_router"
                Status = "Completed"
                ReasonCode = "diagnostics_ready"
                Facts = [ordered]@{ stage = "diagnostics" }
            }
        )
        RequestedToolNames = @(
            "dataagent.scenario_context.read",
            "dataagent.query_plan.propose",
            "dataagent.diagnostics.progress.read"
        )
    }
}

function Copy-Response {
    param($Response)
    return ConvertFrom-DataJson (ConvertTo-DataJson $Response)
}

function Assert-Accepts {
    param([string]$Name, [scriptblock]$Action)

    try {
        & $Action
        Write-Output ("PASS {0}" -f $Name)
    }
    catch {
        Write-Output ("FAIL {0}: {1}" -f $Name, $_.Exception.Message)
        exit 1
    }
}

function Assert-Rejects {
    param([string]$Name, [scriptblock]$Action)

    try {
        & $Action
        Write-Output ("FAIL {0}: accepted malformed payload" -f $Name)
        exit 1
    }
    catch {
        Write-Output ("PASS {0}: {1}" -f $Name, $_.Exception.Message)
    }
}

$request = New-SmokeHandshakeRequest
$valid = ConvertFrom-DataJson (ConvertTo-DataJson (New-ValidResponse $request))

Assert-Accepts "valid response accepted" {
    Test-HandshakeResponse $valid $request
}

$sqlTool = Copy-Response $valid
$sqlTool.RequestedToolNames = @("sql.execute")
Assert-Rejects "rejects sql.execute requested tool" {
    Test-HandshakeResponse $sqlTool $request
}

$unknownTool = Copy-Response $valid
$unknownTool.RequestedToolNames = @("unknown.tool")
Assert-Rejects "rejects unknown requested tool" {
    Test-HandshakeResponse $unknownTool $request
}

$emptyTools = Copy-Response $valid
$emptyTools.RequestedToolNames = @()
Assert-Rejects "rejects empty requested tool list" {
    Test-HandshakeResponse $emptyTools $request
}

$objectSelectedNode = Copy-Response $valid
$objectSelectedNode.SelectedNodes = @([pscustomobject]@{ Name = "scenario_knowledge" })
Assert-Rejects "rejects SelectedNodes object entry" {
    Test-HandshakeResponse $objectSelectedNode $request
}

$unknownSelectedNode = Copy-Response $valid
$unknownSelectedNode.SelectedNodes = @("unknown_node")
Assert-Rejects "rejects SelectedNodes unknown node" {
    Test-HandshakeResponse $unknownSelectedNode $request
}

$unknownProgressNode = Copy-Response $valid
$unknownProgressNode.NodeProgress[0].NodeName = "unknown_node"
Assert-Rejects "rejects NodeProgress unknown node" {
    Test-HandshakeResponse $unknownProgressNode $request
}

$scalarFacts = Copy-Response $valid
$scalarFacts.NodeProgress[0].Facts = "not-an-object"
Assert-Rejects "rejects scalar Facts" {
    Test-HandshakeResponse $scalarFacts $request
}

$arrayFacts = Copy-Response $valid
$arrayFacts.NodeProgress[0].Facts = @("not", "object")
Assert-Rejects "rejects array Facts" {
    Test-HandshakeResponse $arrayFacts $request
}

$reservedFacts = Copy-Response $valid
$reservedFacts.NodeProgress[0].Facts = [pscustomobject]@{ source = "graph_sidecar" }
Assert-Rejects "rejects Facts reserved source key" {
    Test-HandshakeResponse $reservedFacts $request
}

$nonStringFactValue = Copy-Response $valid
$nonStringFactValue.NodeProgress[0].Facts = [pscustomobject]@{ safe = 123 }
Assert-Rejects "rejects Facts non-string value" {
    Test-HandshakeResponse $nonStringFactValue $request
}

$ndjsonSql = Copy-Response $valid
$ndjsonSql.RequestedToolNames = @("sql.execute")
$progressEvent = [ordered]@{
    Kind = "Progress"
    Progress = [ordered]@{
        NodeName = "scenario_knowledge"
        Status = "Completed"
        ReasonCode = "scenario_ready"
        Facts = [ordered]@{ safe = "true" }
    }
}
$finalEvent = [ordered]@{
    Kind = "FinalResponse"
    Response = $ndjsonSql
}
$ndjson = (ConvertTo-DataJson $progressEvent) + "`n" + (ConvertTo-DataJson $finalEvent)
Assert-Rejects "rejects NDJSON final response sql.execute" {
    Test-NdjsonStream $ndjson $request
}

$nullFacts = Copy-Response $valid
$nullFacts.NodeProgress[0].Facts = $null
Assert-Accepts "accepts null Facts" {
    Test-HandshakeResponse $nullFacts $request
}
""";
    }

    static ScriptResult RunPowerShell(string command)
    {
        string powerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        ProcessStartInfo startInfo = new()
        {
            FileName = powerShell,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start PowerShell.");

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();

        if (process.WaitForExit(15000) == false)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Smoke script contract harness did not exit within 15 seconds.");
        }

        return new ScriptResult(process.ExitCode, stdout, stderr);
    }

    static string FindRepoRoot(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tools")) &&
                Directory.Exists(Path.Combine(directory.FullName, "Tests")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    readonly record struct ScriptResult(int ExitCode, string StandardOutput, string StandardError);
}
```

- [ ] **Step 2: Run the focused V3.5 test**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphSidecarSmokeScriptContractTests" -v:minimal
```

Expected: PASS on current `master`, because V3.4 already contains the hardened smoke script. The meaningful regression proof is that this test will fail if the V3.4 hardening is removed or weakened.

- [ ] **Step 3: Prove the test is not a live runtime test**

Run:

```powershell
rg -n "Invoke-WebRequest|127\.0\.0\.1:8765|Start-Process|uvicorn app:app|text/event-stream|EventSource" Tests\Alife.Test.DataAgent\DataAgentGraphSidecarSmokeScriptContractTests.cs
```

Expected: no output, exit code `1`. The new test should not contain live HTTP calls, the default V3.4 port, process startup, uvicorn startup, or SSE literals.

- [ ] **Step 4: Commit the V3.5 regression test**

Run:

```powershell
git add Tests\Alife.Test.DataAgent\DataAgentGraphSidecarSmokeScriptContractTests.cs
git commit -m "Add DataAgent V3.5 smoke contract tests"
```

---

### Task 2: Verify V3.5 Boundaries And Adjacent Readiness

**Files:**
- Read only: `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeDevSidecarStubTests.cs`
- Read only: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
- Read only: `tools/check-dataagent-readiness.ps1`
- Read only: `tools/check-qchat-engineering-map.ps1`
- Read only: `sources/Alife.Function/Alife.Function.QChat/**`

- [ ] **Step 1: Run adjacent V3.4/V3.5 focused tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeDevSidecarStubTests|FullyQualifiedName~DataAgentReadinessTests|FullyQualifiedName~DataAgentGraphSidecarSmokeScriptContractTests" -v:minimal
```

Expected: PASS. This confirms the new V3.5 regression tests coexist with the V3.4 static smoke and readiness guards.

- [ ] **Step 2: Run the DataAgent readiness script**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected output includes:

```text
PASS     GraphHandshakeDevSidecarLiveSmokeHarnessPresent
Summary: 90 required passed, 0 required missing
```

- [ ] **Step 3: Run the QChat engineering map**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected output includes:

```text
Summary: 63 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 4: Confirm QChat production source boundary remains clean**

Run:

```powershell
rg -n "DataAgentGraphHandshakeStream|DataAgentGraphSidecarProgress|DataAgentGraphHandshake" sources\Alife.Function\Alife.Function.QChat
```

Expected: no output, exit code `1`.

- [ ] **Step 5: Run the default runtime boundary scan**

Run:

```powershell
rg -n "run-dataagent-graph-sidecar-smoke|Invoke-WebRequest|127\.0\.0\.1:8765|uvicorn|Start-Process" Tests sources
```

Expected:

- The new V3.5 test may mention `run-dataagent-graph-sidecar-smoke.ps1` only as a file path for parsing functions.
- No default test invokes the smoke script main flow.
- No default test or production source uses `Invoke-WebRequest`, the default live sidecar port, uvicorn startup, or `Start-Process`.

- [ ] **Step 6: Run diff hygiene**

Run:

```powershell
git diff --check
```

Expected: exit code `0`.

- [ ] **Step 7: Run the DataAgent test project**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
```

Expected: PASS. Existing environment-gated live PostgreSQL tests may be skipped.

- [ ] **Step 8: Commit only if verification required fixes**

If Task 2 required no file changes, do not create a commit. If it required a small correction to the new V3.5 test, run:

```powershell
git add Tests\Alife.Test.DataAgent\DataAgentGraphSidecarSmokeScriptContractTests.cs
git commit -m "Harden DataAgent V3.5 smoke contract verification"
```

---

## Self-Review

- Spec coverage: Task 1 implements the no-network harness, valid response acceptance, malformed handshake rejection, malformed NDJSON final response rejection, and static self-boundary checks. Task 2 covers adjacent V3.4 tests, readiness, QChat map, QChat source boundary, runtime boundary scan, diff hygiene, and full DataAgent project tests.
- Completeness scan: the plan contains no incomplete filler steps.
- Type consistency: the plan uses existing repository names and test helper patterns: `FindRepoRoot`, `ScriptResult`, `RunPowerShell`, `DataAgentGraphHandshakeDevSidecarStubTests`, and `DataAgentReadinessTests`.
- Scope check: the plan is a single test-hardening task and does not modify runtime, readiness counts, QChat production source, Python startup behavior, or SSE behavior.
- TDD note: the target smoke script is already hardened in V3.4, so the new regression test should pass on current `master`. The regression value is preserved by asserting the exact malformed cases that previously reached final review.
