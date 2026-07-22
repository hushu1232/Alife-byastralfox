$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Start-NapCatDualAccount.ps1')
function Assert-Equal($Actual,$Expected){if($Actual-ne$Expected){throw "Expected '$Expected', got '$Actual'."}}
$root=Join-Path ([IO.Path]::GetTempPath()) ('napcat-dual-'+[Guid]::NewGuid().ToString('N'))
try {
  function New-RoleFixture($Name,$Port){
    $roleRoot=Join-Path $root $Name;$napcat=Join-Path $roleRoot 'versions\9.9.26\resources\app\napcat';$config=Join-Path $napcat 'config'
    [IO.Directory]::CreateDirectory($config)|Out-Null
    $launch=Join-Path $roleRoot 'NapCatWinBootMain.exe';New-Item -ItemType File -Path $launch|Out-Null
    New-Item -ItemType File -Path (Join-Path $roleRoot 'napcat.quick.bat')|Out-Null
    New-Item -ItemType File -Path (Join-Path $napcat 'launcher-user.bat')|Out-Null
    New-Item -ItemType File -Path (Join-Path $napcat 'launcher-win10-user.bat')|Out-Null
    @{UserName=$Name;RoleRoot=$roleRoot;OneBotPort=$Port;LaunchPath=$launch}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $roleRoot 'alife-napcat-role-host.json')
    "{`"network`":{`"websocketServers`":[{`"host`":`"127.0.0.1`",`"port`":$Port}]}}"|Set-Content -LiteralPath (Join-Path $config "onebot11_$Port.json")
    return [pscustomobject]@{RoleRoot=$roleRoot;LaunchPath=$launch}
  }
  $mixu=New-RoleFixture 'mixu' 3001;$xiayu=New-RoleFixture 'xiayu' 3002
  $plan=Get-NapCatDualAccountPlan -NapCatRoot $root
  Assert-Equal $plan.Count 2;Assert-Equal (($plan.Port|Sort-Object)-join ',') '3001,3002'
  $accountA=$plan|Where-Object Port -eq 3001;$accountB=$plan|Where-Object Port -eq 3002
  Assert-Equal $accountA.RoleRoot $mixu.RoleRoot;Assert-Equal $accountA.LaunchPath $mixu.LaunchPath
  Assert-Equal $accountB.RoleRoot $xiayu.RoleRoot;Assert-Equal $accountB.LaunchPath $xiayu.LaunchPath
  if($accountA.RoleRoot-eq$accountB.RoleRoot){throw 'Both account slots share RoleRoot.'}
  if($accountA.LaunchPath-eq$accountB.LaunchPath){throw 'Both account slots share LaunchPath.'}
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
