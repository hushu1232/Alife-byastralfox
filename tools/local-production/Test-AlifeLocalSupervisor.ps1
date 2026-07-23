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
$supervisorPath = Join-Path $PSScriptRoot 'Start-AlifeLocalSupervisor.ps1'
$supervisorSource = Get-Content -LiteralPath $supervisorPath -Raw
if ($supervisorSource -notmatch 'GetEnvironmentVariable\(\$slot\.oneBotTokenEnvironmentVariable,''Process''\)') { throw 'Supervisor must fall back to the inherited process token when the user environment is unavailable.' }
if ($supervisorSource -notmatch '\$env:ALIFE_QZONE_LOOPBACK_OPERATOR_URL=\$Slot\.qZoneLoopbackOperatorUrl') { throw 'Supervisor must inject the character-local operator endpoint into the child process environment.' }
if ($supervisorSource -notmatch '\$env:ALIFE_ACCOUNT_A_ONEBOT_TOKEN=\$null') { throw 'Supervisor must remove account-scoped token names before starting a role process.' }
if ($supervisorSource -notmatch 'Get-Process -Id \$pidValue') { throw 'Supervisor must retain a still-running account worker instead of starting duplicates on every poll.' }
$slot=[pscustomobject]@{id='account-a';drainTimeoutSeconds=90}
$now=[DateTimeOffset]::UtcNow
Assert-Equal (Invoke-AccountRecovery -Slot $slot -ActiveWorkCount 1 -Now $now).Action 'drain'
Assert-Equal (Invoke-AccountRecovery -Slot $slot -ActiveWorkCount 0 -Now $now).Action 'restart-worker'

$runtimeHealthRoot = Join-Path ([IO.Path]::GetTempPath()) ("alife-runtime-health-" + [Guid]::NewGuid().ToString('N'))
try {
    [IO.Directory]::CreateDirectory($runtimeHealthRoot) | Out-Null
    @{
        version = 1
        account = 'account-a'
        components = @(@{ component = 'model'; health = 'unavailable'; reason = 'ModelAuthRejected' })
    } | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $runtimeHealthRoot 'runtime-health.json') -Encoding UTF8
    $snapshot = Read-AccountRuntimeHealthSnapshot -StorageRoot $runtimeHealthRoot -AccountId 'account-a'
    Assert-Equal $snapshot.components[0].reason 'ModelAuthRejected'
    Assert-Equal (Read-AccountRuntimeHealthSnapshot -StorageRoot $runtimeHealthRoot -AccountId 'account-b') $null

    @{
        version = 1
        account = 'account-a'
        components = @(@{ component = 'model'; health = 'unavailable'; reason = 'raw exception' })
    } | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $runtimeHealthRoot 'runtime-health.json') -Encoding UTF8
    Assert-Equal (Read-AccountRuntimeHealthSnapshot -StorageRoot $runtimeHealthRoot -AccountId 'account-a') $null
}
finally {
    Remove-Item -LiteralPath $runtimeHealthRoot -Recurse -Force -ErrorAction SilentlyContinue
}

$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
try {
    $listener.Start()
    $port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port
    Assert-Equal (Test-OneBotLoopbackTcpReachable -OneBotUrl ("ws://127.0.0.1:" + $port)) $true
}
finally {
    $listener.Stop()
}
