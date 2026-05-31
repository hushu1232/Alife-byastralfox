#Requires -Version 5.1
<#
.SYNOPSIS
    Alife Launcher - System Initialization and Silent Setup
.DESCRIPTION
    Checks dependencies (VC++, .NET, Python, PyTorch CUDA 12.8),
    installs missing components, then launches Alife.Client.
#>

$ErrorActionPreference = "SilentlyContinue"
$env:PYTHONIOENCODING = "utf-8"
$env:PYTHONUTF8 = "1"

# ============================================================
# Auto Elevate to Administrator
# ============================================================
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell.exe -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs
    exit
}

Set-Location $PSScriptRoot

Write-Host "===================================================" -ForegroundColor Cyan
Write-Host "[Alife] System Initialization and Silent Setup"    -ForegroundColor Cyan
Write-Host "===================================================" -ForegroundColor Cyan
Write-Host ""

# ============================================================
# Step 1/4: Check System Dependencies
# ============================================================
Write-Host "[Alife] [Step 1/4] Checking System Dependencies..." -ForegroundColor Yellow

# --- Check VC++ ---
$vcRedist = "$env:SystemRoot\System32\vcruntime140.dll"
if (-not (Test-Path $vcRedist)) {
    Write-Host "[Alife] Missing Dependency: Visual C++. Starting auto-installation..."
    Write-Host "[Alife] Downloading Visual C++..."
    Invoke-WebRequest -Uri "https://aka.ms/vs/17/release/vc_redist.x64.exe" -OutFile "$env:TEMP\vc_setup.exe"
    Write-Host "[Alife] Installing Visual C++ (Silent Mode)..."
    Start-Process "$env:TEMP\vc_setup.exe" -ArgumentList "/install /quiet /norestart" -Wait
    Remove-Item "$env:TEMP\vc_setup.exe" -ErrorAction SilentlyContinue
    Write-Host "[Alife] Visual C++ installed successfully." -ForegroundColor Green
}
Write-Host "[Alife] Visual C++ Runtime is ready." -ForegroundColor Green

# --- Check .NET 9 Desktop ---
$dotnetReady = $false
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    $runtimes = dotnet --list-runtimes 2>$null
    if ($runtimes -match "Microsoft\.WindowsDesktop\.App 9") {
        $dotnetReady = $true
    }
}
if (-not $dotnetReady) {
    Write-Host "[Alife] Missing Dependency: .NET 9 Desktop. Starting auto-installation..."
    Write-Host "[Alife] Downloading .NET 9 Desktop..."
    Invoke-WebRequest -Uri "https://aka.ms/dotnet/9.0/windowsdesktop-runtime-win-x64.exe" -OutFile "$env:TEMP\dotnet_setup.exe"
    Write-Host "[Alife] Installing .NET 9 Desktop (Silent Mode)..."
    Start-Process "$env:TEMP\dotnet_setup.exe" -ArgumentList "/quiet /norestart" -Wait
    Remove-Item "$env:TEMP\dotnet_setup.exe" -ErrorAction SilentlyContinue
    Write-Host "[Alife] .NET 9 Desktop installed successfully." -ForegroundColor Green
}
Write-Host "[Alife] .NET Desktop Runtime is ready." -ForegroundColor Green
Write-Host ""

# ============================================================
# Step 2/4: Setup Python Environment
# ============================================================
Write-Host "[Alife] [Step 2/4] Verifying Private Python Environment..." -ForegroundColor Yellow

$pyDir = $null

# 1. Check system Python 3.12 (default install location)
$systemPy = Join-Path $env:LOCALAPPDATA "Programs\Python\Python312"
if (Test-Path (Join-Path $systemPy "python.exe")) {
    $pyDir = $systemPy
    Write-Host "[Alife] Found system Python 3.12 at: $pyDir" -ForegroundColor Green
}

# 2. Fall back to bundled Python
if (-not $pyDir) {
    $bundledPy = Join-Path $PSScriptRoot "Runtime\Python312"
    if (Test-Path (Join-Path $bundledPy "python.exe")) {
        $pyDir = $bundledPy
    }
}

