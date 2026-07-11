using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV30ReadinessTests
{
    [Test]
    public void CoreReadinessIncludesGraphHandshakeBoundary()
    {
        IReadOnlyList<DataAgentReadinessCheck> checks = DataAgentReadiness.CheckCore(NewDatabasePath());
        DataAgentReadinessCheck check = checks.Single(item => item.Name == "GraphHandshakeBoundaryPresent");

        Assert.Multiple(() =>
        {
            Assert.That(check.Passed, Is.True, check.Detail);
            Assert.That(check.Detail, Does.Contain("default_enabled=false"));
            Assert.That(check.Detail, Does.Contain("validator=true"));
            Assert.That(check.Detail, Does.Contain("no_sql_authority=true"));
            Assert.That(check.Detail, Does.Contain("secret_marker_safety=true"));
            Assert.That(check.Detail, Does.Contain("scoped_node_manifest=true"));
            Assert.That(check.Detail, Does.Contain("fallback=true"));
            Assert.That(check.Detail, Does.Contain("runtime_required=false"));
            Assert.That(check.Detail, Does.Not.Contain("SELECT"));
            Assert.That(check.Detail, Does.Not.Contain("DROP"));
            Assert.That(check.Detail, Does.Not.Contain("document_index"));
            Assert.That(check.Detail, Does.Not.Contain("hidden_context"));
            Assert.That(check.Detail, Does.Not.Contain("[tool_route_context]"));
            Assert.That(check.Detail, Does.Not.Contain("api_key"));
        });
    }

    [Test]
    public void StaticReadinessScriptContainsV30Markers()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string script = File.ReadAllText(Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1"));
        string declaration = FindReadinessCheckDeclaration(script, "GraphHandshakeBoundaryPresent");

        Assert.Multiple(() =>
        {
            Assert.That(declaration, Is.Not.Empty, "Missing readiness declaration for GraphHandshakeBoundaryPresent.");
            Assert.That(declaration, Does.Contain("DataAgentGraphHandshakeCoordinator"));
            Assert.That(declaration, Does.Contain("DataAgentGraphHandshakeValidator"));
            Assert.That(declaration, Does.Contain("DataAgentGraphHandshakeUnsafeDiagnosticDetector"));
            Assert.That(declaration, Does.Contain("DataAgentGraphHandshakeManifestFactory"));
            Assert.That(declaration, Does.Contain("default_enabled=false"));
            Assert.That(declaration, Does.Contain("no_sql_authority=true"));
            Assert.That(declaration, Does.Contain("secret_marker_safety=true"));
            Assert.That(declaration, Does.Contain("scoped_node_manifest=true"));
            Assert.That(declaration, Does.Contain("fallback=true"));
            Assert.That(script, Does.Contain("$expectedRequired = 118"));
        });
    }

    [Test]
    public void DynamicReadinessUsesIndependentHandshakeSafetyProbes()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string source = File.ReadAllText(Path.Combine(
            repoRoot,
            "sources",
            "Alife.Function",
            "Alife.Function.DataAgent",
            "DataAgentReadiness.cs"));
        string declaration = FindGraphHandshakeReadinessBlock(source);
        string normalizedDeclaration = declaration.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(declaration, Does.Contain("graphHandshakeSqlAuthorityValidation"));
            Assert.That(declaration, Does.Contain("graphHandshakeUnsafeTraceValidation"));
            Assert.That(declaration, Does.Contain("graphHandshakeUnsafeMarkerValidation"));
            Assert.That(declaration, Does.Contain("\"sql_authority_requested\""));
            Assert.That(declaration, Does.Contain("\"unsafe_trace\""));
            Assert.That(declaration, Does.Contain("secret_marker_safety=true"));
            Assert.That(normalizedDeclaration, Does.Not.Contain("NoSqlAuthority = false,\n                TraceSummary"));
        });
    }

    [Test]
    public void DynamicReadinessValidatesConservativeHandshakeManifestShape()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string source = File.ReadAllText(Path.Combine(
            repoRoot,
            "sources",
            "Alife.Function",
            "Alife.Function.DataAgent",
            "DataAgentReadiness.cs"));
        string declaration = FindGraphHandshakeReadinessBlock(source);

        Assert.Multiple(() =>
        {
            Assert.That(declaration, Does.Contain("graphHandshakeManifests.Count > 0"));
            Assert.That(declaration, Does.Contain("DataAgentWorkflowNodeNames.ReadOnlyExecute"));
            Assert.That(declaration, Does.Contain("AllowedToolNames.Count == 0"));
            Assert.That(declaration, Does.Contain("DeniedCapabilityMarkers.Contains(DataAgentNodeCapabilities.ExecuteReadOnlyQuery"));
            Assert.That(declaration, Does.Contain("ContainsBroadGraphHandshakeAuthorityToken"));
            Assert.That(declaration, Does.Contain("DataAgentGraphHandshakeToolNames.ExecuteReadOnlyQuery"));
            Assert.That(source, Does.Contain("\"execute\""));
            Assert.That(source, Does.Contain("\"sql.compile\""));
            Assert.That(source, Does.Contain("\"mutation\""));
            Assert.That(source, Does.Contain("\"checkpoint\""));
            Assert.That(declaration, Does.Contain("DataAgentWorkflowNodeNames.QueryPlanner"));
            Assert.That(declaration, Does.Contain("DataAgentGraphHandshakeToolNames.ProposeQueryPlan"));
            Assert.That(declaration, Does.Contain("DataAgentWorkflowNodeNames.DiagnosticsRouter"));
            Assert.That(declaration, Does.Contain("DataAgentGraphHandshakeToolNames.ReadProgressDiagnostics"));
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

    static string FindGraphHandshakeReadinessBlock(string source)
    {
        const string startMarker = "DataAgentGraphHandshakeOptions graphHandshakeDefaultOptions";
        const string endMarker = "checks.Add(graphHandshakeReady";
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0)
            return string.Empty;

        int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        if (end < 0)
            return string.Empty;

        return source[start..end];
    }

    static string NewDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-v30-readiness-tests");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
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
