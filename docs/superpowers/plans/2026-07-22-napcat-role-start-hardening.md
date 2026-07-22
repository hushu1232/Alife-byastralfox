# NapCat Role Startup Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the two-role NapCat launcher use each role's manifest, distinguish quick recovery from QR login, and report only verified per-role readiness.

**Architecture:** The launcher derives slots from exactly two `alife-napcat-role-host.json` files rather than one global launcher plus unrelated recursive configs. Each slot owns its role root, host executable, OneBot endpoint, quick wrapper, and QR entry. A safe status probe reports host, TCP, and authenticated OneBot state independently; QR mode requests one interactive login and never treats launcher creation as readiness.

**Tech Stack:** Windows PowerShell 5+, built-in JSON cmdlets, `TcpClient`, `ClientWebSocket`, existing NapCat role manifests, existing custom PowerShell tests.

---

## File structure

- Modify: `tools/local-production/Start-NapCatDualAccount.ps1` — manifest discovery, explicit login mode, safe readiness report, OneBot status probe.
- Modify: `tools/local-production/Test-NapCatDualAccountDiscovery.ps1` — synthetic two-root mapping and pure OneBot response classification tests.
- Modify: `docs/runbooks/alife-local-dual-account-production.md` — manifest discovery, quick recovery, single-role QR, and status commands.

### Task 1: Lock the role-root mapping with failing tests

**Files:**

- Modify: `tools/local-production/Test-NapCatDualAccountDiscovery.ps1:4-13`
- Test: `tools/local-production/Test-NapCatDualAccountDiscovery.ps1`

- [ ] **Step 1: Replace the one-root fixture with two independent role roots**

Create one manifest, quick wrapper, QR launcher, host executable placeholder, and matching OneBot config per root. The fixture makes it impossible for one globally selected launcher to satisfy both assertions.

```powershell
function New-RoleFixture([string]$Name,[int]$Port) {
  $roleRoot=Join-Path $root $Name
  $napcat=Join-Path $roleRoot 'versions\9.9.26\resources\app\napcat'
  $config=Join-Path $napcat 'config'
  [IO.Directory]::CreateDirectory($config)|Out-Null
  $launch=Join-Path $roleRoot 'NapCatWinBootMain.exe'
  New-Item -ItemType File -Path $launch|Out-Null
  New-Item -ItemType File -Path (Join-Path $roleRoot 'napcat.quick.bat')|Out-Null
  New-Item -ItemType File -Path (Join-Path $napcat 'launcher-win10-user.bat')|Out-Null
  @{UserName=$Name;RoleRoot=$roleRoot;OneBotPort=$Port;LaunchPath=$launch}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $roleRoot 'alife-napcat-role-host.json')
  "{`"network`":{`"websocketServers`":[{`"host`":`"127.0.0.1`",`"port`":$Port}]}}"|Set-Content -LiteralPath (Join-Path $config "onebot11_$Port.json")
  return $roleRoot
}
```

- [ ] **Step 2: Add the mapping and classifier assertions**

```powershell
$mixu=New-RoleFixture 'mixu' 3001
$xiayu=New-RoleFixture 'xiayu' 3002
$plan=Get-NapCatDualAccountPlan -NapCatRoot $root
Assert-Equal $plan.Count 2
Assert-Equal (($plan.Port|Sort-Object)-join ',') '3001,3002'
Assert-Equal (($plan|Where-Object Port -eq 3001).RoleRoot) $mixu
Assert-Equal (($plan|Where-Object Port -eq 3002).RoleRoot) $xiayu
Assert-Equal (ConvertTo-OneBotStatus '{"data":{"online":true}}') 'online'
Assert-Equal (ConvertTo-OneBotStatus '{"data":{"online":false}}') 'offline'
Assert-Equal (ConvertTo-OneBotStatus '{"data":{}}') 'unknown'
```

- [ ] **Step 3: Run the test before implementation**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/local-production/Test-NapCatDualAccountDiscovery.ps1
```

