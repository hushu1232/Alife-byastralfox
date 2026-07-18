$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalProduction.Configuration.psm1')

function Assert-Equal($Actual, $Expected) { if ($Actual -ne $Expected) { throw "Expected '$Expected', got '$Actual'." } }
function Assert-Throws([scriptblock]$Action, [string]$Text) { try { & $Action; throw 'Expected throw.' } catch { if ($_.Exception.Message -notmatch $Text) { throw } } }

Assert-Equal (Get-OverallStatus @{ 'account-a' = 'Healthy'; 'account-b' = 'Degraded' }) 'degraded'
Assert-Equal (Get-OverallStatus @{ 'account-a' = 'Unavailable'; 'account-b' = 'Unavailable' }) 'unavailable'
Assert-Throws { Read-LocalProductionPlan '{"accounts":[{"id":"account-a","oneBotUrl":"ws://0.0.0.0:3001"}]}' } 'loopback'
$operatorPlan = Read-LocalProductionPlan '{"accounts":[{"id":"account-a","oneBotUrl":"ws://127.0.0.1:3001","qZoneLoopbackOperatorUrl":"http://127.0.0.1:5101/qzone/"},{"id":"account-b","oneBotUrl":"ws://127.0.0.1:3002","qZoneLoopbackOperatorUrl":"http://localhost:5102/qzone/"}]}'
Assert-Equal $operatorPlan.accounts[0].qZoneLoopbackOperatorUrl 'http://127.0.0.1:5101/qzone/'
Assert-Throws { Read-LocalProductionPlan '{"accounts":[{"id":"account-a","oneBotUrl":"ws://127.0.0.1:3001","qZoneLoopbackOperatorUrl":"http://example.invalid:5101/qzone/"},{"id":"account-b","oneBotUrl":"ws://127.0.0.1:3002","qZoneLoopbackOperatorUrl":"http://127.0.0.1:5102/qzone/"}]}' } 'operator'
Assert-Throws { Read-LocalProductionPlan '{"accounts":[{"id":"account-a","oneBotUrl":"ws://127.0.0.1:3001","qZoneLoopbackOperatorUrl":"http://127.0.0.1:5101/qzone"},{"id":"account-b","oneBotUrl":"ws://127.0.0.1:3002","qZoneLoopbackOperatorUrl":"http://127.0.0.1:5101/qzone/"}]}' } 'unique'
$slashlessOperatorPlan = Read-LocalProductionPlan '{"accounts":[{"id":"account-a","oneBotUrl":"ws://127.0.0.1:3001","qZoneLoopbackOperatorUrl":"http://127.0.0.1:5101/qzone"},{"id":"account-b","oneBotUrl":"ws://127.0.0.1:3002","qZoneLoopbackOperatorUrl":"http://127.0.0.1:5102/qzone"}]}'
Assert-Equal $slashlessOperatorPlan.accounts[0].qZoneLoopbackOperatorUrl 'http://127.0.0.1:5101/qzone/'
Assert-Equal $slashlessOperatorPlan.accounts[1].qZoneLoopbackOperatorUrl 'http://127.0.0.1:5102/qzone/'
$lifecycleHostPath = Join-Path $PSScriptRoot '..\..\sources\Alife\Alife.Client\QZoneLoopbackOperatorLifecycleHost.cs'
$lifecycleHostSource = Get-Content -LiteralPath $lifecycleHostPath -Raw
if ($lifecycleHostSource -notmatch 'ALIFE_QZONE_LOOPBACK_OPERATOR_URL') { throw 'Character lifecycle host must consume the supervisor-provided operator endpoint.' }
$slot=[pscustomobject]@{id='account-a';drainTimeoutSeconds=90}
$now=[DateTimeOffset]::UtcNow
Assert-Equal (Invoke-AccountRecovery -Slot $slot -ActiveWorkCount 1 -Now $now).Action 'drain'
Assert-Equal (Invoke-AccountRecovery -Slot $slot -ActiveWorkCount 0 -Now $now).Action 'restart-worker'
