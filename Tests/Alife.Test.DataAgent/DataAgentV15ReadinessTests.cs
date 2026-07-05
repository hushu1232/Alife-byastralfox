namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV15ReadinessTests
{
    static readonly string[] RequiredChecks =
    [
        "AnalysisToolHandlerPresent",
        "AnalysisToolsRegisteredInModule",
        "AnalysisTerminalToolsDoNotQuery",
        "ToolCapabilityManifestPresent",
        "ToolCapabilityRouterPresent",
        "ToolExecutionGatePresent",
        "ToolBrokerDynamicExposurePresent",
        "ToolRouteRuntimeWiringPresent",
        "QChatToolRouteStateScopePresent",
        "ToolBrokerRuntimeTestsPresent"
    ];

    [Test]
    public void StaticReadinessScriptContainsAllV15ToolBrokerMarkers()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string script = File.ReadAllText(Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1"));

        Assert.Multiple(() =>
        {
            foreach (string checkName in RequiredChecks)
                Assert.That(script, Does.Contain(checkName), checkName);

            Assert.That(script, Does.Contain("sources/Alife.Function/Alife.Function.FunctionCaller/ToolCapabilityManifest.cs"));
            Assert.That(script, Does.Contain("ToolCapabilityManifest"));
            Assert.That(script, Does.Contain("ToolCapabilityDomain"));
            Assert.That(script, Does.Contain("ToolCapabilityPrecondition"));

            Assert.That(script, Does.Contain("sources/Alife.Function/Alife.Function.FunctionCaller/ToolCapabilityRouter.cs"));
            Assert.That(script, Does.Contain("ToolCapabilityRouter"));
            Assert.That(script, Does.Contain("Route"));
            Assert.That(script, Does.Contain("dataagent_analysis_continue"));

            Assert.That(script, Does.Contain("sources/Alife.Function/Alife.Function.FunctionCaller/XmlStruct.cs"));
            Assert.That(script, Does.Contain("CurrentRoute"));
            Assert.That(script, Does.Contain("tool_not_allowed_in_current_route"));
            Assert.That(script, Does.Contain("SetGovernedToolNames"));
            Assert.That(script, Does.Contain("tool_route_required"));
            Assert.That(script, Does.Contain("tool_session_not_allowed_in_current_route"));

            Assert.That(script, Does.Contain("sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs"));
            Assert.That(script, Does.Contain("DataAgentAnalysisToolHandler"));
            Assert.That(script, Does.Contain("dataagent_analysis_start"));
            Assert.That(script, Does.Contain("dataagent_analysis_continue"));
            Assert.That(script, Does.Contain("dataagent_analysis_summarize"));
            Assert.That(script, Does.Contain("dataagent_analysis_end"));

            Assert.That(script, Does.Contain("sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs"));
            Assert.That(script, Does.Contain("InMemoryDataAgentAnalysisSessionStore"));
            Assert.That(script, Does.Contain("PublishAnalysisContext"));
            Assert.That(script, Does.Contain("UpdateDataAgentAnalysisRouteSessionFromContext"));
            Assert.That(script, Does.Contain("Only use DataAgent XML tools when they appear in current [tool_route_context]"));

            Assert.That(script, Does.Contain("sources/Alife.Function/Alife.Function.FunctionCaller/XmlFunctionCaller.cs"));
            Assert.That(script, Does.Contain("RouteCurrentTurn"));
            Assert.That(script, Does.Contain("BuildRoutedFunctionGuide"));
            Assert.That(script, Does.Contain("[tool_route_context]"));

            Assert.That(script, Does.Contain("sources/Alife.Function/Alife.Function.QChat/QChatService.cs"));
            Assert.That(script, Does.Contain("protected virtual async Task<string> DispatchToModelAsync"));
            Assert.That(script, Does.Contain("functionService.CreateToolRouteState"));
            Assert.That(script, Does.Contain("functionService.UseToolRouteState(routeState)"));
            Assert.That(script, Does.Contain("ChatBot.ChatAsync(ChatTextFilter"));

            Assert.That(script, Does.Contain("Tests/Alife.Test.QChat/QChatToolRouteStateWiringTests.cs"));
            Assert.That(script, Does.Contain("HandleRejectsGovernedDataAgentToolWhenRouteIsMissing"));
            Assert.That(script, Does.Contain("HandleRejectsSessionScopedDataAgentToolWhenRouteSessionDoesNotMatch"));
            Assert.That(script, Does.Contain("Tests/Alife.Test.DataAgent/DataAgentAnalysisToolHandlerTests.cs"));
            Assert.That(script, Does.Contain("SummarizeCallsOrchestratorAndPublishesTerminalContext"));
            Assert.That(script, Does.Contain("EndCallsOrchestratorAndPublishesTerminalContext"));
            Assert.That(script, Does.Contain("foreach ($group in @(\"Core\", \"Schema\", \"Safety\", \"Query\", \"Context\", \"Planner\", \"Tool\", \"ToolBroker\", \"Store\", \"Governance\", \"Analysis\"))"));
        });
    }

    [Test]
    public void StaticReadinessScriptContainsV17CapabilityBoundaryMarkers()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string script = File.ReadAllText(Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1"));

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("CapabilityBoundaryPresent"));
            Assert.That(script, Does.Contain("IDataAgentCapabilityProvider"));
            Assert.That(script, Does.Contain("DataAgentCapabilityRegistry"));
            Assert.That(script, Does.Contain("DataAgentQueryCapabilityProvider"));
            Assert.That(script, Does.Contain("DataAgentAnalysisCapabilityProvider"));
            Assert.That(script, Does.Contain("DataAgentToolCapabilityManifests"));
        });
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
}
