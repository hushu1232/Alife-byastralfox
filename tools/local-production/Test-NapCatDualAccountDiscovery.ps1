$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Start-NapCatDualAccount.ps1')
function Assert-Equal($Actual,$Expected){if($Actual-ne$Expected){throw "Expected '$Expected', got '$Actual'."}}
$root=Join-Path ([IO.Path]::GetTempPath()) ('napcat-dual-'+[Guid]::NewGuid().ToString('N'))
try {
  function New-RoleFixture($Name,$Port){
    $roleRoot=Join-Path $root $Name;$napcat=Join-Path $roleRoot 'versions\9.9.26\resources\app\napcat';$config=Join-Path $napcat 'config'
    [IO.Directory]::CreateDirectory($config)|Out-Null
    $launch=Join-Path $roleRoot 'NapCatWinBootMain.exe';New-Item -ItemType File -Path $launch|Out-Null
    $quickEntry=Join-Path $roleRoot 'napcat.quick.bat'
    [IO.File]::WriteAllText($quickEntry,((@('@echo off','chcp 65001 >nul','.\NapCatWinBootMain.exe System.Collections.Hashtable','pause')-join"`r`n")+"`r`n"),[Text.Encoding]::Unicode)
    New-Item -ItemType File -Path (Join-Path $napcat 'launcher-user.bat')|Out-Null
    New-Item -ItemType File -Path (Join-Path $napcat 'launcher-win10-user.bat')|Out-Null
    @{UserName=$Name;RoleRoot=$roleRoot;OneBotPort=$Port;LaunchPath=$launch}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $roleRoot 'alife-napcat-role-host.json')
    $quickLoginAccount=if($Port-eq3001){'100001'}else{'100002'}
    $configPath=Join-Path $config "onebot11_$quickLoginAccount.json"
    "{`"network`":{`"websocketServers`":[{`"enable`":true,`"host`":`"127.0.0.1`",`"port`":$Port}]}}"|Set-Content -LiteralPath $configPath
    return [pscustomobject]@{RoleRoot=$roleRoot;LaunchPath=$launch;ConfigPath=$configPath}
  }
  $mixu=New-RoleFixture 'mixu' 3001;$xiayu=New-RoleFixture 'xiayu' 3002
  $plan=Get-NapCatDualAccountPlan -NapCatRoot $root
  Assert-Equal $plan.Count 2;Assert-Equal (($plan.Port|Sort-Object)-join ',') '3001,3002'
  $accountA=$plan|Where-Object Port -eq 3001;$accountB=$plan|Where-Object Port -eq 3002
  Assert-Equal $accountA.RoleRoot $mixu.RoleRoot;Assert-Equal $accountA.LaunchPath $mixu.LaunchPath
  Assert-Equal $accountB.RoleRoot $xiayu.RoleRoot;Assert-Equal $accountB.LaunchPath $xiayu.LaunchPath
  if($accountA.RoleRoot-eq$accountB.RoleRoot){throw 'Both account slots share RoleRoot.'}
  if($accountA.LaunchPath-eq$accountB.LaunchPath){throw 'Both account slots share LaunchPath.'}
  $plan|ForEach-Object{Sync-NapCatQuickLauncher -Slot $_|Out-Null}
  foreach($slot in $plan){
    $quick=Get-Content -LiteralPath $slot.QuickEntry -Raw
    if($quick-match'System\.Collections\.Hashtable'){throw 'Quick launcher retained the bad argument.'}
    if($quick-notmatch'(?m)^\.\\NapCatWinBootMain\.exe \d+\r?$'){throw 'Quick launcher does not have a numeric account argument.'}
    $quickBytes=[IO.File]::ReadAllBytes($slot.QuickEntry)
    if($quickBytes.Length-lt2-or$quickBytes[0]-ne0xff-or$quickBytes[1]-ne0xfe){throw 'Quick launcher encoding was not preserved.'}
  }
  $stopOutput=& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Start-NapCatDualAccount.ps1') -NapCatRoot $root -Stop -LoginMode Qr 2>&1
  if($LASTEXITCODE-ne0){throw 'Stop must not require QR login arguments.'}
  Assert-Equal (($stopOutput|ConvertFrom-Json).stopped) $true
  Rename-Item -LiteralPath $mixu.ConfigPath -NewName 'onebot11_invalid.json'
  $invalidAccountRejected=$false
  try{Get-NapCatDualAccountPlan -NapCatRoot $root|Out-Null}catch{$invalidAccountRejected=$true}
  Assert-Equal $invalidAccountRejected $true
  Assert-Equal (ConvertTo-OneBotStatus '{"data":{"online":true}}') 'online'
  Assert-Equal (ConvertTo-OneBotStatus '{"data":{"online":false}}') 'offline'
  Assert-Equal (ConvertTo-OneBotStatus '{"data":{}}') 'unknown'
  function Get-CimInstance{throw 'CIM unavailable'}
  try{Assert-Equal (Test-NapCatHost ([pscustomobject]@{LaunchPath='C:\missing\NapCatWinBootMain.exe'})) $false}
  finally{Remove-Item Function:\Get-CimInstance -ErrorAction SilentlyContinue}
  "{`"network`":{`"websocketServers`":[{`"host`":`"127.0.0.1`",`"port`":3002}]}}"|Set-Content -LiteralPath (Join-Path $mixu.RoleRoot 'versions\9.9.26\resources\app\napcat\config\onebot11_duplicate.json')
  @{UserName='xiayu';RoleRoot=$mixu.RoleRoot;OneBotPort=3002;LaunchPath=$mixu.LaunchPath}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $xiayu.RoleRoot 'alife-napcat-role-host.json')
  $duplicateRootRejected=$false
  try{Get-NapCatDualAccountPlan -NapCatRoot $root|Out-Null}catch{$duplicateRootRejected=$true}
  Assert-Equal $duplicateRootRejected $true
} finally {if(Test-Path -LiteralPath $root){Remove-Item -LiteralPath $root -Recurse -Force}}
