$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalProduction.Configuration.psm1')

function Assert-Equal($Actual, $Expected) { if ($Actual -ne $Expected) { throw "Expected '$Expected', got '$Actual'." } }
function Assert-Throws([scriptblock]$Action, [string]$Text) { try { & $Action; throw 'Expected throw.' } catch { if ($_.Exception.Message -notmatch $Text) { throw } } }

Assert-Equal (Get-OverallStatus @{ 'account-a' = 'Healthy'; 'account-b' = 'Degraded' }) 'degraded'
Assert-Equal (Get-OverallStatus @{ 'account-a' = 'Unavailable'; 'account-b' = 'Unavailable' }) 'unavailable'
Assert-Throws { Read-LocalProductionPlan '{"accounts":[{"id":"account-a","oneBotUrl":"ws://0.0.0.0:3001"}]}' } 'loopback'
