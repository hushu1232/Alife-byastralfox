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
    IDataAgentGraphSidecarClient? sidecarClient = null)
{
    readonly DataAgentGraphHandshakeOptions options = options;
    readonly IDataAgentGraphSidecarClient sidecarClient = sidecarClient ?? DisabledDataAgentGraphSidecarClient.Instance;

    public DataAgentGraphHandshakeOutcome TryHandshake(
        string callerId,
        string goalOrQuestion,
        DataAgentOrchestrationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        string normalizedCaller = string.IsNullOrWhiteSpace(callerId)
            ? "local"
            : callerId.Trim();
        string normalizedQuestion = string.IsNullOrWhiteSpace(goalOrQuestion)
            ? "dataagent_analysis"
            : goalOrQuestion.Trim();
        DataAgentGraphHandshakeRequest request = BuildRequest(normalizedCaller, normalizedQuestion, result);

        if (options.Enabled == false)
            return Outcome(DataAgentGraphHandshakeStatus.Disabled, "sidecar_disabled", fallbackRequired: true, request);

        try
        {
            DataAgentGraphHandshakeResponse response = sidecarClient.TryHandshake(request);
            DataAgentGraphHandshakeValidationResult validation = DataAgentGraphHandshakeValidator.Validate(request, response);
            if (validation.Accepted == false)
            {
                return Outcome(
                    DataAgentGraphHandshakeStatus.Rejected,
                    validation.ReasonCode,
                    fallbackRequired: true,
                    request,
                    response,
                    validation);
            }

            return Outcome(
                DataAgentGraphHandshakeStatus.Accepted,
                validation.ReasonCode,
                response.FallbackRequired,
                request,
                response,
                validation);
        }
        catch (TimeoutException)
        {
            return Outcome(DataAgentGraphHandshakeStatus.Timeout, "sidecar_timeout", fallbackRequired: true, request);
        }
        catch (Exception)
        {
            return Outcome(DataAgentGraphHandshakeStatus.Unavailable, "sidecar_unavailable", fallbackRequired: true, request);
        }
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
            : $"route_present=true;route_allows_query={LowerBool(result.RouteContext.AllowsQuery)};route_reason_code={result.RouteContext.ReasonCode}";
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

    static DataAgentGraphHandshakeOutcome Outcome(
        DataAgentGraphHandshakeStatus status,
        string reasonCode,
        bool fallbackRequired,
        DataAgentGraphHandshakeRequest? request,
        DataAgentGraphHandshakeResponse? response = null,
        DataAgentGraphHandshakeValidationResult? validation = null)
    {
        return new DataAgentGraphHandshakeOutcome(
            status,
            reasonCode,
            fallbackRequired,
            request,
            response,
            validation ?? new DataAgentGraphHandshakeValidationResult(false, reasonCode));
    }

    static string Bound(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    static string LowerBool(bool value)
    {
        return value ? "true" : "false";
    }
}
