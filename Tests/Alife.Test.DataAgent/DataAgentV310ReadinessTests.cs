namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV310ReadinessTests
{
    [Test]
    public void StaticReadinessScriptDeclaresV310RuntimeAdmissionContract()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string script = File.ReadAllText(Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1"));
        string declaration = FindReadinessCheckDeclaration(script, "LangGraphRuntimeReadinessContractPresent");

        Assert.Multiple(() =>
        {
            Assert.That(declaration, Is.Not.Empty,
                "Missing readiness declaration for LangGraphRuntimeReadinessContractPresent.");
            Assert.That(declaration, Does.Contain("docs/dataagent/dataagent-v3.10-langgraph-runtime-readiness-contract.md"));
            Assert.That(script, Does.Contain("$expectedRequired = 117"));
        });
    }

    [Test]
    public void StaticReadinessDeclarationPreservesBoundedLifecycleAndAuthorityMarkers()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string script = File.ReadAllText(Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1"));
        string declaration = FindReadinessCheckDeclaration(script, "LangGraphRuntimeReadinessContractPresent");

        string[] requiredMarkers =
        {
            "manual_only=true",
            "advisory_only=true",
            "loopback_only=true",
            "starts_runtime=false",
            "installs_dependencies=false",
            "creates_venv=false",
            "binds_port=false",
            "supervises_process=false",
            "no_sql_authority=true",
            "no_checkpoint_mutation=true",
            "no_visible_text=true",
            "fallback_required=true",
            "replay_parity_required=true",
            "default_tests_live_runtime=false"
        };

        Assert.Multiple(() =>
        {
            foreach (string marker in requiredMarkers)
                Assert.That(declaration, Does.Contain(marker), $"Missing bounded marker: {marker}");
        });
    }

    [Test]
    public void ContractDocumentsAdmissionEndpointsVersionHandoffAndCSharpAuthority()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string contractPath = Path.Combine(
            repoRoot,
            "docs",
            "dataagent",
            "dataagent-v3.10-langgraph-runtime-readiness-contract.md");
        Assert.That(File.Exists(contractPath), Is.True, "Missing V3.10 runtime readiness contract document.");

        string doc = File.ReadAllText(contractPath);

        Assert.Multiple(() =>
        {
            Assert.That(doc, Does.Contain("V3.10 is not runtime integration"));
            Assert.That(doc, Does.Contain("GET /health"));
            Assert.That(doc, Does.Contain("POST /handshake"));
            Assert.That(doc, Does.Contain("POST /handshake-stream"));
            Assert.That(doc, Does.Contain("V3.11  Real LangGraph sidecar skeleton"));
            Assert.That(doc, Does.Contain("manual-only, loopback-only, default-disabled"));
            Assert.That(doc, Does.Contain("V3.12  Replay parity / shadow comparison"));
            Assert.That(doc, Does.Contain("V4.0   Advisory runtime integration"));
            Assert.That(doc, Does.Contain("C# remains the authority"));
            Assert.That(doc, Does.Contain("SQL execution"));
            Assert.That(doc, Does.Contain("checkpoint mutation"));
            Assert.That(doc, Does.Contain("Tool Broker route decisions"));
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
