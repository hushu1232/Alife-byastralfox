namespace Alife.Function.DataAgent;

public sealed class DataAgentAnalysisOrchestrator : IDataAgentAnalysisOrchestrator
{
    const string RouteDeniedReason = "tool_route_required";

    readonly DataAgentAnalysisService analysisService;
    readonly IDataAgentAnalysisSessionStore sessionStore;
    readonly DataAgentFollowUpInterpreter followUpInterpreter;

    public DataAgentAnalysisOrchestrator(
        DataAgentAnalysisService analysisService,
        IDataAgentAnalysisSessionStore sessionStore,
        DataAgentFollowUpInterpreter? followUpInterpreter = null)
    {
        ArgumentNullException.ThrowIfNull(analysisService);
        ArgumentNullException.ThrowIfNull(sessionStore);

        this.analysisService = analysisService;
        this.sessionStore = sessionStore;
        this.followUpInterpreter = followUpInterpreter ?? new DataAgentFollowUpInterpreter();
    }

    public DataAgentOrchestrationResult Start(DataAgentOrchestrationRequest request)
    {
        ValidateStartRequest(request);

        if (request.RouteAllowsQuery == false)
            return BuildRejectedResult(
                string.Empty,
                DataAgentAnalysisTurnIntent.NewQuestion,
                RouteDeniedReason,
                Step(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Rejected, RouteDeniedReason, false));

        List<DataAgentOrchestrationStep> steps = [];
        steps.Add(Step(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Succeeded, "route_allowed", false));
        steps.Add(Step(DataAgentOrchestrationNodeKind.SchemaContext, DataAgentOrchestrationStepStatus.Succeeded, "dataagent_catalog_available", false));

        DataAgentAnalysisResponse response = analysisService.Start(request.CallerId, request.Input);
        AppendAnswerSteps(steps, response);
        return BuildResult(response, steps);
    }

    public DataAgentOrchestrationResult Continue(DataAgentOrchestrationRequest request)
    {
        ValidateContinueRequest(request);

        DataAgentAnalysisSession? session = sessionStore.Get(request.SessionId!);
        if (session is null)
        {
            DataAgentAnalysisResponse missingSessionResponse = analysisService.Continue(request.SessionId!, request.Input);
            return BuildResult(
                missingSessionResponse,
                [
                    Step(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Succeeded, "route_allowed", false),
                    Step(DataAgentOrchestrationNodeKind.Reject, DataAgentOrchestrationStepStatus.Rejected, missingSessionResponse.RejectedReason, false),
                    Step(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
                ]);
        }

        DataAgentAnalysisTurnIntent intent = followUpInterpreter.Interpret(request.Input, session);
        bool queryProducing = intent.ProducesQuery();
        if (request.RouteAllowsQuery == false && queryProducing)
        {
            return BuildRejectedResult(
                request.SessionId!,
                intent,
                RouteDeniedReason,
                Step(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Rejected, RouteDeniedReason, false));
        }

        DataAgentAnalysisResponse response = analysisService.Continue(request.SessionId!, request.Input);
        List<DataAgentOrchestrationStep> steps =
        [
            Step(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Succeeded, queryProducing ? "route_allowed" : "terminal_route_not_required", false)
        ];
        AppendAnswerSteps(steps, response);
        return BuildResult(response, steps);
    }

    DataAgentOrchestrationResult BuildRejectedResult(
        string sessionId,
        DataAgentAnalysisTurnIntent intent,
        string reason,
        DataAgentOrchestrationStep routeStep)
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
        return new DataAgentOrchestrationResult(
            sessionId,
            checkpoint.SessionStatus,
            steps,
            checkpoint,
            response);
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
        IReadOnlyList<DataAgentOrchestrationStep> steps)
    {
        DataAgentOrchestrationCheckpoint checkpoint = BuildCheckpoint(response.SessionId, response.Status);
        return new DataAgentOrchestrationResult(
            response.SessionId,
            response.Status,
            steps.ToArray(),
            checkpoint,
            response);
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
