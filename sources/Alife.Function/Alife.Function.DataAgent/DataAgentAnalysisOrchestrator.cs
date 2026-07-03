namespace Alife.Function.DataAgent;

public sealed class DataAgentAnalysisOrchestrator : IDataAgentAnalysisOrchestrator
{
    const string RouteDeniedReason = "tool_route_required";

    readonly DataAgentAnalysisService analysisService;
    readonly IDataAgentAnalysisSessionStore sessionStore;
    readonly DataAgentFollowUpInterpreter followUpInterpreter;
    readonly IDataAgentProgressSink? progressSink;
    readonly Func<DateTimeOffset> progressClock;

    public DataAgentAnalysisOrchestrator(
        DataAgentAnalysisService analysisService,
        IDataAgentAnalysisSessionStore sessionStore,
        DataAgentFollowUpInterpreter? followUpInterpreter = null,
        IDataAgentProgressSink? progressSink = null,
        Func<DateTimeOffset>? progressClock = null)
    {
        ArgumentNullException.ThrowIfNull(analysisService);
        ArgumentNullException.ThrowIfNull(sessionStore);

        this.analysisService = analysisService;
        this.sessionStore = sessionStore;
        this.followUpInterpreter = followUpInterpreter ?? new DataAgentFollowUpInterpreter();
        this.progressSink = progressSink;
        this.progressClock = progressClock ?? (() => DateTimeOffset.UtcNow);
    }

    public DataAgentOrchestrationResult Start(DataAgentOrchestrationRequest request)
    {
        ValidateStartRequest(request);

        if (request.RouteAllowsQuery == false)
        {
            string reason = ResolveRouteDeniedReason(request.RouteContext);
            return BuildRejectedResult(
                string.Empty,
                DataAgentAnalysisTurnIntent.NewQuestion,
                reason,
                Step(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Rejected, reason, false),
                request.RouteContext);
        }

        List<DataAgentOrchestrationStep> steps = [];
        steps.Add(Step(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Succeeded, "route_allowed", false));
        steps.Add(Step(DataAgentOrchestrationNodeKind.SchemaContext, DataAgentOrchestrationStepStatus.Succeeded, "dataagent_catalog_available", false));

        DataAgentAnalysisResponse response = analysisService.Start(request.CallerId, request.Input);
        AppendAnswerSteps(steps, response);
        DataAgentOrchestrationResult result = BuildResult(response, steps, request.RouteContext);
        PublishRouteAllowed(result.SessionId, result.Checkpoint.TurnCount);
        PublishProgress(
            result.SessionId,
            DataAgentProgressEventKind.SchemaContext,
            DataAgentProgressEventPhase.Completed,
            DataAgentProgressEventStatus.Succeeded,
            "dataagent_catalog_available",
            result.Checkpoint.TurnCount,
            executedSql: false,
            queryAllowed: true,
            terminal: false);
        PublishCheckpointProgress(result.Checkpoint);
        return result;
    }

