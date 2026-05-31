@echo off
setlocal enabledelayedexpansion

set "ROOT=%~dp0"
set "PLUGIN_TARGET=%1"
if "%PLUGIN_TARGET%"=="" set "PLUGIN_TARGET=%ROOT%Storage\Plugins"
set "SRC=%ROOT%Sources"
set "OUT=%ROOT%Outputs"

echo [Build] Plugin target: %PLUGIN_TARGET%
echo.

:: Build
echo [1/3] Building...
dotnet build "%SRC%\Alife\Alife.Client\Alife.Client.csproj" -c Release -nologo --verbosity quiet
dotnet build "%SRC%\Alife.DeskPet\Alife.DeskPet.Client\Alife.DeskPet.Client.csproj" -c Release -nologo --verbosity quiet
for /d %%d in ("%SRC%\Alife.Function\Alife.Function.*") do (
    dotnet build "%%d\%%~nxd.csproj" -c Release -nologo --verbosity quiet
)

:: Copy Function sources
echo [2/3] Copying Function sources...
if exist "%PLUGIN_TARGET%" (
    echo   Cleaning: %PLUGIN_TARGET%
    rd /s /q "%PLUGIN_TARGET%"
)
mkdir "%PLUGIN_TARGET%"
for /d %%d in ("%SRC%\Alife.Function\Alife.Function.*") do (
    set "target=%PLUGIN_TARGET%\%%~nxd"
    if not exist "!target!" mkdir "!target!"
    copy /y "%%d\*.cs" "!target!" >nul 2>&1
    for /f "delims=" %%f in ('dir /s /b "%%d\obj\Release\generated\Microsoft.CodeAnalysis.Razor.Compiler\*_razor.g.cs" 2^>nul') do (
        set "gcsfile=%%~nxf"
        set "razorname=!gcsfile:_razor.g.cs=!"
        if exist "%%d\!razorname!.razor" (
            copy /y "%%f" "!target!" >nul 2>&1
        ) else (
            echo   [skip] %%~nxf
        )
    )
    echo   [done] %%~nxd
)

:: Copy extra deps (all files in Function output but not in Alife.Client output) to shared NuGet dir
echo.
echo [3/3] Syncing NuGet deps...

set "NUGET_DIR=%PLUGIN_TARGET%\BaseDirectory"
if not exist "%NUGET_DIR%" mkdir "%NUGET_DIR%"

powershell -ExecutionPolicy Bypass -File "%ROOT%SyncPlugins.ps1" -OutputsDir "%OUT%" -NuGetDir "%NUGET_DIR%"

echo.
echo [Build] Done. Plugins in: %PLUGIN_TARGET%
echo         Shared NuGet deps in: %NUGET_DIR%
