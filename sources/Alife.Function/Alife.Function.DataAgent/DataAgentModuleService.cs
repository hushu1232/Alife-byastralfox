using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;

namespace Alife.Function.DataAgent;

[Module(
    "DataAgent",
    "Provides governed natural-language analytics over local Alife engineering evidence.",
    defaultCategory: "astralfox-alife/Data Analytics")]
public sealed class DataAgentModuleService(XmlFunctionCaller functionService)
    : InteractiveModule<DataAgentModuleService>
{
    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);

        string databasePath = Path.Combine(AppContext.BaseDirectory, "DataAgent", "dataagent.sqlite");
        DataAgentSchemaInitializer.Initialize(databasePath);
        DataAgentFixtureImporter.Import(databasePath);

        DataAgentService service = new(databasePath);
        XmlHandler xmlHandler = new XmlHandler(new DataAgentToolHandler(service, Poke));
        functionService.RegisterHandlerWithoutDocument(xmlHandler);

        InMemoryDataAgentAnalysisSessionStore analysisSessionStore = new InMemoryDataAgentAnalysisSessionStore();
        DataAgentAnalysisService analysisService = new DataAgentAnalysisService(service, analysisSessionStore);
        XmlHandler analysisXmlHandler = new XmlHandler(new DataAgentAnalysisToolHandler(analysisService, PublishAnalysisContext));
        functionService.RegisterHandlerWithoutDocument(analysisXmlHandler);

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
