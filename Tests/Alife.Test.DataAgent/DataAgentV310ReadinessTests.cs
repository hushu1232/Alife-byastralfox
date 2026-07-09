namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV310ReadinessTests
{
    [Test]
    public void StaticReadinessScriptIncludesLangGraphRuntimeReadinessContract()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string script = File.ReadAllText(Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1"));
        string declaration = FindReadinessCheckDeclaration(script, "LangGraphRuntimeReadinessContractPresent");

        Assert.Multiple(() =>
        {
            Assert.That(declaration, Is.Not.Empty, "Missing readiness declaration for LangGraphRuntimeReadinessContractPresent.");
            Assert.That(declaration, Does.Contain("docs/dataagent/dataagent-v3.10-langgraph-runtime-readiness-contract.md"));
            Assert.That(declaration, Does.Contain("manual_only=true"));
            Assert.That(declaration, Does.Contain("advisory_only=true"));
            Assert.That(declaration, Does.Contain("loopback_only=true"));
            Assert.That(declaration, Does.Contain("starts_runtime=false"));
            Assert.That(declaration, Does.Contain("installs_dependencies=false"));
            Assert.That(declaration, Does.Contain("creates_venv=false"));
            Assert.That(declaration, Does.Contain("binds_port=false"));
            Assert.That(declaration, Does.Contain("supervises_process=false"));
            Assert.That(declaration, Does.Contain("no_sql_authority=true"));
            Assert.That(declaration, Does.Contain("no_checkpoint_mutation=true"));
            Assert.That(declaration, Does.Contain("no_visible_text=true"));
            Assert.That(declaration, Does.Contain("fallback_required=true"));
            Assert.That(declaration, Does.Contain("replay_parity_required=true"));
            Assert.That(declaration, Does.Contain("default_tests_live_runtime=false"));
            Assert.That(script, Does.Contain("$expectedRequired = 94"));
        });
    }

    [Test]
    public void ContractDocumentDefinesPreRuntimeAdmissionBoundary()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string doc = File.ReadAllText(Path.Combine(
            repoRoot,
            "docs",
            "dataagent",
            "dataagent-v3.10-langgraph-runtime-readiness-contract.md"));

        Assert.Multiple(() =>
        {
            Assert.That(doc, Does.Contain("V3.10 is not runtime integration"));
            Assert.That(doc, Does.Contain("GET /health"));
            Assert.That(doc, Does.Contain("POST /handshake"));
            Assert.That(doc, Does.Contain("POST /handshake-stream"));
            Assert.That(doc, Does.Contain("V3.11"));
            Assert.That(doc, Does.Contain("manual-only"));
            Assert.That(doc, Does.Contain("loopback-only"));
            Assert.That(doc, Does.Contain("default-disabled"));
            Assert.That(doc, Does.Contain("V3.12"));
            Assert.That(doc, Does.Contain("replay parity"));
            Assert.That(doc, Does.Contain("V4.0"));
            Assert.That(doc, Does.Contain("advisory mode"));
            Assert.That(doc, Does.Contain("C# remains the authority"));
            Assert.That(doc, Does.Contain("manual_only=true"));
            Assert.That(doc, Does.Contain("advisory_only=true"));
            Assert.That(doc, Does.Contain("loopback_only=true"));
            Assert.That(doc, Does.Contain("no_sql_authority=true"));
            Assert.That(doc, Does.Contain("no_checkpoint_mutation=true"));
            Assert.That(doc, Does.Contain("no_visible_text=true"));
            Assert.That(doc, Does.Contain("fallback_required=true"));
            Assert.That(doc, Does.Contain("replay_parity_required=true"));
        });
    }

    [Test]
    public void ContractDocumentForbidsRuntimeAndAuthorityExpansion()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string doc = File.ReadAllText(Path.Combine(
            repoRoot,
            "docs",
            "dataagent",
            "dataagent-v3.10-langgraph-runtime-readiness-contract.md"));

        Assert.Multiple(() =>
        {
            Assert.That(doc, Does.Contain("starts_runtime=false"));
            Assert.That(doc, Does.Contain("installs_dependencies=false"));
            Assert.That(doc, Does.Contain("creates_venv=false"));
            Assert.That(doc, Does.Contain("binds_port=false"));
            Assert.That(doc, Does.Contain("supervises_process=false"));
            Assert.That(doc, Does.Contain("default_tests_live_runtime=false"));
            Assert.That(doc, Does.Contain("SQL execution"));
            Assert.That(doc, Does.Contain("checkpoint mutation"));
            Assert.That(doc, Does.Contain("Tool Broker route"));
            Assert.That(doc, Does.Contain("QChat visible text"));
            Assert.That(doc, Does.Contain("QQ ingress"));
            Assert.That(doc, Does.Contain("DataAgentGraphHandshakeValidator"));
            Assert.That(doc, Does.Contain("V3.9 replay"));
        });
    }

    static string FindReadinessCheckDeclaration(string script, string checkName)
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
                Directory.Exists(Path.Combine(directory.FullName, "docs")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tools")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
