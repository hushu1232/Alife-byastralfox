$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Start-NapCatDualAccount.ps1')
function Assert-Equal($Actual,$Expected){if($Actual-ne$Expected){throw "Expected '$Expected', got '$Actual'."}}
$root=Join-Path ([IO.Path]::GetTempPath()) ('napcat-dual-'+[Guid]::NewGuid().ToString('N'))
try {
  $config=Join-Path $root 'config';$boot=Join-Path $root 'bootmain';[IO.Directory]::CreateDirectory($config)|Out-Null;[IO.Directory]::CreateDirectory($boot)|Out-Null
  New-Item -ItemType File -Path (Join-Path $boot 'NapCatWinBootMain.exe')|Out-Null
  '{"network":{"websocketServers":[{"host":"127.0.0.1","port":3001}]}}'|Set-Content -LiteralPath (Join-Path $config 'onebot11_11111.json')
  '{"network":{"websocketServers":[{"host":"127.0.0.1","port":3002}]}}'|Set-Content -LiteralPath (Join-Path $config 'onebot11_22222.json')
  $plan=Get-NapCatDualAccountPlan -NapCatRoot $root
  Assert-Equal $plan.Count 2;Assert-Equal (($plan.Port|Sort-Object)-join ',') '3001,3002'
} finally {if(Test-Path -LiteralPath $root){Remove-Item -LiteralPath $root -Recurse -Force}}
