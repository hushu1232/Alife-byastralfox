namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV313ReadinessTests
{
    [Test]
    public void V313DocumentDeclaresBoundedDiagnosticsExplanationBoundary()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string doc = File.ReadAllText(Path.Combine(
            repoRoot,
            "docs",
            "dataagent",
            "dataagent-v3.13-bounded-diagnostics-explanation.md"));

        Assert.Multiple(() =>
        {
            Assert.That(doc, Does.Contain("bounded_explanation=true"));
            Assert.That(doc, Does.Contain("advisory_only=true"));
            Assert.That(doc, Does.Contain("csharp_write_authority=true"));
            Assert.That(doc, Does.Contain("sidecar_write_authority=false"));
            Assert.That(doc, Does.Contain("requests_visible_text=false"));
            Assert.That(doc, Does.Contain("unsafe_text_rejected=true"));
            Assert.That(doc, Does.Contain("fallback_required=true"));
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
