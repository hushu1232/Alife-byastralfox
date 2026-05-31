#Requires -Version 5.1
<#
.SYNOPSIS
    Alife Build Script - Build all projects and sync plugins
.DESCRIPTION
    Builds Client, DeskPet, and all Function plugins.
    Copies plugin sources and syncs shared NuGet dependencies.
.PARAMETER PluginTarget
    Output directory for plugins. Defaults to Storage\Plugins.
#>

param(
    [string]$PluginTarget = ""
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$Src = Join-Path $Root "Sources"
$Out = Join-Path $Root "Outputs"

if (-not $PluginTarget) {
    $PluginTarget = Join-Path $Root "Storage\Plugins"
}

Write-Host "[Build] Plugin target: $PluginTarget" -ForegroundColor Cyan
Write-Host ""

# ============================================================
# Step 1/3: Build
# ============================================================
Write-Host "[1/3] Building..." -ForegroundColor Yellow

dotnet build (Join-Path $Src "Alife\Alife.Client\Alife.Client.csproj") -c Release -nologo --verbosity quiet
dotnet build (Join-Path $Src "Alife.DeskPet\Alife.DeskPet.Client\Alife.DeskPet.Client.csproj") -c Release -nologo --verbosity quiet

$functionDirs = Get-ChildItem (Join-Path $Src "Alife.Function") -Directory | Where-Object { $_.Name -match '^Alife\.Function\.' }
foreach ($dir in $functionDirs) {
    $csproj = Join-Path $dir.FullName "$($dir.Name).csproj"
    dotnet build $csproj -c Release -nologo --verbosity quiet
}

# ============================================================
# Step 2/3: Copy Function Sources
# ============================================================
Write-Host "[2/3] Copying Function sources..." -ForegroundColor Yellow

if (Test-Path $PluginTarget) {
    Write-Host "  Cleaning: $PluginTarget"
    Remove-Item $PluginTarget -Recurse -Force
}
New-Item -ItemType Directory -Path $PluginTarget -Force | Out-Null

foreach ($dir in $functionDirs) {
    $target = Join-Path $PluginTarget $dir.Name
    New-Item -ItemType Directory -Path $target -Force | Out-Null

    # Copy .cs files
    Get-ChildItem $dir.FullName -Filter "*.cs" -File | ForEach-Object {
        Copy-Item $_.FullName $target -Force
    }

    # Copy generated Razor .g.cs files (only if corresponding .razor exists)
    $generatedDir = Join-Path $dir.FullName "obj\Release\generated\Microsoft.CodeAnalysis.Razor.Compiler"
    if (Test-Path $generatedDir) {
        Get-ChildItem $generatedDir -Filter "*_razor.g.cs" -Recurse -File | ForEach-Object {
            $razorName = $_.Name -replace '_razor\.g\.cs$', ''
            $razorFile = Join-Path $dir.FullName "$razorName.razor"
            if (Test-Path $razorFile) {
                Copy-Item $_.FullName $target -Force
            } else {
                Write-Host "  [skip] $($_.Name)"
            }
        }
    }

    Write-Host "  [done] $($dir.Name)" -ForegroundColor Green
}

# ============================================================
# Step 3/3: Sync NuGet Dependencies
# ============================================================
Write-Host ""
Write-Host "[3/3] Syncing NuGet deps..." -ForegroundColor Yellow

$nuGetDir = Join-Path $PluginTarget "BaseDirectory"
New-Item -ItemType Directory -Path $nuGetDir -Force | Out-Null

$clientDir = Join-Path $Out "Alife.Client"
$functionOutputDirs = Get-ChildItem $Out -Directory | Where-Object { $_.Name -match '^Alife\.Function\.' }

foreach ($funcDir in $functionOutputDirs) {
    $files = Get-ChildItem $funcDir.FullName -Recurse -File
    foreach ($file in $files) {
        # Skip Alife.Function.* assemblies (already in plugin dir)
        if ($file.Name -match '^Alife\.Function\.') { continue }
        # Skip files that already exist in Client output
        if (Test-Path (Join-Path $clientDir $file.Name)) { continue }

        $relativePath = $file.FullName.Substring($funcDir.FullName.Length + 1)
        $destPath = Join-Path $nuGetDir $relativePath
        $destDir = Split-Path $destPath -Parent

        if (-not (Test-Path $destDir)) {
            New-Item $destDir -ItemType Directory -Force | Out-Null
        }

        Copy-Item $file.FullName $destPath -Force
        Write-Host "  [sync] $relativePath"
    }
}

Write-Host ""
Write-Host "[Build] Done." -ForegroundColor Green
Write-Host "  Plugins: $PluginTarget"
Write-Host "  NuGet:   $nuGetDir"
