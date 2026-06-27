param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$ErrorActionPreference = 'Stop'

$storageRoot = Join-Path $ProjectRoot 'Storage'
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupRoot = Join-Path $storageRoot "Backups\cross-agent-memory-clean-$timestamp"

function New-Text([int[]]$Codes) {
    return -join ($Codes | ForEach-Object { [char]$_ })
}

function Join-RegexAlternation([string[]]$Values) {
    return ($Values | ForEach-Object { [regex]::Escape($_) }) -join '|'
}

$xiayu = New-Text @(0x590F, 0x7FBD)
$zhenyang = New-Text @(0x771F, 0x592E)
$mixu = New-Text @(0x54AA, 0x7EEA)
$amamiyaMixu = New-Text @(0x96E8, 0x5BAB, 0x54AA, 0x7EEA)
$fromLabel = New-Text @(0x6765, 0x81EA)
$messageLabel = New-Text @(0x7684, 0x6D88, 0x606F)

$targets = @(
    @{
        Character = $xiayu
        Names = @($zhenyang, $mixu, $amamiyaMixu)
    },
    @{
        Character = $zhenyang
        Names = @($xiayu)
    }
)

$contextPattern = Join-RegexAlternation @(
    (New-Text @(0x70E4, 0x9C7C)),
    (New-Text @(0x70E4, 0x4E32)),
    (New-Text @(0x8111, 0x82B1)),
    (New-Text @(0x5C0F, 0x5403, 0x8857)),
    (New-Text @(0x751C, 0x54C1, 0x5E97)),
    (New-Text @(0x8349, 0x8393, 0x6155, 0x65AF)),
    (New-Text @(0x8FF7, 0x5BAB)),
    (New-Text @(0x5409, 0x4ED6, 0x7FA4, 0x767D, 0x540D, 0x5355))
)

function Test-CrossAgentText([string]$Text, [string]$namePattern) {
    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $false
    }

    return $Text -match $namePattern `
        -or $Text -match '<\s*call\b' `
        -or $Text -match ('\[' + [regex]::Escape($fromLabel) + '\s*(' + $namePattern + ')\s*' + [regex]::Escape($messageLabel) + '\]')
}

function Remove-CrossAgentLines([string]$Text, [string]$namePattern) {
    if ([string]::IsNullOrEmpty($Text)) {
        return $Text
    }

    $hasCrossAgent = Test-CrossAgentText $Text $namePattern
    $lines = $Text -split "`r?`n"
    $kept = New-Object System.Collections.Generic.List[string]
    $skipCallBlock = $false

    foreach ($line in $lines) {
        if ($skipCallBlock) {
            if ($line -match '</\s*call\s*>') {
                $skipCallBlock = $false
            }
            continue
        }

        if ($line -match '<\s*call\b') {
            if ($line -notmatch '</\s*call\s*>') {
                $skipCallBlock = $true
            }
            continue
        }

        if ($line -match ('\[' + [regex]::Escape($fromLabel) + '\s*(' + $namePattern + ')\s*' + [regex]::Escape($messageLabel) + '\]')) {
            continue
        }

        if ($line -match $namePattern) {
            continue
        }

        if ($line -match $contextPattern) {
            continue
        }

        $kept.Add($line)
    }

    return ($kept -join "`r`n").Trim()
}

$report = [ordered]@{
    BackupRoot = $backupRoot
    Characters = @()
}

foreach ($target in $targets) {
    $characterName = [string]$target.Character
    $memoryRoot = Join-Path $storageRoot "Character\$characterName\Memory"
    if (-not (Test-Path -LiteralPath $memoryRoot)) {
        continue
    }

    $namePattern = Join-RegexAlternation ([string[]]$target.Names)
    $characterBackupRoot = Join-Path $backupRoot "$characterName\Memory"
    New-Item -ItemType Directory -Path $characterBackupRoot -Force | Out-Null
    Copy-Item -LiteralPath $memoryRoot -Destination (Split-Path -Parent $characterBackupRoot) -Recurse -Force

    $changedFiles = 0
    $removedBackupFiles = 0
    $removedIndexFiles = 0

    $historyPath = Join-Path $memoryRoot 'History.json'
    if (Test-Path -LiteralPath $historyPath) {
        $historyText = Get-Content -LiteralPath $historyPath -Raw -Encoding UTF8
        $history = $historyText | ConvertFrom-Json
        $historyChanged = $false

        foreach ($entry in $history) {
            if ($null -eq $entry.Content) {
                continue
            }

            $oldContent = [string]$entry.Content
            $newContent = Remove-CrossAgentLines $oldContent $namePattern
            if ($newContent -ne $oldContent) {
                $entry.Content = $newContent
                $historyChanged = $true
            }
        }

        if ($historyChanged) {
            $history | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $historyPath -Encoding UTF8 -NoNewline
            $changedFiles++
        }
    }

    $textFiles = Get-ChildItem -LiteralPath $memoryRoot -Recurse -File |
        Where-Object { $_.Extension -eq '.txt' }

    foreach ($file in $textFiles) {
        $oldContent = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
        $newContent = Remove-CrossAgentLines $oldContent $namePattern
        if ($newContent -ne $oldContent) {
            Set-Content -LiteralPath $file.FullName -Value $newContent -Encoding UTF8 -NoNewline
            $changedFiles++
        }
    }

    $backupTextFiles = Get-ChildItem -LiteralPath $memoryRoot -Recurse -File |
        Where-Object { $_.Name -like '*.bak*' }

    foreach ($file in $backupTextFiles) {
        $content = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
        if (Test-CrossAgentText $content $namePattern) {
            Remove-Item -LiteralPath $file.FullName -Force
            $removedBackupFiles++
        }
    }

    $indexFiles = Get-ChildItem -LiteralPath $memoryRoot -File -Filter 'memory_index*'
    foreach ($file in $indexFiles) {
        Remove-Item -LiteralPath $file.FullName -Force
        $removedIndexFiles++
    }

    $report.Characters += [ordered]@{
        Character = $characterName
        ChangedFiles = $changedFiles
        RemovedBackupFiles = $removedBackupFiles
        RemovedIndexFiles = $removedIndexFiles
    }
}

$report | ConvertTo-Json -Depth 8