Expected: failure because the current implementation has no `RoleRoot` slot property and no `ConvertTo-OneBotStatus` function.

### Task 2: Implement manifest-bound launch and readiness

**Files:**

- Modify: `tools/local-production/Start-NapCatDualAccount.ps1:1-35`
- Test: `tools/local-production/Test-NapCatDualAccountDiscovery.ps1`

- [ ] **Step 1: Add explicit modes while preserving the no-start default**

Replace the parameter line with:

```powershell
param(
  [string]$NapCatRoot='D:\NapCat',
  [ValidateSet(0,3001,3002)][int]$AccountPort=0,
  [ValidateSet('Quick','Qr')][string]$LoginMode='Quick',
  [switch]$Start,
  [switch]$Interactive,
  [switch]$RestartLaunchers,
  [int]$ProbeTimeoutSeconds=5
)
```

Discover only `alife-napcat-role-host.json`. For each manifest, resolve `RoleRoot`, require it to remain below `$NapCatRoot`, require `OneBotPort` and `LaunchPath`, then locate the matching OneBot config, `napcat.quick.bat`, and `launcher-win10-user.bat` below that one role root. Parse host and port from the matched config but never print its raw JSON. Return `RoleRoot`, `LaunchPath`, `QuickEntry`, `QrEntry`, `Host`, `Port`, and `TokenEnvironmentName`; map port `3001` to `ALIFE_ACCOUNT_A_ONEBOT_TOKEN` and port `3002` to `ALIFE_ACCOUNT_B_ONEBOT_TOKEN`.

- [ ] **Step 2: Add the smallest local status helpers**

Add these helpers in the same script; do not create a new module for one caller:

```powershell
function Test-NapCatHost([object]$Slot) {
  return $null -ne (Get-CimInstance Win32_Process -Filter "Name='NapCatWinBootMain.exe'" |
    Where-Object { $_.ExecutablePath -eq $Slot.LaunchPath } |
    Select-Object -First 1)
}

function Test-OneBotPort([string]$HostName,[int]$Port,[int]$TimeoutSeconds) {
  $client=[Net.Sockets.TcpClient]::new()
  try { $task=$client.ConnectAsync($HostName,$Port); return $task.Wait($TimeoutSeconds*1000) -and $client.Connected }
  catch { return $false }
  finally { $client.Dispose() }
}

function ConvertTo-OneBotStatus([string]$Payload) {
  try { $online=($Payload|ConvertFrom-Json -ErrorAction Stop).data.online } catch { return 'unknown' }
  if($online -is [bool]) { if($online){return 'online'};return 'offline' }
  return 'unknown'
}
```

Implement the protocol probe exactly as a safe, local operation:

```powershell
function Invoke-OneBotStatusProbe([object]$Slot,[int]$TimeoutSeconds) {
  $token=[Environment]::GetEnvironmentVariable($Slot.TokenEnvironmentName,'User')
  if([string]::IsNullOrWhiteSpace($token)) { $token=[Environment]::GetEnvironmentVariable($Slot.TokenEnvironmentName,'Process') }
  if([string]::IsNullOrWhiteSpace($token)) { return 'unknown' }
  $socket=[Net.WebSockets.ClientWebSocket]::new()
  $cancel=[Threading.CancellationTokenSource]::new()
  try {
    $cancel.CancelAfter([TimeSpan]::FromSeconds($TimeoutSeconds))
    $socket.Options.SetRequestHeader('Authorization',"Bearer $token")
    $socket.ConnectAsync([Uri]("ws://{0}:{1}/" -f $Slot.Host,$Slot.Port),$cancel.Token).GetAwaiter().GetResult()
    $request=[Text.Encoding]::UTF8.GetBytes('{"action":"get_status","echo":"alife-startup-probe"}')
    $socket.SendAsync([ArraySegment[byte]]::new($request),[Net.WebSockets.WebSocketMessageType]::Text,$true,$cancel.Token).GetAwaiter().GetResult()
    $buffer=New-Object byte[] 4096
    $received=$socket.ReceiveAsync([ArraySegment[byte]]::new($buffer),$cancel.Token).GetAwaiter().GetResult()
    return ConvertTo-OneBotStatus ([Text.Encoding]::UTF8.GetString($buffer,0,$received.Count))
  } catch { return 'unknown' }
  finally { $cancel.Dispose();$socket.Dispose() }
}
```

