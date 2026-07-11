$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Initialize-NapCatDualAccountTokens.ps1')
function Assert-True($Value){if(-not $Value){throw 'Expected true.'}}
$root=Join-Path ([IO.Path]::GetTempPath()) ('napcat-token-'+[Guid]::NewGuid().ToString('N'))
try {
  [IO.Directory]::CreateDirectory($root)|Out-Null
  '{"network":{"websocketServers":[{"host":"127.0.0.1","port":3001,"token":""}]}}'|Set-Content -LiteralPath (Join-Path $root 'onebot11_11111.json')
  '{"network":{"websocketServers":[{"host":"127.0.0.1","port":3002,"token":""}]}}'|Set-Content -LiteralPath (Join-Path $root 'onebot11_22222.json')
  $preview=Initialize-NapCatDualAccountTokens -NapCatRoot $root -EnvironmentTarget Process
  Assert-True (@($preview|Where-Object tokenState -eq 'empty').Count-eq2)
  Initialize-NapCatDualAccountTokens -NapCatRoot $root -EnvironmentTarget Process -Apply|Out-Null
  Assert-True (-not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable('ALIFE_ACCOUNT_A_ONEBOT_TOKEN','Process')))
  Assert-True (-not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable('ALIFE_ACCOUNT_B_ONEBOT_TOKEN','Process')))
} finally {
  [Environment]::SetEnvironmentVariable('ALIFE_ACCOUNT_A_ONEBOT_TOKEN',$null,'Process');[Environment]::SetEnvironmentVariable('ALIFE_ACCOUNT_B_ONEBOT_TOKEN',$null,'Process')
  if(Test-Path -LiteralPath $root){Remove-Item -LiteralPath $root -Recurse -Force}
}