    public DataAgentOrchestrationResult Continue(DataAgentOrchestrationRequest request)
    {
        ValidateContinueRequest(request);

        DataAgentAnalysisTurnIntent requestIntent = followUpInterpreter.Interpret(request.Input);
        DataAgentOrchestrationResult? preSessionRouteRejection = BuildRouteDeniedResultIfNeeded(
            request.SessionId!,
            requestIntent,
            request.RouteAllowsQuery,
            request.RouteContext);
        if (preSessionRouteRejection is not null)
            return preSessionRouteRejection;

        DataAgentAnalysisSession? session = sessionStore.Get(request.SessionId!);
        if (session is null)
        {
            DataAgentAnalysisResponse missingSessionResponse = analysisService.Continue(request.SessionId!, request.Input);
            DataAgentOrchestrationResult missingSessionResult = BuildResult(
                missingSessionResponse,
                [
                    Step(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Succeeded, "route_allowed", false),
                    Step(DataAgentOrchestrationNodeKind.Reject, DataAgentOrchestrationStepStatus.Rejected, missingSessionResponse.RejectedReason, false),
                    Step(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
                ],
                request.RouteContext);
            PublishRouteAllowed(missingSessionResult.SessionId, missingSessionResult.Checkpoint.TurnCount);
            PublishCheckpointProgress(missingSessionResult.Checkpoint);
            return missingSessionResult;
        }

        DataAgentAnalysisTurnIntent intent = followUpInterpreter.Interpret(request.Input, session);
        DataAgentOrchestrationResult? routeRejection = BuildRouteDeniedResultIfNeeded(
            request.SessionId!,
            intent,
            request.RouteAllowsQuery,
            request.RouteContext);
        if (routeRejection is not null)
            return routeRejection;

        if (intent == DataAgentAnalysisTurnIntent.Summarize)
            return Summarize(request.SessionId!, request.RouteContext);

        if (intent == DataAgentAnalysisTurnIntent.End)
            return End(request.SessionId!, request.RouteContext);

        bool queryProducing = intent.ProducesQuery();

        DataAgentAnalysisResponse response = analysisService.Continue(request.SessionId!, request.Input);
        List<DataAgentOrchestrationStep> steps =
        [
            Step(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Succeeded, queryProducing ? "route_allowed" : "terminal_route_not_required", false)
        ];
        AppendAnswerSteps(steps, response);
        DataAgentOrchestrationResult result = BuildResult(response, steps, request.RouteContext);
        PublishRouteAllowed(result.SessionId, result.Checkpoint.TurnCount);
        PublishCheckpointProgress(result.Checkpoint);
        return result;
    }

    public DataAgentOrchestrationResult Summarize(string sessionId, DataAgentToolRouteContext? routeContext = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        string? terminalRouteDeniedReason = BuildTerminalRouteDeniedReason(routeContext);
        if (terminalRouteDeniedReason is not null)
            return BuildRejectedResult(
                sessionId,
                DataAgentAnalysisTurnIntent.Summarize,
                terminalRouteDeniedReason,
                Step(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Rejected, terminalRouteDeniedReason, false),
                routeContext);

        DataAgentAnalysisResponse response = analysisService.Summarize(sessionId);
        DataAgentOrchestrationResult result = BuildResult(
            response,
            [
                Step(
                    DataAgentOrchestrationNodeKind.Summarize,
                    response.Accepted ? DataAgentOrchestrationStepStatus.Succeeded : DataAgentOrchestrationStepStatus.Rejected,
                    response.Accepted ? "terminal_summary" : response.RejectedReason,
                    false),
                Step(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
            ],
            routeContext);
        PublishCheckpointProgress(result.Checkpoint);
        return result;
    }

    public DataAgentOrchestrationResult End(string sessionId, DataAgentToolRouteContext? routeContext = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        string? terminalRouteDeniedReason = BuildTerminalRouteDeniedReason(routeContext);
        if (terminalRouteDeniedReason is not null)
            return BuildRejectedResult(
                sessionId,
                DataAgentAnalysisTurnIntent.End,
                terminalRouteDeniedReason,
                Step(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Rejected, terminalRouteDeniedReason, false),
                routeContext);

        DataAgentAnalysisResponse response = analysisService.End(sessionId);
        DataAgentOrchestrationResult result = BuildResult(
            response,
            [
                Step(
                    DataAgentOrchestrationNodeKind.End,
                    response.Accepted ? DataAgentOrchestrationStepStatus.Succeeded : DataAgentOrchestrationStepStatus.Rejected,
                    response.Accepted ? "terminal_end" : response.RejectedReason,
                    false),
                Step(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
            ],
            routeContext);
        PublishCheckpointProgress(result.Checkpoint);
        return result;
    }

    DataAgentOrchestrationResult? BuildRouteDeniedResultIfNeeded(
        string sessionId,
        DataAgentAnalysisTurnIntent intent,
        bool routeAllowsQuery,
        DataAgentToolRouteContext? routeContext)
    {
        string? reason = BuildRouteDeniedReason(intent, routeAllowsQuery, routeContext);
        if (reason is null)
            return null;

        return BuildRejectedResult(
            sessionId,
            intent,
            reason,
            Step(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Rejected, reason, false),
            routeContext);
    }

    static string? BuildRouteDeniedReason(
        DataAgentAnalysisTurnIntent intent,
        bool routeAllowsQuery,
        DataAgentToolRouteContext? routeContext)
    {
        if (intent.ProducesQuery())
            return routeAllowsQuery ? null : ResolveRouteDeniedReason(routeContext);

        return BuildTerminalRouteDeniedReason(routeContext);
    }

    static string? BuildTerminalRouteDeniedReason(DataAgentToolRouteContext? routeContext)
    {
        return routeContext?.AllowsTool == true
            ? null
            : ResolveRouteDeniedReason(routeContext);
    }

    static string ResolveRouteDeniedReason(DataAgentToolRouteContext? routeContext)
    {
        if (routeContext is null ||
            string.IsNullOrWhiteSpace(routeContext.ReasonCode) ||
            string.Equals(routeContext.ReasonCode, "route_allowed", StringComparison.Ordinal))
        {
            return RouteDeniedReason;
        }

        return routeContext.ReasonCode;
    }

    DataAgentOrchestrationResult BuildRejectedResult(
        string sessionId,
        DataAgentAnalysisTurnIntent intent,
        string reason,
        DataAgentOrchestrationStep routeStep,
        DataAgentToolRouteContext? routeContext)
    {
        DataAgentAnalysisResponse response = new(
            sessionId,
            DataAgentAnalysisSessionStatus.Rejected,
            intent,
            null,
            string.Empty,
            string.Empty,
            false,
            reason);

        DataAgentOrchestrationStep[] steps =
        [
            routeStep,
            Step(DataAgentOrchestrationNodeKind.Reject, DataAgentOrchestrationStepStatus.Rejected, reason, false),
            Step(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
        ];

        DataAgentOrchestrationCheckpoint checkpoint = BuildCheckpoint(sessionId, response.Status);
        string progressSessionId = string.IsNullOrWhiteSpace(sessionId) ? "pending" : sessionId;
        PublishProgress(
            progressSessionId,
            DataAgentProgressEventKind.RouteGate,
            DataAgentProgressEventPhase.Completed,
            DataAgentProgressEventStatus.Rejected,
            reason,
            checkpoint.TurnCount,
            executedSql: false,
            queryAllowed: false,
            terminal: checkpoint.Terminal);
        PublishProgress(
            progressSessionId,
            DataAgentProgressEventKind.Reject,
            DataAgentProgressEventPhase.Completed,
            DataAgentProgressEventStatus.Rejected,
            reason,
            checkpoint.TurnCount,
            executedSql: false,
            queryAllowed: false,
            terminal: checkpoint.Terminal);
        PublishCheckpointProgress(checkpoint, progressSessionId);
        return new DataAgentOrchestrationResult(
            sessionId,
            checkpoint.SessionStatus,
            steps,
            checkpoint,
            response,
            routeContext);
    }

    void AppendAnswerSteps(List<DataAgentOrchestrationStep> steps, DataAgentAnalysisResponse response)
    {
        if (response.Answer is null)
        {
            if (response.Intent == DataAgentAnalysisTurnIntent.Summarize)
                steps.Add(Step(DataAgentOrchestrationNodeKind.Summarize, DataAgentOrchestrationStepStatus.Succeeded, "terminal_summary", false));
            else if (response.Intent == DataAgentAnalysisTurnIntent.End)
                steps.Add(Step(DataAgentOrchestrationNodeKind.End, DataAgentOrchestrationStepStatus.Succeeded, "terminal_end", false));
            else
                steps.Add(Step(DataAgentOrchestrationNodeKind.Reject, DataAgentOrchestrationStepStatus.Rejected, response.RejectedReason, false));

            steps.Add(Step(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false));
            return;
        }

        steps.Add(Step(DataAgentOrchestrationNodeKind.Plan, DataAgentOrchestrationStepStatus.Succeeded, "planner_response_received", false));

        if (response.Answer.RejectedReason == "needs_clarification")
        {
            steps.Add(Step(DataAgentOrchestrationNodeKind.Validate, DataAgentOrchestrationStepStatus.Skipped, "needs_clarification", false));
            steps.Add(Step(DataAgentOrchestrationNodeKind.Clarification, DataAgentOrchestrationStepStatus.Succeeded, "needs_clarification", false));
            steps.Add(Step(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false));
            return;
        }

        if (response.Answer.Validated == false)
        {
            string reason = string.IsNullOrWhiteSpace(response.Answer.RejectedReason)
                ? "answer_rejected"
                : response.Answer.RejectedReason;
            steps.Add(Step(DataAgentOrchestrationNodeKind.Validate, DataAgentOrchestrationStepStatus.Rejected, reason, false));
            steps.Add(Step(DataAgentOrchestrationNodeKind.Reject, DataAgentOrchestrationStepStatus.Rejected, reason, false));
            steps.Add(Step(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false));
            return;
        }

        steps.Add(Step(DataAgentOrchestrationNodeKind.Validate, DataAgentOrchestrationStepStatus.Succeeded, "validated", false));
        steps.Add(Step(DataAgentOrchestrationNodeKind.Execute, DataAgentOrchestrationStepStatus.Succeeded, "read_only_query_executed", true));
        steps.Add(Step(DataAgentOrchestrationNodeKind.Explain, DataAgentOrchestrationStepStatus.Succeeded, "result_explained", false));
        steps.Add(Step(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false));
    }

    DataAgentOrchestrationResult BuildResult(
        DataAgentAnalysisResponse response,
        IReadOnlyList<DataAgentOrchestrationStep> steps,
        DataAgentToolRouteContext? routeContext)
    {
        DataAgentOrchestrationCheckpoint checkpoint = BuildCheckpoint(response.SessionId, response.Status);
        return new DataAgentOrchestrationResult(
            response.SessionId,
            response.Status,
            steps.ToArray(),
            checkpoint,
            response,
            routeContext);
    }

    DataAgentOrchestrationCheckpoint BuildCheckpoint(
        string sessionId,
        DataAgentAnalysisSessionStatus fallbackStatus)
    {
        DataAgentAnalysisSession? session = sessionStore.Get(sessionId);
        DataAgentAnalysisSessionStatus status = session?.Status ?? fallbackStatus;
        int turnCount = session?.Turns.Count ?? 0;
        bool terminal = status is DataAgentAnalysisSessionStatus.Ended or DataAgentAnalysisSessionStatus.Rejected;

        return new DataAgentOrchestrationCheckpoint(
            sessionId,
            status,
            session?.LastDataset ?? string.Empty,
            turnCount,
            CanContinue: terminal == false,
            CanSummarize: terminal == false && turnCount > 0,
            Terminal: terminal);
    }

    static DataAgentOrchestrationStep Step(
        DataAgentOrchestrationNodeKind node,
        DataAgentOrchestrationStepStatus status,
        string reason,
        bool executedSql)
    {
        return new DataAgentOrchestrationStep(node, status, reason, executedSql);
    }

    void PublishRouteAllowed(string sessionId, int turnCount)
    {
        PublishProgress(
            sessionId,
            DataAgentProgressEventKind.RouteGate,
            DataAgentProgressEventPhase.Completed,
            DataAgentProgressEventStatus.Succeeded,
            "route_allowed",
            turnCount,
            executedSql: false,
            queryAllowed: true,
            terminal: false);
    }

    void PublishCheckpointProgress(DataAgentOrchestrationCheckpoint checkpoint, string? overrideSessionId = null)
    {
        PublishProgress(
            overrideSessionId ?? checkpoint.SessionId,
            DataAgentProgressEventKind.Checkpoint,
            DataAgentProgressEventPhase.Completed,
            DataAgentProgressEventStatus.Succeeded,
            "checkpoint_created",
            checkpoint.TurnCount,
            executedSql: false,
            queryAllowed: checkpoint.Terminal == false,
            terminal: checkpoint.Terminal);
    }

    void PublishProgress(
        string sessionId,
        DataAgentProgressEventKind kind,
        DataAgentProgressEventPhase phase,
        DataAgentProgressEventStatus status,
        string reasonCode,
        int turnCount,
        bool executedSql,
        bool queryAllowed,
        bool terminal)
    {
        progressSink?.Publish(new DataAgentProgressEvent(
            sessionId,
            kind,
            phase,
            status,
            reasonCode,
            turnCount,
            progressClock(),
            executedSql,
            queryAllowed,
            terminal,
            new Dictionary<string, string>()));
    }

    static void ValidateStartRequest(DataAgentOrchestrationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CallerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Input);
    }

    static void ValidateContinueRequest(DataAgentOrchestrationRequest request)
    {
        ValidateStartRequest(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SessionId);
    }
}
