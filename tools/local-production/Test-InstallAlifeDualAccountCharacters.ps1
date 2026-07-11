[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-False([bool]$Condition, [string]$Message) {
    if ($Condition) { throw $Message }
}

function Assert-Equal([string]$Expected, [string]$Actual, [string]$Message) {
    if ($Expected -cne $Actual) { throw $Message }
}

function New-FakeCharacter(
    [string]$StorageRoot,
    [string]$Name,
    [string]$Sentinel
) {
    $root = Join-Path $StorageRoot ('Character\' + $Name)
    New-Item -ItemType Directory -Path (Join-Path $root 'Configuration') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $root 'Memory') -Force | Out-Null
    [pscustomobject]@{
        Name = $Name
        AutoActivate = $true
        Modules = @('Fake.Module')
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root 'index.json') -Encoding UTF8
    [pscustomobject]@{ Enabled = $true } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'Configuration\fake.json') -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $root 'Memory\sentinel.txt') -Value $Sentinel -NoNewline -Encoding UTF8
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$installer = Join-Path $PSScriptRoot 'Install-AlifeDualAccountCharacters.ps1'
$testRoot = Join-Path $repositoryRoot ('.tmp\character-installer-tests\' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $testRoot 'source'
$accountA = Join-Path $testRoot 'account-a'
$accountB = Join-Path $testRoot 'account-b'
$mioName = [string][char]0x771F + [string][char]0x592E
$xiaYuName = [string][char]0x590F + [string][char]0x7FBD

try {
    Assert-True (Test-Path -LiteralPath $installer -PathType Leaf) 'installer script is missing'
    $installerText = Get-Content -LiteralPath $installer -Raw -Encoding UTF8
    Assert-False ($installerText.IndexOf('D:\\Alife', [StringComparison]::Ordinal) -ge 0) 'default paths contain doubled separators'
    New-FakeCharacter $source $mioName 'mio-memory-sentinel'
    New-FakeCharacter $source $xiaYuName 'xiayu-memory-sentinel'

    $allOutput = [System.Collections.Generic.List[string]]::new()
    $dryOutput = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer `
        -SourceStorageRoot $source -AccountAStorageRoot $accountA -AccountBStorageRoot $accountB 2>&1)
    Assert-Equal '0' ([string]$LASTEXITCODE) 'dry-run failed'
    foreach ($line in $dryOutput) { $allOutput.Add([string]$line) }
    Assert-False (Test-Path -LiteralPath (Join-Path $accountA 'Character')) 'dry-run changed account-a'
    Assert-False (Test-Path -LiteralPath (Join-Path $accountB 'Character')) 'dry-run changed account-b'

    $installOutput = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer `
        -SourceStorageRoot $source -AccountAStorageRoot $accountA -AccountBStorageRoot $accountB -Install 2>&1)
    Assert-Equal '0' ([string]$LASTEXITCODE) 'install failed'
    foreach ($line in $installOutput) { $allOutput.Add([string]$line) }

    $aInstance = Join-Path $accountA ('Character\' + $mioName)
    $bInstance = Join-Path $accountB ('Character\' + $xiaYuName)
    Assert-True (Test-Path -LiteralPath (Join-Path $aInstance 'index.json')) 'account-a instance missing'
    Assert-True (Test-Path -LiteralPath (Join-Path $bInstance 'index.json')) 'account-b instance missing'
    Assert-False (Test-Path -LiteralPath (Join-Path $accountA ('Character\' + $xiaYuName))) 'account-a contains cross-instance'
    Assert-False (Test-Path -LiteralPath (Join-Path $accountB ('Character\' + $mioName))) 'account-b contains cross-instance'
    Assert-Equal 'mio-memory-sentinel' (Get-Content -LiteralPath (Join-Path $aInstance 'Memory\sentinel.txt') -Raw) 'account-a content mismatch'
    Assert-Equal 'xiayu-memory-sentinel' (Get-Content -LiteralPath (Join-Path $bInstance 'Memory\sentinel.txt') -Raw) 'account-b content mismatch'

    $mioSource = Join-Path $source ('Character\' + $mioName)
    Set-Content -LiteralPath (Join-Path $mioSource 'Memory\sentinel.txt') -Value 'mio-memory-updated' -NoNewline -Encoding UTF8
    $secondOutput = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer `
        -SourceStorageRoot $source -AccountAStorageRoot $accountA -AccountBStorageRoot $accountB -Install 2>&1)
    Assert-Equal '0' ([string]$LASTEXITCODE) 'second install failed'
    foreach ($line in $secondOutput) { $allOutput.Add([string]$line) }
    Assert-Equal 'mio-memory-updated' (Get-Content -LiteralPath (Join-Path $aInstance 'Memory\sentinel.txt') -Raw) 'replacement content mismatch'
    $backupIndexes = @(Get-ChildItem -LiteralPath (Join-Path $accountA 'CharacterBackups') -Filter 'index.json' -File -Recurse)
    Assert-True ($backupIndexes.Count -ge 1) 'account-a backup missing'

    $beforeFailure = Get-FileHash -LiteralPath (Join-Path $aInstance 'index.json') -Algorithm SHA256
    $badIndex = [pscustomobject]@{ Name = 'wrong-name'; AutoActivate = $true; Modules = @() } | ConvertTo-Json
    Set-Content -LiteralPath (Join-Path $mioSource 'index.json') -Value $badIndex -Encoding UTF8
    $failureOutput = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer `
        -SourceStorageRoot $source -AccountAStorageRoot $accountA -AccountBStorageRoot $accountB -Install 2>&1)
    Assert-False ($LASTEXITCODE -eq 0) 'invalid metadata was accepted'
    foreach ($line in $failureOutput) { $allOutput.Add([string]$line) }
    $afterFailure = Get-FileHash -LiteralPath (Join-Path $aInstance 'index.json') -Algorithm SHA256
    Assert-Equal $beforeFailure.Hash $afterFailure.Hash 'failed validation changed destination'

    $outputText = $allOutput -join [Environment]::NewLine
    foreach ($unsafe in @($testRoot, 'Token', 'Authorization', 'BotId', 'mio-memory-sentinel', 'xiayu-memory-sentinel')) {
        Assert-False ($outputText.IndexOf($unsafe, [StringComparison]::OrdinalIgnoreCase) -ge 0) 'installer output exposed unsafe detail'
    }

    Write-Output 'character_installer_tests=PASS'
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
