@echo off
setlocal enabledelayedexpansion
title Alife Launcher [Auto-Repair]

echo ============================================================
echo                Alife System Launcher
echo ============================================================
echo.

:CHECK_DOTNET
echo [System] Checking .NET 9 Desktop Runtime...
dotnet --list-runtimes >nul 2>&1
if %errorlevel% neq 0 goto INSTALL_DOTNET
dotnet --list-runtimes | findstr /c:"Microsoft.WindowsDesktop.App 9." >nul 2>&1
if %errorlevel% equ 0 goto CHECK_PYTHON

:INSTALL_DOTNET
echo [Warning] .NET 9 Desktop Runtime is missing.
set /p "install_net=Install .NET 9 Desktop Runtime now? (y/n): "
if /i "!install_net!" neq "y" (
    echo [Error] .NET 9 Desktop Runtime is required.
    pause
    exit /b 1
)

echo [Info] Downloading .NET 9 Desktop Runtime (this may take a minute)...
powershell -Command "Invoke-WebRequest -Uri 'https://aka.ms/dotnet/9.0/windowsdesktop-runtime-win-x64.exe' -OutFile '%TEMP%\dotnet_installer.exe'"

echo [Info] Installing .NET 9 Desktop Runtime...
start /wait "" "%TEMP%\dotnet_installer.exe" /quiet /norestart
del "%TEMP%\dotnet_installer.exe"
echo [Success] .NET 9 Desktop Runtime installed.
echo.

:CHECK_PYTHON
echo [System] Checking Python environment...

:: 1. Check if python command is valid
python --version >nul 2>&1
if %errorlevel% neq 0 goto TRY_FIX

:: 2. Check if it is the Windows Store stub (even if version check passed)
where python | findstr /i "WindowsApps" >nul 2>&1
if %errorlevel% equ 0 (
    echo [Warning] Windows Store Python stub detected.
    goto TRY_FIX
)

:: 3. Check for 'py' launcher
py --version >nul 2>&1
if %errorlevel% equ 0 (
    set "PYTHON_CMD=py"
    goto PYTHON_READY
)
set "PYTHON_CMD=python"
goto PYTHON_READY

:TRY_FIX
:: --- Auto-Repair Logic ---
echo [Info] Python not properly configured in PATH. Searching registry...
set "FOUND_PY_DIR="
for /f "delims=" %%i in ('powershell -Command "foreach($h in 'HKCU','HKLM'){ $p='Registry::'+$h+'\Software\Python\PythonCore\3.12\InstallPath'; if(Test-Path $p){ (Get-ItemProperty $p).'(default)'; break } }"') do set "FOUND_PY_DIR=%%i"

if defined FOUND_PY_DIR (
    if exist "!FOUND_PY_DIR!\python.exe" (
        echo [Info] Found Python at !FOUND_PY_DIR!, fixing environment...
        set "PATH=!FOUND_PY_DIR!;!FOUND_PY_DIR!\Scripts;!PATH!"
        
        :: Disable Store Aliases
        powershell -Command "foreach($n in 'python.exe_0','python3.exe_0','python.exe','python3.exe'){ $p='HKCU:\Software\Microsoft\Windows\CurrentVersion\AppInstaller\AppExecutionAliases\'+$n; if(Test-Path $p){ Set-ItemProperty $p -Name 'State' -Value 0 -ErrorAction SilentlyContinue } }" >nul 2>&1
        :: Permanently reorder User PATH
        powershell -Command "$p=[Environment]::GetEnvironmentVariable('Path','User'); $l=$p.Split(';',[System.StringSplitOptions]::RemoveEmptyEntries)|Where-Object{$_ -notlike '*Python312*'}; $nList=@('!FOUND_PY_DIR!','!FOUND_PY_DIR!\Scripts')+$l; [Environment]::SetEnvironmentVariable('Path',($nList -join ';'),'User')" >nul 2>&1
        
        echo [Success] Environment fixed permanently.
        :: Re-verify after fix
        python --version >nul 2>&1
        if %errorlevel% equ 0 goto PYTHON_READY
    )
)
:: --- End of Auto-Repair Logic ---

echo [Warning] Python 3.12 is missing.
set /p "install=Install Python 3.12.10 now? (y/n): "
if /i "!install!" neq "y" (
    echo [Error] Python required.
    pause
    exit /b 1
)

echo [Info] Downloading Python...
powershell -Command "Invoke-WebRequest -Uri 'https://repo.huaweicloud.com/python/3.12.10/python-3.12.10-amd64.exe' -OutFile '%TEMP%\python_installer.exe'"

echo [Info] Installing Python...
start /wait "" "%TEMP%\python_installer.exe" /quiet InstallAllUsers=0 PrependPath=1 Include_test=0
set "EXIT_CODE=%errorlevel%"
del "%TEMP%\python_installer.exe"

:: Refresh session PATH and retry
set "PS_CMD=$p = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User'); [Environment]::ExpandEnvironmentVariables($p)"
for /f "delims=" %%i in ('powershell -Command "%PS_CMD%"') do set "PATH=%%i"
goto CHECK_PYTHON

:PYTHON_READY
echo [Success] Python detected and ready.
if "!PYTHON_CMD!"=="" set "PYTHON_CMD=python"
!PYTHON_CMD! --version

echo.
echo [Info] Configuring Pip...
!PYTHON_CMD! -m pip config set global.index-url https://mirrors.aliyun.com/pypi/simple/ >nul 2>&1

echo.
echo [Info] Launching Alife...
if exist "Outputs\Alife.exe" (
    "Outputs\Alife.exe"
) else (
    echo [Error] 'Outputs\Alife.exe' not found.
    pause
    exit /b 1
)
pause
