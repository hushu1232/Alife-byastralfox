# NapCat Dual-Role Operation Normalization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the manifest-bound dual-role launcher safely stop either role set and self-correct each selected role's Quick launcher before cached-session recovery.

**Architecture:** Keep `Start-NapCatDualAccount.ps1` as the only operation entrypoint. Extend its existing per-role config discovery to retain an internal numeric Quick-login account, use that value only to synchronize a role's one batch command line before Quick launch, and add a role-root-limited stop mode. No new process supervisor, configuration file, or external dependency is introduced.

**Tech Stack:** Windows PowerShell 5+, built-in JSON and file APIs, existing NapCat manifests/configs, existing PowerShell synthetic test.

---

## File structure

- Modify: `tools/local-production/Start-NapCatDualAccount.ps1` — internal Quick-account discovery, atomic wrapper synchronization, role-bound stop mode, parameter validation.
- Modify: `tools/local-production/Test-NapCatDualAccountDiscovery.ps1` — failing and passing synthetic wrapper/config checks.
- Modify: `docs/runbooks/alife-local-dual-account-production.md` — stable stop and Quick recovery commands.

### Task 1: Lock Quick-account synchronization with a failing synthetic check

**Files:**

- Modify: `tools/local-production/Test-NapCatDualAccountDiscovery.ps1:6-15,17-35`
- Test: `tools/local-production/Test-NapCatDualAccountDiscovery.ps1`

- [ ] **Step 1: Make each fixture Quick wrapper intentionally invalid**