The function never logs the token or response payload.

- [ ] **Step 3: Implement launch and report behavior**

Enforce the QR boundary and select entries as follows:

```powershell
if($LoginMode -eq 'Qr' -and ($AccountPort -eq 0 -or -not $Interactive)) {
  throw 'QR login requires -AccountPort 3001 or 3002 and -Interactive.'
}
if($Start) {
  foreach($slot in $slots) {
    $entry=if($LoginMode -eq 'Quick'){$slot.QuickEntry}else{$slot.QrEntry}
    $arguments=@()
    Start-Process -FilePath $entry -ArgumentList $arguments -WorkingDirectory (Split-Path -Parent $entry) -WindowStyle $(if($Interactive){'Normal'}else{'Hidden'})
  }
}
```

For `-RestartLaunchers`, stop only processes whose executable path equals the selected slot's manifest `LaunchPath`; do not enumerate or stop generic `D:\QQ.exe`. Evaluate `hostRunning`, `portReachable`, and `oneBotStatus` first, then output one safe object per slot with `port`, `loginMode`, `qrRequested`, those three values, and `ready=$hostRunning -and $portReachable -and $oneBotStatus -eq 'online'`. Keep `started` only as a launch-request field, never as readiness.

- [ ] **Step 4: Run focused verification and commit**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/local-production/Test-NapCatDualAccountDiscovery.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/local-production/Start-NapCatDualAccount.ps1 -NapCatRoot D:\NapCat
git diff --check
```

Expected: test exits `0`; discovery returns two manifest-bound slots and starts no process; `git diff --check` prints nothing. Then commit:

```bash
git add tools/local-production/Start-NapCatDualAccount.ps1 tools/local-production/Test-NapCatDualAccountDiscovery.ps1
git commit -m "fix(ops): bind NapCat startup to role manifests"
```

### Task 3: Document the safe operation

**Files:**

- Modify: `docs/runbooks/alife-local-dual-account-production.md:7-13`
- Test: `tools/local-production/Test-NapCatDualAccountDiscovery.ps1`

- [ ] **Step 1: Replace bootstrap commands with the verified modes**

Document these commands:

```powershell
# Read role-bound slots; starts nothing.
powershell.exe -NoProfile -File tools/local-production/Start-NapCatDualAccount.ps1 -NapCatRoot D:\NapCat

# Recover cached sessions for both roles and report verified readiness.
powershell.exe -NoProfile -File tools/local-production/Start-NapCatDualAccount.ps1 -NapCatRoot D:\NapCat -Start -LoginMode Quick

# Request one visible QR flow only.
powershell.exe -NoProfile -File tools/local-production/Start-NapCatDualAccount.ps1 -NapCatRoot D:\NapCat -Start -LoginMode Qr -AccountPort 3001 -Interactive
```

State that only `ready=true` means usable, while `unknown` means the local OneBot probe could not verify QQ online state.

- [ ] **Step 2: Run final safe verification and commit**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/local-production/Test-NapCatDualAccountDiscovery.ps1
git diff --check
git status --short
```

Expected: test exits `0`; whitespace check prints nothing; status contains only the intended runbook edit plus pre-existing ignored runtime artifacts. Then commit:

```bash
git add docs/runbooks/alife-local-dual-account-production.md
git commit -m "docs(ops): document manifest-bound NapCat recovery"
```
