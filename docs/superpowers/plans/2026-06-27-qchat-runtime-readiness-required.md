# QChat Runtime Readiness Required Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Promote QChat runtime readiness from the final optional engineering-map item to a required, tested gate with a stable default mode and explicit live strict mode.

**Architecture:** Keep this slice script-first: `tools/check-qchat-runtime-readiness.ps1` becomes the executable readiness gate, `tools/check-qchat-engineering-map.ps1` promotes it to required, and `Tests/Alife.Test.QChat` locks the contract. The script supports injected voice-root and port parameters so tests can prove strict failure without depending on the developer machine's actual GPT-SoVITS, Agnes key, or local endpoints.

**Tech Stack:** PowerShell 5+, NUnit, .NET 9 test runner, existing QChat engineering-map script.

---

## File Structure

- Modify: `tools/check-qchat-runtime-readiness.ps1`
  - Owns human-readable and JSON runtime readiness output.
  - Supports default, `-Live`, `-Strict`, and `-Json` modes.
  - Keeps stable field names already used by engineering checks.
- Modify: `tools/check-qchat-engineering-map.ps1`
  - Promotes `Runtime readiness script` from optional to required.
  - Adds markers that prove live strict mode and failure semantics exist.
- Create: `Tests/Alife.Test.QChat/QChatRuntimeReadinessScriptTests.cs`
  - Verifies the script contract and strict failure behavior.
- Modify: `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`
  - Adds `Runtime readiness script` to the required-v2 list so future regressions cannot reclassify it as optional.

---

### Task 1: Add Failing Runtime Readiness Script Contract Tests

**Files:**
- Create: `Tests/Alife.Test.QChat/QChatRuntimeReadinessScriptTests.cs`
- Modify: `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`

- [ ] **Step 1: Create the failing script contract test file**

Create `Tests/Alife.Test.QChat/QChatRuntimeReadinessScriptTests.cs` with this content:

```csharp
using NUnit.Framework;
using System.Diagnostics;
using System.IO;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatRuntimeReadinessScriptTests
{
    [Test]
    public void EngineeringMapDeclaresRuntimeReadinessAsRequired()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string engineeringMapPath = Path.Combine(repoRoot, "tools", "check-qchat-engineering-map.ps1");
        string script = File.ReadAllText(engineeringMapPath);

        string declaration = FindAddCheckDeclaration(script, "Runtime readiness script");

        Assert.Multiple(() =>
        {
            Assert.That(declaration, Is.Not.Empty);
            Assert.That(declaration, Does.Not.Contain("-Required $false"));
            Assert.That(declaration, Does.Contain("QChat Runtime Readiness"));
            Assert.That(declaration, Does.Contain("-Live"));
            Assert.That(declaration, Does.Contain("-Strict"));
            Assert.That(declaration, Does.Contain("exit 1"));
        });
    }

    [Test]
    public void RuntimeReadinessScriptKeepsStableContractMarkers()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-qchat-runtime-readiness.ps1");
        string script = File.ReadAllText(scriptPath);

        string[] markers =
        [
            "QChat Runtime Readiness",
            "AgnesVisionKeyConfigured",
            "XiayuTts9880Reachable",
            "MixuTts9881Reachable",
            "XiayuZhRef",
            "XiayuJaRef",
            "MixuZhRef",
            "MixuJaRef",
            "-Live",
            "-Strict",
            "-Json",
            "exit 1"
        ];

        Assert.Multiple(() =>
        {
            foreach (string marker in markers)
                Assert.That(script, Does.Contain(marker), $"Missing marker '{marker}'.");
        });
    }

    [Test]
    public void RuntimeReadinessScriptDefaultModeExitsZeroAndPrintsSummary()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-qchat-runtime-readiness.ps1");

        ScriptResult result = RunPowerShellScript(scriptPath);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("QChat Runtime Readiness"));
            Assert.That(result.StandardOutput, Does.Contain("[Vision]"));
            Assert.That(result.StandardOutput, Does.Contain("[Voice]"));
            Assert.That(result.StandardOutput, Does.Contain("Summary:"));
        });
    }

    [Test]
    public void RuntimeReadinessScriptLiveStrictFailsWhenReferenceAudioRootIsMissing()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-qchat-runtime-readiness.ps1");
        string missingVoiceRoot = Path.Combine(TestContext.CurrentContext.WorkDirectory, "missing-qchat-voice-root");

        if (Directory.Exists(missingVoiceRoot))
            Directory.Delete(missingVoiceRoot, recursive: true);

        ScriptResult result = RunPowerShellScript(
            scriptPath,
            "-Live",
            "-Strict",
            "-VoiceRootPath",
            missingVoiceRoot);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Not.EqualTo(0));
            Assert.That(result.StandardOutput, Does.Contain("QChat Runtime Readiness"));
            Assert.That(result.StandardOutput, Does.Contain("MISSING"));
            Assert.That(result.StandardOutput, Does.Contain("reference audio"));
        });
    }

    static ScriptResult RunPowerShellScript(string scriptPath, params string[] arguments)
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
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);

        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start PowerShell.");

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();

        if (process.WaitForExit(15000) == false)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Runtime readiness script did not exit within 15 seconds.");
        }

        return new ScriptResult(process.ExitCode, stdout, stderr);
    }

    static string FindAddCheckDeclaration(string script, string checkName)
    {
        string marker = $"-Name \"{checkName}\"";
        int nameIndex = script.IndexOf(marker, StringComparison.Ordinal);
        if (nameIndex < 0)
            return string.Empty;

        int start = script.LastIndexOf("Add-Check", nameIndex, StringComparison.Ordinal);
        if (start < 0)
            return string.Empty;

        int next = script.IndexOf("Add-Check", nameIndex + marker.Length, StringComparison.Ordinal);
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
                Directory.Exists(Path.Combine(directory.FullName, "tools")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test directory.");
    }

    readonly record struct ScriptResult(int ExitCode, string StandardOutput, string StandardError);
}
```

