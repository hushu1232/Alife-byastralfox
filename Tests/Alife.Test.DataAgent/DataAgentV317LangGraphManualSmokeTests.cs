namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV317LangGraphManualSmokeTests
{
    [Test]
    public void ManualSmokeHarnessIsOperatorOnlyAndDoesNotStartRuntime()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string script = File.ReadAllText(Path.Combine(repoRoot, "tools", "run-dataagent-langgraph-manual-smoke.ps1"));

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("manual_only=true"));
            Assert.That(script, Does.Contain("starts_runtime=false"));
            Assert.That(script, Does.Contain("installs_dependencies=false"));
            Assert.That(script, Does.Contain("creates_venv=false"));
            Assert.That(script, Does.Contain("binds_port=false"));
            Assert.That(script, Does.Contain("loopback_only=true"));
            Assert.That(script, Does.Contain("smoke_valid_advisory=true"));
            Assert.That(script, Does.Contain("smoke_forbidden_authority_rejected=true"));
            Assert.That(script, Does.Contain("smoke_timeout_fallback=true"));
            Assert.That(script, Does.Not.Contain("Start-Process"));
            Assert.That(script, Does.Not.Contain("pip install"));
            Assert.That(script, Does.Not.Contain("python -m venv"));
            Assert.That(script, Does.Not.Contain("uvicorn"));
        });
    }

    [Test]
    public void V317DocumentDeclaresManualSmokeBoundary()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string doc = File.ReadAllText(Path.Combine(repoRoot, "docs", "dataagent", "dataagent-v3.17-langgraph-manual-smoke.md"));

        Assert.Multiple(() =>
        {
            Assert.That(doc, Does.Contain("manual_smoke=true"));
            Assert.That(doc, Does.Contain("operator_only=true"));
            Assert.That(doc, Does.Contain("default_result_changed=false"));
            Assert.That(doc, Does.Contain("sidecar_write_authority=false"));
            Assert.That(doc, Does.Contain("csharp_execution_authority=true"));
            Assert.That(doc, Does.Contain("fallback_required=true"));
            Assert.That(doc, Does.Contain("manual_only=true"));
            Assert.That(doc, Does.Contain("loopback_only=true"));
        });
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
