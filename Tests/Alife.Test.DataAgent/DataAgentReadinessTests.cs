using Alife.Function.DataAgent;
using System.Diagnostics;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentReadinessTests
{
    [Test]
    public void CoreReadinessChecksAllPass()
    {
        string databasePath = NewDatabasePath();

        IReadOnlyList<DataAgentReadinessCheck> checks = DataAgentReadiness.CheckCore(databasePath);

        Assert.Multiple(() =>
        {
            Assert.That(checks, Has.Count.EqualTo(56));
            Assert.That(checks.All(check => check.Passed), Is.True, string.Join(Environment.NewLine, checks.Select(check => $"{check.Name}:{check.Detail}")));
            Assert.That(checks.Select(check => check.Name), Does.Contain("DataAgentModulePresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("SqliteSchemaInitializes"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("FixtureDataImports"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("QueryPlanFixturesPass"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("DangerousSqlRejected"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("ReadOnlyQueryExecutes"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("DataAgentStoreBoundaryPresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("SqliteStoreCompatibilityPresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("PostgresStoreProviderPresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("PostgresLiveTestsEnvironmentGated"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("DataAgentServiceUsesStoreBoundary"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("ContextContributionStable"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("PlannerInterfacePresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("DeterministicPlannerPassesFixtures"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("ServiceUsesInjectedPlanner"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("UnsafePlannerOutputRejected"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("ToolHandlerReturnsDataAgentContext"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("LlmPlannerInterfacePresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("LlmPlannerPromptUsesSchemaSnapshot"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("LlmPlannerStrictJsonParser"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("LlmPlannerRejectsInvalidOutput"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("LlmPlannerFallbackPreservesSafety"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("ClarificationRequestSupported"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("NaturalLanguageResultExplanationPresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("ToolBrokerAuditLogPresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("CapabilityBoundaryPresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("DataAgentOrchestratorPresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("OrchestratorNodeBoundaryPresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("OrchestratorCheckpointPresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("OrchestratorRouteGateFailClosed"));
            DataAgentReadinessCheck routeGateCheck = checks.Single(check => check.Name == "OrchestratorRouteGateFailClosed");
            Assert.That(routeGateCheck.Detail, Does.Contain("continue"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("OrchestratorTerminalNodesDoNotQuery"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("OrchestratorStateMachineTransitions"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("AnalysisToolHandlerUsesOrchestrator"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("OrchestratorTraceContextPresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("OrchestratorCheckpointContextPresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("OrchestratorRuntimeStartPathCovered"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("OrchestratorRuntimeContinuePathCovered"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("OrchestratorRuntimeTerminalPathCovered"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("OrchestratorRuntimeRouteDeniedFailClosed"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("AnalysisHandlerConsumesToolRouteContext"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("OrchestrationRequestUsesRuntimeRouteDecision"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("RouteMissingRequestFailsClosed"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("RouteEvidenceContextPresent"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("RouteSessionScopePreserved"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("TerminalRouteDoesNotQuery"));
            DataAgentReadinessCheck terminalRouteCheck = checks.Single(check => check.Name == "TerminalRouteDoesNotQuery");
            Assert.That(terminalRouteCheck.Detail, Does.Contain("route_tool=dataagent_analysis_summarize"));
            Assert.That(terminalRouteCheck.Detail, Does.Contain("route_allows_query=true"));
            Assert.That(terminalRouteCheck.Detail, Does.Match("route_session_id=[0-9a-f]{32}"));
            Assert.That(terminalRouteCheck.Detail, Does.Contain("answer_calls_unchanged=true"));
            Assert.That(terminalRouteCheck.Detail, Does.Contain("denied_terminal_fail_closed=true"));
            Assert.That(checks.Select(check => check.Name), Does.Contain("DataAgentEvidencePackPresent"));
            DataAgentReadinessCheck evidencePackCheck = checks.Single(check => check.Name == "DataAgentEvidencePackPresent");
            Assert.That(evidencePackCheck.Detail, Does.Contain("accepted=true"));
            Assert.That(evidencePackCheck.Detail, Does.Contain("accepted_route_context=runtime"));
            Assert.That(evidencePackCheck.Detail, Does.Contain("denied=true"));
            Assert.That(evidencePackCheck.Detail, Does.Contain("terminal=true"));
        });
    }

    [Test]
    public void ReadinessScriptDefaultModeExitsZeroAndPrintsSummary()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1");

        ScriptResult result = RunPowerShellScript(scriptPath);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("DataAgent Readiness"));
            Assert.That(result.StandardOutput, Does.Contain("DataAgentModulePresent"));
            Assert.That(result.StandardOutput, Does.Contain("[Analysis]"));
            Assert.That(result.StandardOutput, Does.Contain("AnalysisSummaryWindowPresent"));
            Assert.That(GetSummaryLines(result.StandardOutput), Is.EqualTo(new[]
            {
                "  Summary: 70 required passed, 0 required missing"
            }));
            Assert.That(result.StandardOutput, Does.Contain("AnalysisToolHandlerUsesOrchestrator"));
            Assert.That(result.StandardOutput, Does.Contain("OrchestratorTraceContextPresent"));
            Assert.That(result.StandardOutput, Does.Contain("OrchestratorRuntimeRouteDeniedFailClosed"));
            Assert.That(result.StandardOutput, Does.Contain("AnalysisHandlerConsumesToolRouteContext"));
            Assert.That(result.StandardOutput, Does.Contain("OrchestrationRequestUsesRuntimeRouteDecision"));
            Assert.That(result.StandardOutput, Does.Contain("RouteMissingRequestFailsClosed"));
            Assert.That(result.StandardOutput, Does.Contain("RouteEvidenceContextPresent"));
            Assert.That(result.StandardOutput, Does.Contain("RouteSessionScopePreserved"));
            Assert.That(result.StandardOutput, Does.Contain("TerminalRouteDoesNotQuery"));
            Assert.That(result.StandardOutput, Does.Contain("DataAgentEvidencePackPresent"));
            Assert.That(result.StandardOutput, Does.Not.Contain("Baseline Summary"));
        });
    }

    [Test]
    public void ReadinessScriptProtectsV23RouteGateContract()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1");
        string script = File.ReadAllText(scriptPath);

        string declaration = FindNewCheckDeclaration(script, "OrchestrationRequestUsesRuntimeRouteDecision");

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("$expectedRequired = 70"));
            Assert.That(script, Does.Contain("readiness check count mismatch"));
            Assert.That(script, Does.Contain("function Test-FileOrderedMarkers"));
            Assert.That(declaration, Does.Contain("Test-FileOrderedMarkers"));
            Assert.That(declaration, Does.Contain("new DataAgentOrchestrationRequest("));
            Assert.That(declaration, Does.Contain("routeContext.AllowsQuery"));
            Assert.That(declaration, Does.Contain("routeContext))"));
        });
    }

    [Test]
    public void EngineeringMapDeclaresDataAgentReadinessAsRequired()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-qchat-engineering-map.ps1");
        string script = File.ReadAllText(scriptPath);

        string declaration = FindAddCheckDeclaration(script, "DataAgent readiness script");

        Assert.Multiple(() =>
        {
            Assert.That(declaration, Is.Not.Empty);
            Assert.That(declaration, Does.Not.Contain("-Required $false"));
            Assert.That(declaration, Does.Contain("DataAgent Readiness"));
            Assert.That(declaration, Does.Contain("QueryPlanFixturesPass"));
            Assert.That(declaration, Does.Contain("ContextContributionStable"));
            Assert.That(declaration, Does.Contain("PlannerInterfacePresent"));
            Assert.That(declaration, Does.Contain("ToolHandlerReturnsDataAgentContext"));
            Assert.That(declaration, Does.Contain("CapabilityBoundaryPresent"));
        });
    }

    [Test]
    public void EngineeringMapDeclaresDataAgentStoreBoundaryAsRequired()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "check-qchat-engineering-map.ps1");
        string script = File.ReadAllText(scriptPath);

        string declaration = FindAddCheckDeclaration(script, "DataAgent store provider boundary");

        Assert.Multiple(() =>
        {
            Assert.That(declaration, Is.Not.Empty);
            Assert.That(declaration, Does.Not.Contain("-Required $false"));
            Assert.That(declaration, Does.Contain("DataAgentStoreBoundaryPresent"));
            Assert.That(declaration, Does.Contain("SqliteStoreCompatibilityPresent"));
            Assert.That(declaration, Does.Contain("PostgresStoreProviderPresent"));
            Assert.That(declaration, Does.Contain("PostgresLiveTestsEnvironmentGated"));
            Assert.That(declaration, Does.Contain("DataAgentServiceUsesStoreBoundary"));
        });
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
                "Summary: 43 required passed, 0 required missing, 0 optional present, 0 optional missing"
            }));
        });
    }

    static string NewDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-readiness-tests");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
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
            throw new TimeoutException("DataAgent readiness script did not exit within 15 seconds.");
        }

        return new ScriptResult(process.ExitCode, stdout, stderr);
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

    static string FindNewCheckDeclaration(string script, string checkName)
    {
        string marker = $"-Name \"{checkName}\"";
        int nameIndex = script.IndexOf(marker, StringComparison.Ordinal);
        if (nameIndex < 0)
            return string.Empty;

        int start = script.LastIndexOf("New-Check", nameIndex, StringComparison.Ordinal);
        if (start < 0)
            return string.Empty;

        int next = script.IndexOf("New-Check", nameIndex + marker.Length, StringComparison.Ordinal);
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

    static string[] GetSummaryLines(string output)
    {
        return output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("  Summary:", StringComparison.Ordinal))
            .ToArray();
    }

    static string[] GetEngineeringMapSummaryLines(string output)
    {
        return output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("Summary:", StringComparison.Ordinal))
            .ToArray();
    }

    readonly record struct ScriptResult(int ExitCode, string StandardOutput, string StandardError);
}