- [ ] **Step 2: Add runtime readiness to required-v2 test list**

Modify `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs` so `RequiredV2Checks` includes `Runtime readiness script`:

```csharp
static readonly string[] RequiredV2Checks =
[
    "Vision readiness tests",
    "Voice warmup coordinator tests",
    "Model reply loop live tests",
    "Prompt leak contract tests",
    "Runtime readiness script",
    "Voice warmup retry coordinator",
    "Semantic settle window contract tests",
    "Voice warmup contract tests",
    "XiaYu self-state machine",
    "Persona intensity prompt formatter",
    "Persona frame prompt",
    "XiaYu private state prompt",
    "Semantic window summary prompt"
];
```

- [ ] **Step 3: Run the new tests and verify they fail**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests/Alife.Test.QChat/Alife.Test.QChat.csproj --no-restore --no-build --filter "QChatRuntimeReadinessScriptTests|QChatEngineeringMapRequiredV2Tests" -v:minimal
```

Expected result before implementation:

```text
Failed! - Failed: 3 or more
```

Expected failure reasons:

```text
Runtime readiness script declaration still contains -Required $false
Missing marker 'QChat Runtime Readiness'
Missing marker '-Live'
Missing marker '-Strict'
Missing marker '-Json'
Missing marker 'exit 1'
```

- [ ] **Step 4: Commit the failing tests**

Run:

```powershell
git add Tests/Alife.Test.QChat/QChatRuntimeReadinessScriptTests.cs Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs
git commit -m "test: require QChat runtime readiness gate"
```

---

### Task 2: Replace Runtime Readiness Script With Default And Live Strict Modes

**Files:**
- Modify: `tools/check-qchat-runtime-readiness.ps1`
- Test: `Tests/Alife.Test.QChat/QChatRuntimeReadinessScriptTests.cs`

- [ ] **Step 1: Replace the readiness script**

Replace the entire content of `tools/check-qchat-runtime-readiness.ps1` with:

```powershell
param(
    [switch]$Live,
    [switch]$Strict,
    [switch]$Json,
    [string]$VoiceRootPath = 'D:\Alife\Runtime\TTS\voices',
    [string]$ComputerName = '127.0.0.1',
    [int]$XiayuTtsPort = 9880,
    [int]$MixuTtsPort = 9881,
    [string]$AgnesVisionApiKey
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Resolve-AgnesVisionApiKey {
    param([string]$ExplicitKey)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitKey)) {
        return $ExplicitKey
    }

    $userKey = [Environment]::GetEnvironmentVariable('ALIFE_AGNES_VISION_API_KEY', [EnvironmentVariableTarget]::User)
    if (-not [string]::IsNullOrWhiteSpace($userKey)) {
        return $userKey
    }

    return [Environment]::GetEnvironmentVariable('ALIFE_AGNES_VISION_API_KEY', [EnvironmentVariableTarget]::Process)
}

