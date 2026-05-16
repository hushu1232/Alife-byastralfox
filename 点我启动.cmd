@echo off
setlocal enabledelayedexpansion

:: Auto Request Administrator Privileges
net session >nul 2>&1 || (powershell -Command "Start-Process -FilePath '%~dpnx0' -Verb RunAs" & exit)

cd /d "%~dp0"
title Alife Launcher [Private Runtime Mode]
echo [Alife] System Initializing...

:: 1. Check Visual C++ & .NET Desktop Runtime
powershell -Command "if(Test-Path \"$env:SystemRoot\System32\vcruntime140.dll\"){exit 0}else{exit 1}" || call :INSTALL "Visual C++" "https://aka.ms/vs/17/release/vc_redist.x64.exe" "/install /quiet /norestart"
dotnet --list-runtimes | findstr "Microsoft.WindowsDesktop.App [9-99]" >nul || call :INSTALL ".NET 9 Desktop" "https://aka.ms/dotnet/9.0/windowsdesktop-runtime-win-x64.exe" "/quiet /norestart"

:: 2. Setup Forced Private Python Environment
set "PY_DIR=%~dp0Runtime\Python312"

if not exist "!PY_DIR!\python.exe" (
    echo [Alife] Downloading Private Python 3.12...
    if not exist "!PY_DIR!" mkdir "!PY_DIR!"
    powershell -Command "Invoke-WebRequest -Uri 'https://repo.huaweicloud.com/python/3.12.10/python-3.12.10-embed-amd64.zip' -OutFile '%TEMP%\py.zip'"
    echo [Alife] Extracting Python...
    powershell -Command "Expand-Archive -Path '%TEMP%\py.zip' -DestinationPath '!PY_DIR!' -Force"
    del "%TEMP%\py.zip"
    
    :: Fix .pth file (enable site-packages)
    echo [Alife] Configuring path environment...
    powershell -Command "$p = Join-Path '!PY_DIR!' 'python312._pth'; (Get-Content $p) | ForEach-Object { $_ -replace '#import site', 'import site' } | Set-Content $p"
    
    :: Install Pip
    echo [Alife] Installing Pip...
    powershell -Command "Invoke-WebRequest -Uri 'https://bootstrap.pypa.io/get-pip.py' -OutFile '%TEMP%\get-pip.py'"
    "!PY_DIR!\python.exe" "%TEMP%\get-pip.py" --no-warn-script-location --index-url https://mirrors.aliyun.com/pypi/simple/
    del "%TEMP%\get-pip.py"
)

:: 3. PATH Injection & Launch
:: Use private runtime and its scripts directly
set "PATH=!PY_DIR!;!PY_DIR!\Scripts;%PATH%"
set "PIP_INDEX_URL=https://mirrors.aliyun.com/pypi/simple/"

echo [Alife] Isolated environment ready.
python --version
python -m pip install pip setuptools wheel >nul 2>&1

if exist "Outputs\Alife\Alife.exe" (
    "Outputs\Alife\Alife.exe"
) else (
    echo [Error] Alife.exe not found.
)
echo.
echo [Alife] Process ended.
pause
exit /b

:INSTALL
echo [Warning] %~1 is missing.
echo [Alife] Downloading and installing %~1...
powershell -Command "Invoke-WebRequest -Uri '%~2' -OutFile '%TEMP%\setup.exe'"
start /wait "" "%TEMP%\setup.exe" %~3
del "%TEMP%\setup.exe"
echo [Success] %~1 installed.
echo.
goto :eof