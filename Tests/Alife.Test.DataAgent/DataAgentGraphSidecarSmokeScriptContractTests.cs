using System.Diagnostics;
using System.Text;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentGraphSidecarSmokeScriptContractTests
{
    static readonly string[] ExpectedHarnessPassLines =
    [
        "PASS valid response accepted",
        "PASS rejects sql.execute requested tool",
        "PASS rejects unknown requested tool",
        "PASS rejects empty requested tool list",
        "PASS rejects SelectedNodes object entry",
        "PASS rejects SelectedNodes unknown node",
        "PASS rejects NodeProgress unknown node",
        "PASS rejects scalar Facts",
        "PASS rejects array Facts",
        "PASS rejects Facts reserved source key",
        "PASS rejects Facts non-string value",
        "PASS rejects NDJSON final response sql.execute",
        "PASS accepts null Facts"
    ];

    [Test]
    public void SmokeScriptRejectsMalformedHandshakeAndStreamContractsWithoutNetwork()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "run-dataagent-graph-sidecar-smoke.ps1");
        string harness = BuildPowerShellHarness(scriptPath);

        ScriptResult result = RunPowerShell(harness);
        string combinedOutput = result.StandardOutput + Environment.NewLine + result.StandardError;

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(0), combinedOutput);
            foreach (string expectedLine in ExpectedHarnessPassLines)
            {
                Assert.That(result.StandardOutput, Does.Contain(expectedLine), combinedOutput);
            }
        });
    }

    [Test]
    public void SmokeContractRegressionTestDoesNotInvokeLiveRuntime()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string source = File.ReadAllText(Path.Combine(repoRoot, "Tests", "Alife.Test.DataAgent", "DataAgentGraphSidecarSmokeScriptContractTests.cs"));
        string[] forbiddenRuntimeMarkers =
        [
            "Invoke-" + "WebRequest",
            "Start-" + "Process",
            "uvicorn " + "app:app",
            "text/" + "event-stream",
            "Event" + "Source"
        ];

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("Parser.ParseInput"));
            Assert.That(source, Does.Contain("FunctionDefinitionAst"));
            Assert.That(source, Does.Contain("Test-HandshakeResponse"));
            Assert.That(source, Does.Contain("Test-NdjsonStream"));
            Assert.That(source, Does.Not.Contain("127.0.0.1:" + "8765"));
            foreach (string marker in forbiddenRuntimeMarkers)
            {
                Assert.That(source, Does.Not.Contain(marker));
            }
        });
    }

    static string BuildPowerShellHarness(string scriptPath)
    {
        string escapedScriptPath = scriptPath.Replace("'", "''", StringComparison.Ordinal);
        return $$"""
$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$scriptPath = '{{escapedScriptPath}}'
$source = Get-Content -LiteralPath $scriptPath -Raw
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors -and $parseErrors.Count -gt 0) {
    throw ("Smoke script parse errors: {0}" -f ($parseErrors | ForEach-Object { $_.Message } | Out-String))
}

$functions = $ast.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst]
}, $true)

foreach ($function in $functions) {
    Invoke-Expression $function.Extent.Text
}

function ConvertTo-DataJson {
    param($Value)
    return ($Value | ConvertTo-Json -Depth 32 -Compress)
}

function ConvertFrom-DataJson {
    param([string]$Json)
    return ($Json | ConvertFrom-Json)
}

function New-ValidResponse {
    param($Request)

    [ordered]@{
        RequestId = $Request.RequestId
        Accepted = $true
        NoSqlAuthority = $true
        ReadOnly = $true
        FallbackRequired = $false
        RequestsCheckpointMutation = $false
        RequestsVisibleText = $false
        SelectedNodes = @(
            "scenario_knowledge",
            "query_planner",
            "diagnostics_router"
        )
        NodeProgress = @(
            [ordered]@{
                NodeName = "scenario_knowledge"
                Status = "Completed"
                ReasonCode = "scenario_ready"
                Facts = [ordered]@{ stage = "scenario" }
            },
            [ordered]@{
                NodeName = "query_planner"
                Status = "Completed"
                ReasonCode = "planner_ready"
                Facts = [ordered]@{ stage = "planner" }
            },
            [ordered]@{
                NodeName = "diagnostics_router"
                Status = "Completed"
                ReasonCode = "diagnostics_ready"
                Facts = [ordered]@{ stage = "diagnostics" }
            }
        )
        RequestedToolNames = @(
            "dataagent.scenario_context.read",
            "dataagent.query_plan.propose",
            "dataagent.diagnostics.progress.read"
        )
    }
}

function Copy-Response {
    param($Response)
    return ConvertFrom-DataJson (ConvertTo-DataJson $Response)
}

function Assert-Accepts {
    param([string]$Name, [scriptblock]$Action)

    try {
        & $Action
        Write-Output ("PASS {0}" -f $Name)
    }
    catch {
        Write-Output ("FAIL {0}: {1}" -f $Name, $_.Exception.Message)
        exit 1
    }
}

function Assert-Rejects {
    param([string]$Name, [scriptblock]$Action)

    try {
        & $Action
        Write-Output ("FAIL {0}: accepted malformed payload" -f $Name)
        exit 1
    }
    catch {
        Write-Output ("PASS {0}: {1}" -f $Name, $_.Exception.Message)
    }
}

$request = New-SmokeHandshakeRequest
$valid = ConvertFrom-DataJson (ConvertTo-DataJson (New-ValidResponse $request))

Assert-Accepts "valid response accepted" {
    Test-HandshakeResponse $valid $request
}

$sqlTool = Copy-Response $valid
$sqlTool.RequestedToolNames = @("sql.execute")
Assert-Rejects "rejects sql.execute requested tool" {
    Test-HandshakeResponse $sqlTool $request
}

$unknownTool = Copy-Response $valid
$unknownTool.RequestedToolNames = @("unknown.tool")
Assert-Rejects "rejects unknown requested tool" {
    Test-HandshakeResponse $unknownTool $request
}

$emptyTools = Copy-Response $valid
$emptyTools.RequestedToolNames = @()
Assert-Rejects "rejects empty requested tool list" {
    Test-HandshakeResponse $emptyTools $request
}

$objectSelectedNode = Copy-Response $valid
$objectSelectedNode.SelectedNodes = @([pscustomobject]@{ Name = "scenario_knowledge" })
Assert-Rejects "rejects SelectedNodes object entry" {
    Test-HandshakeResponse $objectSelectedNode $request
}

$unknownSelectedNode = Copy-Response $valid
$unknownSelectedNode.SelectedNodes = @("unknown_node")
Assert-Rejects "rejects SelectedNodes unknown node" {
    Test-HandshakeResponse $unknownSelectedNode $request
}

$unknownProgressNode = Copy-Response $valid
$unknownProgressNode.NodeProgress[0].NodeName = "unknown_node"
Assert-Rejects "rejects NodeProgress unknown node" {
    Test-HandshakeResponse $unknownProgressNode $request
}

$scalarFacts = Copy-Response $valid
$scalarFacts.NodeProgress[0].Facts = "not-an-object"
Assert-Rejects "rejects scalar Facts" {
    Test-HandshakeResponse $scalarFacts $request
}

$arrayFacts = Copy-Response $valid
$arrayFacts.NodeProgress[0].Facts = @("not", "object")
Assert-Rejects "rejects array Facts" {
    Test-HandshakeResponse $arrayFacts $request
}

$reservedFacts = Copy-Response $valid
$reservedFacts.NodeProgress[0].Facts = [pscustomobject]@{ source = "graph_sidecar" }
Assert-Rejects "rejects Facts reserved source key" {
    Test-HandshakeResponse $reservedFacts $request
}

$nonStringFactValue = Copy-Response $valid
$nonStringFactValue.NodeProgress[0].Facts = [pscustomobject]@{ safe = 123 }
Assert-Rejects "rejects Facts non-string value" {
    Test-HandshakeResponse $nonStringFactValue $request
}

$ndjsonSql = Copy-Response $valid
$ndjsonSql.RequestedToolNames = @("sql.execute")
$progressEvent = [ordered]@{
    Kind = "Progress"
    Progress = [ordered]@{
        NodeName = "scenario_knowledge"
        Status = "Completed"
        ReasonCode = "scenario_ready"
        Facts = [ordered]@{ safe = "true" }
    }
}
$finalEvent = [ordered]@{
    Kind = "FinalResponse"
    Response = $ndjsonSql
}
$ndjson = (ConvertTo-DataJson $progressEvent) + "`n" + (ConvertTo-DataJson $finalEvent)
Assert-Rejects "rejects NDJSON final response sql.execute" {
    Test-NdjsonStream $ndjson $request
}

$nullFacts = Copy-Response $valid
$nullFacts.NodeProgress[0].Facts = $null
Assert-Accepts "accepts null Facts" {
    Test-HandshakeResponse $nullFacts $request
}
""";
    }

    static ScriptResult RunPowerShell(string command)
    {
        string powerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        ProcessStartInfo startInfo = new()
        {
            FileName = powerShell,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start PowerShell.");

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();

        if (process.WaitForExit(15000) == false)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Smoke script contract harness did not exit within 15 seconds.");
        }

        return new ScriptResult(process.ExitCode, stdout, stderr);
    }

    static string FindRepoRoot(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tools")) &&
                Directory.Exists(Path.Combine(directory.FullName, "Tests")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    readonly record struct ScriptResult(int ExitCode, string StandardOutput, string StandardError);
}