function Test-PortQuiet {
    param(
        [string]$HostName,
        [int]$Port,
        [int]$TimeoutMilliseconds = 500
    )

    $client = $null
    try {
        $client = [System.Net.Sockets.TcpClient]::new()
        $connectTask = $client.ConnectAsync($HostName, $Port)
        if (-not $connectTask.Wait($TimeoutMilliseconds)) {
            return $false
        }

        return $client.Connected
    }
    catch {
        return $false
    }
    finally {
        if ($null -ne $client) {
            $client.Dispose()
        }
    }
}

function New-ReadinessCheck {
    param(
        [string]$Group,
        [string]$Name,
        [string]$Field,
        [bool]$Value,
        [string]$PassReason,
        [string]$FailReason,
        [bool]$Required
    )

    $status = if ($Value) {
        'PASS'
    }
    elseif ($Required) {
        'MISSING'
    }
    else {
        'WARN'
    }

    $reason = if ($Value) { $PassReason } else { $FailReason }

    [pscustomobject]@{
        Group = $Group
        Name = $Name
        Field = $Field
        Value = $Value
        Status = $status
        Reason = $reason
        Required = $Required
    }
}

$visionKey = Resolve-AgnesVisionApiKey -ExplicitKey $AgnesVisionApiKey
$strictLiveRequired = [bool]($Live -and $Strict)

$xiayuZhRef = Join-Path $VoiceRootPath 'xiayu\zh\ref.wav'
$xiayuJaRef = Join-Path $VoiceRootPath 'xiayu\ja\ref.wav'
$mixuZhRef = Join-Path $VoiceRootPath 'mixu\zh\ref.wav'
$mixuJaRef = Join-Path $VoiceRootPath 'mixu\ja\ref.wav'

$AgnesVisionKeyConfigured = -not [string]::IsNullOrWhiteSpace($visionKey)
$XiayuTts9880Reachable = if ($Live) { Test-PortQuiet -HostName $ComputerName -Port $XiayuTtsPort } else { $false }
$MixuTts9881Reachable = if ($Live) { Test-PortQuiet -HostName $ComputerName -Port $MixuTtsPort } else { $false }
$XiayuZhRef = Test-Path -LiteralPath $xiayuZhRef
$XiayuJaRef = Test-Path -LiteralPath $xiayuJaRef
$MixuZhRef = Test-Path -LiteralPath $mixuZhRef
$MixuJaRef = Test-Path -LiteralPath $mixuJaRef

$checks = @(
    New-ReadinessCheck -Group 'Vision' -Name 'Agnes vision API key' -Field 'AgnesVisionKeyConfigured' -Value $AgnesVisionKeyConfigured -PassReason 'configured' -FailReason 'agnes_vision_api_key_missing' -Required $strictLiveRequired
    New-ReadinessCheck -Group 'Voice' -Name ("Xiayu TTS endpoint {0}" -f $XiayuTtsPort) -Field 'XiayuTts9880Reachable' -Value $XiayuTts9880Reachable -PassReason 'reachable' -FailReason 'xiayu_tts_endpoint_unreachable' -Required $strictLiveRequired
    New-ReadinessCheck -Group 'Voice' -Name ("Mixu TTS endpoint {0}" -f $MixuTtsPort) -Field 'MixuTts9881Reachable' -Value $MixuTts9881Reachable -PassReason 'reachable' -FailReason 'mixu_tts_endpoint_unreachable' -Required $strictLiveRequired
    New-ReadinessCheck -Group 'Voice' -Name 'Xiayu zh reference audio' -Field 'XiayuZhRef' -Value $XiayuZhRef -PassReason 'present' -FailReason 'xiayu_zh_reference_audio_missing' -Required $strictLiveRequired
    New-ReadinessCheck -Group 'Voice' -Name 'Xiayu ja reference audio' -Field 'XiayuJaRef' -Value $XiayuJaRef -PassReason 'present' -FailReason 'xiayu_ja_reference_audio_missing' -Required $strictLiveRequired
    New-ReadinessCheck -Group 'Voice' -Name 'Mixu zh reference audio' -Field 'MixuZhRef' -Value $MixuZhRef -PassReason 'present' -FailReason 'mixu_zh_reference_audio_missing' -Required $strictLiveRequired
    New-ReadinessCheck -Group 'Voice' -Name 'Mixu ja reference audio' -Field 'MixuJaRef' -Value $MixuJaRef -PassReason 'present' -FailReason 'mixu_ja_reference_audio_missing' -Required $strictLiveRequired
)

