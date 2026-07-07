using System.ComponentModel;
using Alife.Function.Interpreter;

namespace Alife.Function.DataAgent;

[Description("Runs multi-turn DataAgent analysis sessions and returns data_agent_analysis_session_context blocks.")]
public sealed class DataAgentAnalysisToolHandler(
    IDataAgentAnalysisOrchestrator orchestrator,
    Action<string>? resultPublisher = null,
    IDataAgentToolRouteContextAccessor? routeContextAccessor = null,
    Action<string>? evidenceDiagnosticsPublisher = null,
    Action<string>? traceDiagnosticsPublisher = null,
    IDataAgentTraceRecorder? traceRecorder = null,
    Func<DateTimeOffset>? traceClock = null,
    Action<string>? dataQueryGraphDiagnosticsPublisher = null,
    DataAgentGraphHandshakeCoordinator? graphHandshakeCoordinator = null)
{
    readonly IDataAgentToolRouteContextAccessor routeContextAccessor =
        routeContextAccessor ?? MissingDataAgentToolRouteContextAccessor.Instance;
    readonly IDataAgentTraceRecorder? traceRecorder =
        traceDiagnosticsPublisher is null ? null : traceRecorder ?? new DataAgentTraceRecorder();
    readonly Func<DateTimeOffset> traceClock = traceClock ?? (() => DateTimeOffset.UtcNow);
    readonly DataAgentGraphHandshakeCoordinator graphHandshakeCoordinator =
        graphHandshakeCoordinator ?? new DataAgentGraphHandshakeCoordinator(DataAgentGraphHandshakeOptions.Disabled);

    [XmlFunction(FunctionMode.OneShot, name: "dataagent_analysis_start")]
    [Description("Start a DataAgent analysis session for a caller and goal or question.")]
    public string Start(string callerId, string goalOrQuestion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(goalOrQuestion);

        DataAgentToolRouteContext routeContext = this.routeContextAccessor.Get("dataagent_analysis_start", null);
        DataAgentOrchestrationResult result = orchestrator.Start(new DataAgentOrchestrationRequest(
            callerId,
            goalOrQuestion,
            null,
            routeContext.AllowsQuery,
            routeContext));
        string context = DataAgentOrchestrationContextProvider.Build(result);
        PublishResult(result, context, callerId, goalOrQuestion);
        return context;
    }

    [XmlFunction(FunctionMode.OneShot, name: "dataagent_analysis_continue")]
    [Description("Continue an existing DataAgent analysis session with a follow-up question.")]
    public string Continue(string sessionId, string question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        DataAgentToolRouteContext routeContext = this.routeContextAccessor.Get("dataagent_analysis_continue", sessionId);
        DataAgentOrchestrationResult result = orchestrator.Continue(new DataAgentOrchestrationRequest(
            "local",
            question,
            sessionId,
            routeContext.AllowsQuery,
            routeContext));
        string context = DataAgentOrchestrationContextProvider.Build(result);
        PublishResult(result, context, "local", question);
        return context;
    }

    [XmlFunction(FunctionMode.OneShot, name: "dataagent_analysis_summarize")]
    [Description("Summarize an existing DataAgent analysis session without running a new query.")]
    public string Summarize(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        DataAgentToolRouteContext routeContext = this.routeContextAccessor.Get("dataagent_analysis_summarize", sessionId);
        DataAgentOrchestrationResult result = orchestrator.Summarize(sessionId, routeContext);
        string context = DataAgentOrchestrationContextProvider.Build(result);
        PublishResult(result, context, "local", "summarize");
        return context;
    }

    [XmlFunction(FunctionMode.OneShot, name: "dataagent_analysis_end")]
    [Description("End an existing DataAgent analysis session without running a new query.")]
    public string End(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        DataAgentToolRouteContext routeContext = this.routeContextAccessor.Get("dataagent_analysis_end", sessionId);
        DataAgentOrchestrationResult result = orchestrator.End(sessionId, routeContext);
        string context = DataAgentOrchestrationContextProvider.Build(result);
        PublishResult(result, context, "local", "end");
        return context;
    }

    void PublishResult(DataAgentOrchestrationResult result, string context, string callerId, string goalOrQuestion)
    {
        resultPublisher?.Invoke(context);
        PublishDataQueryGraphDiagnostics(result, callerId, goalOrQuestion);

        if (evidenceDiagnosticsPublisher is null && traceDiagnosticsPublisher is null)
            return;

        DataAgentEvidencePack pack = new DataAgentEvidencePackBuilder().Build(result);
        evidenceDiagnosticsPublisher?.Invoke(DataAgentEvidenceDiagnosticsFormatter.Format(pack));

        if (traceDiagnosticsPublisher is null || traceRecorder is null)
            return;

        DateTimeOffset now = traceClock();
        DataAgentTraceTimeline timeline = new DataAgentTraceTimelineBuilder().Build(result, pack, now);
        traceRecorder.Record(timeline);
        DataAgentTraceTimeline? latestTimeline = traceRecorder.GetLatest(result.SessionId, now);
        traceDiagnosticsPublisher(DataAgentTraceDiagnosticsFormatter.Format(latestTimeline));
    }

    void PublishDataQueryGraphDiagnostics(DataAgentOrchestrationResult result, string callerId, string goalOrQuestion)
    {
        if (dataQueryGraphDiagnosticsPublisher is null)
            return;

        string dataQueryGraphDiagnostics;
        try
        {
            DataAgentDataQueryGraphDryRunResult graphResult = DataAgentDataQueryGraphPilot.DryRun(result);
            dataQueryGraphDiagnostics = DataAgentDataQueryGraphTraceFormatter.Format(graphResult);
        }
        catch (Exception)
        {
            dataQueryGraphDiagnostics = DataAgentDataQueryGraphTraceFormatter.Format(null);
        }

        DataAgentGraphHandshakeOutcome handshakeOutcome = graphHandshakeCoordinator.TryHandshake(callerId, goalOrQuestion, result);
        string handshakeDiagnostics = DataAgentGraphHandshakeDiagnosticsFormatter.Format(handshakeOutcome);
        string diagnostics = $"{dataQueryGraphDiagnostics}{Environment.NewLine}{handshakeDiagnostics}";

        dataQueryGraphDiagnosticsPublisher(diagnostics);
    }
}
