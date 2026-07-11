param([string]$PlanPath=$env:ALIFE_LOCAL_PRODUCTION_PLAN,[switch]$Once,[string]$StatusPath=(Join-Path $PSScriptRoot 'local-production-status.json'),[string]$StatePath=(Join-Path $PSScriptRoot 'local-production-state.json'),[switch]$DryRun,[int]$PollSeconds=5)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalProduction.Configuration.psm1') -Force
if([string]::IsNullOrWhiteSpace($PlanPath)){throw 'PlanPath is required.'}
function Write-SafeJson($Value,[string]$Path){$parent=Split-Path -Parent $Path;if($parent){[IO.Directory]::CreateDirectory($parent)|Out-Null};$temp="$Path.tmp";$Value|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $temp -Encoding UTF8;Move-Item -LiteralPath $temp -Destination $Path -Force}
$plan=Read-LocalProductionPlan (Get-Content -LiteralPath $PlanPath -Raw)
do {
  $accounts=[ordered]@{}
  foreach($slot in $plan.accounts){$accounts[$slot.id]=[ordered]@{id=$slot.id;pid=0;health=if($DryRun){'Degraded'}else{'Unavailable'};failures=0;restartCount=0;draining=$false;activeCount=0;reason=if($DryRun){'DependencyUnavailable'}else{'ConfigurationRejected'}}}
  $health=@{};foreach($entry in $accounts.GetEnumerator()){$health[$entry.Key]=$entry.Value.health}
  $safe=[ordered]@{overall=Get-OverallStatus $health;accounts=$accounts;reason=if($DryRun){'DependencyUnavailable'}else{'ConfigurationRejected'};observedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')}
  Write-SafeJson $safe $StatePath;Write-SafeJson $safe $StatusPath
  if(-not $Once){Start-Sleep -Seconds ([Math]::Max(1,$PollSeconds))}
} while(-not $Once)
