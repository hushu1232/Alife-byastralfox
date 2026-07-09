namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV312ReadinessTests
{
    [Test]
    public void V312DocumentDeclaresReplayParityShadowBoundary()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string doc = File.ReadAllText(Path.Combine(
            repoRoot,
            "docs",
            "dataagent",
            "dataagent-v3.12-replay-parity-shadow-comparison.md"));

        Assert.Multiple(() =>
        {
            Assert.That(doc, Does.Contain("shadow_only=true"));
            Assert.That(doc, Does.Contain("default_result_changed=false"));
            Assert.That(doc, Does.Contain("replay_parity_required=true"));
            Assert.That(doc, Does.Contain("no_sql_authority=true"));
            Assert.That(doc, Does.Contain("fallback_required=true"));
            Assert.That(doc, Does.Contain("accepted_advisory_difference"));
            Assert.That(doc, Does.Contain("rejected_authority_claim"));
            Assert.That(doc, Does.Contain("timeout_or_transport_failure"));
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