$requiredPassed = @($checks | Where-Object { $_.Required -and $_.Value }).Count
$requiredMissing = @($checks | Where-Object { $_.Required -and -not $_.Value }).Count
$warnings = @($checks | Where-Object { -not $_.Required -and -not $_.Value }).Count

$result = [pscustomobject]@{
    Mode = if ($Live) { if ($Strict) { 'live-strict' } else { 'live' } } else { 'default' }
    AgnesVisionKeyConfigured = $AgnesVisionKeyConfigured
    XiayuTts9880Reachable = $XiayuTts9880Reachable
    MixuTts9881Reachable = $MixuTts9881Reachable
    XiayuZhRef = $XiayuZhRef
    XiayuJaRef = $XiayuJaRef
    MixuZhRef = $MixuZhRef
    MixuJaRef = $MixuJaRef
    Checks = $checks
    Summary = [pscustomobject]@{
        RequiredPassed = $requiredPassed
        RequiredMissing = $requiredMissing
        Warnings = $warnings
    }
}

if ($Json) {
    $result | ConvertTo-Json -Depth 5
}
else {
    Write-Output 'QChat Runtime Readiness'
    foreach ($group in @('Vision', 'Voice')) {
        Write-Output ("[{0}]" -f $group)
        foreach ($check in ($checks | Where-Object { $_.Group -eq $group })) {
            Write-Output ("  {0,-7} {1}: {2}" -f $check.Status, $check.Name, $check.Reason)
        }
    }

    Write-Output '[Summary]'
    Write-Output ("  Summary: {0} required passed, {1} required missing, {2} warnings" -f $requiredPassed, $requiredMissing, $warnings)
}

if ($Strict -and $requiredMissing -gt 0) {
    exit 1
}

exit 0
```

- [ ] **Step 2: Run the script in default mode**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/check-qchat-runtime-readiness.ps1
```

Expected result:

```text
QChat Runtime Readiness
[Vision]
[Voice]
[Summary]
  Summary: 0 required passed, 0 required missing, N warnings
```

Expected exit code: `0`.

- [ ] **Step 3: Run the script in JSON mode**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/check-qchat-runtime-readiness.ps1 -Json
```

Expected result:

```text
"AgnesVisionKeyConfigured"
"XiayuTts9880Reachable"
"MixuTts9881Reachable"
"Summary"
```

Expected exit code: `0`.

- [ ] **Step 4: Run the new script tests**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests/Alife.Test.QChat/Alife.Test.QChat.csproj --no-restore --no-build --filter "QChatRuntimeReadinessScriptTests" -v:minimal
```

Expected result after this task:

```text
Failed! - Runtime readiness script is still optional in engineering map
```

The script contract tests should pass except the engineering-map required declaration test, which is fixed in Task 3.

- [ ] **Step 5: Commit the script implementation**

Run:

```powershell
git add tools/check-qchat-runtime-readiness.ps1
git commit -m "feat: add QChat runtime readiness strict mode"
```

---

### Task 3: Promote Runtime Readiness To Required Engineering Gate

**Files:**
- Modify: `tools/check-qchat-engineering-map.ps1`
- Modify: `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`
- Test: `Tests/Alife.Test.QChat/QChatRuntimeReadinessScriptTests.cs`

- [ ] **Step 1: Update the engineering-map runtime readiness declaration**

In `tools/check-qchat-engineering-map.ps1`, replace the current runtime readiness line:

```powershell
Add-Check -Group "Harness" -Name "Runtime readiness script" -Path "tools/check-qchat-runtime-readiness.ps1" -Patterns @("AgnesVisionKeyConfigured") -Required $false
```

