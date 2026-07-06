using NUnit.Framework;
using System.Diagnostics;
using System.IO;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatEngineeringMapRequiredV2Tests
{
    static readonly string[] RequiredV2Checks =
    [
        "Vision readiness tests",
        "Voice warmup coordinator tests",
        "Model reply loop live tests",
        "Prompt leak contract tests",
        "Runtime readiness script",
        "Voice warmup retry coordinator",
        "Semantic settle window contract tests",
        "Voice warmup contract tests",
        "XiaYu self-state machine",
        "Persona intensity prompt formatter",
        "Persona frame prompt",
        "XiaYu private state prompt",
        "Semantic window summary prompt",
        "Tool broker runtime wiring",
        "QChat tool route state wiring",
        "DataAgent dynamic tool route contract",
        "QChat owner Tool Broker diagnostics",
        "QChat semantic diagnostics",
        "DataAgent owner evidence diagnostics",
        "QChat recent diagnostics cache",
        "QChat recent diagnostics command",
        "QChat diagnostics cache redaction",
        "DataAgent trace diagnostics",
        "DataAgent progress diagnostics",
        "DataAgent scenario context diagnostics",
        "DataAgent runtime scenario context activation",
        "DataAgent PostgreSQL checkpoint persistence",
        "DataAgent graph sidecar contract",
        "DataAgent DataQueryGraph pilot",
        "DataAgent DataQueryGraph owner diagnostics",
        "QChat Kalman semantic state estimator",
        "QChat Kalman settle window integration",
        "Alife capability governance catalog",
        "DataAgent node tool scope policy"
    ];

    [Test]
    public void RequiredV2ChecksAreNotDeclaredOptional()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-qchat-engineering-map.ps1");
        string script = File.ReadAllText(scriptPath);

        foreach (string checkName in RequiredV2Checks)
        {
            string declaration = FindAddCheckDeclaration(script, checkName);

            Assert.Multiple(() =>
            {
                Assert.That(declaration, Is.Not.Empty, $"Missing Add-Check declaration for '{checkName}'.");
                Assert.That(declaration, Does.Not.Contain("-Required $false"), $"'{checkName}' must be required.");
            });
        }
    }

    [Test]
    public void DiagnosticsCacheRedactionCheckRequiresUnsafeInputDetectors()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-qchat-engineering-map.ps1");
        string script = File.ReadAllText(scriptPath);

        string declaration = FindAddCheckDeclaration(script, "QChat diagnostics cache redaction");

        Assert.Multiple(() =>
        {
            Assert.That(declaration, Does.Contain("HiddenContextPattern"));
            Assert.That(declaration, Does.Contain("SqlFragmentPattern"));
        });
    }

    [Test]
    public void ScenarioContextDiagnosticsCheckRequiresQChatNoDirectImportGuard()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-qchat-engineering-map.ps1");
        string script = File.ReadAllText(scriptPath);

        string declaration = FindAddCheckDeclaration(script, "DataAgent scenario context diagnostics");

        Assert.Multiple(() =>
        {
            Assert.That(declaration, Does.Contain("tools/check-dataagent-readiness.ps1"));
            Assert.That(declaration, Does.Contain("DataAgentScenarioContextIntegrated"));
            Assert.That(declaration, Does.Contain("Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs"));
            Assert.That(declaration, Does.Contain("QChatDoesNotDirectlyImportDataAgentBoundaryTypes"));
            Assert.That(declaration, Does.Contain("DataAgentScenarioKnowledgePackProvider"));
            Assert.That(declaration, Does.Contain("DataAgentScenarioContextBuilder"));
            Assert.That(declaration, Does.Contain("DataAgentToolScopePolicy"));
            Assert.That(script, Does.Contain("function Test-DirectoryOmitsMarker"));
            Assert.That(declaration, Does.Contain("sources/Alife.Function/Alife.Function.QChat"));
            Assert.That(declaration, Does.Contain("*.cs"));
            Assert.That(declaration, Does.Contain("AllDirectories"));
        });
    }

    [Test]
    public void RuntimeScenarioContextActivationCheckRequiresDataAgentRuntimeAndQChatBoundary()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-qchat-engineering-map.ps1");
        string script = File.ReadAllText(scriptPath);

        string declaration = FindAddCheckDeclaration(script, "DataAgent runtime scenario context activation");

        Assert.Multiple(() =>
        {
            Assert.That(declaration, Does.Contain("DataAgentRuntimeScenarioContextActivationPresent"));
            Assert.That(declaration, Does.Contain("DataAgentRuntimeScenarioContextActivationTests"));
            Assert.That(declaration, Does.Contain("DataAgentScenarioKnowledgePackProvider"));
            Assert.That(declaration, Does.Contain("DataAgentScenarioContextBuilder"));
            Assert.That(declaration, Does.Contain("DataAgentToolScopePolicy"));
            Assert.That(declaration, Does.Contain("sources/Alife.Function/Alife.Function.QChat"));
        });
    }

    [Test]
    public void PostgresCheckpointPersistenceCheckRequiresDataAgentRuntimeAndQChatBoundary()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-qchat-engineering-map.ps1");
        string script = File.ReadAllText(scriptPath);

        string declaration = FindAddCheckDeclaration(script, "DataAgent PostgreSQL checkpoint persistence");

        Assert.Multiple(() =>
        {
            Assert.That(declaration, Does.Contain("PostgresCheckpointPersistencePresent"));
            Assert.That(declaration, Does.Contain("PostgresDataAgentAnalysisSessionStore"));
            Assert.That(declaration, Does.Contain("DataAgentAnalysisSessionStoreFactory"));
            Assert.That(declaration, Does.Contain("DataAgentModuleService"));
            Assert.That(declaration, Does.Contain("session_store=true"));
            Assert.That(declaration, Does.Contain("factory=true"));
            Assert.That(declaration, Does.Contain("module_wiring=true"));
            Assert.That(declaration, Does.Contain("live_test_gated="));
            Assert.That(declaration, Does.Contain("sources/Alife.Function/Alife.Function.QChat"));
        });
    }

    [Test]
    public void GraphSidecarContractCheckRequiresDataAgentRuntimeAndQChatBoundary()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-qchat-engineering-map.ps1");
        string script = File.ReadAllText(scriptPath);

        string declaration = FindAddCheckDeclaration(script, "DataAgent graph sidecar contract");

        Assert.Multiple(() =>
        {
            Assert.That(declaration, Does.Contain("GraphSidecarContractPresent"));
            Assert.That(declaration, Does.Contain("DataAgentGraphSidecarContract"));
            Assert.That(declaration, Does.Contain("ALIFE_DATAAGENT_GRAPH_SIDECAR_ENABLED"));
            Assert.That(declaration, Does.Contain("no_sql_authority=true"));
            Assert.That(declaration, Does.Contain("no_runtime=true"));
            Assert.That(declaration, Does.Contain("QChatDoesNotDirectlyImportDataAgentBoundaryTypes"));
            Assert.That(declaration, Does.Contain("sources/Alife.Function/Alife.Function.QChat"));
            Assert.That(declaration, Does.Contain("DataAgentGraphSidecarNodeKind"));
            Assert.That(declaration, Does.Contain("DataAgentGraphSidecarAuthority"));
            Assert.That(declaration, Does.Contain("DataAgentGraphSidecarOptions"));
            Assert.That(declaration, Does.Contain("DataAgentGraphSidecarPolicy"));
            Assert.That(declaration, Does.Contain("DataAgentGraphSidecarRequest"));
            Assert.That(declaration, Does.Contain("DataAgentGraphSidecarResponse"));
        });
    }

    [Test]
    public void DataQueryGraphPilotCheckRequiresDataAgentRuntimeAndQChatBoundary()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-qchat-engineering-map.ps1");
        string script = File.ReadAllText(scriptPath);

        string declaration = FindAddCheckDeclaration(script, "DataAgent DataQueryGraph pilot");

        Assert.Multiple(() =>
        {
            Assert.That(declaration, Does.Contain("DataQueryGraphPilotPresent"));
            Assert.That(declaration, Does.Contain("DataAgentDataQueryGraphPilot"));
            Assert.That(declaration, Does.Contain("ALIFE_DATAAGENT_DATAQUERYGRAPH_PILOT_ENABLED"));
            Assert.That(declaration, Does.Contain("no_langgraph_runtime=true"));
            Assert.That(declaration, Does.Contain("node_scope=true"));
            Assert.That(declaration, Does.Contain("no_sql_authority=true"));
            Assert.That(declaration, Does.Contain("plan_shape=true"));
            Assert.That(declaration, Does.Contain("transition_shape=true"));
            Assert.That(declaration, Does.Contain("execute_scope=true"));
            Assert.That(declaration, Does.Contain("QChatDoesNotDirectlyImportDataAgentBoundaryTypes"));
            Assert.That(declaration, Does.Contain("sources/Alife.Function/Alife.Function.QChat"));
            Assert.That(declaration, Does.Contain("DataAgentDataQueryGraphOptions"));
            Assert.That(declaration, Does.Contain("DataAgentDataQueryGraphPilot"));
            Assert.That(declaration, Does.Contain("DataAgentDataQueryGraphPlan"));
            Assert.That(declaration, Does.Contain("DataAgentDataQueryGraphNode"));
            Assert.That(declaration, Does.Contain("DataAgentDataQueryGraphTransition"));
            Assert.That(declaration, Does.Contain("DataAgentDataQueryGraphDryRunResult"));
            Assert.That(declaration, Does.Contain("DataAgentDataQueryGraphTraceFormatter"));
        });
    }

    [Test]
    public void DataQueryGraphOwnerDiagnosticsCheckRequiresStringBridgeAndQChatBoundary()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-qchat-engineering-map.ps1");
        string script = File.ReadAllText(scriptPath);

        string declaration = FindAddCheckDeclaration(script, "DataAgent DataQueryGraph owner diagnostics");

        Assert.Multiple(() =>
        {
            Assert.That(declaration, Does.Contain("DataQueryGraphOwnerDiagnosticsPresent"));
            Assert.That(declaration, Does.Contain("RecentDataAgentGraph"));
            Assert.That(declaration, Does.Contain("DataAgentGraph"));
            Assert.That(declaration, Does.Contain("diag graph"));
            Assert.That(declaration, Does.Contain("sources/Alife.Function/Alife.Function.QChat"));
            Assert.That(declaration, Does.Contain("*.cs"));
            Assert.That(declaration, Does.Contain("AllDirectories"));
            Assert.That(declaration, Does.Contain("DataAgentDataQueryGraph"));
            Assert.That(declaration, Does.Not.Contain("DataAgentDataQueryGraphOptions"));
            Assert.That(declaration, Does.Not.Contain("DataAgentDataQueryGraphPilot"));
            Assert.That(declaration, Does.Not.Contain("DataAgentDataQueryGraphPlan"));
            Assert.That(declaration, Does.Not.Contain("DataAgentDataQueryGraphNode"));
            Assert.That(declaration, Does.Not.Contain("DataAgentDataQueryGraphTransition"));
            Assert.That(declaration, Does.Not.Contain("DataAgentDataQueryGraphDryRunResult"));
            Assert.That(declaration, Does.Not.Contain("DataAgentDataQueryGraphTraceFormatter"));
            Assert.That(declaration, Does.Not.Contain("dataagent_graph_recent"));
        });
    }

    [Test]
    public void QChatDoesNotDirectlyImportDataAgentBoundaryTypes()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string qchatRoot = Path.Combine(repoRoot, "sources", "Alife.Function", "Alife.Function.QChat");
        string[] forbiddenMarkers =
        [
            "DataAgentScenarioKnowledgePackProvider",
            "DataAgentScenarioContextBuilder",
            "DataAgentToolScopePolicy",
            "PostgresDataAgentAnalysisSessionStore",
            "DataAgentAnalysisSessionStoreFactory",
            "DataAgentGraphSidecarContract",
            "DataAgentGraphSidecarNodeKind",
            "DataAgentGraphSidecarAuthority",
            "DataAgentGraphSidecarOptions",
            "DataAgentGraphSidecarPolicy",
            "DataAgentGraphSidecarRequest",
            "DataAgentGraphSidecarResponse",
            "DataAgentDataQueryGraph"
        ];

        string[] offenders = Directory.EnumerateFiles(qchatRoot, "*.cs", SearchOption.AllDirectories)
            .SelectMany(path =>
            {
                string text = File.ReadAllText(path);
                return forbiddenMarkers
                    .Where(marker => text.Contains(marker, StringComparison.Ordinal))
                    .Select(marker => $"{Path.GetRelativePath(repoRoot, path)}:{marker}");
            })
            .ToArray();

        Assert.That(offenders, Is.Empty);
    }

    [Test]
    public void QChatEngineeringMapDefaultModeExitsZeroAndPrintsSummary()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-qchat-engineering-map.ps1");

        ScriptResult result = RunPowerShellScript(scriptPath);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            Assert.That(GetEngineeringMapSummaryLines(result.StandardOutput), Is.EqualTo(new[]
            {
                "Summary: 60 required passed, 0 required missing, 0 optional present, 0 optional missing"
            }));
        });
    }

    [Test]
    public void QChatEngineeringMapScriptProtectsRequiredCheckCount()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-qchat-engineering-map.ps1");
        string script = File.ReadAllText(scriptPath);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("$expectedRequired = 60"));
            Assert.That(script, Does.Contain("engineering map check count mismatch"));
            Assert.That(script, Does.Contain("$requiredTotal"));
        });
    }

    static ScriptResult RunPowerShellScript(string scriptPath)
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
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start PowerShell.");

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();

        if (process.WaitForExit(15000) == false)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("QChat engineering map script did not exit within 15 seconds.");
        }

        return new ScriptResult(process.ExitCode, stdout, stderr);
    }

    static string[] GetEngineeringMapSummaryLines(string output)
    {
        return output
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Where(line => line.StartsWith("Summary:", StringComparison.Ordinal))
            .ToArray();
    }

    static string FindAddCheckDeclaration(string script, string checkName)
    {
        string marker = $"-Name \"{checkName}\"";
        int nameIndex = script.IndexOf(marker, StringComparison.Ordinal);
        if (nameIndex < 0)
            return string.Empty;

        int start = script.LastIndexOf("Add-Check", nameIndex, StringComparison.Ordinal);
        if (start < 0)
            return string.Empty;

        int next = script.IndexOf("Add-Check", nameIndex + marker.Length, StringComparison.Ordinal);
        return next < 0
            ? script[start..]
            : script[start..next];
    }

    static string FindRepoRoot(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tools")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test directory.");
    }

    readonly record struct ScriptResult(int ExitCode, string StandardOutput, string StandardError);
}
