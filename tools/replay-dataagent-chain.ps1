param(
    [string]$Fixture = "",
    [string]$Format = "markdown"
)

$ErrorActionPreference = "Stop"

$scriptDirectory = Split-Path -Parent $PSCommandPath
$repoRoot = Split-Path -Parent $scriptDirectory

if ([string]::IsNullOrWhiteSpace($Fixture)) {
    $Fixture = "Tests\Alife.Test.DataAgent\Fixtures\DataAgentReplay\v3.9-owner-readiness-analysis.json"
}

if ([System.IO.Path]::IsPathRooted($Fixture) -eq $false) {
    $Fixture = Join-Path $repoRoot $Fixture
}

if ((Test-Path -LiteralPath $Fixture -PathType Leaf) -eq $false) {
    [Console]::Error.WriteLine("Fixture not found: $Fixture")
    exit 1
}

if ($Format -ne "markdown" -and $Format -ne "json") {
    [Console]::Error.WriteLine("Unsupported format: $Format. Supported formats: markdown, json.")
    exit 1
}

$localDotnet = "C:\Users\hu shu\.dotnet\dotnet.exe"
if (Test-Path -LiteralPath $localDotnet -PathType Leaf) {
    $dotnet = $localDotnet
}
else {
    $dotnetVersion = dotnet --version
    if ($dotnetVersion.StartsWith("9.") -eq $false) {
        [Console]::Error.WriteLine(".NET 9 SDK required for DataAgent replay; found: $dotnetVersion")
        exit 1
    }

    $dotnet = "dotnet"
}

& $dotnet run --no-restore --project (Join-Path $repoRoot "tools\dataagent-replay\Alife.Tools.DataAgentReplay.csproj") -- --fixture $Fixture --format $Format
exit $LASTEXITCODE
