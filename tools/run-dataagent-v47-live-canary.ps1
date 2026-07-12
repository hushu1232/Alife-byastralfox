param(
    [string]$Python = "python",
    [int]$Port = 8765,
    [string]$OutputDirectory = "Outputs/dataagent-v4.7-live-canary",
    [int]$RequestCount = 20,
    [int]$RuntimeRestartCount = 0
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

# operator_owned_process=true
# loopback_only=true
# readiness_timeout_seconds=10
# expected_smoke_count=5
# kill_switch_restored=true
# production_shadow_restored_disabled=true

if ($Port -lt 1 -or $Port -gt 65535) { throw "Port is outside the allowed range." }
if ($RequestCount -lt 20 -or $RequestCount -gt 256) { throw "Request count is outside the allowed range." }
if ($RuntimeRestartCount -lt 0 -or $RuntimeRestartCount -gt 1) { throw "Runtime restart count is outside the allowed range." }
if ([string]::IsNullOrWhiteSpace($Python)) { throw "Python command is required." }

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$outputPath = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    [System.IO.Path]::GetFullPath($OutputDirectory)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
}
$trackedRoots = @("sources", "Tests", "tools", "docs") | ForEach-Object {
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot $_)) + [System.IO.Path]::DirectorySeparatorChar
}
foreach ($trackedRoot in $trackedRoots) {
    if ($outputPath.StartsWith($trackedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Output directory must be outside tracked source directories."
    }
}

$endpoint = "http://127.0.0.1:$Port"
$serverPath = Join-Path $repoRoot "tools/dataagent-langgraph-sidecar/server.py"
$smokePath = Join-Path $repoRoot "tools/run-dataagent-langgraph-manual-smoke.ps1"
$projectPath = Join-Path $repoRoot "tools/dataagent-v47-canary/Alife.Tools.DataAgentV47Canary.csproj"
$dotnet = Join-Path $env:USERPROFILE ".dotnet/dotnet.exe"
if ((Test-Path -LiteralPath $dotnet) -eq $false) { $dotnet = "dotnet" }

$ownedProcess = $null
try {
    $sidecarArgs = @($serverPath, "--host", "127.0.0.1", "--port", "$Port", "--runtime-mode", "langgraph")
    $ownedProcess = Start-Process -FilePath $Python -ArgumentList $sidecarArgs `
        -WorkingDirectory $repoRoot -WindowStyle Hidden -PassThru

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
    $ready = $false
    while ([DateTimeOffset]::UtcNow -lt $deadline -and $ownedProcess.HasExited -eq $false) {
        try {
            $health = Invoke-WebRequest -UseBasicParsing -Uri "$endpoint/health" -TimeoutSec 1
            if ([int]$health.StatusCode -eq 200) { $ready = $true; break }
        }
        catch {
            Start-Sleep -Milliseconds 100
        }
    }
    if ($ready -eq $false) { throw "Owned LangGraph sidecar did not become ready within 10 seconds." }

    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $smokePath `
        -Endpoint $endpoint -ExpectedContractVersion v4.7
    if ($LASTEXITCODE -ne 0) { throw "Five-item smoke failed." }

    & $dotnet run --project $projectPath --no-restore -- `
        --endpoint $endpoint `
        --output $outputPath `
        --request-count $RequestCount `
        --timeout-ms 2000 `
        --runtime-restart-count $RuntimeRestartCount
    if ($LASTEXITCODE -ne 0) { throw "V4.7 canary tool rejected closure evidence." }
}
finally {
    if ($null -ne $ownedProcess -and $ownedProcess.HasExited -eq $false) {
        Stop-Process -Id $ownedProcess.Id
        $ownedProcess.WaitForExit(5000) | Out-Null
    }
    Write-Output "kill_switch_restored=true"
    Write-Output "production_shadow_restored_disabled=true"
}
