# QChat Required V2 Engineering Map Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Promote the most critical QChat Harness, Loop, and Prompt optional checks into required engineering gates without adding live-service dependencies.

**Architecture:** Keep `tools/check-qchat-engineering-map.ps1` as the single command-line source of truth for required/optional engineering-map checks. Add a focused NUnit contract test that statically guards the required-v2 items so a later edit cannot silently downgrade them to optional. Do not require real QQ, real browser, real vision API, or real TTS startup in the required gate; only require deterministic local code and test contracts.

**Tech Stack:** PowerShell, .NET 9, NUnit, existing QChat test project, existing QChat engineering-map script.

---

## File Structure

- Modify: `tools/check-qchat-engineering-map.ps1`
  - Responsibility: report Harness/Loop/Prompt engineering-map status and fail when required checks are missing.
  - Required-v2 changes: remove `-Required $false` from selected checks that already have deterministic local files and markers.

- Create: `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`
  - Responsibility: static contract that verifies selected required-v2 check names are not declared optional in the engineering-map script.
  - This test deliberately does not run external services; it reads the script text and verifies required-gate intent.

- Modify: `Tests/Alife.Test.QChat/Alife.Test.QChat.csproj`
  - Responsibility: normally no manual change should be needed because SDK-style projects include `*.cs` automatically. Only touch this file if this project has explicit compile item filtering that excludes the new test file.

- Optional modify: `docs/qchat-capability-matrix.md`
  - Responsibility: document that required-v2 means these chains are now release-gated. Only update if the existing doc has an engineering-map or capability status section where this belongs.

## Required-V2 Items

Upgrade these optional checks to required:

- Harness: `Vision readiness tests`
- Harness: `Voice warmup coordinator tests`
- Loop: `Voice warmup retry coordinator`
- Loop: `Semantic settle window contract tests`
- Loop: `Voice warmup contract tests`
- Loop: `XiaYu self-state machine`
- Prompt: `Persona frame prompt`
- Prompt: `XiaYu private state prompt`
- Prompt: `Semantic window summary prompt`

Leave these optional for now:

- Harness: `Runtime readiness script`
- Prompt: `Persona intensity prompt formatter`

Reason: runtime readiness may depend on local deployment shape, and persona intensity is useful but less central than state, settle, vision readiness, and warmup. They can become required-v3 after a stable local readiness contract exists.

---

### Task 1: Add Required-V2 Contract Test

**Files:**
- Create: `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`

- [ ] **Step 1: Write the failing test**

Create `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`:

```csharp
using NUnit.Framework;
using System.IO;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatEngineeringMapRequiredV2Tests
{
    static readonly string[] RequiredV2Checks =
    [
        "Vision readiness tests",
        "Voice warmup coordinator tests",
        "Voice warmup retry coordinator",
        "Semantic settle window contract tests",
        "Voice warmup contract tests",
        "XiaYu self-state machine",
        "Persona frame prompt",
        "XiaYu private state prompt",
        "Semantic window summary prompt"
    ];

    [Test]
    public void RequiredV2ChecksAreNotDeclaredOptional()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-qchat-engineering-map.ps1");
        string script = File.ReadAllText(scriptPath);

        foreach (string checkName in RequiredV2Checks)
        {
            string declaration = FindAddCheckDeclaration(script, checkName);

            Assert.Multiple(() =>
            {
                Assert.That(declaration, Is.Not.Empty, $"Missing Add-Check declaration for '{checkName}'.");
                Assert.That(declaration, Does.Not.Contain("-Required $false"), $"'{checkName}' must be required.");
            });
        }
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
}
```

