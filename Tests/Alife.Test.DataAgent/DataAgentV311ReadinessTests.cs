using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV311ReadinessTests
{
    [Test]
    public void V311DocumentDeclaresManualOnlyLoopbackBoundary()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string doc = File.ReadAllText(Path.Combine(
            repoRoot,
            "docs",
            "dataagent",
            "dataagent-v3.11-real-langgraph-sidecar-skeleton.md"));

        Assert.Multiple(() =>
        {
            Assert.That(doc, Does.Contain("manual_only=true"));
            Assert.That(doc, Does.Contain("loopback_only=true"));
            Assert.That(doc, Does.Contain("default_enabled=false"));
            Assert.That(doc, Does.Contain("default_tests_live_runtime=false"));
            Assert.That(doc, Does.Contain("starts_runtime=false"));
            Assert.That(doc, Does.Contain("installs_dependencies=false"));
            Assert.That(doc, Does.Contain("creates_venv=false"));
            Assert.That(doc, Does.Contain("binds_port=false"));
            Assert.That(doc, Does.Contain("supervises_process=false"));
            Assert.That(doc, Does.Contain("no_sql_authority=true"));
            Assert.That(doc, Does.Contain("no_checkpoint_mutation=true"));
            Assert.That(doc, Does.Contain("no_visible_text=true"));
            Assert.That(doc, Does.Contain("fallback_required=true"));
        });
    }

    [Test]
    public void ManualSkeletonDoesNotInstallStartOrSuperviseRuntimeFromCSharp()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string moduleSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "sources",
            "Alife.Function",
            "Alife.Function.DataAgent",
            "DataAgentModuleService.cs"));
        string skeleton = File.ReadAllText(Path.Combine(
            repoRoot,
            "tools",
            "dataagent-langgraph-sidecar",
            "server.py"));

        Assert.Multiple(() =>
        {
            Assert.That(moduleSource, Does.Not.Contain("Process.Start"));
            Assert.That(moduleSource, Does.Not.Contain("uvicorn"));
            Assert.That(moduleSource, Does.Not.Contain("FastAPI"));
            Assert.That(moduleSource, Does.Not.Contain("pip install"));
            Assert.That(skeleton, Does.Not.Contain("pip install"));
            Assert.That(skeleton, Does.Not.Contain("subprocess"));
            Assert.That(skeleton, Does.Not.Contain("requests."));
        });
    }

    [Test]
    public void ManualSkeletonEvolvedToStrictTypedLangGraphRuntime()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string runtime = File.ReadAllText(Path.Combine(
            repoRoot,
            "tools",
            "dataagent-langgraph-sidecar",
            "runtime.py"));
        string graph = File.ReadAllText(Path.Combine(
            repoRoot, "tools", "dataagent-langgraph-sidecar", "graph.py"));
        string contracts = File.ReadAllText(Path.Combine(
            repoRoot, "tools", "dataagent-langgraph-sidecar", "contracts.py"));

        Assert.Multiple(() =>
        {
            Assert.That(runtime, Does.Contain("runtime_dependency_unavailable"));
            Assert.That(runtime, Does.Contain("runtime_graph_compile_failed"));
            Assert.That(runtime, Does.Contain("PINNED_LANGGRAPH_VERSION = \"0.3.34\""));
            Assert.That(graph, Does.Contain("from langgraph.graph import END, StateGraph"));
            Assert.That(graph, Does.Contain("AdvisoryGraphState"));
            Assert.That(graph, Does.Contain("workflow.compile()"));
            Assert.That(contracts, Does.Contain("\"FallbackRequired\": False"));
            Assert.That(contracts, Does.Contain("\"NoSqlAuthority\": True"));
        });
    }

    [Test]
    public void ExistingOptionsKeepManualLoopbackDefaultDisabledContract()
    {
        DataAgentGraphHandshakeOptions graphOptions = DataAgentGraphHandshakeOptions.FromValue(null);
        DataAgentGraphHandshakeHttpOptions httpOptions =
            DataAgentGraphHandshakeHttpOptions.FromValues("http://127.0.0.1:8765/handshake", "800");
        DataAgentGraphHandshakeHttpOptions nonLoopbackOptions =
            DataAgentGraphHandshakeHttpOptions.FromValues("http://example.com/handshake", "800");

        Assert.Multiple(() =>
        {
            Assert.That(graphOptions.Enabled, Is.False);
            Assert.That(httpOptions.Configured, Is.True);
            Assert.That(httpOptions.Endpoint!.IsLoopback, Is.True);
            Assert.That(httpOptions.RuntimeStarted, Is.False);
            Assert.That(nonLoopbackOptions.Configured, Is.False);
            Assert.That(nonLoopbackOptions.Endpoint, Is.Null);
            Assert.That(nonLoopbackOptions.RuntimeStarted, Is.False);
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