# 3. Download bundled Python if neither exists
if (-not $pyDir) {
    $pyDir = Join-Path $PSScriptRoot "Runtime\Python312"
    Write-Host "[Alife] Python environment not found. Initiating silent download..."
    New-Item -ItemType Directory -Path $pyDir -Force | Out-Null

    Write-Host "[Alife] Downloading Python 3.12.10..."
    Invoke-WebRequest -Uri "https://repo.huaweicloud.com/python/3.12.10/python-3.12.10-embed-amd64.zip" -OutFile "$env:TEMP\py.zip"

    Write-Host "[Alife] Extracting Python to Private Runtime..."
    Expand-Archive -Path "$env:TEMP\py.zip" -DestinationPath $pyDir -Force
    Remove-Item "$env:TEMP\py.zip" -ErrorAction SilentlyContinue

    # Fix .pth file (enable site-packages)
    Write-Host "[Alife] Configuring site-packages mapping (.pth)..."
    $pthFile = Join-Path $pyDir "python312._pth"
    (Get-Content $pthFile) | ForEach-Object { $_ -replace '#import site', 'import site' } | Set-Content $pthFile

    # Install Pip
    Write-Host "[Alife] Downloading and installing Pip silently..."
    Invoke-WebRequest -Uri "https://bootstrap.pypa.io/get-pip.py" -OutFile "$env:TEMP\get-pip.py"
    & (Join-Path $pyDir "python.exe") "$env:TEMP\get-pip.py" --no-warn-script-location --index-url https://mirrors.aliyun.com/pypi/simple/ | Out-Null
    Remove-Item "$env:TEMP\get-pip.py" -ErrorAction SilentlyContinue

    Write-Host "[Alife] Python environment setup complete." -ForegroundColor Green
}

Write-Host "[Alife] Private Python environment is ready." -ForegroundColor Green

# ============================================================
# Install PyTorch CUDA 12.8 (Blackwell / RTX 50 Series)
# ============================================================
Write-Host "[Alife] Checking PyTorch CUDA 12.8 support..." -ForegroundColor Yellow

$pyExe = Join-Path $pyDir "python.exe"
$torchCheck = & $pyExe -c "import torch; print('ok' if torch.version.cuda and '12.8' in torch.version.cuda else 'no')" 2>$null

if ($torchCheck -eq "ok") {
    Write-Host "[Alife] PyTorch CUDA 12.8 already installed, skipping." -ForegroundColor Green
} else {
    Write-Host "[Alife] Installing PyTorch with CUDA 12.8 support (Blackwell compatible)..."
    & $pyExe -m pip uninstall torch torchvision -y 2>$null | Out-Null
    & $pyExe -m pip install torch==2.10.0+cu128 torchvision==0.25.0+cu128 `
        --find-links https://mirrors.aliyun.com/pytorch-wheels/cu128/ `
        --extra-index-url https://mirrors.aliyun.com/pypi/simple/
    Write-Host "[Alife] PyTorch CUDA 12.8 ready." -ForegroundColor Green
}
Write-Host ""

# ============================================================
# Install Additional Python Tools
# ============================================================
Write-Host "[Alife] Installing Additional Python Tools..." -ForegroundColor Yellow

Write-Host "[Alife] Installing uv (uvx)..."
& $pyExe -m pip install uv 2>$null | Out-Null
Write-Host "[Alife] Additional Python tools ready." -ForegroundColor Green
Write-Host ""

# ============================================================
# Step 3/4: PATH Injection & Update Packages
# ============================================================
Write-Host "[Alife] [Step 3/4] Injecting Variables and Updating Packages..." -ForegroundColor Yellow

$env:PATH = "$pyDir;$(Join-Path $pyDir 'Scripts');$env:PATH"
$env:PIP_INDEX_URL = "https://mirrors.aliyun.com/pypi/simple/"

Write-Host "[Alife] Updating basic Python tools (pip, setuptools, wheel)..."
python -m pip install --upgrade pip setuptools wheel 2>$null | Out-Null
Write-Host "[Alife] Isolated environment injected successfully." -ForegroundColor Green
Write-Host ""

# ============================================================
# Step 4/4: Launch Application
# ============================================================
Write-Host "[Alife] [Step 4/4] Launching Application..." -ForegroundColor Yellow

$exePath = Join-Path $PSScriptRoot "Outputs\Alife.Client\Alife.Client.exe"
if (Test-Path $exePath) {
    Write-Host "[Alife] Starting Alife.Client.exe..." -ForegroundColor Cyan
    Write-Host "===================================================" -ForegroundColor Cyan
    & $exePath 2>&1
} else {
    Write-Host "[Error] Outputs\Alife.Client\Alife.Client.exe not found!" -ForegroundColor Red
}

Write-Host ""
Write-Host "[Alife] Application process ended." -ForegroundColor Cyan
Write-Host "Press any key to exit..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
