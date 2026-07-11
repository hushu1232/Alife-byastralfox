param([string]$NapCatRoot='D:\NapCat',[switch]$Start,[switch]$Interactive,[switch]$RestartLaunchers)
$ErrorActionPreference='Stop'

function Get-NapCatDualAccountPlan {
  param([Parameter(Mandatory)][string]$NapCatRoot)
  if(-not (Test-Path -LiteralPath $NapCatRoot -PathType Container)){throw 'NapCat root was not found.'}
  $launcher=Get-ChildItem -LiteralPath $NapCatRoot -Recurse -File -Filter 'launcher-user.bat'|Sort-Object FullName|Select-Object -First 1
  if($null-eq$launcher){throw 'NapCat launcher was not found.'}
  $bootMain=Join-Path $launcher.DirectoryName 'NapCatWinBootMain.exe'
  $configs=@(Get-ChildItem -LiteralPath $NapCatRoot -Recurse -File -Filter 'onebot11_*.json')
  $slots=@()
  foreach($file in $configs){
    if($file.BaseName-notmatch '^onebot11_(\d+)$'){continue};$accountId=$Matches[1]
    $raw=Get-Content -LiteralPath $file.FullName -Raw
    $portMatch=[regex]::Match($raw,'(?i)"port"\s*:\s*(\d{2,5})');if(-not $portMatch.Success){continue}
    $hostMatch=[regex]::Match($raw,'(?i)"host"\s*:\s*"([^"]+)"');$hostName=if($hostMatch.Success){$hostMatch.Groups[1].Value}else{'127.0.0.1'}
    if($hostName-notin @('127.0.0.1','localhost','::1')){throw 'OneBot host must use loopback.'}
    $slots+=[pscustomobject]@{AccountId=$accountId;Port=[int]$portMatch.Groups[1].Value;Launcher=$launcher.FullName;BootMain=$bootMain;WorkingDirectory=$launcher.DirectoryName}
  }
  $slots=@($slots|Sort-Object Port -Unique)
  if($slots.Count-ne2-or @($slots.Port|Sort-Object -Unique).Count-ne2){throw 'Exactly two unique OneBot account ports are required.'}
  return $slots
}

if($MyInvocation.InvocationName-ne'.'){
  $slots=Get-NapCatDualAccountPlan -NapCatRoot $NapCatRoot
  if($RestartLaunchers){if(-not$Start){throw '-RestartLaunchers requires -Start.'};$bootMainPath=$slots[0].BootMain;Get-Process NapCatWinBootMain -ErrorAction SilentlyContinue|Where-Object{$_.Path-eq$bootMainPath}|Stop-Process -Force}
  if($Start){foreach($slot in $slots){$arguments=@('-q',$slot.AccountId);if($Interactive){Start-Process -FilePath $slot.Launcher -ArgumentList $arguments -WorkingDirectory $slot.WorkingDirectory}else{Start-Process -FilePath $slot.Launcher -ArgumentList $arguments -WorkingDirectory $slot.WorkingDirectory -WindowStyle Hidden}}}
  [pscustomobject]@{accountCount=$slots.Count;ports=@($slots.Port|Sort-Object);started=[bool]$Start;interactive=[bool]$Interactive}|ConvertTo-Json -Depth 3
}
