@echo off
setlocal enabledelayedexpansion

:: ====================================================
:: Full Publish: build plugins + publish apps
:: ====================================================

set "ROOT=%~dp0"
set "DIST_DIR=%ROOT%..\输出文件\Alife\Outputs"
set "PUBLISH_PLUGINS=%ROOT%..\输出文件\Alife\Storage\Plugins"

echo ===================================================
echo [Alife] Starting Unified Publish Workflow...
echo ===================================================
echo.

:: Step 1: Build and copy plugins to publish dir
echo [Step 1/3] Building plugins...
call "%ROOT%Build.cmd" "%PUBLISH_PLUGINS%"
echo.

:: Step 2: Clean old publish dir
echo [Step 2/3] Cleaning %DIST_DIR%...
if exist "%DIST_DIR%" rd /s /q "%DIST_DIR%"
mkdir "%DIST_DIR%"

:: Step 3: Publish applications
echo [Step 3/3] Publishing applications...

echo   Publishing Alife.Client...
dotnet publish "%ROOT%Sources\Alife\Alife.Client\Alife.Client.csproj" ^
    -c Release -o "%DIST_DIR%\Alife.Client" --self-contained false

echo   Publishing Alife.DeskPet.Client...
dotnet publish "%ROOT%Sources\Alife.DeskPet\Alife.DeskPet.Client\Alife.DeskPet.Client.csproj" ^
    -c Release -o "%DIST_DIR%\Alife.DeskPet.Client" --self-contained false

:: Clean runtime DLLs
echo   Cleaning runtime DLLs...
if exist "%DIST_DIR%\Alife.Client" (
    pushd "%DIST_DIR%\Alife.Client"
    del /f /q hostfxr.dll 2>nul
    del /f /q hostpolicy.dll 2>nul
    del /f /q coreclr.dll 2>nul
    del /f /q clrjit.dll 2>nul
    del /f /q createdump.exe 2>nul
    popd
)

echo.
echo ======================================================
echo [Success] Release ready in: %DIST_DIR%
echo ======================================================
pause
