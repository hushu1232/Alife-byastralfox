# DataAgent V3.4 Dev Sidecar Live Smoke Harness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a manual PowerShell live smoke harness that validates an already running DataAgent graph dev sidecar over `/health`, `/handshake`, and `/handshake-stream` without starting Python or changing default tests.

**Architecture:** The smoke harness is a standalone PowerShell script under `tools/` that performs loopback-only HTTP checks and validates the existing dev stub wire contract. Production C# runtime behavior is unchanged; default tests use static guards and readiness markers only. Readiness gains one static required check while dynamic core readiness and QChat engineering map counts stay unchanged.

**Tech Stack:** PowerShell 5-compatible script, NUnit static guard tests, existing DataAgent readiness script, Markdown docs, .NET 9 test/build commands via `C:\Users\hu shu\.dotnet\dotnet.exe`.

---

## File Structure

- Create: `tools/run-dataagent-graph-sidecar-smoke.ps1`
  - Standalone manual smoke script.
  - Validates loopback `-BaseUri`.
  - Calls `/health`, `/handshake`, and `/handshake-stream`.
  - Uses PowerShell-native `Invoke-WebRequest`, `ConvertTo-Json`, and `ConvertFrom-Json`.
  - Does not start Python, create venvs, install dependencies, or manage processes.
- Create: `docs/dataagent/dataagent-v3.4-dev-sidecar-live-smoke-harness.md`
  - User-facing manual smoke docs.
  - Explains non-goals, command, expected output, failure meaning, QChat/SSE/runtime boundaries.
- Modify: `tools/dataagent-graph-sidecar/README.md`
  - Adds V3.4 manual smoke section after V3.3.
