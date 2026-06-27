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

        Prompt($"""
                DataAgent provides governed NL2SQL analytics over local Alife engineering evidence.

                ## Tool contract
                - Use dataagent_query when the user asks about project state, readiness, tests, gates, or DataAgent documentation.
                - The input is a natural-language question, not raw SQL.
                - The output is dynamic data context and should be summarized after the tool result is available.

                ## Available tool
                {xmlHandler.FunctionDocument()}
                """);
    }
}