```powershell
@('@echo off','chcp 65001 >nul','.\\NapCatWinBootMain.exe System.Collections.Hashtable','pause') |
  Set-Content -LiteralPath (Join-Path $roleRoot 'napcat.quick.bat')
$account=if($Port -eq 3001){'100001'}else{'100002'}
 $configPath=Join-Path $config "onebot11_$account.json"
"{`"network`":{`"websocketServers`":[{`"enable`":true,`"host`":`"127.0.0.1`",`"port`":$Port}]}}" |
  Set-Content -LiteralPath $configPath
return [pscustomobject]@{RoleRoot=$roleRoot;LaunchPath=$launch;ConfigPath=$configPath}
```

- [ ] **Step 2: Add the desired synchronization assertion**

```powershell
$plan | ForEach-Object { Sync-NapCatQuickLauncher -Slot $_ | Out-Null }
foreach($slot in $plan){
  $quick=Get-Content -LiteralPath $slot.QuickEntry -Raw
  if($quick -match 'System\.Collections\.Hashtable'){throw 'Quick launcher retained the bad argument.'}
  if($quick -notmatch '(?m)^\.\\NapCatWinBootMain\.exe \d+\r?$'){throw 'Quick launcher does not have a numeric account argument.'}
}
```

- [ ] **Step 3: Add the invalid account-filename assertion**

After the wrapper assertion, rename one matching fixture config and assert discovery rejects it:

```powershell
Rename-Item -LiteralPath $mixu.ConfigPath -NewName 'onebot11_invalid.json'
$invalidAccountRejected=$false
try{Get-NapCatDualAccountPlan -NapCatRoot $root|Out-Null}catch{$invalidAccountRejected=$true}
Assert-Equal $invalidAccountRejected $true
```

- [ ] **Step 4: Run the focused test and verify RED**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/local-production/Test-NapCatDualAccountDiscovery.ps1
```

Expected: failure because `Sync-NapCatQuickLauncher` does not yet exist.

### Task 2: Add the smallest role-bound synchronizer and stop mode

**Files:**

- Modify: `tools/local-production/Start-NapCatDualAccount.ps1:1-78,145-167`
- Test: `tools/local-production/Test-NapCatDualAccountDiscovery.ps1`

- [ ] **Step 1: Retain the account only inside each slot**

When `Get-OneBotEndpointForPort` finds the sole enabled endpoint, validate the filename with `^onebot11_(\d+)\.json$` and return the capture in an internal `QuickLoginAccount` property. Add that property to the slot object but do not add it to the command's final JSON result.

```powershell
$accountMatch=[regex]::Match($file.Name,'^onebot11_(\d+)\.json$',[Text.RegularExpressions.RegexOptions]::IgnoreCase)
if(-not $accountMatch.Success){throw 'A role Quick-login account filename is invalid.'}
$matches+=[pscustomobject]@{Host=$hostName;Port=$serverPort;QuickLoginAccount=$accountMatch.Groups[1].Value}
```

- [ ] **Step 2: Implement atomic, single-line Quick synchronization**

```powershell
function Sync-NapCatQuickLauncher {
  param([Parameter(Mandatory)][object]$Slot)
  $current=[IO.File]::ReadAllText($Slot.QuickEntry)
  $pattern='(?m)^\.\\NapCatWinBootMain\.exe[^\r\n]*'
  $matches=[regex]::Matches($current,$pattern)
  if($matches.Count-ne1){throw 'A role Quick launcher must contain exactly one NapCat command.'}
  $expected='.\\NapCatWinBootMain.exe '+$Slot.QuickLoginAccount
  if($matches[0].Value-eq$expected){return $false}
  $replacement=$current.Substring(0,$matches[0].Index)+$expected+$current.Substring($matches[0].Index+$matches[0].Length)
  $temporary=$Slot.QuickEntry+'.alife-sync'
  [IO.File]::WriteAllText($temporary,$replacement,[Text.UTF8Encoding]::new($false))
  Move-Item -LiteralPath $temporary -Destination $Slot.QuickEntry -Force
  return $true
}
```

- [ ] **Step 3: Add `-Stop` and connect Quick synchronization**

Add `[switch]$Stop` to the existing parameter block. Reject `-Stop` with `-Start` or `-RestartLaunchers`. In the non-dot-sourced command block, select slots first, then execute this stop branch before any launch branch:

```powershell
if($Stop){
  foreach($slot in $slots){
    Get-CimInstance Win32_Process |
      Where-Object { $_.Name -in @('QQ.exe','NapCatWinBootMain.exe') -and $_.ExecutablePath -and (Test-PathWithin -Path $_.ExecutablePath -Root $slot.RoleRoot) } |
      ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
  }
}
if($Start -and $LoginMode -eq 'Quick'){
  foreach($slot in $slots){Sync-NapCatQuickLauncher -Slot $slot | Out-Null}
}
```

Keep existing `-RestartLaunchers` semantics after synchronization. The final JSON adds `stopped=[bool]$Stop`, but does not add account or token fields.

- [ ] **Step 4: Run the focused test and verify GREEN**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/local-production/Test-NapCatDualAccountDiscovery.ps1
```

Expected: exit code `0`; both synthetic wrappers contain one numeric Quick argument and neither contains `System.Collections.Hashtable`.

### Task 3: Document and verify the stable commands

**Files:**

- Modify: `docs/runbooks/alife-local-dual-account-production.md:15-41`
- Test: `tools/local-production/Test-NapCatDualAccountDiscovery.ps1`

- [ ] **Step 1: Document the two normal role operations**

```powershell
# Stop only both manifest-bound local role instances.
powershell.exe -NoProfile -File tools/local-production/Start-NapCatDualAccount.ps1 -NapCatRoot D:\NapCat -Stop

# Synchronize each selected Quick wrapper from its own enabled OneBot config, then recover cached sessions.
powershell.exe -NoProfile -File tools/local-production/Start-NapCatDualAccount.ps1 -NapCatRoot D:\NapCat -Start -LoginMode Quick
```

State that the synchronizer does not expose the resolved account, token, cookie, or chat content; it repairs only the role-local batch command before launch.

- [ ] **Step 2: Run full safe verification**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/local-production/Test-NapCatDualAccountDiscovery.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/local-production/Start-NapCatDualAccount.ps1 -NapCatRoot D:\NapCat
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/local-production/Start-NapCatDualAccount.ps1 -NapCatRoot D:\NapCat -Stop
git diff --check
git status --short
```

Expected: the synthetic test exits `0`; discovery finds two slots and starts nothing; stop exits safely when roles are already stopped; whitespace check is empty; status contains only the intended source/docs changes plus pre-existing untracked runtime artifacts.

- [ ] **Step 3: Commit only the intended source and documentation files**

```bash
git add tools/local-production/Start-NapCatDualAccount.ps1 tools/local-production/Test-NapCatDualAccountDiscovery.ps1 docs/runbooks/alife-local-dual-account-production.md docs/superpowers/specs/2026-07-22-napcat-dual-role-operation-design.md docs/superpowers/plans/2026-07-22-napcat-dual-role-operation-normalization.md
git commit -m "fix(ops): normalize NapCat role operations"
```