- Modify: `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeDevSidecarStubTests.cs`
  - Adds static tests for the script, docs, README markers, and non-startup boundaries.
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
  - Updates static readiness count assertions from `89` to `90`.
  - Adds static readiness marker test for `GraphHandshakeDevSidecarLiveSmokeHarnessPresent`.
  - Keeps `CoreReadinessChecksAllPass` at `75`.
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV210ReadinessTests.cs`
  - Updates legacy guard `$expectedRequired = 89` to `$expectedRequired = 90`.
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV216ReadinessTests.cs`
  - Updates legacy guard `$expectedRequired = 89` to `$expectedRequired = 90`.
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV30ReadinessTests.cs`
  - Updates legacy guard `$expectedRequired = 89` to `$expectedRequired = 90`.
- Modify: `tools/check-dataagent-readiness.ps1`
  - Adds required static check and increments `$expectedRequired` to `90`.
- Do not modify: `sources/Alife.Function/Alife.Function.QChat/**`
- Do not modify: `tools/check-qchat-engineering-map.ps1` required count.
- Do not create: any live NUnit tests that call ports.

---

### Task 1: Add Failing Static Guards For V3.4 Smoke Harness

**Files:**
- Modify: `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeDevSidecarStubTests.cs`

- [ ] **Step 1: Add a failing script marker test**

Add this test after `PythonDevStubDocumentsV33NdjsonStreamWithoutRuntimeDependency`:

```csharp
[Test]
public void PythonDevStubDocumentsV34LiveSmokeHarnessWithoutRuntimeStartup()
{
    string root = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
    string script = File.ReadAllText(Path.Combine(root, "tools", "run-dataagent-graph-sidecar-smoke.ps1"));
    string readme = File.ReadAllText(Path.Combine(root, "tools", "dataagent-graph-sidecar", "README.md"));
    string doc = File.ReadAllText(Path.Combine(root, "docs", "dataagent", "dataagent-v3.4-dev-sidecar-live-smoke-harness.md"));

    Assert.Multiple(() =>
    {
        Assert.That(script, Does.Contain("DataAgent graph sidecar live smoke"));
        Assert.That(script, Does.Contain("/health"));
        Assert.That(script, Does.Contain("/handshake"));
        Assert.That(script, Does.Contain("/handshake-stream"));
        Assert.That(script, Does.Contain("application/x-ndjson"));
        Assert.That(script, Does.Contain("Assert-LoopbackBaseUri"));
        Assert.That(script, Does.Contain("Invoke-SidecarRequest"));
        Assert.That(script, Does.Contain("Test-HandshakeResponse"));
        Assert.That(script, Does.Contain("Test-NdjsonStream"));
        Assert.That(script, Does.Contain("starts_runtime=false"));
        Assert.That(script, Does.Contain("installs_dependencies=false"));
        Assert.That(script, Does.Contain("manual_only=true"));
        Assert.That(script, Does.Not.Contain("Start-Process"));
        Assert.That(script, Does.Not.Contain("pip install"));
        Assert.That(script, Does.Not.Contain("python -m venv"));
        Assert.That(script, Does.Not.Contain("uvicorn app:app"));
        Assert.That(script, Does.Not.Contain("text/event-stream"));
        Assert.That(script, Does.Not.Contain("EventSource"));
        Assert.That(readme, Does.Contain("V3.4"));
        Assert.That(readme, Does.Contain("run-dataagent-graph-sidecar-smoke.ps1"));
        Assert.That(readme, Does.Contain("does not start Python"));
        Assert.That(readme, Does.Contain("does not install dependencies"));
        Assert.That(readme, Does.Contain("already running sidecar"));
        Assert.That(readme, Does.Contain("SSE is deferred"));
        Assert.That(doc, Does.Contain("DataAgent V3.4"));
        Assert.That(doc, Does.Contain("manual live smoke"));
        Assert.That(doc, Does.Contain("already running sidecar"));
        Assert.That(doc, Does.Contain("default tests do not call a live sidecar"));
        Assert.That(doc, Does.Contain("QChat"));
    });
}
```

- [ ] **Step 2: Run the test and verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeDevSidecarStubTests" -v:minimal
```

Expected: FAIL because `tools/run-dataagent-graph-sidecar-smoke.ps1` and `docs/dataagent/dataagent-v3.4-dev-sidecar-live-smoke-harness.md` do not exist.

- [ ] **Step 3: Commit the RED test only if your workflow requires test commits**

Preferred for this repo: do not commit RED alone. Keep it staged/unstaged and continue to Task 2.

---

### Task 2: Implement The PowerShell Endpoint-Only Smoke Script

**Files:**
- Create: `tools/run-dataagent-graph-sidecar-smoke.ps1`

- [ ] **Step 1: Create the script skeleton**

Create `tools/run-dataagent-graph-sidecar-smoke.ps1` with strict mode, parameters, and the required non-startup markers:

```powershell
Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

param(
    [string]$BaseUri = "http://127.0.0.1:8765",
    [int]$TimeoutMs = 2000
)

# V3.4 smoke boundary markers:
# manual_only=true
# starts_runtime=false
# installs_dependencies=false
# loopback_only=true
# default_tests_live_runtime=false

$script:PassedChecks = 0
$script:FailedChecks = 0
```

- [ ] **Step 2: Add reporting helpers**

Append:

```powershell
function Write-Pass {
    param([string]$Name, [string]$Detail = "")

    $script:PassedChecks++
    if ([string]::IsNullOrWhiteSpace($Detail)) {
        Write-Output ("PASS {0}" -f $Name)
        return
    }

    Write-Output ("PASS {0} {1}" -f $Name, $Detail)
}

function Write-Fail {
    param([string]$Name, [string]$Detail)

    $script:FailedChecks++
    Write-Output ("FAIL {0} {1}" -f $Name, $Detail)
}

function Complete-Smoke {
    Write-Output ("Summary: {0} passed, {1} failed" -f $script:PassedChecks, $script:FailedChecks)
    if ($script:FailedChecks -gt 0) {
        exit 1
    }

    exit 0
}
```

- [ ] **Step 3: Add loopback URI validation**

Append:

```powershell
function Assert-LoopbackBaseUri {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw "BaseUri is required."
    }

    $uri = $null
    if ([System.Uri]::TryCreate($Value.TrimEnd('/'), [System.UriKind]::Absolute, [ref]$uri) -eq $false) {
        throw "BaseUri must be an absolute URI."
    }

    if ($uri.Scheme -ne "http" -and $uri.Scheme -ne "https") {
        throw "BaseUri must use http or https."
    }

    $allowedHosts = @("127.0.0.1", "localhost")
    if ($allowedHosts -notcontains $uri.Host) {
        throw "BaseUri must target loopback host 127.0.0.1 or localhost."
    }

    if ($uri.Host -eq "0.0.0.0") {
        throw "BaseUri must not target wildcard host 0.0.0.0."
    }

    return $uri
}
```

- [ ] **Step 4: Add fixture and authority helpers**

Append:

```powershell
function New-SmokeHandshakeRequest {
    [ordered]@{
        RequestId = "graph-sidecar-smoke-session-1-turn-1"
        SessionId = "graph-sidecar-smoke-session-1"
        TurnId = "turn-1"
        CallerId = "owner"
        GoalOrQuestion = "Which readiness gates should the dev sidecar suggest?"
        ScenarioContextSummary = "scenario_context=dev_sidecar_smoke"
        RouteScope = "route_present=true;route_allows_query=true;route_reason_code=route_allowed"
        QueryConstraints = "status=Active;executed_sql=false;terminal=false"
        NodeManifests = @(
            [ordered]@{
                NodeName = "scenario_knowledge"
                Purpose = "Read scenario context"
                AllowedToolNames = @("dataagent.scenario_context.read")
                DeniedCapabilityMarkers = @("sql.execute", "qchat", "browser", "file", "checkpoint.write")
                InputShape = "scenario_context"
                OutputShape = "scenario_summary"
                BusinessTerms = @("readiness", "scenario")
                SafetyNotes = "No SQL or runtime side effects"
            },
            [ordered]@{
                NodeName = "query_planner"
                Purpose = "Suggest read-only query plan"
                AllowedToolNames = @("dataagent.query_plan.propose")
                DeniedCapabilityMarkers = @("sql.execute", "qchat", "browser", "file", "checkpoint.write")
                InputShape = "question"
                OutputShape = "query_plan"
                BusinessTerms = @("planner", "readiness")
                SafetyNotes = "No SQL execution authority"
            },
            [ordered]@{
                NodeName = "diagnostics_router"
                Purpose = "Suggest diagnostics route"
                AllowedToolNames = @("dataagent.diagnostics.progress.read")
                DeniedCapabilityMarkers = @("sql.execute", "qchat", "browser", "file", "checkpoint.write")
                InputShape = "diagnostics_request"
                OutputShape = "diagnostics_hint"
                BusinessTerms = @("diagnostics", "progress")
                SafetyNotes = "No owner diagnostics publishing authority"
            }
        )
        NoSqlAuthority = $true
        ReadOnly = $true
        FallbackAvailable = $true
        TraceBudgetChars = 1800
        ProgressBudget = 16
    }
}

function Test-ForbiddenToolName {
    param([string]$ToolName)

    if ([string]::IsNullOrWhiteSpace($ToolName)) {
        return $true
    }

    $forbiddenMarkers = @(
        "qchat",
        "qq",
        "browser",
        "file",
        "rag.manage",
        "checkpoint.write",
        "dataagent.query.execute_readonly"
    )

    foreach ($marker in $forbiddenMarkers) {
        if ($ToolName.IndexOf($marker, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return $true
        }
    }

    return $false
}

function Test-ReservedProgressFacts {
    param($Facts)

    if ($null -eq $Facts) {
        return $false
    }

    $reserved = @("source", "node", "request_id")
    foreach ($key in $Facts.PSObject.Properties.Name) {
        foreach ($reservedKey in $reserved) {
            if ([string]::Equals($key, $reservedKey, [System.StringComparison]::OrdinalIgnoreCase)) {
                return $true
            }
        }
    }

    return $false
}
```

- [ ] **Step 5: Add response validation helpers**

Append:

```powershell
function Assert-PropertyPresent {
    param($Object, [string]$Name)

    if ($null -eq $Object -or $Object.PSObject.Properties.Name -notcontains $Name) {
        throw ("missing property {0}" -f $Name)
    }
}

function Test-HandshakeResponse {
    param($Response, [string]$ExpectedRequestId)

    Assert-PropertyPresent $Response "RequestId"
    Assert-PropertyPresent $Response "Accepted"
    Assert-PropertyPresent $Response "NoSqlAuthority"
    Assert-PropertyPresent $Response "ReadOnly"
    Assert-PropertyPresent $Response "FallbackRequired"
    Assert-PropertyPresent $Response "RequestsCheckpointMutation"
    Assert-PropertyPresent $Response "RequestsVisibleText"
    Assert-PropertyPresent $Response "SelectedNodes"
    Assert-PropertyPresent $Response "NodeProgress"
    Assert-PropertyPresent $Response "RequestedToolNames"

    if ($Response.RequestId -ne $ExpectedRequestId) {
        throw ("RequestId mismatch: expected {0}, got {1}" -f $ExpectedRequestId, $Response.RequestId)
    }

    if ($Response.Accepted -ne $true) {
        throw "Accepted must be true."
    }

    if ($Response.NoSqlAuthority -ne $true -or $Response.ReadOnly -ne $true) {
        throw "Response must preserve NoSqlAuthority=true and ReadOnly=true."
    }

    if ($Response.FallbackRequired -ne $false) {
        throw "FallbackRequired must be false for accepted smoke response."
    }

    if ($Response.RequestsCheckpointMutation -ne $false -or $Response.RequestsVisibleText -ne $false) {
        throw "Response must not request checkpoint mutation or visible text."
    }

    if (@($Response.SelectedNodes).Count -le 0) {
        throw "SelectedNodes must be non-empty."
    }

    if (@($Response.NodeProgress).Count -le 0) {
        throw "NodeProgress must be non-empty."
    }

    foreach ($toolName in @($Response.RequestedToolNames)) {
        if (Test-ForbiddenToolName ([string]$toolName)) {
            throw ("RequestedToolNames contains forbidden authority marker: {0}" -f $toolName)
        }
    }

    foreach ($progress in @($Response.NodeProgress)) {
        Assert-PropertyPresent $progress "NodeName"
        Assert-PropertyPresent $progress "Status"
        Assert-PropertyPresent $progress "ReasonCode"
        if (Test-ReservedProgressFacts $progress.Facts) {
            throw "NodeProgress Facts must not include reserved C# stamped keys source, node, or request_id."
        }
    }
}
```

- [ ] **Step 6: Add HTTP and NDJSON helpers**

Append:

```powershell
function Join-SidecarUri {
    param([System.Uri]$Base, [string]$Path)

    return [System.Uri]::new($Base, $Path)
}

function Invoke-SidecarRequest {
    param(
        [string]$Method,
        [System.Uri]$Uri,
        [object]$Body = $null,
        [int]$TimeoutSeconds
    )

    $parameters = @{
        Method = $Method
        Uri = $Uri
        TimeoutSec = $TimeoutSeconds
        UseBasicParsing = $true
    }

    if ($null -ne $Body) {
        $parameters.Body = ($Body | ConvertTo-Json -Depth 16 -Compress)
        $parameters.ContentType = "application/json"
    }

    Invoke-WebRequest @parameters
}

function ConvertFrom-StrictJson {
    param([string]$Json)

    if ([string]::IsNullOrWhiteSpace($Json)) {
        throw "JSON payload is empty."
    }

    $Json | ConvertFrom-Json
}

function Test-NdjsonStream {
    param([string]$Body, [string]$ExpectedRequestId)

    $lines = @($Body -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($lines.Count -eq 0) {
        throw "NDJSON response had no events."
    }

    $progressCount = 0
    $finalCount = 0
    $seenFinal = $false

    foreach ($line in $lines) {
        $event = ConvertFrom-StrictJson $line
        Assert-PropertyPresent $event "Kind"

        if ($event.Kind -ne "Progress" -and $event.Kind -ne "FinalResponse") {
            throw ("invalid NDJSON event Kind: {0}" -f $event.Kind)
        }

        if ($seenFinal) {
            throw "No event may appear after FinalResponse."
        }

        if ($event.Kind -eq "Progress") {
            if ($event.PSObject.Properties.Name -notcontains "Progress" -or $event.PSObject.Properties.Name -contains "Response") {
                throw "Progress event must contain Progress and must not contain Response."
            }

            Assert-PropertyPresent $event.Progress "NodeName"
            Assert-PropertyPresent $event.Progress "Status"
            Assert-PropertyPresent $event.Progress "ReasonCode"
            if (Test-ReservedProgressFacts $event.Progress.Facts) {
                throw "Stream progress Facts must not include reserved C# stamped keys source, node, or request_id."
            }

            $progressCount++
            continue
        }

        if ($event.PSObject.Properties.Name -notcontains "Response" -or $event.PSObject.Properties.Name -contains "Progress") {
            throw "FinalResponse event must contain Response and must not contain Progress."
        }

        $seenFinal = $true
        $finalCount++
        Test-HandshakeResponse $event.Response $ExpectedRequestId
    }

    if ($progressCount -le 0) {
        throw "Expected at least one Progress event."
    }

    if ($finalCount -ne 1) {
        throw ("Expected exactly one FinalResponse event, got {0}." -f $finalCount)
    }

    [pscustomobject]@{
        ProgressCount = $progressCount
        FinalResponse = $true
    }
}
```

- [ ] **Step 7: Add main execution flow**

Append:

```powershell
Write-Output "DataAgent graph sidecar live smoke"

try {
    if ($TimeoutMs -le 0) {
        throw "TimeoutMs must be greater than zero."
    }

    $base = Assert-LoopbackBaseUri $BaseUri
    $timeoutSeconds = [Math]::Max(1, [int][Math]::Ceiling($TimeoutMs / 1000.0))
    $request = New-SmokeHandshakeRequest
    Write-Output ("BaseUri: {0}" -f $base.AbsoluteUri.TrimEnd('/'))

    try {
        $healthResponse = Invoke-SidecarRequest -Method "GET" -Uri (Join-SidecarUri $base "/health") -TimeoutSeconds $timeoutSeconds
        $health = ConvertFrom-StrictJson $healthResponse.Content
        if ($health.status -ne "ok" -or $health.runtime -ne "dev_sidecar") {
            throw ("expected status=ok runtime=dev_sidecar, got status={0} runtime={1}" -f $health.status, $health.runtime)
        }

        Write-Pass "health" "status=ok runtime=dev_sidecar"
    }
    catch {
        Write-Fail "health" ("{0}. Start the sidecar manually using tools/dataagent-graph-sidecar/README.md." -f $_.Exception.Message)
        Complete-Smoke
    }

    try {
        $handshakeResponse = Invoke-SidecarRequest -Method "POST" -Uri (Join-SidecarUri $base "/handshake") -Body $request -TimeoutSeconds $timeoutSeconds
        $handshake = ConvertFrom-StrictJson $handshakeResponse.Content
        Test-HandshakeResponse $handshake $request.RequestId
        Write-Pass "handshake" ("accepted=true selected_nodes={0} progress={1}" -f @($handshake.SelectedNodes).Count, @($handshake.NodeProgress).Count)
    }
    catch {
        Write-Fail "handshake" $_.Exception.Message
    }

    try {
        $streamResponse = Invoke-SidecarRequest -Method "POST" -Uri (Join-SidecarUri $base "/handshake-stream") -Body $request -TimeoutSeconds $timeoutSeconds
        $contentType = [string]$streamResponse.Headers["Content-Type"]
        if ($contentType.IndexOf("text/event-stream", [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw "SSE text/event-stream is deferred and must not be returned by V3.4 smoke."
        }

        if ($contentType.IndexOf("application/x-ndjson", [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
            throw ("expected application/x-ndjson content type, got {0}" -f $contentType)
        }

        $stream = Test-NdjsonStream $streamResponse.Content $request.RequestId
        Write-Pass "handshake-stream" ("progress={0} final_response={1}" -f $stream.ProgressCount, $stream.FinalResponse.ToString().ToLowerInvariant())
    }
    catch {
        Write-Fail "handshake-stream" $_.Exception.Message
    }
}
catch {
    Write-Fail "setup" $_.Exception.Message
}

Complete-Smoke
```

- [ ] **Step 8: Run focused static test and verify GREEN for script markers after docs are added**

Do not run yet if Task 3 docs are not present. Continue to Task 3 first.

---

### Task 3: Add V3.4 Documentation

**Files:**
- Create: `docs/dataagent/dataagent-v3.4-dev-sidecar-live-smoke-harness.md`
- Modify: `tools/dataagent-graph-sidecar/README.md`

- [ ] **Step 1: Create the V3.4 doc**

Create `docs/dataagent/dataagent-v3.4-dev-sidecar-live-smoke-harness.md`:

```markdown
# DataAgent V3.4 Dev Sidecar Live Smoke Harness

DataAgent V3.4 adds a manual live smoke harness for the graph dev sidecar. It validates an already running local sidecar over `/health`, `/handshake`, and `/handshake-stream`.

The harness is manual by design. Default tests do not call a live sidecar, bind a port, start Python, start uvicorn, install dependencies, create a virtual environment, or require network access.

## Run

Start the sidecar manually first:

```powershell
cd tools\dataagent-graph-sidecar
.\.venv\Scripts\python.exe -m uvicorn app:app --host 127.0.0.1 --port 8765
```

Then run the smoke harness from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File tools\run-dataagent-graph-sidecar-smoke.ps1 `
  -BaseUri "http://127.0.0.1:8765" `
  -TimeoutMs 2000
```

Expected success:

```text
DataAgent graph sidecar live smoke
BaseUri: http://127.0.0.1:8765
PASS health status=ok runtime=dev_sidecar
PASS handshake accepted=true selected_nodes=3 progress=3
PASS handshake-stream progress=3 final_response=true
Summary: 3 passed, 0 failed
```

## What It Checks

- `/health` returns `status=ok` and `runtime=dev_sidecar`.
- `/handshake` returns an accepted graph handshake response with safe authority flags.
- `/handshake-stream` returns `application/x-ndjson` with progress events followed by exactly one final response.
- Progress facts do not include reserved C# stamped keys: `source`, `node`, or `request_id`.
- Requested tools do not include SQL execution, QChat, QQ, browser, file, RAG management, or checkpoint write authority.

## Failure Meaning

A health failure usually means the sidecar is not running or `-BaseUri` points to the wrong port.

A handshake failure means the live stub no longer matches the C# graph handshake authority contract.

A handshake-stream failure means the NDJSON stream no longer matches the V3.3 envelope contract.

## Boundaries

The smoke script does not start Python, create venvs, install dependencies, launch uvicorn, bind ports, or manage background processes. It only sends HTTP requests to a loopback endpoint that the developer already started manually.

The sidecar has no SQL, checkpoint, Tool Broker, QChat, QQ, file, browser, desktop, plugin, diagnostics, evidence, audit, or visible-text authority. C# remains the authority boundary.

SSE is deferred. V3.4 does not implement `text/event-stream`, event ids, heartbeats, reconnect behavior, or browser-facing streaming semantics.
```

- [ ] **Step 2: Update the sidecar README**

Append this section to `tools/dataagent-graph-sidecar/README.md` after the V3.3 section:

```markdown
## V3.4 Manual Live Smoke

V3.4 adds a manual smoke harness for developers who have already started the local sidecar. The smoke script validates `/health`, `/handshake`, and `/handshake-stream` against a loopback endpoint:

```powershell
powershell -ExecutionPolicy Bypass -File tools\run-dataagent-graph-sidecar-smoke.ps1 `
  -BaseUri "http://127.0.0.1:8765" `
  -TimeoutMs 2000
```

Run the command from the repository root while the sidecar is already running.

The smoke script does not start Python, create a venv, install dependencies, run `pip install`, launch uvicorn, bind ports, or manage background processes. It fails closed if the sidecar is not running or if `-BaseUri` is not loopback.

The stream smoke expects `application/x-ndjson` from `/handshake-stream`. SSE is deferred; the smoke script does not parse `text/event-stream`, event ids, heartbeats, or reconnect behavior.

Default tests do not call this live smoke script and do not require Python, FastAPI, uvicorn, a live port, network access, QChat, QQ, PostgreSQL, browser automation, model calls, or a live sidecar.
```

- [ ] **Step 3: Run the V3.4 stub/docs static test**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeDevSidecarStubTests" -v:minimal
```

Expected: PASS for all `DataAgentGraphHandshakeDevSidecarStubTests`.

- [ ] **Step 4: Commit script and docs**

Run:

```powershell
git add tools\run-dataagent-graph-sidecar-smoke.ps1 tools\dataagent-graph-sidecar\README.md docs\dataagent\dataagent-v3.4-dev-sidecar-live-smoke-harness.md Tests\Alife.Test.DataAgent\DataAgentGraphHandshakeDevSidecarStubTests.cs
git commit -m "Add DataAgent V3.4 sidecar smoke harness"
```

---

### Task 4: Add Static Readiness Marker And Count Guards

**Files:**
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV210ReadinessTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV216ReadinessTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV30ReadinessTests.cs`
- Modify: `tools/check-dataagent-readiness.ps1`

- [ ] **Step 1: Add failing readiness assertions**

In `DataAgentReadinessTests.CoreReadinessChecksAllPass`, keep the dynamic count unchanged:

```csharp
Assert.That(checks, Has.Count.EqualTo(75));
```

Do not add `GraphHandshakeDevSidecarLiveSmokeHarnessPresent` to `CheckCore`, because V3.4 is static/manual-only and must not imply runtime availability.

In `ReadinessScriptDefaultModeExitsZeroAndPrintsSummary`, update summary expectation:

```csharp
Assert.That(GetSummaryLines(result.StandardOutput), Is.EqualTo(new[]
{
    "  Summary: 90 required passed, 0 required missing"
}));
```

In `ReadinessScriptProtectsRequiredCheckCount`, update:

```csharp
Assert.That(script, Does.Contain("$expectedRequired = 90"));
```

Add this test near the existing V3.3 static readiness marker test:

```csharp
[Test]
public void StaticReadinessScriptContainsV34LiveSmokeHarnessMarkers()
{
    string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
    string script = File.ReadAllText(Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1"));
    string declaration = FindNewCheckDeclaration(script, "GraphHandshakeDevSidecarLiveSmokeHarnessPresent");

    Assert.Multiple(() =>
    {
        Assert.That(declaration, Does.Contain("run-dataagent-graph-sidecar-smoke.ps1"));
        Assert.That(declaration, Does.Contain("/health"));
        Assert.That(declaration, Does.Contain("/handshake"));
        Assert.That(declaration, Does.Contain("/handshake-stream"));
        Assert.That(declaration, Does.Contain("application/x-ndjson"));
        Assert.That(declaration, Does.Contain("manual_only=true"));
        Assert.That(declaration, Does.Contain("starts_runtime=false"));
        Assert.That(declaration, Does.Contain("installs_dependencies=false"));
        Assert.That(declaration, Does.Contain("loopback_only=true"));
        Assert.That(declaration, Does.Contain("default_tests_live_runtime=false"));
        Assert.That(declaration, Does.Contain("sse_deferred=true"));
        Assert.That(declaration, Does.Contain("qchat_boundary=true"));
    });
}
```

- [ ] **Step 2: Update legacy count guard tests**

In these files, replace `$expectedRequired = 89` with `$expectedRequired = 90`:

```text
Tests/Alife.Test.DataAgent/DataAgentV210ReadinessTests.cs
Tests/Alife.Test.DataAgent/DataAgentV216ReadinessTests.cs
Tests/Alife.Test.DataAgent/DataAgentV30ReadinessTests.cs
```

Do not change QChat expected count.

- [ ] **Step 3: Run readiness tests and verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests|FullyQualifiedName~DataAgentV210ReadinessTests|FullyQualifiedName~DataAgentV216ReadinessTests|FullyQualifiedName~DataAgentV30ReadinessTests" -v:minimal
```

Expected: FAIL because `tools/check-dataagent-readiness.ps1` still reports `$expectedRequired = 89` and lacks `GraphHandshakeDevSidecarLiveSmokeHarnessPresent`.

- [ ] **Step 4: Add the static readiness check**

In `tools/check-dataagent-readiness.ps1`, add this `New-Check` immediately after `GraphHandshakeDevSidecarStreamingTransportPresent`:

```powershell
    New-Check -Group "Store" -Name "GraphHandshakeDevSidecarLiveSmokeHarnessPresent" -Passed ((Test-FileMarker "tools/run-dataagent-graph-sidecar-smoke.ps1" @("DataAgent graph sidecar live smoke", "/health", "/handshake", "/handshake-stream", "application/x-ndjson", "Assert-LoopbackBaseUri", "Invoke-SidecarRequest", "Test-HandshakeResponse", "Test-NdjsonStream", "manual_only=true", "starts_runtime=false", "installs_dependencies=false", "loopback_only=true", "default_tests_live_runtime=false")) -and (Test-FileOmitsMarker "tools/run-dataagent-graph-sidecar-smoke.ps1" @("Start-Process", "pip install", "python -m venv", "uvicorn app:app", "text/event-stream", "EventSource")) -and (Test-FileMarker "tools/dataagent-graph-sidecar/README.md" @("V3.4", "run-dataagent-graph-sidecar-smoke.ps1", "already running", "does not start Python", "does not install dependencies", "SSE is deferred")) -and (Test-FileMarker "docs/dataagent/dataagent-v3.4-dev-sidecar-live-smoke-harness.md" @("DataAgent V3.4", "manual live smoke", "already running", "default tests do not call a live sidecar", "QChat", "SSE is deferred"))) -Detail "V3.4 graph handshake dev sidecar live smoke harness markers manual_only=true starts_runtime=false installs_dependencies=false loopback_only=true handshake=true ndjson_stream=true sse_deferred=true qchat_boundary=true default_tests_live_runtime=false"
```

Then update:

```powershell
$expectedRequired = 90
```

- [ ] **Step 5: Run readiness tests and verify GREEN**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests|FullyQualifiedName~DataAgentV210ReadinessTests|FullyQualifiedName~DataAgentV216ReadinessTests|FullyQualifiedName~DataAgentV30ReadinessTests" -v:minimal
```

Expected: PASS.

- [ ] **Step 6: Run readiness script**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected:

```text
PASS     GraphHandshakeDevSidecarLiveSmokeHarnessPresent
Summary: 90 required passed, 0 required missing
```

- [ ] **Step 7: Commit readiness guards**

Run:

```powershell
git add tools\check-dataagent-readiness.ps1 Tests\Alife.Test.DataAgent\DataAgentReadinessTests.cs Tests\Alife.Test.DataAgent\DataAgentV210ReadinessTests.cs Tests\Alife.Test.DataAgent\DataAgentV216ReadinessTests.cs Tests\Alife.Test.DataAgent\DataAgentV30ReadinessTests.cs
git commit -m "Add DataAgent V3.4 smoke readiness"
```

---

### Task 5: Verify QChat Boundary And Final Solution

**Files:**
- Read only unless a verification failure identifies a necessary test guard.
- Do not modify `sources/Alife.Function/Alife.Function.QChat/**`.
- Do not modify `tools/check-qchat-engineering-map.ps1` required count.

- [ ] **Step 1: Run focused V3.4 DataAgent tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeDevSidecarStubTests|FullyQualifiedName~DataAgentReadinessTests|FullyQualifiedName~DataAgentV210ReadinessTests|FullyQualifiedName~DataAgentV216ReadinessTests|FullyQualifiedName~DataAgentV30ReadinessTests" -v:minimal
```

Expected: PASS.

- [ ] **Step 2: Run DataAgent readiness script**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected:

```text
Summary: 90 required passed, 0 required missing
```

- [ ] **Step 3: Run QChat engineering map**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected:

```text
Summary: 63 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 4: Run QChat boundary scan**

Run:

```powershell
rg -n "DataAgentGraphHandshakeStream|DataAgentGraphSidecarProgress|DataAgentGraphHandshake" sources\Alife.Function\Alife.Function.QChat
```

Expected: no output; exit code `1` is expected and means no matches.

- [ ] **Step 5: Run diff hygiene**

Run:

```powershell
git diff --check
```

Expected: exit code `0`.

- [ ] **Step 6: Build solution**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" build Alife.slnx --no-restore -v:minimal
```

Expected: exit code `0`; existing QChat CS0067 warnings are acceptable.

- [ ] **Step 7: Run full solution tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore --no-build -m:1 -v:minimal
```

Expected: exit code `0`; live sidecar tests are not required and no Python/uvicorn process is started.

- [ ] **Step 8: Confirm no live runtime behavior slipped into default tests**

Run:

```powershell
rg -n "run-dataagent-graph-sidecar-smoke|Invoke-WebRequest|127\.0\.0\.1:8765|uvicorn|Start-Process" Tests sources
```

Expected:

- No default test invokes the smoke script.
- No production source starts uvicorn or `Start-Process`.
- Static tests may mention script marker names only.

- [ ] **Step 9: Final commit only if Task 5 needed fixes**

If Task 5 required no changes, do not create a commit. If it required changes, commit narrowly:

```powershell
git add tools\run-dataagent-graph-sidecar-smoke.ps1 docs\dataagent\dataagent-v3.4-dev-sidecar-live-smoke-harness.md tools\dataagent-graph-sidecar\README.md tools\check-dataagent-readiness.ps1 Tests\Alife.Test.DataAgent\DataAgentGraphHandshakeDevSidecarStubTests.cs Tests\Alife.Test.DataAgent\DataAgentReadinessTests.cs Tests\Alife.Test.DataAgent\DataAgentV210ReadinessTests.cs Tests\Alife.Test.DataAgent\DataAgentV216ReadinessTests.cs Tests\Alife.Test.DataAgent\DataAgentV30ReadinessTests.cs
git commit -m "Harden DataAgent V3.4 smoke verification"
```

---

## Manual Live Smoke Optional Check

Do not require this for default completion unless the user explicitly asks to run live Python.

If the user asks, run sidecar manually in a separate shell outside default test flow:

```powershell
cd tools\dataagent-graph-sidecar
.\.venv\Scripts\python.exe -m uvicorn app:app --host 127.0.0.1 --port 8765
```

Then from repo root:

```powershell
powershell -ExecutionPolicy Bypass -File tools\run-dataagent-graph-sidecar-smoke.ps1 -BaseUri "http://127.0.0.1:8765" -TimeoutMs 2000
```

Expected:

```text
DataAgent graph sidecar live smoke
BaseUri: http://127.0.0.1:8765
PASS health status=ok runtime=dev_sidecar
PASS handshake accepted=true selected_nodes=3 progress=3
PASS handshake-stream progress=3 final_response=true
Summary: 3 passed, 0 failed
```

Stop the manually started uvicorn process after the check. Do not leave a background sidecar running.

---

## Self-Review

- Spec coverage: This plan covers the endpoint-only PowerShell script, loopback URI validation, `/health`, `/handshake`, `/handshake-stream`, NDJSON validation, authority checks, docs, README, static readiness marker, required count `90`, unchanged dynamic core count `75`, unchanged QChat engineering map count `63`, QChat source boundary, and default no-live-runtime rule.
- Red-flag scan: The plan contains no unresolved marker text and gives exact files, snippets, commands, and expected results.
- Type consistency: The plan uses existing names from the repository: `DataAgentGraphHandshakeDevSidecarStubTests`, `DataAgentReadinessTests`, `GraphHandshakeDevSidecarStreamingTransportPresent`, `GraphHandshakeDevSidecarLiveSmokeHarnessPresent`, and `FindNewCheckDeclaration`.
- Non-goal consistency: The plan does not implement SSE, Python startup, venv creation, pip installation, uvicorn process supervision, live NUnit tests, QChat production imports, or production runtime changes.
- Verification coverage: Focused tests, readiness script, QChat map, QChat source scan, diff hygiene, build, and full solution tests are required before completion.
