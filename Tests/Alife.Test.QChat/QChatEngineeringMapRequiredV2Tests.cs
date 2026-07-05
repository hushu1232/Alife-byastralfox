using NUnit.Framework;
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
            "DataAgentAnalysisSessionStoreFactory"
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
