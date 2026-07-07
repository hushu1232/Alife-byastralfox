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
    public IReadOnlyList<string> RegisteredCapabilityProviderNames { get; private set; } = [];
    public IReadOnlyList<string> RegisteredCapabilityToolNames { get; private set; } = [];

    internal static IDataAgentAnalysisSessionStore CreateAnalysisSessionStore(
        DataAgentAnalysisSessionStoreOptions options) =>
        DataAgentAnalysisSessionStoreFactory.Create(options);

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
        DataAgentGraphHandshakeCoordinator graphHandshakeCoordinator = new(
            DataAgentGraphHandshakeOptions.FromEnvironment(),
            DisabledDataAgentGraphSidecarClient.Instance);

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
