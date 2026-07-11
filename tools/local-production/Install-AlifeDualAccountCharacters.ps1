[CmdletBinding()]
param(
    [string]$SourceStorageRoot = 'D:\Alife\Storage',
    [string]$AccountAStorageRoot = 'D:\Alife\storage\account-a',
    [string]$AccountBStorageRoot = 'D:\Alife\storage\account-b',
    [switch]$Install
)

$ErrorActionPreference = 'Stop'

function Resolve-FullPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw 'storage root is required'
    }
    return [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
}

function Assert-ChildPath([string]$Root, [string]$Child) {
    $prefix = (Resolve-FullPath $Root) + '\'
    if (-not (Resolve-FullPath $Child).StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'path escaped assigned storage root'
    }
}

function Read-CharacterIndex([string]$Source, [string]$ExpectedName) {
    $index = Join-Path $Source 'index.json'
    if (-not (Test-Path -LiteralPath $index -PathType Leaf)) {
        throw 'character index missing'
    }
    $value = Get-Content -LiteralPath $index -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]$value.Name -cne $ExpectedName) {
        throw 'character name mismatch'
    }
}

function New-InstallEntry(
    [string]$Label,
    [string]$Name,
    [string]$OtherName,
    [string]$SourceRoot,
    [string]$DestinationRoot
) {
    $characterRoot = Join-Path $DestinationRoot 'Character'
    return [pscustomobject]@{
        Label = $Label
        Name = $Name
        Source = Join-Path $SourceRoot ('Character\' + $Name)
        DestinationRoot = $DestinationRoot
        CharacterRoot = $characterRoot
        Destination = Join-Path $characterRoot $Name
        OppositeDestination = Join-Path $characterRoot $OtherName
        Staging = $null
    }
}

function Test-InstallEntry($Entry, [string]$SourceRoot) {
    $script:safeStage = 'validate_' + $Entry.Label + '_source_path'
    Assert-ChildPath $SourceRoot $Entry.Source
    $script:safeStage = 'validate_' + $Entry.Label + '_destination_paths'
    Assert-ChildPath $Entry.DestinationRoot $Entry.CharacterRoot
    Assert-ChildPath $Entry.DestinationRoot $Entry.Destination
    $expectedSource = Join-Path $SourceRoot ('Character\' + $Entry.Name)
    $sourceMapped = $Entry.Source -ceq $expectedSource
    $script:safeStage = 'validate_' + $Entry.Label + '_source_directory_mapped_' + ([string]$sourceMapped).ToLowerInvariant()
    if (-not (Test-Path -LiteralPath $Entry.Source -PathType Container)) {
        throw 'character source missing'
    }
    $script:safeStage = 'validate_' + $Entry.Label + '_index'
    Read-CharacterIndex $Entry.Source $Entry.Name
    $script:safeStage = 'validate_' + $Entry.Label + '_opposite_absent'
    if (Test-Path -LiteralPath $Entry.OppositeDestination) {
        throw 'opposite character exists in assigned account'
    }
}

function Install-Entry($Entry) {
    New-Item -ItemType Directory -Path $Entry.CharacterRoot -Force | Out-Null
    $Entry.Staging = Join-Path $Entry.CharacterRoot ('.install-' + [guid]::NewGuid().ToString('N'))
    Assert-ChildPath $Entry.CharacterRoot $Entry.Staging
    Copy-Item -LiteralPath $Entry.Source -Destination $Entry.Staging -Recurse -Force
    Read-CharacterIndex $Entry.Staging $Entry.Name

    $backup = $null
    if (Test-Path -LiteralPath $Entry.Destination) {
        $stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss-fff') + '-' + [guid]::NewGuid().ToString('N')
        $backup = Join-Path $Entry.DestinationRoot ('CharacterBackups\' + $stamp + '\' + $Entry.Name)
        Assert-ChildPath $Entry.DestinationRoot $backup
        New-Item -ItemType Directory -Path (Split-Path -Parent $backup) -Force | Out-Null
        Move-Item -LiteralPath $Entry.Destination -Destination $backup
    }

    try {
        Move-Item -LiteralPath $Entry.Staging -Destination $Entry.Destination
        $Entry.Staging = $null
    }
    catch {
        if ($null -ne $backup -and (Test-Path -LiteralPath $backup) -and -not (Test-Path -LiteralPath $Entry.Destination)) {
            Move-Item -LiteralPath $backup -Destination $Entry.Destination
        }
        throw
    }
}

$safeStage = 'resolve_roots'
try {
    $sourceRoot = Resolve-FullPath $SourceStorageRoot
    $accountARoot = Resolve-FullPath $AccountAStorageRoot
    $accountBRoot = Resolve-FullPath $AccountBStorageRoot
    if ($accountARoot -eq $accountBRoot) {
        throw 'account storage roots must be distinct'
    }

    $safeStage = 'create_entries'
    $mioName = [string][char]0x771F + [string][char]0x592E
    $xiaYuName = [string][char]0x590F + [string][char]0x7FBD
    $entries = @(
        (New-InstallEntry 'account-a' $mioName $xiaYuName $sourceRoot $accountARoot),
        (New-InstallEntry 'account-b' $xiaYuName $mioName $sourceRoot $accountBRoot)
    )
    foreach ($entry in $entries) {
        $safeStage = 'validate_' + $entry.Label
        Test-InstallEntry $entry $sourceRoot
    }

    if (-not $Install) {
        foreach ($entry in $entries) {
            Write-Output ($entry.Label + ' validated=true installed=false')
        }
        exit 0
    }

    try {
        foreach ($entry in $entries) {
            $safeStage = 'install_' + $entry.Label
            Install-Entry $entry
            Write-Output ($entry.Label + ' validated=true installed=true')
        }
    }
    finally {
        foreach ($entry in $entries) {
            if ($null -ne $entry.Staging -and (Test-Path -LiteralPath $entry.Staging)) {
                Remove-Item -LiteralPath $entry.Staging -Recurse -Force
            }
        }
    }
}
catch {
    Write-Output ('character_install=FAIL reason=validation_or_install_failed stage=' + $safeStage)
    exit 1
}