with:

```powershell
Add-Check -Group "Harness" -Name "Runtime readiness script" -Path "tools/check-qchat-runtime-readiness.ps1" -Patterns @("QChat Runtime Readiness", "AgnesVisionKeyConfigured", "XiayuTts9880Reachable", "MixuTts9881Reachable", "-Live", "-Strict", "exit 1")
```

- [ ] **Step 2: Confirm required-v2 list includes runtime readiness**

Ensure `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs` still contains:

```csharp
"Runtime readiness script",
```

inside `RequiredV2Checks`.

- [ ] **Step 3: Run the focused tests**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests/Alife.Test.QChat/Alife.Test.QChat.csproj --no-restore --no-build --filter "QChatRuntimeReadinessScriptTests|QChatEngineeringMapRequiredV2Tests" -v:minimal
```

Expected result:

```text
Passed! - Failed: 0
```

- [ ] **Step 4: Run the engineering map**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/check-qchat-engineering-map.ps1
```

Expected result:

```text
PASS     Runtime readiness script: tools/check-qchat-runtime-readiness.ps1
Summary: 31 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 5: Commit the required gate promotion**

Run:

```powershell
git add tools/check-qchat-engineering-map.ps1 Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs Tests/Alife.Test.QChat/QChatRuntimeReadinessScriptTests.cs
git commit -m "feat: require QChat runtime readiness gate"
```

---

### Task 4: Full Verification And Upload Readiness

**Files:**
- Verify: `tools/check-qchat-runtime-readiness.ps1`
- Verify: `tools/check-qchat-engineering-map.ps1`
- Verify: `Alife.slnx`

- [ ] **Step 1: Run runtime readiness default mode**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/check-qchat-runtime-readiness.ps1
```

Expected result:

```text
QChat Runtime Readiness
[Summary]
  Summary: 0 required passed, 0 required missing, N warnings
```

Expected exit code: `0`.

- [ ] **Step 2: Run strict live validation as an informational machine check**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/check-qchat-runtime-readiness.ps1 -Live -Strict
```

Expected result depends on the machine:

```text
Exit 0 when Agnes key, Xiayu/Mixu TTS endpoints, and reference audio are all present.
Exit 1 with MISSING lines when any live prerequisite is absent.
```

Do not treat an exit 1 here as a code failure unless the output format is broken. This command proves the real machine is or is not ready to launch live multimodal QChat.

- [ ] **Step 3: Run engineering map required gate**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/check-qchat-engineering-map.ps1
```

Expected result:

```text
Summary: 31 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 4: Run full .NET 9 test suite**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Alife.slnx --no-restore --no-build -v:minimal
```

Expected result:

```text
Failed: 0
```

- [ ] **Step 5: Run whitespace check**

Run:

```powershell
git diff --check
```

Expected result: exit code `0` and no output.

- [ ] **Step 6: Check final status**

Run:

```powershell
git status --short --branch
git log --oneline -5
```

Expected result:

```text
Only intended commits are ahead of the upstream branch.
No unstaged or staged changes remain.
```

- [ ] **Step 7: Upload to default GitHub target if requested**

Use the current default target from `AGENTS.md`:

```text
git@github.com:hushu1232/Alife-byastralfox.git
```

Use the snapshot upload workflow that was already proven for this repository. Verify the remote with:

```powershell
git ls-remote alife-byastralfox refs/heads/master
```

Report the remote commit hash after upload.

---

## Self-Review

Spec coverage:

- Required gate promotion is covered by Task 3.
- Stable script output and field names are covered by Tasks 1 and 2.
- Default mode not depending on live services is covered by Tasks 1, 2, and 4.
- `-Live -Strict` hard machine validation is covered by Tasks 1, 2, and 4.
- No new plugin module in this slice is preserved by the file structure and task scope.

Completion-marker scan:

- The plan contains no deferred work markers, unfinished task entries, or undefined file paths.
- Each code-changing task includes the exact file and the code or replacement line to use.

Type consistency:

- Test helper names are defined in the same test file where they are used.
- PowerShell parameter names used by tests match the script parameters: `-Live`, `-Strict`, `-Json`, and `-VoiceRootPath`.
- Engineering-map markers match literal strings in the replacement script.
