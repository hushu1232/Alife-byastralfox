using System.ComponentModel;
using Alife.Function.Interpreter;

namespace Alife.Function.DataAgent;

[Description("Runs approved DataAgent natural-language analytics queries and returns a data_agent_context block.")]
public sealed class DataAgentToolHandler(DataAgentService service, Action<string>? resultPublisher = null)
{
    [XmlFunction(FunctionMode.OneShot, name: "dataagent_query")]
    [Description("Ask DataAgent a natural-language analytics question. The result is returned as a data_agent_context block.")]
    public string Query(string question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        string context = service.Answer(question).Context;
        resultPublisher?.Invoke(context);
        return context;
    }
}
