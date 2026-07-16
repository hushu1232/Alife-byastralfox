namespace Alife.Function.DataAgent;

public interface IDataAgentGraphSidecarClient
{
    DataAgentGraphHandshakeResponse TryHandshake(DataAgentGraphHandshakeRequest request);
}

public sealed class DisabledDataAgentGraphSidecarClient : IDataAgentGraphSidecarClient
{
    public static DisabledDataAgentGraphSidecarClient Instance { get; } = new();

    DisabledDataAgentGraphSidecarClient()
    {
    }

    public DataAgentGraphHandshakeResponse TryHandshake(DataAgentGraphHandshakeRequest request)
    {
        throw new InvalidOperationException("sidecar_disabled");
    }
}

public sealed class DataAgentGraphHandshakeCoordinator(
    DataAgentGraphHandshakeOptions options,
    IDataAgentGraphSidecarClient? sidecarClient = null,
    DataAgentGraphSidecarProgressBridge? progressBridge = null,
    IDataAgentGraphHandshakeStreamClient? streamClient = null,
    DataAgentGraphSidecarObservabilityContext? observabilityContext = null,
    IDataAgentV45ProductionObservationSink? observationRecorder = null,
    Func<DateTimeOffset>? observationClock = null)
{
    readonly DataAgentGraphHandshakeOptions options = options ?? throw new ArgumentNullException(nameof(options));
    readonly IDataAgentGraphSidecarClient sidecarClient = sidecarClient ?? DisabledDataAgentGraphSidecarClient.Instance;
    readonly DataAgentGraphSidecarProgressBridge? progressBridge = progressBridge;
    readonly IDataAgentGraphHandshakeStreamClient? streamClient = streamClient;
    readonly DataAgentGraphSidecarObservabilityContext observabilityContext =
        observabilityContext ?? InferObservabilityContext(sidecarClient, streamClient);
    readonly IDataAgentV45ProductionObservationSink? observationRecorder = observationRecorder;
    readonly Func<DateTimeOffset> observationClock = observationClock ?? (() => DateTimeOffset.UtcNow);

    public DataAgentGraphHandshakeOutcome TryHandshake(
        string callerId,
        string goalOrQuestion,
        DataAgentOrchestrationResult result)
    {
        DateTimeOffset startedAt = observationClock();
        DataAgentGraphHandshakeOutcome outcome = TryHandshakeCore(callerId, goalOrQuestion, result);
        DateTimeOffset completedAt = observationClock();
        try
        {
            observationRecorder?.Record(outcome, completedAt - startedAt, completedAt);
        }
        catch (Exception)
        {
            // V4.5 production observation is diagnostic and cannot change the deterministic outcome.
        }

        return outcome;
    }

    DataAgentGraphHandshakeOutcome TryHandshakeCore(
        string callerId,
        string goalOrQuestion,
        DataAgentOrchestrationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (options.Enabled == false)
            return Outcome(DataAgentGraphHandshakeStatus.Disabled, "sidecar_disabled", fallbackRequired: true, request: null);

        string normalizedCaller = string.IsNullOrWhiteSpace(callerId)
            ? "local"
            : callerId.Trim();
        string normalizedQuestion = string.IsNullOrWhiteSpace(goalOrQuestion)
            ? "dataagent_analysis"
            : goalOrQuestion.Trim();
        if (TryBuildRequest(normalizedCaller, normalizedQuestion, result, out DataAgentGraphHandshakeRequest? request) == false)
        {
            return Outcome(
                DataAgentGraphHandshakeStatus.Invalid,
                "invalid_request_schema",
                fallbackRequired: true,
                request: null,
                observabilityReasonCode: DataAgentGraphSidecarObservabilityReasonCodes.FallbackUsed);
        }

        if (streamClient is not null)
            return TryStreamHandshake(request!, result);

        try
        {
            DataAgentGraphHandshakeResponse response = sidecarClient.TryHandshake(request!);
            DataAgentGraphHandshakeValidationResult validation = DataAgentGraphHandshakeValidator.Validate(request, response);
            if (validation.Accepted == false)
            {
                return Outcome(
                    DataAgentGraphHandshakeStatus.Rejected,
                    validation.ReasonCode,
                    fallbackRequired: true,
                    request,
                    response: null,
                    validation,
                    networkAttempted: true);
            }

            PublishProgressIfAvailable(request!, result, response.NodeProgress);

            return Outcome(
                DataAgentGraphHandshakeStatus.Accepted,
                validation.ReasonCode,
                response.FallbackRequired,
                request,
                response,
                validation,
                networkAttempted: true);
        }
        catch (DataAgentV44ProductionShadowException exception)
        {
            DataAgentGraphHandshakeStatus status = exception.ReasonCode switch
            {
                "production_shadow_timeout" => DataAgentGraphHandshakeStatus.Timeout,
                "production_shadow_invalid_response" => DataAgentGraphHandshakeStatus.Invalid,
                _ => DataAgentGraphHandshakeStatus.Unavailable
            };
            return Outcome(
                status,
                exception.ReasonCode,
                fallbackRequired: true,
                request,
                networkAttempted: exception.NetworkAttempted);
        }
        catch (DataAgentGraphSidecarInvalidResponseException)
        {
            return Outcome(
                DataAgentGraphHandshakeStatus.Invalid,
                "invalid_response_schema",
                fallbackRequired: true,
                request,
                networkAttempted: observabilityContext.EndpointConfigured,
                observabilityReasonCode: DataAgentGraphSidecarObservabilityReasonCodes.ResponseRejected);
        }
        catch (TimeoutException)
        {
            return Outcome(
                DataAgentGraphHandshakeStatus.Timeout,
                "sidecar_timeout",
                fallbackRequired: true,
                request,
                networkAttempted: observabilityContext.EndpointConfigured);
        }
        catch (Exception)
        {
            return Outcome(
                DataAgentGraphHandshakeStatus.Unavailable,
                "sidecar_unavailable",
                fallbackRequired: true,
                request,
                networkAttempted: observabilityContext.EndpointConfigured);
        }
    }

    DataAgentGraphHandshakeOutcome TryStreamHandshake(
        DataAgentGraphHandshakeRequest request,
        DataAgentOrchestrationResult result)
    {
        try
        {
            DataAgentGraphHandshakeStreamResult streamResult = streamClient!.TryHandshakeStream(request);
            DataAgentGraphHandshakeValidationResult validation =
                DataAgentGraphHandshakeValidator.Validate(request, streamResult.Response);
            if (validation.Accepted == false)
            {
                return Outcome(
                    DataAgentGraphHandshakeStatus.Rejected,
                    validation.ReasonCode,
                    fallbackRequired: true,
                    request,
                    response: null,
                    validation,
                    networkAttempted: true,
                    observabilityReasonCode: DataAgentGraphSidecarObservabilityReasonCodes.StreamFinalResponseRejected);
            }

            PublishProgressIfAvailable(request, result, streamResult.Progress);

            return Outcome(
                DataAgentGraphHandshakeStatus.Accepted,
                validation.ReasonCode,
                streamResult.Response.FallbackRequired,
                request,
                streamResult.Response,
                validation,
                networkAttempted: true);
        }
        catch (DataAgentGraphSidecarInvalidStreamException exception)
        {
            return Outcome(
                DataAgentGraphHandshakeStatus.Invalid,
                exception.ReasonCode,
                fallbackRequired: true,
                request,
                networkAttempted: observabilityContext.EndpointConfigured,
                observabilityReasonCode: MapInvalidStreamObservabilityReasonCode(exception.ReasonCode));
        }
        catch (TimeoutException)
        {
            return Outcome(
                DataAgentGraphHandshakeStatus.Timeout,
                "sidecar_timeout",
                fallbackRequired: true,
                request,
                networkAttempted: observabilityContext.EndpointConfigured);
        }
        catch (Exception)
        {
            return Outcome(
                DataAgentGraphHandshakeStatus.Unavailable,
                "sidecar_unavailable",
                fallbackRequired: true,
                request,
                networkAttempted: observabilityContext.EndpointConfigured);
        }
    }

    static bool TryBuildRequest(
        string callerId,
        string goalOrQuestion,
        DataAgentOrchestrationResult result,
        out DataAgentGraphHandshakeRequest? request)
    {
        request = null;
        if (result.Checkpoint is null ||
            result.Steps is null ||
            result.Steps.Any(step => step is null))
        {
            return false;
        }

        request = BuildRequest(callerId, goalOrQuestion, result);
        return true;
    }

    static DataAgentGraphHandshakeRequest BuildRequest(
        string callerId,
        string goalOrQuestion,
        DataAgentOrchestrationResult result)
    {
        string turnId = result.Checkpoint.TurnCount <= 0
            ? "turn-0"
            : $"turn-{result.Checkpoint.TurnCount}";
        string rawSessionId = result.SessionId ?? string.Empty;
        string sessionId = string.IsNullOrWhiteSpace(rawSessionId)
            ? "pending"
            : Bound(rawSessionId.Trim(), DataAgentGraphHandshakeLimits.MaxSessionIdLength);
        string routeScope = result.RouteContext is null
            ? "route_present=false"
            : $"route_present=true;route_allows_query={LowerBool(result.RouteContext.AllowsQuery)};route_reason_code={NormalizeMachineToken(result.RouteContext.ReasonCode)}";
        string constraints =
            $"status={result.SessionStatus};executed_sql={LowerBool(result.Steps.Any(step => step.ExecutedSql))};terminal={LowerBool(result.Checkpoint.Terminal)}";

        return new DataAgentGraphHandshakeRequest(
            Bound($"graph-handshake-{rawSessionId.Trim()}-{turnId}", DataAgentGraphHandshakeLimits.MaxRequestIdLength),
            sessionId,
            Bound(turnId, DataAgentGraphHandshakeLimits.MaxTurnIdLength),
            Bound(callerId, DataAgentGraphHandshakeLimits.MaxCallerIdLength),
            Bound(goalOrQuestion, DataAgentGraphHandshakeLimits.MaxQuestionLength),
            "scenario_context=deterministic_csharp",
            Bound(routeScope, DataAgentGraphHandshakeLimits.MaxRouteScopeLength),
            Bound(constraints, DataAgentGraphHandshakeLimits.MaxQueryConstraintsLength),
            DataAgentGraphHandshakeManifestFactory.CreateDefault(),
            NoSqlAuthority: true,
            ReadOnly: true,
            FallbackAvailable: true,
            TraceBudgetChars: DataAgentGraphHandshakeLimits.MaxTraceSummaryChars,
            ProgressBudget: DataAgentGraphHandshakeLimits.MaxProgressEvents);
    }

    static DataAgentGraphSidecarObservabilityContext InferObservabilityContext(
        IDataAgentGraphSidecarClient? sidecarClient,
        IDataAgentGraphHandshakeStreamClient? streamClient)
    {
        return new DataAgentGraphSidecarObservabilityContext(
            EndpointConfigured: (sidecarClient is not null && sidecarClient is not DisabledDataAgentGraphSidecarClient) ||
                streamClient is not null,
            RuntimeStartedByAlife: false);
    }

    DataAgentGraphHandshakeOutcome Outcome(
        DataAgentGraphHandshakeStatus status,
        string reasonCode,
        bool fallbackRequired,
        DataAgentGraphHandshakeRequest? request,
        DataAgentGraphHandshakeResponse? response = null,
        DataAgentGraphHandshakeValidationResult? validation = null,
        bool networkAttempted = false,
        string? observabilityReasonCode = null)
    {
        DataAgentGraphSidecarObservabilitySnapshot observability = CreateObservabilitySnapshot(
            status,
            fallbackRequired,
            networkAttempted,
            observabilityReasonCode);

        return new DataAgentGraphHandshakeOutcome(
            status,
            reasonCode,
            fallbackRequired,
            request,
            response,
            validation ?? new DataAgentGraphHandshakeValidationResult(false, reasonCode),
            observability);
    }

    DataAgentGraphSidecarObservabilitySnapshot CreateObservabilitySnapshot(
        DataAgentGraphHandshakeStatus status,
        bool fallbackRequired,
        bool networkAttempted,
        string? reasonCodeOverride)
    {
        bool sidecarEnabled = options.Enabled;
        bool endpointConfigured = sidecarEnabled && observabilityContext.EndpointConfigured;
        DataAgentGraphSidecarObservabilityStatus observabilityStatus = MapObservabilityStatus(
            status,
            sidecarEnabled,
            endpointConfigured,
            networkAttempted,
            fallbackRequired);
        string reasonCode = reasonCodeOverride ?? MapObservabilityReasonCode(observabilityStatus);
        bool accepted = observabilityStatus == DataAgentGraphSidecarObservabilityStatus.Accepted;

        return new DataAgentGraphSidecarObservabilitySnapshot(
            reasonCode,
            observabilityStatus,
            sidecarEnabled,
            endpointConfigured,
            observabilityContext.RuntimeStartedByAlife,
            networkAttempted,
            accepted,
            fallbackRequired,
            SafeSummary: reasonCode);
    }

    static DataAgentGraphSidecarObservabilityStatus MapObservabilityStatus(
        DataAgentGraphHandshakeStatus status,
        bool sidecarEnabled,
        bool endpointConfigured,
        bool networkAttempted,
        bool fallbackRequired)
    {
        if (sidecarEnabled == false)
            return DataAgentGraphSidecarObservabilityStatus.Disabled;

        if (endpointConfigured == false &&
            status is DataAgentGraphHandshakeStatus.Unavailable or DataAgentGraphHandshakeStatus.Disabled)
        {
            return DataAgentGraphSidecarObservabilityStatus.NotConfigured;
        }

        return status switch
        {
            DataAgentGraphHandshakeStatus.Accepted => DataAgentGraphSidecarObservabilityStatus.Accepted,
            DataAgentGraphHandshakeStatus.Rejected => DataAgentGraphSidecarObservabilityStatus.Rejected,
            DataAgentGraphHandshakeStatus.Invalid when networkAttempted => DataAgentGraphSidecarObservabilityStatus.Rejected,
            DataAgentGraphHandshakeStatus.Invalid => DataAgentGraphSidecarObservabilityStatus.Fallback,
            DataAgentGraphHandshakeStatus.Timeout or DataAgentGraphHandshakeStatus.Unavailable => DataAgentGraphSidecarObservabilityStatus.RuntimeUnavailable,
            _ when fallbackRequired => DataAgentGraphSidecarObservabilityStatus.Fallback,
            _ => DataAgentGraphSidecarObservabilityStatus.Fallback
        };
    }

    static string MapInvalidStreamObservabilityReasonCode(string reasonCode)
    {
        return reasonCode switch
        {
            "missing_stream_final_response" => DataAgentGraphSidecarObservabilityReasonCodes.StreamFinalResponseMissing,
            "stream_progress_over_budget" => DataAgentGraphSidecarObservabilityReasonCodes.ProgressRejected,
            _ => DataAgentGraphSidecarObservabilityReasonCodes.FallbackUsed
        };
    }

    static string MapObservabilityReasonCode(DataAgentGraphSidecarObservabilityStatus status)
    {
        return status switch
        {
            DataAgentGraphSidecarObservabilityStatus.Disabled => DataAgentGraphSidecarObservabilityReasonCodes.Disabled,
            DataAgentGraphSidecarObservabilityStatus.NotConfigured => DataAgentGraphSidecarObservabilityReasonCodes.NotConfigured,
            DataAgentGraphSidecarObservabilityStatus.RuntimeUnavailable => DataAgentGraphSidecarObservabilityReasonCodes.RuntimeUnavailable,
            DataAgentGraphSidecarObservabilityStatus.Rejected => DataAgentGraphSidecarObservabilityReasonCodes.ResponseRejected,
            DataAgentGraphSidecarObservabilityStatus.Accepted => DataAgentGraphSidecarObservabilityReasonCodes.Accepted,
            _ => DataAgentGraphSidecarObservabilityReasonCodes.FallbackUsed
        };
    }

    void PublishProgressIfAvailable(
        DataAgentGraphHandshakeRequest request,
        DataAgentOrchestrationResult result,
        IReadOnlyList<DataAgentGraphHandshakeProgress> nodeProgress)
    {
        try
        {
            progressBridge?.PublishHandshakeProgress(request, result, nodeProgress);
        }
        catch (Exception)
        {
            // Progress publishing is diagnostic and must not demote an accepted handshake.
        }
    }

    static string Bound(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    static string LowerBool(bool value)
    {
        return value ? "true" : "false";
    }

    static string NormalizeMachineToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "reason_redacted";

        string trimmed = value.Trim();
        if (trimmed.Length > DataAgentGraphHandshakeLimits.MaxReasonCodeLength)
            return "reason_redacted";

        foreach (char ch in trimmed)
        {
            if (IsAsciiMachineTokenChar(ch) == false)
                return "reason_redacted";
        }

        return trimmed;
    }

    static bool IsAsciiMachineTokenChar(char ch)
    {
        return ch is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '_'
            or '-'
            or '.';
    }
}
