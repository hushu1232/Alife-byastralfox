namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV316ReadinessTests
{
    [Test]
    public void V316RunbookDeclaresManualLangGraphLiveSmokeReadiness()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string doc = File.ReadAllText(Path.Combine(
            repoRoot,
            "docs",
            "dataagent",
            "dataagent-v3.16-langgraph-live-smoke-readiness.md"));

        Assert.Multiple(() =>
        {
            Assert.That(doc, Does.Contain("operator_runbook=true"));
            Assert.That(doc, Does.Contain("manual_start=true"));
            Assert.That(doc, Does.Contain("loopback_check=true"));
            Assert.That(doc, Does.Contain("smoke_valid_advisory=true"));
            Assert.That(doc, Does.Contain("smoke_forbidden_authority_rejected=true"));
            Assert.That(doc, Does.Contain("smoke_timeout_fallback=true"));
            Assert.That(doc, Does.Contain("kill_switch=true"));
            Assert.That(doc, Does.Contain("default_tests_live_runtime=false"));
            Assert.That(doc, Does.Contain("starts_runtime=false"));
            Assert.That(doc, Does.Contain("installs_dependencies=false"));
            Assert.That(doc, Does.Contain("how to start sidecar manually"));
            Assert.That(doc, Does.Contain("how to verify loopback binding"));
            Assert.That(doc, Does.Contain("how to run smoke tests"));
            Assert.That(doc, Does.Contain("how to inspect diagnostics"));
            Assert.That(doc, Does.Contain("how to stop sidecar"));
            Assert.That(doc, Does.Contain("how to confirm fallback works"));
            Assert.That(doc, Does.Contain("how to prove default chain is unchanged"));
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
