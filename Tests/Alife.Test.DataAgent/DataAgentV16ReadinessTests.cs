namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV16ReadinessTests
{
    static readonly string[] RequiredDataAgentChecks =
    [
        "ToolBrokerRouteDecisionReasonCodesPresent",
        "ToolBrokerExecutionAuditPresent",
        "ToolBrokerAuditLogPresent"
    ];

    [Test]
    public void StaticReadinessScriptContainsAllV16ToolBrokerObservabilityMarkers()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string script = File.ReadAllText(Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1"));

        Assert.Multiple(() =>
        {
            foreach (string checkName in RequiredDataAgentChecks)
                Assert.That(script, Does.Contain(checkName), checkName);

            Assert.That(script, Does.Contain("sources/Alife.Function/Alife.Function.FunctionCaller/ToolRouteModels.cs"));
            Assert.That(script, Does.Contain("ReasonCode"));
            Assert.That(script, Does.Contain("route_allowed"));
            Assert.That(script, Does.Contain("owner_private_required"));

            Assert.That(script, Does.Contain("sources/Alife.Function/Alife.Function.FunctionCaller/XmlStruct.cs"));
            Assert.That(script, Does.Contain("XmlFunctionExecutionAuditRecord"));
            Assert.That(script, Does.Contain("ExecutionAudited"));

            Assert.That(script, Does.Contain("sources/Alife.Function/Alife.Function.DataAgent/DataAgentToolBrokerAuditLog.cs"));
            Assert.That(script, Does.Contain("DataAgentToolBrokerAuditRecord"));
            Assert.That(script, Does.Contain("tool_broker_audit"));
        });
    }

    [Test]
    public void QChatEngineeringMapRequiresOwnerToolBrokerDiagnostics()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string script = File.ReadAllText(Path.Combine(repoRoot, "tools", "check-qchat-engineering-map.ps1"));

        string declaration = FindAddCheckDeclaration(script, "QChat owner Tool Broker diagnostics");

        Assert.Multiple(() =>
        {
            Assert.That(declaration, Is.Not.Empty);
            Assert.That(declaration, Does.Not.Contain("-Required $false"));
            Assert.That(declaration, Does.Contain("QChatDiagnosticsService.cs"));
            Assert.That(declaration, Does.Contain("RecentToolRouteTrace"));
            Assert.That(declaration, Does.Contain("Tool Broker diagnostics"));
        });
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
}