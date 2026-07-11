using System.Reflection;
using Alife.Framework;
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentModuleServiceTests
{
    [Test]
    public void ModuleServiceHasModuleAttribute()
    {
        ModuleAttribute attribute = typeof(DataAgentModuleService).GetCustomAttribute<ModuleAttribute>()!;

        Assert.Multiple(() =>
        {
            Assert.That(attribute, Is.Not.Null);
            Assert.That(attribute.Name, Is.EqualTo("DataAgent"));
            Assert.That(attribute.DefaultCategory, Is.EqualTo("astralfox-alife/Data Analytics"));
        });
    }

    [Test]
    public void AwakeRegistersDataAgentCapabilityProviders()
    {
        string source = ReadModuleSource();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("DataAgentCapabilityRegistry"));
            Assert.That(source, Does.Contain("DataAgentQueryCapabilityProvider"));
            Assert.That(source, Does.Contain("DataAgentAnalysisCapabilityProvider"));
            Assert.That(source, Does.Contain("DataAgentCapabilityRegistrar"));
            Assert.That(source, Does.Contain("provider.Register(registrar)"));
        });
    }

    [Test]
    public void AwakeUsesConfiguredDataAgentStoreBoundary()
    {
        string source = ReadModuleSource();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("IDataAgentStore"));
            Assert.That(source, Does.Contain("DataAgentStoreFactory.Create"));
            Assert.That(source, Does.Contain("store.Initialize()"));
            Assert.That(source, Does.Contain("store.ImportFixtures()"));
            Assert.That(source, Does.Contain("new(store)"));
        });
    }

    [Test]
    public void AwakeUsesConfiguredAnalysisSessionStoreBoundary()
    {
        string source = ReadModuleSource();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("IDataAgentAnalysisSessionStore"));
            Assert.That(source, Does.Contain("DataAgentAnalysisSessionStoreFactory.Create"));
            Assert.That(source, Does.Contain("DataAgentAnalysisSessionStoreFactory.FromEnvironment"));
            Assert.That(source, Does.Not.Contain("new InMemoryDataAgentAnalysisSessionStore()"));
        });
    }

    [Test]
    public void ModuleAnalysisSessionStoreWiringUsesDataAgentFactoryWithoutPostgresConnection()
    {
        MethodInfo method = typeof(DataAgentModuleService).GetMethod(
            "CreateAnalysisSessionStore",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        Assert.That(method, Is.Not.Null);

        IDataAgentAnalysisSessionStore store = (IDataAgentAnalysisSessionStore)method.Invoke(
            null,
            [new DataAgentAnalysisSessionStoreOptions(string.Empty, string.Empty)])!;

        Assert.That(store, Is.TypeOf<InMemoryDataAgentAnalysisSessionStore>());
    }

    [Test]
    public void ModuleExposesRegisteredProviderAndToolNamesForDiagnostics()
    {
        string source = ReadModuleSource();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("RegisteredCapabilityProviderNames"));
            Assert.That(source, Does.Contain("RegisteredCapabilityToolNames"));
            Assert.That(source, Does.Contain("capabilityRegistry.ProviderNames"));
            Assert.That(source, Does.Contain("capabilityRegistry.ToolNames"));
        });
    }

    [Test]
    public void AwakeConstructsAnalysisOrchestratorForRuntimeTools()
    {
        string source = ReadModuleSource();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("DataAgentAnalysisOrchestrator"));
            Assert.That(source, Does.Contain("IDataAgentAnalysisOrchestrator"));
            Assert.That(source, Does.Contain("new DataAgentAnalysisOrchestrator"));
            Assert.That(source, Does.Contain("new DataAgentAnalysisCapabilityProvider("));
            Assert.That(source, Does.Contain("analysisOrchestrator"));
        });
    }

    [Test]
    public void AwakeWiresRuntimeToolRouteContextAccessor()
    {
        string source = ReadModuleSource();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("XmlPolicyDataAgentToolRouteContextAccessor"));
            Assert.That(source, Does.Contain("functionService.ExecutionPolicy"));
            Assert.That(source, Does.Contain("new DataAgentAnalysisCapabilityProvider("));
            Assert.That(source, Does.Contain("analysisOrchestrator"));
            Assert.That(source, Does.Contain("PublishAnalysisContext"));
            Assert.That(source, Does.Contain("routeContextAccessor"));
        });
    }

    [Test]
    public void AwakeWiresEvidenceDiagnosticsToFunctionCallerBridge()
    {
        string source = ReadModuleSource();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("functionService.RecordRecentDataAgentEvidenceDiagnostics"));
        });
    }

    [Test]
    public void AwakeInjectsToolBrokerPromptWithoutStaticToolDocuments()
    {
        string source = ReadModuleSource();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("Prompt("));
            Assert.That(source, Does.Contain("Tool Broker contract"));
            Assert.That(source, Does.Contain("Only use DataAgent XML tools when they appear in current [tool_route_context]"));
            Assert.That(source, Does.Contain("PublishAnalysisContext"));
            Assert.That(source, Does.Not.Contain("{xmlHandler.FunctionDocument()}"));
            Assert.That(source, Does.Not.Contain("{analysisXmlHandler.FunctionDocument()}"));
        });
    }

    [Test]
    public void AwakeWiresGraphHandshakeHttpClientThroughLoopbackOptionsWithoutStartingRuntime()
    {
        string source = ReadModuleSource();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("DataAgentGraphHandshakeHttpOptions.FromEnvironment"));
            Assert.That(source, Does.Contain("CreateGraphHandshakeSidecarClient"));
            Assert.That(source, Does.Contain("DataAgentGraphHandshakeHttpClient"));
            Assert.That(source, Does.Contain("DisabledDataAgentGraphSidecarClient.Instance"));
            Assert.That(source, Does.Not.Contain("Process.Start"));
            Assert.That(source, Does.Not.Contain("uvicorn"));
            Assert.That(source, Does.Not.Contain("FastAPI"));
        });
    }

    [Test]
    public void GraphHandshakeSidecarFactoryKeepsDefaultDisabledClient()
    {
        MethodInfo method = typeof(DataAgentModuleService).GetMethod(
            "CreateGraphHandshakeSidecarClient",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;

        object client = method.Invoke(
            null,
            [DataAgentGraphHandshakeOptions.Disabled, DataAgentGraphHandshakeHttpOptions.Disabled, DisabledProductionShadowOptions()])!;

        Assert.That(client, Is.SameAs(DisabledDataAgentGraphSidecarClient.Instance));
    }

    [Test]
    public void GraphHandshakeSidecarFactoryCreatesHttpClientOnlyForEnabledLoopbackEndpoint()
    {
        MethodInfo method = typeof(DataAgentModuleService).GetMethod(
            "CreateGraphHandshakeSidecarClient",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        DataAgentGraphHandshakeHttpOptions options =
            DataAgentGraphHandshakeHttpOptions.FromValues("http://127.0.0.1:8765/handshake", "800");

        object client = method.Invoke(null, [new DataAgentGraphHandshakeOptions(true), options, DisabledProductionShadowOptions()])!;

        Assert.That(client, Is.TypeOf<DataAgentGraphHandshakeHttpClient>());
    }

    [Test]
    public void GraphHandshakeSidecarFactoryDecoratesOnlyExplicitlyEnabledProductionShadow()
    {
        MethodInfo method = typeof(DataAgentModuleService).GetMethod(
            "CreateGraphHandshakeSidecarClient",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        DataAgentGraphHandshakeHttpOptions httpOptions =
            DataAgentGraphHandshakeHttpOptions.FromValues("http://127.0.0.1:8765/handshake", "800");
        DataAgentV44ProductionShadowOptions ready = DataAgentV44ProductionShadowOptions.FromValues(
            "true", "false", "80", "proven_useful", "2", "3", "30000");

        object client = method.Invoke(
            null,
            [new DataAgentGraphHandshakeOptions(true), httpOptions, ready])!;

        Assert.That(client, Is.TypeOf<DataAgentV44ProductionShadowClient>());
    }

    [Test]
    public void EnabledProductionShadowWithKillSwitchFailsClosedBeforeHttpCall()
    {
        MethodInfo method = typeof(DataAgentModuleService).GetMethod(
            "CreateGraphHandshakeSidecarClient",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        DataAgentGraphHandshakeHttpOptions httpOptions =
            DataAgentGraphHandshakeHttpOptions.FromValues("http://127.0.0.1:8765/handshake", "800");
        DataAgentV44ProductionShadowOptions killed = DataAgentV44ProductionShadowOptions.FromValues(
            "true", "true", "80", "proven_useful", "2", "3", "30000");
        IDataAgentGraphSidecarClient client = (IDataAgentGraphSidecarClient)method.Invoke(
            null,
            [new DataAgentGraphHandshakeOptions(true), httpOptions, killed])!;

        DataAgentV44ProductionShadowException error = Assert.Throws<DataAgentV44ProductionShadowException>(
            () => client.TryHandshake(MinimalRequest()))!;

        Assert.That(error.ReasonCode, Is.EqualTo("production_shadow_kill_switch_active"));
        Assert.That(error.NetworkAttempted, Is.False);
    }

    [Test]
    public void CreateGraphHandshakeStreamClientReturnsNullUnlessGraphAndStreamAreConfigured()
    {
        MethodInfo method = StreamClientFactoryMethod();
        DataAgentGraphHandshakeStreamOptions configuredStreamOptions =
            DataAgentGraphHandshakeStreamOptions.FromValues("true", "http://127.0.0.1:8765/handshake-stream", "800");

        object? disabledGraphClient = method.Invoke(
            null,
            [DataAgentGraphHandshakeOptions.Disabled, configuredStreamOptions]);
        object? disabledStreamClient = method.Invoke(
            null,
            [new DataAgentGraphHandshakeOptions(true), DataAgentGraphHandshakeStreamOptions.Disabled]);

        Assert.Multiple(() =>
        {
            Assert.That(disabledGraphClient, Is.Null);
            Assert.That(disabledStreamClient, Is.Null);
        });
    }

    [Test]
    public void CreateGraphHandshakeStreamClientCreatesNdjsonClientForConfiguredLoopbackEndpoint()
    {
        MethodInfo method = StreamClientFactoryMethod();
        DataAgentGraphHandshakeStreamOptions configuredStreamOptions =
            DataAgentGraphHandshakeStreamOptions.FromValues("true", "http://127.0.0.1:8765/handshake-stream", "800");

        object client = method.Invoke(
            null,
            [new DataAgentGraphHandshakeOptions(true), configuredStreamOptions])!;

        Assert.That(client, Is.TypeOf<DataAgentGraphHandshakeNdjsonStreamClient>());
    }

    static string ReadModuleSource()
    {
        string root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(
            root,
            "Sources",
            "Alife.Function",
            "Alife.Function.DataAgent",
            "DataAgentModuleService.cs"));
    }

    static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Sources")) &&
                File.Exists(Path.Combine(directory.FullName, "Alife.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }

    static MethodInfo StreamClientFactoryMethod()
    {
        MethodInfo? method = typeof(DataAgentModuleService).GetMethod(
            "CreateGraphHandshakeStreamClient",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.That(method, Is.Not.Null);
        return method!;
    }

    static DataAgentV44ProductionShadowOptions DisabledProductionShadowOptions() =>
        DataAgentV44ProductionShadowOptions.FromValues(null, null, null, null, null, null, null);

    static DataAgentGraphHandshakeRequest MinimalRequest() => new(
        "request-1",
        "session-1",
        "turn-1",
        "owner",
        "review",
        "scenario_context=deterministic_csharp",
        "route_present=true",
        "status=Active",
        DataAgentGraphHandshakeManifestFactory.CreateDefault(),
        true,
        true,
        true,
        DataAgentGraphHandshakeLimits.MaxTraceSummaryChars,
        DataAgentGraphHandshakeLimits.MaxProgressEvents);
}
