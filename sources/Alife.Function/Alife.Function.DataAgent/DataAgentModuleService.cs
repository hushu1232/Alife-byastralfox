using Alife.Framework;
using Alife.Function.FunctionCaller;

namespace Alife.Function.DataAgent;

[Module(
    "DataAgent",
    "Provides governed natural-language analytics over local Alife engineering evidence.",
    defaultCategory: "astralfox-alife/Data Analytics")]
public sealed class DataAgentModuleService(XmlFunctionCaller functionService)
    : InteractiveModule<DataAgentModuleService>
{
    readonly DataAgentV45ProductionObservationRecorder productionObservationRecorder = new();

    public IReadOnlyList<string> RegisteredCapabilityProviderNames { get; private set; } = [];
    public IReadOnlyList<string> RegisteredCapabilityToolNames { get; private set; } = [];

    public DataAgentV45ProductionObservationSnapshot GetProductionShadowObservationSnapshot(
        DateTimeOffset? now = null) =>
        productionObservationRecorder.GetSnapshot(now ?? DateTimeOffset.UtcNow);

    internal static IDataAgentAnalysisSessionStore CreateAnalysisSessionStore(
        DataAgentAnalysisSessionStoreOptions options) =>
        DataAgentAnalysisSessionStoreFactory.Create(options);

    internal static IDataAgentGraphSidecarClient CreateGraphHandshakeSidecarClient(
        DataAgentGraphHandshakeOptions graphOptions,
        DataAgentGraphHandshakeHttpOptions httpOptions,
        DataAgentV44ProductionShadowOptions productionShadowOptions)
    {
        return CreateGraphHandshakeSidecarClientWithProvider(
            graphOptions,
            httpOptions,
            productionShadowOptions,
            () => productionShadowOptions);
    }

    static IDataAgentGraphSidecarClient CreateGraphHandshakeSidecarClientWithProvider(
        DataAgentGraphHandshakeOptions graphOptions,
        DataAgentGraphHandshakeHttpOptions httpOptions,
        DataAgentV44ProductionShadowOptions productionShadowOptions,
        Func<DataAgentV44ProductionShadowOptions> optionsProvider)
    {
        if (graphOptions.Enabled == false ||
            httpOptions.Configured == false ||
            httpOptions.Endpoint is null)
        {
            return DisabledDataAgentGraphSidecarClient.Instance;
        }

        IDataAgentGraphSidecarClient httpClient =
            new DataAgentGraphHandshakeHttpClient(new HttpClient(), httpOptions);
        return productionShadowOptions.Enabled
            ? new DataAgentV44ProductionShadowClient(
                httpClient,
                productionShadowOptions,
                optionsProvider: optionsProvider)
            : httpClient;
    }

    internal static IDataAgentGraphHandshakeStreamClient? CreateGraphHandshakeStreamClient(
        DataAgentGraphHandshakeOptions graphOptions,
        DataAgentGraphHandshakeStreamOptions streamOptions)
    {
        if (graphOptions.Enabled == false ||
            streamOptions.Enabled == false ||
            streamOptions.Configured == false ||
            streamOptions.Endpoint is null)
        {
            return null;
        }

        return new DataAgentGraphHandshakeNdjsonStreamClient(new HttpClient(), streamOptions);
    }

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);

        string databasePath = Path.Combine(AppContext.BaseDirectory, "DataAgent", "dataagent.sqlite");
        IDataAgentStore store = DataAgentStoreFactory.Create(DataAgentStoreFactory.FromEnvironment(databasePath));
        store.Initialize();
        store.ImportFixtures();

        DataAgentService service = new(store);
        IDataAgentAnalysisSessionStore analysisSessionStore = CreateAnalysisSessionStore(
            DataAgentAnalysisSessionStoreFactory.FromEnvironment());
        DataAgentProgressRecorder progressRecorder = new();
        IDataAgentProgressSink progressSink = new DataAgentProgressDiagnosticsPublisher(
            progressRecorder,
            functionService.RecordRecentDataAgentProgressDiagnostics);
        DataAgentAnalysisService analysisService = new DataAgentAnalysisService(
            service,
            analysisSessionStore,
            progressSink: progressSink);
        IDataAgentAnalysisOrchestrator analysisOrchestrator = new DataAgentAnalysisOrchestrator(
            analysisService,
            analysisSessionStore,
            progressSink: progressSink);
        IDataAgentToolRouteContextAccessor routeContextAccessor =
            new XmlPolicyDataAgentToolRouteContextAccessor(functionService.ExecutionPolicy);
        IDataAgentTraceRecorder traceRecorder = new DataAgentTraceRecorder();
        DataAgentGraphHandshakeOptions graphHandshakeOptions = DataAgentGraphHandshakeOptions.FromEnvironment();
        DataAgentGraphHandshakeHttpOptions graphHandshakeHttpOptions =
            DataAgentGraphHandshakeHttpOptions.FromEnvironment();
        DataAgentGraphHandshakeStreamOptions graphHandshakeStreamOptions =
            DataAgentGraphHandshakeStreamOptions.FromEnvironment();
        DataAgentV44ProductionShadowOptions productionShadowOptions =
            DataAgentV44ProductionShadowOptions.FromEnvironment();
        DataAgentGraphHandshakeCoordinator graphHandshakeCoordinator = new(
            graphHandshakeOptions,
            CreateGraphHandshakeSidecarClientWithProvider(
                graphHandshakeOptions,
                graphHandshakeHttpOptions,
                productionShadowOptions,
                optionsProvider: DataAgentV44ProductionShadowOptions.FromEnvironment),
            new DataAgentGraphSidecarProgressBridge(progressSink),
            productionShadowOptions.Enabled
                ? null
                : CreateGraphHandshakeStreamClient(graphHandshakeOptions, graphHandshakeStreamOptions),
            new DataAgentGraphSidecarObservabilityContext(
                graphHandshakeHttpOptions.Configured || graphHandshakeStreamOptions.Configured,
                graphHandshakeHttpOptions.RuntimeStarted || graphHandshakeStreamOptions.RuntimeStarted),
            observationRecorder: productionObservationRecorder);

        DataAgentCapabilityRegistry capabilityRegistry = new();
        capabilityRegistry.Add(new DataAgentQueryCapabilityProvider(service, Poke));
        capabilityRegistry.Add(new DataAgentAnalysisCapabilityProvider(
            orchestrator: analysisOrchestrator,
            resultPublisher: PublishAnalysisContext,
            routeContextAccessor: routeContextAccessor,
            evidenceDiagnosticsPublisher: functionService.RecordRecentDataAgentEvidenceDiagnostics,
            traceDiagnosticsPublisher: functionService.RecordRecentDataAgentTraceDiagnostics,
            traceRecorder: traceRecorder,
            dataQueryGraphDiagnosticsPublisher: functionService.RecordRecentDataAgentGraphDiagnostics,
            graphHandshakeCoordinator: graphHandshakeCoordinator));

        DataAgentCapabilityRegistrar registrar = new(functionService);
        foreach (IDataAgentCapabilityProvider provider in capabilityRegistry.Providers)
            provider.Register(registrar);

        RegisteredCapabilityProviderNames = capabilityRegistry.ProviderNames;
        RegisteredCapabilityToolNames = capabilityRegistry.ToolNames;

        Prompt($"""
                DataAgent provides governed NL2SQL analytics over local Alife engineering evidence.

                ## Tool Broker contract
                - DataAgent XML tools are governed per turn by Tool Broker route state.
                - Only use DataAgent XML tools when they appear in current [tool_route_context].
                - The input to routed DataAgent query tools is natural language, not raw SQL.
                - Analysis summarize and end actions do not execute SQL.
                - If the current route does not show a DataAgent tool, do not call it from memory.
                """);

        void PublishAnalysisContext(string context)
        {
            functionService.UpdateDataAgentAnalysisRouteSessionFromContext(context);
            Poke(context);
        }
    }
}
