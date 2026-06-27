param(
    [int]$Port = 9881,
    [string]$ConfigPath = "D:\Alife\Runtime\TTS\configs\tts_infer_mixu.yaml",
    [int]$StartupProbeSeconds = 10
)

$ErrorActionPreference = "Stop"
$PythonPath = "D:\GPT-SoVITS\.venv\Scripts\python.exe"
$ApiPath = "D:\GPT-SoVITS\api_v2.py"
$WorkingDirectory = "D:\GPT-SoVITS"
$LogDirectory = "D:\Alife\Temp\GPT-SoVITS\logs"
$StdoutLogPath = Join-Path $LogDirectory "mixu-$Port.out.log"
$StderrLogPath = Join-Path $LogDirectory "mixu-$Port.err.log"

function Test-GptSoVitsEndpoint {
    param([int]$ProbePort)

    try {
        $response = Invoke-WebRequest -Uri "http://127.0.0.1:$ProbePort/docs" -UseBasicParsing -TimeoutSec 3
        return $response.StatusCode -eq 200
    }
    catch {
        return $false
    }
}

if (-not (Test-Path -LiteralPath $PythonPath)) {
    throw "GPT-SoVITS python not found: $PythonPath"
}
if (-not (Test-Path -LiteralPath $ApiPath)) {
    throw "GPT-SoVITS api_v2.py not found: $ApiPath"
}
if (-not (Test-Path -LiteralPath $ConfigPath)) {
    throw "Mixu GPT-SoVITS config not found: $ConfigPath"
}

if (Test-GptSoVitsEndpoint -ProbePort $Port) {
    Write-Host "Mixu GPT-SoVITS already responds on http://127.0.0.1:$Port."
    return
}

New-Item -ItemType Directory -Force -Path $LogDirectory | Out-Null
Set-Content -LiteralPath $StdoutLogPath -Value "[$(Get-Date -Format o)] starting Mixu GPT-SoVITS on 127.0.0.1:$Port"
Set-Content -LiteralPath $StderrLogPath -Value ""

$command = "`"$PythonPath`" `"$ApiPath`" -a 127.0.0.1 -p $Port -c `"$ConfigPath`" >> `"$StdoutLogPath`" 2>> `"$StderrLogPath`""
$processStartInfo = [System.Diagnostics.ProcessStartInfo]::new()
$processStartInfo.FileName = "$env:SystemRoot\System32\cmd.exe"
$processStartInfo.WorkingDirectory = $WorkingDirectory
$processStartInfo.UseShellExecute = $false
$processStartInfo.CreateNoWindow = $true
$processStartInfo.Arguments = "/s /c `"$command`""
$process = [System.Diagnostics.Process]::Start($processStartInfo)

Write-Host "Started Mixu GPT-SoVITS launcher on 127.0.0.1:$Port (launcher PID $($process.Id))."
Write-Host "Logs: $StdoutLogPath ; $StderrLogPath"

$deadline = [DateTimeOffset]::Now.AddSeconds([Math]::Max(0, $StartupProbeSeconds))
while ([DateTimeOffset]::Now -lt $deadline) {
    Start-Sleep -Seconds 1
    if (Test-GptSoVitsEndpoint -ProbePort $Port) {
        Write-Host "Mixu GPT-SoVITS is ready on http://127.0.0.1:$Port."
        return
    }
}

Write-Host "Mixu GPT-SoVITS launch submitted; readiness not confirmed within $StartupProbeSeconds seconds."
