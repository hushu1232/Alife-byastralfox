namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV314ReadinessTests
{
    [Test]
    public void V314DocumentDeclaresCrossModulePlannerOnlyBoundary()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string doc = File.ReadAllText(Path.Combine(
            repoRoot,
            "docs",
            "dataagent",
            "dataagent-v3.14-cross-module-planner-manifests.md"));

        Assert.Multiple(() =>
        {
            Assert.That(doc, Does.Contain("planner_only=true"));
            Assert.That(doc, Does.Contain("cross_module_advisory=true"));
            Assert.That(doc, Does.Contain("allows_execution=false"));
            Assert.That(doc, Does.Contain("allows_state_write=false"));
            Assert.That(doc, Does.Contain("allows_visible_text=false"));
            Assert.That(doc, Does.Contain("qchat.intent_hint"));
            Assert.That(doc, Does.Contain("memory.candidate_summary"));
            Assert.That(doc, Does.Contain("browser.task_plan"));
            Assert.That(doc, Does.Contain("desktop.task_plan"));
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