- [ ] **Step 2: Run the new test to verify it fails before the script change**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'Tests\Alife.Test.QChat\Alife.Test.QChat.csproj' --no-restore --filter 'FullyQualifiedName~QChatEngineeringMapRequiredV2Tests' -v:minimal
```

Expected before Task 2:

```text
失败 RequiredV2ChecksAreNotDeclaredOptional
```

At least one failure message should say an item such as `Vision readiness tests` or `Voice warmup retry coordinator` must be required.

---

### Task 2: Promote Required-V2 Checks In The Engineering Map

**Files:**
- Modify: `tools/check-qchat-engineering-map.ps1`

- [ ] **Step 1: Remove optional flags from required-v2 Harness checks**

In `tools/check-qchat-engineering-map.ps1`, change these lines:

```powershell
Add-Check -Group "Harness" -Name "Vision readiness tests" -Path "Tests/Alife.Test.QChat/QChatVisionReadinessTests.cs" -Patterns @("QChatVisionReadiness") -Required $false
Add-Check -Group "Harness" -Name "Voice warmup coordinator tests" -Path "Tests/Alife.Test.QChat/QChatVoiceWarmupCoordinatorTests.cs" -Patterns @("QChatVoiceWarmupCoordinator") -Required $false
```

to:

```powershell
Add-Check -Group "Harness" -Name "Vision readiness tests" -Path "Tests/Alife.Test.QChat/QChatVisionReadinessTests.cs" -Patterns @("QChatVisionReadiness")
Add-Check -Group "Harness" -Name "Voice warmup coordinator tests" -Path "Tests/Alife.Test.QChat/QChatVoiceWarmupCoordinatorTests.cs" -Patterns @("QChatVoiceWarmupCoordinator")
```

- [ ] **Step 2: Remove optional flags from required-v2 Loop checks**

In the same script, change these lines:

```powershell
Add-Check -Group "Loop" -Name "Voice warmup retry coordinator" -Path "sources/Alife.Function/Alife.Function.QChat/QChatVoiceWarmupCoordinator.cs" -Patterns @("QChatVoiceWarmupCoordinator", "Task.Delay") -Required $false
Add-Check -Group "Loop" -Name "Semantic settle window contract tests" -Path "Tests/Alife.Test.QChat/QChatSemanticSettleWindowTests.cs" -Patterns @("EmptyWindowNeverSettles", "MaxWindowDurationForcesIncompleteTrailingTextToSettle") -Required $false
Add-Check -Group "Loop" -Name "Voice warmup contract tests" -Path "Tests/Alife.Test.QChat/QChatVoiceWarmupCoordinatorTests.cs" -Patterns @("WarmupAsync_MultipleProfilesTrackIndependentStatuses", "StartAsync_RetriesUntilEndpointBecomesReachable") -Required $false
Add-Check -Group "Loop" -Name "XiaYu self-state machine" -Path "sources/Alife.Function/Alife.Function.QChat/XiaYuSelfStateMachine.cs" -Patterns @("XiaYuSelfStateMachine", "Apply") -Required $false
```

to:

```powershell
Add-Check -Group "Loop" -Name "Voice warmup retry coordinator" -Path "sources/Alife.Function/Alife.Function.QChat/QChatVoiceWarmupCoordinator.cs" -Patterns @("QChatVoiceWarmupCoordinator", "Task.Delay")
Add-Check -Group "Loop" -Name "Semantic settle window contract tests" -Path "Tests/Alife.Test.QChat/QChatSemanticSettleWindowTests.cs" -Patterns @("EmptyWindowNeverSettles", "MaxWindowDurationForcesIncompleteTrailingTextToSettle")
Add-Check -Group "Loop" -Name "Voice warmup contract tests" -Path "Tests/Alife.Test.QChat/QChatVoiceWarmupCoordinatorTests.cs" -Patterns @("WarmupAsync_MultipleProfilesTrackIndependentStatuses", "StartAsync_RetriesUntilEndpointBecomesReachable")
Add-Check -Group "Loop" -Name "XiaYu self-state machine" -Path "sources/Alife.Function/Alife.Function.QChat/XiaYuSelfStateMachine.cs" -Patterns @("XiaYuSelfStateMachine", "Apply")
```

- [ ] **Step 3: Remove optional flags from required-v2 Prompt checks**

In the same script, change these lines:

```powershell
Add-Check -Group "Prompt" -Name "Persona frame prompt" -Path "sources/Alife.Function/Alife.Function.QChat/QChatService.cs" -Patterns @("FormatPersonaFramePrompt", "[qchat persona frame]") -Required $false
Add-Check -Group "Prompt" -Name "XiaYu private state prompt" -Path "sources/Alife.Function/Alife.Function.QChat/XiaYuSelfStateMachine.cs" -Patterns @("XiaYuStatePromptFormatter", "[XiaYu state - private, do not quote]") -Required $false
Add-Check -Group "Prompt" -Name "Semantic window summary prompt" -Path "sources/Alife.Function/Alife.Function.QChat/QChatSemanticWindowSummary.cs" -Patterns @("QChatSemanticWindowSummary", "[semantic_window]") -Required $false
```

to:

```powershell
Add-Check -Group "Prompt" -Name "Persona frame prompt" -Path "sources/Alife.Function/Alife.Function.QChat/QChatService.cs" -Patterns @("FormatPersonaFramePrompt", "[qchat persona frame]")
Add-Check -Group "Prompt" -Name "XiaYu private state prompt" -Path "sources/Alife.Function/Alife.Function.QChat/XiaYuSelfStateMachine.cs" -Patterns @("XiaYuStatePromptFormatter", "[XiaYu state - private, do not quote]")
Add-Check -Group "Prompt" -Name "Semantic window summary prompt" -Path "sources/Alife.Function/Alife.Function.QChat/QChatSemanticWindowSummary.cs" -Patterns @("QChatSemanticWindowSummary", "[semantic_window]")
```

- [ ] **Step 4: Keep non-v2 optional checks optional**

Verify these two declarations still include `-Required $false`:

```powershell
Add-Check -Group "Harness" -Name "Runtime readiness script" -Path "tools/check-qchat-runtime-readiness.ps1" -Patterns @("AgnesVisionKeyConfigured") -Required $false
Add-Check -Group "Prompt" -Name "Persona intensity prompt formatter" -Path "sources/Alife.Function/Alife.Function.QChat/QChatAggressionBoundaryPolicy.cs" -Patterns @("QChatPersonaIntensityPromptFormatter", "persona_intensity") -Required $false
```

- [ ] **Step 5: Run the focused contract test**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'Tests\Alife.Test.QChat\Alife.Test.QChat.csproj' --no-restore --filter 'FullyQualifiedName~QChatEngineeringMapRequiredV2Tests' -v:minimal
```

