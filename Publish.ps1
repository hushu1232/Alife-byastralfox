#Requires -Version 5.1
<#
.SYNOPSIS
    Alife Publish Script - Build + Publish release
.DESCRIPTION
    Builds all plugins, then publishes Client and DeskPet to output directory.
#>

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$Src = Join-Path $Root "Sources"
$DistDir = Join-Path $Root "..\输出文件\Alife\Outputs"
$PublishPlugins = Join-Path $Root "..\输出文件\Alife\Storage\Plugins"

Write-Host "===================================================" -ForegroundColor Cyan
Write-Host "[Alife] Starting Unified Publish Workflow..."       -ForegroundColor Cyan
Write-Host "===================================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Build plugins
Write-Host "[Step 1/3] Building plugins..." -ForegroundColor Yellow
& (Join-Path $Root "Build.ps1") -PluginTarget $PublishPlugins
Write-Host ""

# Step 2: Clean old publish dir
Write-Host "[Step 2/3] Cleaning $DistDir..." -ForegroundColor Yellow
if (Test-Path $DistDir) { Remove-Item $DistDir -Recurse -Force }
New-Item -ItemType Directory -Path $DistDir -Force | Out-Null

# Step 3: Publish applications
Write-Host "[Step 3/3] Publishing applications..." -ForegroundColor Yellow

Write-Host "  Publishing Alife.Client..."
dotnet publish (Join-Path $Src "Alife\Alife.Client\Alife.Client.csproj") `
    -c Release -o (Join-Path $DistDir "Alife.Client") --self-contained false

Write-Host "  Publishing Alife.DeskPet.Client..."
dotnet publish (Join-Path $Src "Alife.DeskPet\Alife.DeskPet.Client\Alife.DeskPet.Client.csproj") `
    -c Release -o (Join-Path $DistDir "Alife.DeskPet.Client") --self-contained false

# Clean runtime DLLs
Write-Host "  Cleaning runtime DLLs..."
$clientOut = Join-Path $DistDir "Alife.Client"
if (Test-Path $clientOut) {
    @("hostfxr.dll","hostpolicy.dll","coreclr.dll","clrjit.dll","createdump.exe") | ForEach-Object {
        Remove-Item (Join-Path $clientOut $_) -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ""
Write-Host "===================================================" -ForegroundColor Green
Write-Host "[Success] Release ready in: $DistDir"              -ForegroundColor Green
Write-Host "===================================================" -ForegroundColor Green
Write-Host "Press any key to exit..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
