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
if ($supervisorSource -notmatch '\$start\.Environment\[''ALIFE_QZONE_LOOPBACK_OPERATOR_URL''\]=\$Slot\.qZoneLoopbackOperatorUrl') { throw 'Supervisor must inject the character-local operator endpoint into the child process environment.' }
if ($supervisorSource -notmatch '\$start\.Environment\.Remove\(''ALIFE_ACCOUNT_A_ONEBOT_TOKEN''\)') { throw 'Supervisor must remove account-scoped token names before starting a role process.' }
if ($supervisorSource -notmatch 'Get-Process -Id \$pidValue') { throw 'Supervisor must retain a still-running account worker instead of starting duplicates on every poll.' }
if ($supervisorSource -notmatch '\$start\.Environment\[''ALIFE_STORAGE_PATH''\]=\$Slot\.storageRoot') { throw 'Supervisor must place the account-local storage root directly in the child process environment.' }
if ($supervisorSource -notmatch '\$start\.Environment\[''ALIFE_ACCOUNT_ID''\]=\$Slot\.id') { throw 'Supervisor must place the account identity directly in the child process environment.' }
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

$restartScriptPath = Join-Path $PSScriptRoot 'Restart-AlifeLocalAccount.ps1'
$restartSource = Get-Content -LiteralPath $restartScriptPath -Raw
if ($restartSource -notmatch 'CommandLine\.IndexOf\(\$clientDll') { throw 'Single-account restart must match the exact account client command line.' }
if ($restartSource -match 'NapCat') { throw 'Single-account restart must not manage NapCat.' }

$restartTestRoot = Join-Path ([IO.Path]::GetTempPath()) ("alife-account-restart-" + [Guid]::NewGuid().ToString('N'))
try {
    [IO.Directory]::CreateDirectory($restartTestRoot) | Out-Null
    $runtimeA = Join-Path $restartTestRoot 'runtime-a'
    $runtimeB = Join-Path $restartTestRoot 'runtime-b'
    $planPath = Join-Path $restartTestRoot 'accounts.json'
    $deployFile = Join-Path $restartTestRoot 'Alife.Function.QChat.dll'
    Set-Content -LiteralPath $deployFile -Value 'fixture' -Encoding UTF8
    @{
        accounts = @(
            @{
                id = 'account-a'
                oneBotUrl = 'ws://127.0.0.1:3001'
                qZoneLoopbackOperatorUrl = 'http://127.0.0.1:5101/qzone/'
                runtimeRoot = $runtimeA
                storageRoot = Join-Path $restartTestRoot 'storage-a'
                tempRoot = Join-Path $restartTestRoot 'temp-a'
                oneBotTokenEnvironmentVariable = 'ALIFE_TEST_ACCOUNT_A_TOKEN'
            },
            @{
                id = 'account-b'
                oneBotUrl = 'ws://127.0.0.1:3002'
                qZoneLoopbackOperatorUrl = 'http://127.0.0.1:5102/qzone/'
                runtimeRoot = $runtimeB
                storageRoot = Join-Path $restartTestRoot 'storage-b'
                tempRoot = Join-Path $restartTestRoot 'temp-b'
                oneBotTokenEnvironmentVariable = 'ALIFE_TEST_ACCOUNT_B_TOKEN'
            }
        )
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $planPath -Encoding UTF8

    $restartPlan = & $restartScriptPath -AccountId account-b -PlanPath $planPath -DeployPath $deployFile -DryRun
    Assert-Equal $restartPlan.AccountId 'account-b'
    Assert-Equal $restartPlan.ClientDll (Join-Path $runtimeB 'ClientBuild\Alife.Client.dll')
    Assert-Equal $restartPlan.OneBotUrl 'ws://127.0.0.1:3002'
    Assert-Equal $restartPlan.PlannedDeployCount 1
    Assert-Equal $restartPlan.DryRun $true
    Assert-Equal (Test-Path -LiteralPath (Join-Path $runtimeB 'ClientBuild')) $false
}
finally {
    Remove-Item -LiteralPath $restartTestRoot -Recurse -Force -ErrorAction SilentlyContinue
}