Expected:

```text
已通过! - 失败:     0，通过:     1
```

---

### Task 3: Verify Engineering Map Output Has Stricter Required Counts

**Files:**
- Verify: `tools/check-qchat-engineering-map.ps1`

- [ ] **Step 1: Run the engineering map script**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File 'D:\Alife\tools\check-qchat-engineering-map.ps1'
```

Expected required-v2 output changes:

```text
[Harness]
  PASS     Vision readiness tests: Tests/Alife.Test.QChat/QChatVisionReadinessTests.cs
  PASS     Voice warmup coordinator tests: Tests/Alife.Test.QChat/QChatVoiceWarmupCoordinatorTests.cs

[Loop]
  PASS     Voice warmup retry coordinator: sources/Alife.Function/Alife.Function.QChat/QChatVoiceWarmupCoordinator.cs
  PASS     Semantic settle window contract tests: Tests/Alife.Test.QChat/QChatSemanticSettleWindowTests.cs
  PASS     Voice warmup contract tests: Tests/Alife.Test.QChat/QChatVoiceWarmupCoordinatorTests.cs
  PASS     XiaYu self-state machine: sources/Alife.Function/Alife.Function.QChat/XiaYuSelfStateMachine.cs

[Prompt]
  PASS     Persona frame prompt: sources/Alife.Function/Alife.Function.QChat/QChatService.cs
  PASS     XiaYu private state prompt: sources/Alife.Function/Alife.Function.QChat/XiaYuSelfStateMachine.cs
  PASS     Semantic window summary prompt: sources/Alife.Function/Alife.Function.QChat/QChatSemanticWindowSummary.cs
