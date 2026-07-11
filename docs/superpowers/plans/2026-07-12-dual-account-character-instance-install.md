# Dual-Account Character Instance Installation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Install the existing 真央-backed 咪绪 instance into account A and the existing 夏羽 instance into account B so each isolated .NET client discovers exactly its assigned character.

**Architecture:** A tracked PowerShell installer validates both source `index.json` files, stages complete copies, backs up an existing assigned destination, and moves staging into place. A PowerShell contract test uses temporary fake instances; the real install writes only ignored account Storage roots and never manages processes.

**Tech Stack:** Windows PowerShell 5.1, JSON character instances, `ALIFE_STORAGE_PATH`, Git.

---

## File structure

| Path | Responsibility |
|---|---|
| `tools/local-production/Install-AlifeDualAccountCharacters.ps1` | Validate, stage, back up, and install assigned instances. |
| `tools/local-production/Test-InstallAlifeDualAccountCharacters.ps1` | Test mapping, isolation, dry-run, validation, backup, and safe output. |
| `docs/runbooks/alife-local-dual-account-production.md` | Document installation and one-character-per-window behavior. |

### Task 1: Build the safe character installer with TDD

**Files:**
- Create: `tools/local-production/Test-InstallAlifeDualAccountCharacters.ps1`
- Create: `tools/local-production/Install-AlifeDualAccountCharacters.ps1`

- [ ] **Step 1: Write the failing contract test**

Build fake `真央` and `夏羽` source instances in a temporary Storage root. Give each a parseable `index.json`, a configuration file, and a memory sentinel. Invoke dry-run and assert no destination is created. Invoke `-Install` and assert:

```powershell
Assert-True (Test-Path (Join-Path $accountA 'Character\真央\index.json')) 'account-a instance missing'
Assert-True (Test-Path (Join-Path $accountB 'Character\夏羽\index.json')) 'account-b instance missing'
Assert-False (Test-Path (Join-Path $accountA 'Character\夏羽')) 'account-a cross-instance'
Assert-False (Test-Path (Join-Path $accountB 'Character\真央')) 'account-b cross-instance'
```

Run a second install with changed source content and assert the prior destination is below `CharacterBackups`. Assert a mismatched `index.json` fails without changing destinations. Capture output and assert it excludes the temporary absolute root, `Token`, `Authorization`, `BotId`, and sentinel content.

- [ ] **Step 2: Run the test and verify RED**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/local-production/Test-InstallAlifeDualAccountCharacters.ps1
```

Expected: FAIL because the installer does not exist.

- [ ] **Step 3: Implement the minimal installer**

Use this public surface:

```powershell
[CmdletBinding()]
param(
    [string]$SourceStorageRoot = 'D:\Alife\Storage',
    [string]$AccountAStorageRoot = 'D:\Alife\storage\account-a',
    [string]$AccountBStorageRoot = 'D:\Alife\storage\account-b',
    [switch]$Install
)
```

Implement metadata and path validation with these helpers:

```powershell
function Resolve-FullPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw 'storage root is required' }
    [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
}
function Assert-ChildPath([string]$Root, [string]$Child) {
    $prefix = (Resolve-FullPath $Root) + '\'
    if (-not (Resolve-FullPath $Child).StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'path escaped assigned storage root'
    }
}
```

Validate `index.json` with:

```powershell
function Read-CharacterIndex([string]$Source, [string]$ExpectedName) {
    $index = Join-Path $Source 'index.json'
    if (-not (Test-Path -LiteralPath $index -PathType Leaf)) { throw 'character index missing' }
    $value = Get-Content -LiteralPath $index -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]$value.Name -cne $ExpectedName) { throw 'character name mismatch' }
}
```

Use fixed mappings account A/`真央` and account B/`夏羽`. Validate both sources and distinct destination roots before writes. Dry-run emits safe labels and booleans only.

For install, stage below `<account-root>\Character`, revalidate staging, move an existing assigned destination below `CharacterBackups\<timestamp>`, then move staging into place. Reject an opposite-role directory rather than deleting it. Clean only installer-created staging in `finally`.

- [ ] **Step 4: Run the test and verify GREEN**

Run the Step 2 command. Expected: exit 0 with one safe PASS summary.

- [ ] **Step 5: Commit**

```powershell
git add tools/local-production/Install-AlifeDualAccountCharacters.ps1 tools/local-production/Test-InstallAlifeDualAccountCharacters.ps1
git commit -m 'feat(ops): install isolated local character instances'
```

### Task 2: Document and execute the local installation

**Files:**
- Modify: `docs/runbooks/alife-local-dual-account-production.md`
- Local-only writes: `D:\Alife\storage\account-a`, `D:\Alife\storage\account-b`

- [ ] **Step 1: Add the runbook procedure**

Document these commands:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/local-production/Install-AlifeDualAccountCharacters.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/local-production/Install-AlifeDualAccountCharacters.ps1 -Install
```

State that account A lists only 真央 (咪绪 identity), account B lists only 夏羽, and restart/activation is separately authorized.

- [ ] **Step 2: Run dry-run validation**

Run the installer without `-Install`. Expected: both safe labels report `validated=true installed=false`; no destination changes.

- [ ] **Step 3: Install the real local instances**

Run with `-Install`. Expected: both labels report `validated=true installed=true`; no process starts or stops.

- [ ] **Step 4: Verify static isolation and Git hygiene**

Parse only the destination `index.json` files. Verify expected names, no opposite-role directory, distinct resolved paths, and no local Storage, memory, state, backup, Token, login, or evidence artifact in `git status --short`.

- [ ] **Step 5: Run regressions**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/local-production/Test-InstallAlifeDualAccountCharacters.ps1
git diff --check
```

Expected: PASS and no whitespace errors.

- [ ] **Step 6: Commit the runbook**

```powershell
git add docs/runbooks/alife-local-dual-account-production.md
git commit -m 'docs(ops): document isolated character installation'
```

## Completion boundary

Complete when both local destinations pass static validation and only the installer, test, and runbook are tracked. Do not start, stop, or restart Alife, NapCat, or QQ, and do not claim the windows are updated before a separately authorized restart.
