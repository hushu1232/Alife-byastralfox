param([string]$PlanPath=$env:ALIFE_LOCAL_PRODUCTION_PLAN,[string]$ClientExecutablePath,[switch]$Once,[string]$StatusPath=(Join-Path $PSScriptRoot 'local-production-status.json'),[string]$StatePath=(Join-Path $PSScriptRoot 'local-production-state.json'),[switch]$DryRun,[int]$PollSeconds=5)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalProduction.Configuration.psm1') -Force
if([string]::IsNullOrWhiteSpace($PlanPath)){throw 'PlanPath is required.'}
function Write-SafeJson($Value,[string]$Path){$parent=Split-Path -Parent $Path;if($parent){[IO.Directory]::CreateDirectory($parent)|Out-Null};$temp="$Path.tmp";$Value|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $temp -Encoding UTF8;Move-Item -LiteralPath $temp -Destination $Path -Force}
function Get-TrackedAccount($State,[string]$AccountId){
  if($null -eq $State -or $null -eq $State.accounts){return $null}
  if($State.accounts -is [System.Collections.IDictionary]){return $State.accounts[$AccountId]}
  return $State.accounts.PSObject.Properties[$AccountId].Value
}
function Get-RunningAccountWorker($State,[string]$AccountId){
  $account=Get-TrackedAccount $State $AccountId
  if($null -eq $account){return $null}
  $pidValue=0
  if(-not [int]::TryParse([string]$account.pid,[ref]$pidValue) -or $pidValue -le 0){return $null}
  return Get-Process -Id $pidValue -ErrorAction SilentlyContinue
}
function Start-AccountWorker($Slot,[string]$Executable,[string]$Token){
  $start=[Diagnostics.ProcessStartInfo]::new()
  if([IO.Path]::GetExtension($Executable)-eq'.dll'){$dotnet=if($env:ALIFE_DOTNET_PATH){$env:ALIFE_DOTNET_PATH}else{'C:\Users\hu shu\.dotnet\dotnet.exe'};if(-not(Test-Path -LiteralPath $dotnet)){throw 'User .NET runtime was not found.'};$start.FileName=$dotnet;$start.Arguments='"'+$Executable+'"'}else{$start.FileName=$Executable}
  $start.UseShellExecute=$false;$start.CreateNoWindow=$true
  $start.Environment['ALIFE_RUNTIME_PATH']=$Slot.runtimeRoot;$start.Environment['ALIFE_STORAGE_PATH']=$Slot.storageRoot;$start.Environment['ALIFE_TEMP_PATH']=$Slot.tempRoot;$start.Environment['ALIFE_ACCOUNT_ID']=$Slot.id
  $start.Environment['ALIFE_WEBVIEW2_USER_DATA_FOLDER']=(Join-Path $Slot.runtimeRoot 'webview2');$start.Environment['ALIFE_ONEBOT_URL']=$Slot.oneBotUrl;$start.Environment['ALIFE_ONEBOT_TOKEN']=$Token;$start.Environment['ALIFE_QZONE_LOOPBACK_OPERATOR_URL']=$Slot.qZoneLoopbackOperatorUrl
  [void]$start.Environment.Remove('ALIFE_ACCOUNT_A_ONEBOT_TOKEN');[void]$start.Environment.Remove('ALIFE_ACCOUNT_B_ONEBOT_TOKEN')
  [Diagnostics.Process]::Start($start)
}
$repoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if([string]::IsNullOrWhiteSpace($ClientExecutablePath)){$ClientExecutablePath=Join-Path $repoRoot 'Outputs\Alife.Client\Alife.Client.dll'}
$previousState=$null
if(Test-Path -LiteralPath $StatePath){try{$previousState=Get-Content -LiteralPath $StatePath -Raw|ConvertFrom-Json}catch{$previousState=$null}}
$plan=Read-LocalProductionPlan (Get-Content -LiteralPath $PlanPath -Raw)
$startedAccounts=@{}
do {
  $accounts=[ordered]@{}
  foreach($slot in $plan.accounts){
    $pidValue=0;$slotHealth='Degraded';$reason='DependencyUnavailable';$restartCount=0;$components=@()
    if(-not $DryRun){
      $token=[Environment]::GetEnvironmentVariable($slot.oneBotTokenEnvironmentVariable,'User')
      if([string]::IsNullOrWhiteSpace($token)){$token=[Environment]::GetEnvironmentVariable($slot.oneBotTokenEnvironmentVariable,'Process')}
      if([string]::IsNullOrWhiteSpace($token)){$slotHealth='Unavailable';$reason='ConfigurationRejected'}
      elseif(-not (Test-Path -LiteralPath $ClientExecutablePath -PathType Leaf)){$slotHealth='Unavailable';$reason='DependencyUnavailable'}
      else{
        $previousAccount=Get-TrackedAccount $previousState $slot.id
        $worker=Get-RunningAccountWorker $previousState $slot.id
        if($null -eq $worker -and $startedAccounts.ContainsKey($slot.id)){$slotHealth='Unavailable';$reason='ClientProcessMissing';if($null -ne $previousAccount){$restartCount=[int]$previousAccount.restartCount}}
        elseif($null -eq $worker){$worker=Start-AccountWorker $slot $ClientExecutablePath $token;$startedAccounts[$slot.id]=$true;$pidValue=$worker.Id;$slotHealth='Degraded';$reason='HealthProbeFailed';$restartCount=if($null -eq $previousAccount){1}else{[int]$previousAccount.restartCount+1}}
        else{$startedAccounts[$slot.id]=$true;$pidValue=$worker.Id;if($null -ne $previousAccount){$restartCount=[int]$previousAccount.restartCount}}

        if($pidValue -gt 0 -and (Test-OneBotLoopbackTcpReachable -OneBotUrl $slot.oneBotUrl) -eq $false){$slotHealth='Unavailable';$reason='OneBotUnavailable'}
        elseif($pidValue -gt 0){
          $snapshot=Read-AccountRuntimeHealthSnapshot -StorageRoot $slot.storageRoot -AccountId $slot.id
          if($null -eq $snapshot){$slotHealth='Degraded';$reason='HealthProbeFailed'}
          else{
            $components=$snapshot.components
            $unavailable=@($components|Where-Object{$_.health -eq 'unavailable'}|Select-Object -First 1)
            $degraded=@($components|Where-Object{$_.health -eq 'degraded'}|Select-Object -First 1)
            if($unavailable.Count -gt 0){$slotHealth='Unavailable';$reason=$unavailable[0].reason}
            elseif($degraded.Count -gt 0){$slotHealth='Degraded';$reason=$degraded[0].reason}
            else{$slotHealth='Healthy';$reason=$components[0].reason}
          }
        }
      }
    }
    $accounts[$slot.id]=[ordered]@{id=$slot.id;pid=$pidValue;health=$slotHealth;failures=0;restartCount=$restartCount;draining=$false;activeCount=0;reason=$reason;components=$components}
  }
  $health=@{};foreach($entry in $accounts.GetEnumerator()){$health[$entry.Key]=$entry.Value.health}
  $safe=[ordered]@{overall=Get-OverallStatus $health;accounts=$accounts;reason=if($DryRun){'DependencyUnavailable'}else{'None'};observedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')}
  Write-SafeJson $safe $StatePath;Write-SafeJson $safe $StatusPath
  $previousState=$safe
  if(-not $Once){Start-Sleep -Seconds ([Math]::Max(1,$PollSeconds))}
} while(-not $Once)
