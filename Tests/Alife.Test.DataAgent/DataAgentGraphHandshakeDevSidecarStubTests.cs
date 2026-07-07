namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentGraphHandshakeDevSidecarStubTests
{
    [Test]
    public void PythonDevStubExposesOnlyHandshakeAndHealthEndpoints()
    {
        string root = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string app = File.ReadAllText(Path.Combine(root, "tools", "dataagent-graph-sidecar", "app.py"));

        Assert.Multiple(() =>
        {
            Assert.That(app, Does.Contain("@app.post(\"/handshake\")"));
            Assert.That(app, Does.Contain("@app.get(\"/health\")"));
            Assert.That(app, Does.Contain("NoSqlAuthority"));
            Assert.That(app, Does.Contain("ReadOnly"));
            Assert.That(app, Does.Contain("RequestsCheckpointMutation"));
            Assert.That(app, Does.Contain("RequestsVisibleText"));
            Assert.That(app, Does.Not.Contain("sqlite"));
            Assert.That(app, Does.Not.Contain("postgres"));
            Assert.That(app, Does.Not.Contain("qchat"));
            Assert.That(app, Does.Not.Contain("qq"));
            Assert.That(app, Does.Not.Contain("browser"));
            Assert.That(app, Does.Not.Contain("checkpoint.write"));
            Assert.That(app, Does.Not.Contain("subprocess"));
            Assert.That(app, Does.Not.Contain("open("));
        });
    }

    [Test]
    public void PythonDevStubReadmeDeclaresManualLocalOnlyNonProductionBoundary()
    {
        string root = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string readme = File.ReadAllText(Path.Combine(root, "tools", "dataagent-graph-sidecar", "README.md"));

        Assert.Multiple(() =>
        {
            Assert.That(readme, Does.Contain("optional"));
            Assert.That(readme, Does.Contain("local-only"));
            Assert.That(readme, Does.Contain("manual"));
            Assert.That(readme, Does.Contain("not a production runtime"));
            Assert.That(readme, Does.Contain("ALIFE_DATAAGENT_GRAPH_HANDSHAKE_ENDPOINT"));
            Assert.That(readme, Does.Contain("ALIFE_DATAAGENT_GRAPH_HANDSHAKE_ENABLED"));
        });
    }

    [Test]
    public void DeveloperNoteDocumentsV31BoundaryAndV32Handoff()
    {
        string root = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string doc = File.ReadAllText(Path.Combine(root, "docs", "dataagent", "dataagent-v3.1-dev-sidecar-adapter.md"));

        Assert.Multiple(() =>
        {
            Assert.That(doc, Does.Contain("dev HTTP adapter"));
            Assert.That(doc, Does.Contain("not a production sidecar runtime"));
            Assert.That(doc, Does.Contain("default tests do not require Python"));
            Assert.That(doc, Does.Contain("C# keeps SQL"));
            Assert.That(doc, Does.Contain("Tool Broker"));
            Assert.That(doc, Does.Contain("QChat"));
            Assert.That(doc, Does.Contain("V3.2"));
            Assert.That(doc, Does.Contain("streaming progress"));
        });
    }

    static string FindRepoRoot(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tools")) &&
                Directory.Exists(Path.Combine(directory.FullName, "docs")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
