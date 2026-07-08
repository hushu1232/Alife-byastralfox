namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentGraphHandshakeDevSidecarStubTests
{
    [Test]
    public void PythonDevStubExposesManualLocalOnlyEndpoints()
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

    [Test]
    public void PythonDevStubDocumentsV32ProgressShapeWithoutRuntimeDependency()
    {
        string root = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string app = File.ReadAllText(Path.Combine(root, "tools", "dataagent-graph-sidecar", "app.py"));
        string readme = File.ReadAllText(Path.Combine(root, "tools", "dataagent-graph-sidecar", "README.md"));
        string doc = File.ReadAllText(Path.Combine(root, "docs", "dataagent", "dataagent-v3.2-sidecar-progress-bridge.md"));

        Assert.Multiple(() =>
        {
            Assert.That(app, Does.Contain("Message: str"));
            Assert.That(app, Does.Contain("Facts: dict[str, str]"));
            Assert.That(app, Does.Contain("scenario_knowledge"));
            Assert.That(app, Does.Contain("\"stage\": \"scenario\""));
            Assert.That(app, Does.Contain("\"stage\": \"planner\""));
            Assert.That(app, Does.Contain("\"stage\": \"diagnostics\""));
            Assert.That(app, Does.Not.Contain("\"source\": \"graph_sidecar\""));
            Assert.That(app, Does.Not.Contain("subprocess"));
            Assert.That(app, Does.Not.Contain("sqlite"));
            Assert.That(app, Does.Not.Contain("postgres"));
            Assert.That(readme, Does.Contain("V3.2"));
            Assert.That(readme, Does.Contain("progress shape"));
            Assert.That(readme, Does.Contain("C# remains the only progress recorder"));
            Assert.That(readme, Does.Contain("C# stamps facts"));
            Assert.That(readme, Does.Contain("source=graph_sidecar"));
            Assert.That(readme, Does.Contain("default tests do not require Python"));
            Assert.That(doc, Does.Contain("DataAgent V3.2"));
            Assert.That(doc, Does.Contain("sidecar progress is untrusted input"));
            Assert.That(doc, Does.Contain("IDataAgentProgressSink"));
            Assert.That(doc, Does.Contain("source=graph_sidecar"));
            Assert.That(doc, Does.Contain("default tests do not require Python"));
            Assert.That(doc, Does.Contain("SSE"));
            Assert.That(doc, Does.Contain("NDJSON"));
        });
    }

    [Test]
    public void PythonDevStubDocumentsV33NdjsonStreamWithoutRuntimeDependency()
    {
        string root = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string app = File.ReadAllText(Path.Combine(root, "tools", "dataagent-graph-sidecar", "app.py"));
        string readme = File.ReadAllText(Path.Combine(root, "tools", "dataagent-graph-sidecar", "README.md"));
        string doc = File.ReadAllText(Path.Combine(root, "docs", "dataagent", "dataagent-v3.3-ndjson-streaming-transport.md"));

        Assert.Multiple(() =>
        {
            Assert.That(app, Does.Contain("@app.post(\"/handshake-stream\")"));
            Assert.That(app, Does.Contain("StreamingResponse"));
            Assert.That(app, Does.Contain("application/x-ndjson"));
            Assert.That(app, Does.Contain("\"Kind\": \"Progress\""));
            Assert.That(app, Does.Contain("\"Kind\": \"FinalResponse\""));
            Assert.That(app, Does.Contain("\"Progress\""));
            Assert.That(app, Does.Contain("\"Response\""));
            Assert.That(app, Does.Contain("\"stage\": \"planner\""));
            Assert.That(app, Does.Not.Contain("\"source\": \"graph_sidecar\""));
            Assert.That(app, Does.Not.Contain("\"node\":"));
            Assert.That(app, Does.Not.Contain("\"request_id\":"));
            Assert.That(app, Does.Not.Contain("EventSource"));
            Assert.That(app, Does.Not.Contain("text/event-stream"));
            Assert.That(app, Does.Not.Contain("subprocess"));
            Assert.That(app, Does.Not.Contain("sqlite"));
            Assert.That(app, Does.Not.Contain("postgres"));
            Assert.That(readme, Does.Contain("V3.3"));
            Assert.That(readme, Does.Contain("/handshake-stream"));
            Assert.That(readme, Does.Contain("NDJSON"));
            Assert.That(readme, Does.Contain("ALIFE_DATAAGENT_GRAPH_HANDSHAKE_STREAM_ENABLED"));
            Assert.That(readme, Does.Contain("ALIFE_DATAAGENT_GRAPH_HANDSHAKE_STREAM_ENDPOINT"));
            Assert.That(readme, Does.Contain("ALIFE_DATAAGENT_GRAPH_HANDSHAKE_STREAM_TIMEOUT_MS"));
            Assert.That(readme, Does.Contain("DataAgentGraphSidecarProgressBridge"));
            Assert.That(readme, Does.Contain("SSE is deferred"));
            Assert.That(readme, Does.Contain("buffered until the final response is accepted"));
            Assert.That(readme, Does.Contain("default tests do not require Python"));
            Assert.That(doc, Does.Contain("DataAgent V3.3"));
            Assert.That(doc, Does.Contain("NDJSON streaming transport smoke"));
            Assert.That(doc, Does.Contain("invalid_stream_schema"));
            Assert.That(doc, Does.Contain("missing_stream_final_response"));
            Assert.That(doc, Does.Contain("stream_progress_over_budget"));
            Assert.That(doc, Does.Contain("sidecar_timeout"));
            Assert.That(doc, Does.Contain("sidecar_unavailable"));
            Assert.That(doc, Does.Contain("Rejected, invalid, timed out, unavailable, malformed, incomplete, and"));
            Assert.That(doc, Does.Contain("over-budget streams publish no sidecar progress"));
            Assert.That(doc, Does.Contain("SSE is deferred"));
        });
    }

    [Test]
    public void PythonDevStubDocumentsV34LiveSmokeHarnessWithoutRuntimeStartup()
    {
        string root = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string script = File.ReadAllText(Path.Combine(root, "tools", "run-dataagent-graph-sidecar-smoke.ps1"));
        string readme = File.ReadAllText(Path.Combine(root, "tools", "dataagent-graph-sidecar", "README.md"));
        string doc = File.ReadAllText(Path.Combine(root, "docs", "dataagent", "dataagent-v3.4-dev-sidecar-live-smoke-harness.md"));

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("DataAgent graph sidecar live smoke"));
            Assert.That(script, Does.Contain("/health"));
            Assert.That(script, Does.Contain("/handshake"));
            Assert.That(script, Does.Contain("/handshake-stream"));
            Assert.That(script, Does.Contain("application/x-ndjson"));
            Assert.That(script, Does.Contain("Assert-LoopbackBaseUri"));
            Assert.That(script, Does.Contain("Invoke-SidecarRequest"));
            Assert.That(script, Does.Contain("Test-HandshakeResponse"));
            Assert.That(script, Does.Contain("Test-NdjsonStream"));
            Assert.That(script, Does.Contain("starts_runtime=false"));
            Assert.That(script, Does.Contain("installs_dependencies=false"));
            Assert.That(script, Does.Contain("manual_only=true"));
            Assert.That(script, Does.Not.Contain("Start-Process"));
            Assert.That(script, Does.Not.Contain("pip install"));
            Assert.That(script, Does.Not.Contain("python -m venv"));
            Assert.That(script, Does.Not.Contain("uvicorn app:app"));
            Assert.That(script, Does.Not.Contain("text/event-stream"));
            Assert.That(script, Does.Not.Contain("EventSource"));
            Assert.That(readme, Does.Contain("V3.4"));
            Assert.That(readme, Does.Contain("run-dataagent-graph-sidecar-smoke.ps1"));
            Assert.That(readme, Does.Contain("does not start Python"));
            Assert.That(readme, Does.Contain("does not install dependencies"));
            Assert.That(readme, Does.Contain("already running sidecar"));
            Assert.That(readme, Does.Contain("SSE is deferred"));
            Assert.That(doc, Does.Contain("DataAgent V3.4"));
            Assert.That(doc, Does.Contain("manual live smoke"));
            Assert.That(doc, Does.Contain("already running sidecar"));
            Assert.That(doc, Does.Contain("default tests do not call a live sidecar"));
            Assert.That(doc, Does.Contain("QChat"));
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