```

Expected summary:

```text
Summary: 29 required passed, 0 required missing, 2 optional present, 0 optional missing
```

- [ ] **Step 2: Confirm optional items are still visible but not gates**

In the same output, confirm:

```text
OPTIONAL Runtime readiness script: tools/check-qchat-runtime-readiness.ps1
OPTIONAL Persona intensity prompt formatter: sources/Alife.Function/Alife.Function.QChat/QChatAggressionBoundaryPolicy.cs
```

---

### Task 4: Optional Documentation Update

**Files:**
- Optional modify: `docs/qchat-capability-matrix.md`

- [ ] **Step 1: Check whether the capability matrix already documents engineering-map gates**

Run:

```powershell
rg -n "Engineering Map|required|optional|Harness|Loop|Prompt" docs/qchat-capability-matrix.md
```

Expected:

```text
Either matching lines are printed, or rg exits with no matches.
```

- [ ] **Step 2: If the document has an engineering gate section, update it**

If `docs/qchat-capability-matrix.md` has an engineering-map or capability status section, add this text under that section:

```markdown
### Required-v2 Engineering Gates

The following QChat chains are now required by `tools/check-qchat-engineering-map.ps1`:

- Harness: vision readiness tests
- Harness: voice warmup coordinator tests
- Loop: voice warmup retry coordinator
- Loop: semantic settle window contract tests
- Loop: voice warmup contract tests
- Loop: XiaYu self-state machine
- Prompt: persona frame prompt
- Prompt: XiaYu private state prompt
- Prompt: semantic window summary prompt

These gates remain deterministic local checks. They must not require live QQ, live browser control, live vision API calls, or live TTS model startup.
```

If the document has no relevant section, skip this task rather than adding unrelated documentation.

---

### Task 5: Full Verification

**Files:**
- Verify all touched files.

- [ ] **Step 1: Run diff whitespace check**

Run:

```powershell
git diff --check
```

Expected:

```text
No output and exit code 0.
```

Git may print the existing global ignore permission warning:

```text
warning: unable to access 'C:\Users\hu shu/.config/git/ignore': Permission denied
```

That warning is environmental and does not fail the diff check.

- [ ] **Step 2: Build with .NET 9**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' build 'Alife.slnx' --no-restore -v:minimal
```

Expected:

```text
已成功生成。
0 个警告
0 个错误
```

- [ ] **Step 3: Run full test suite with .NET 9**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'Alife.slnx' --no-restore --no-build -v:minimal
```

Expected:

```text
失败:     0
```

The exact pass/skip counts may change only if new tests are added. The key requirement is 0 failures.

- [ ] **Step 4: Run engineering map**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File 'D:\Alife\tools\check-qchat-engineering-map.ps1'
```

Expected:

```text
Summary: 29 required passed, 0 required missing, 2 optional present, 0 optional missing
```

- [ ] **Step 5: Run secret scan**

Run:

```powershell
rg -n 'sk-[A-Za-z0-9_-]{20,}' .
```

Expected:

```text
No matches.
```

`rg` returns exit code 1 when no matches are found; that is acceptable for this scan.

---

### Task 6: Commit

**Files:**
- Commit: `tools/check-qchat-engineering-map.ps1`
- Commit: `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`
- Commit if modified: `docs/qchat-capability-matrix.md`

- [ ] **Step 1: Review status**

Run:

```powershell
git status --short --branch
```

Expected touched files:

```text
M  tools/check-qchat-engineering-map.ps1
A  Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs
```

If documentation was updated, this may also appear:

```text
M  docs/qchat-capability-matrix.md
```

- [ ] **Step 2: Stage files**

Run:

```powershell
git add tools/check-qchat-engineering-map.ps1 Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs
```

If documentation was updated, run:

```powershell
git add docs/qchat-capability-matrix.md
```

- [ ] **Step 3: Commit**

Run:

```powershell
git commit -m "Promote QChat engineering map required gates"
```

Expected:

```text
[master <hash>] Promote QChat engineering map required gates
```

---

## Self-Review

**Spec coverage:** This plan upgrades the high-value optional items that directly support real vision readiness, TTS warmup, semantic settle windows, XiaYu state machine, and prompt injection of state/summary/frame context. It intentionally leaves live runtime readiness and persona intensity as optional until they have stricter deterministic local contracts.

**Placeholder scan:** The plan contains concrete file paths, code for the new NUnit test, exact PowerShell script edits, exact commands, and expected outputs. It does not contain implementation placeholders.

**Type consistency:** The new test uses only NUnit, `System.IO`, `StringComparison`, and script names that already exist in `tools/check-qchat-engineering-map.ps1`. The required-v2 names match the existing Add-Check names exactly.
