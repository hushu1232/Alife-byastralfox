using System.ComponentModel;
using Alife.Function.Interpreter;

namespace Alife.Function.DataAgent;

[Description("Runs multi-turn DataAgent analysis sessions and returns data_agent_analysis_session_context blocks.")]
public sealed class DataAgentAnalysisToolHandler(DataAgentAnalysisService service, Action<string>? resultPublisher = null)
{
    [XmlFunction(FunctionMode.OneShot, name: "dataagent_analysis_start")]
    [Description("Start a DataAgent analysis session for a caller and goal or question.")]
    public string Start(string callerId, string goalOrQuestion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(goalOrQuestion);

        string context = service.Start(callerId, goalOrQuestion).Context;
        resultPublisher?.Invoke(context);
        return context;
    }

    [XmlFunction(FunctionMode.OneShot, name: "dataagent_analysis_continue")]
    [Description("Continue an existing DataAgent analysis session with a follow-up question.")]
    public string Continue(string sessionId, string question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        string context = service.Continue(sessionId, question).Context;
        resultPublisher?.Invoke(context);
        return context;
    }

    [XmlFunction(FunctionMode.OneShot, name: "dataagent_analysis_summarize")]
    [Description("Summarize an existing DataAgent analysis session without running a new query.")]
    public string Summarize(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        string context = service.Summarize(sessionId).Context;
        resultPublisher?.Invoke(context);
        return context;
    }

    [XmlFunction(FunctionMode.OneShot, name: "dataagent_analysis_end")]
    [Description("End an existing DataAgent analysis session without running a new query.")]
    public string End(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        string context = service.End(sessionId).Context;
        resultPublisher?.Invoke(context);
        return context;
    }
}
