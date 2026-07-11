param([string]$PlanPath=$env:ALIFE_LOCAL_PRODUCTION_PLAN,[string]$ClientExecutablePath,[switch]$Once,[string]$StatusPath=(Join-Path $PSScriptRoot 'local-production-status.json'),[string]$StatePath=(Join-Path $PSScriptRoot 'local-production-state.json'),[switch]$DryRun,[int]$PollSeconds=5)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalProduction.Configuration.psm1') -Force
if([string]::IsNullOrWhiteSpace($PlanPath)){throw 'PlanPath is required.'}
function Write-SafeJson($Value,[string]$Path){$parent=Split-Path -Parent $Path;if($parent){[IO.Directory]::CreateDirectory($parent)|Out-Null};$temp="$Path.tmp";$Value|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $temp -Encoding UTF8;Move-Item -LiteralPath $temp -Destination $Path -Force}
function Start-AccountWorker($Slot,[string]$Executable,[string]$Token){
  $start=New-Object Diagnostics.ProcessStartInfo;$start.FileName=$Executable;$start.UseShellExecute=$false;$start.CreateNoWindow=$true
  $start.EnvironmentVariables['ALIFE_RUNTIME_PATH']=$Slot.runtimeRoot;$start.EnvironmentVariables['ALIFE_STORAGE_PATH']=$Slot.storageRoot;$start.EnvironmentVariables['ALIFE_TEMP_PATH']=$Slot.tempRoot
  $start.EnvironmentVariables['ALIFE_WEBVIEW2_USER_DATA_FOLDER']=(Join-Path $Slot.runtimeRoot 'webview2');$start.EnvironmentVariables['ALIFE_ONEBOT_URL']=$Slot.oneBotUrl;$start.EnvironmentVariables['ALIFE_ONEBOT_TOKEN']=$Token
  [Diagnostics.Process]::Start($start)
}
$repoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if([string]::IsNullOrWhiteSpace($ClientExecutablePath)){$ClientExecutablePath=Join-Path $repoRoot 'Outputs\Alife.Client\Alife.Client.exe'}
$plan=Read-LocalProductionPlan (Get-Content -LiteralPath $PlanPath -Raw)
do {
  $accounts=[ordered]@{}
  foreach($slot in $plan.accounts){
    $pidValue=0;$slotHealth='Degraded';$reason='DependencyUnavailable';$restartCount=0
    if(-not $DryRun){
      $token=[Environment]::GetEnvironmentVariable($slot.oneBotTokenEnvironmentVariable,'User')
      if([string]::IsNullOrWhiteSpace($token)){$slotHealth='Unavailable';$reason='ConfigurationRejected'}
      elseif(-not (Test-Path -LiteralPath $ClientExecutablePath -PathType Leaf)){$slotHealth='Unavailable';$reason='DependencyUnavailable'}
      else{$worker=Start-AccountWorker $slot $ClientExecutablePath $token;$pidValue=$worker.Id;$slotHealth='Degraded';$reason='HealthProbeFailed';$restartCount=1}
    }
    $accounts[$slot.id]=[ordered]@{id=$slot.id;pid=$pidValue;health=$slotHealth;failures=0;restartCount=$restartCount;draining=$false;activeCount=0;reason=$reason}
  }
  $health=@{};foreach($entry in $accounts.GetEnumerator()){$health[$entry.Key]=$entry.Value.health}
  $safe=[ordered]@{overall=Get-OverallStatus $health;accounts=$accounts;reason=if($DryRun){'DependencyUnavailable'}else{'None'};observedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')}
  Write-SafeJson $safe $StatePath;Write-SafeJson $safe $StatusPath
  if(-not $Once){Start-Sleep -Seconds ([Math]::Max(1,$PollSeconds))}
} while(-not $Once)
