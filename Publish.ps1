#Requires -Version 5.1
<#
.SYNOPSIS
    Alife Publish Script - Shortcut for release publish
.DESCRIPTION
    Calls Build.ps1 to publish to the shared distribution directory.
#>

$Root = $PSScriptRoot

Write-Host "===================================================" -ForegroundColor Cyan
Write-Host "[Alife] Starting Publish..."                         -ForegroundColor Cyan
Write-Host "===================================================" -ForegroundColor Cyan
Write-Host ""

& (Join-Path $Root "Build.ps1") -Mode publish -OutputDir (Join-Path $Root "..\Shared\Alife\Outputs")

Write-Host ""
Write-Host "Press any key to exit..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
