namespace Alife.Function.DataAgent;

public sealed record DataAgentGraphHandshakeOptions(bool Enabled)
{
    public const string EnabledEnvironmentVariable = "ALIFE_DATAAGENT_GRAPH_HANDSHAKE_ENABLED";

    public static DataAgentGraphHandshakeOptions Disabled { get; } = new(false);

    public static DataAgentGraphHandshakeOptions FromEnvironment()
    {
        return FromValue(Environment.GetEnvironmentVariable(EnabledEnvironmentVariable));
    }

    public static DataAgentGraphHandshakeOptions FromValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Disabled;

        return value.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "yes" => new DataAgentGraphHandshakeOptions(true),
            "false" or "0" or "no" => Disabled,
            _ => Disabled
        };
    }
}

public static class DataAgentGraphHandshakeLimits
{
    public const int MaxRequestIdLength = 128;
    public const int MaxSessionIdLength = 128;
    public const int MaxTurnIdLength = 128;
    public const int MaxCallerIdLength = 128;
    public const int MaxQuestionLength = 2048;
    public const int MaxScenarioContextLength = 4096;
    public const int MaxRouteScopeLength = 512;
    public const int MaxQueryConstraintsLength = 1024;
    public const int MaxNodeManifests = 16;
    public const int MaxToolNamesPerNode = 8;
    public const int MaxDeniedMarkersPerNode = 16;
    public const int MaxTraceSummaryChars = 1800;
    public const int MaxContextContributionChars = 1200;
    public const int MaxProgressEvents = 16;
    public const int MaxReasonCodeLength = 128;
}

public enum DataAgentGraphHandshakeStatus
{
    Disabled,
    Accepted,
    Rejected,
    Unavailable,
    Timeout,
    Invalid
}

public enum DataAgentGraphHandshakeProgressStatus
{
    Started,
    Completed,
    Skipped,
    Rejected,
    Failed
}

public static class DataAgentGraphHandshakeToolNames
{
    public const string ReadScenarioContext = "dataagent.scenario_context.read";
    public const string ReadRouteScope = "dataagent.route_scope.read";
    public const string ProposeQueryPlan = "dataagent.query_plan.propose";
    public const string ReadQueryPlanValidationStatus = "dataagent.query_plan.validation_status.read";
    public const string ReadSqlSafetyStatus = "dataagent.sql_safety.status.read";
    public const string InterpretControlledResult = "dataagent.result.interpret_controlled";
    public const string ReadEvidenceDiagnostics = "dataagent.diagnostics.evidence.read";
    public const string ReadTraceDiagnostics = "dataagent.diagnostics.trace.read";
    public const string ReadProgressDiagnostics = "dataagent.diagnostics.progress.read";
    public const string ExecuteReadOnlyQuery = "dataagent.query.execute_readonly";
}

public sealed record DataAgentGraphNodeManifest(
    string NodeName,
    string Purpose,
    IReadOnlyList<string> AllowedToolNames,
    IReadOnlyList<string> DeniedCapabilityMarkers,
    string InputShape,
    string OutputShape,
    IReadOnlyList<string> BusinessTerms,
    string SafetyNotes);

public sealed record DataAgentGraphHandshakeRequest(
    string RequestId,
    string SessionId,
    string TurnId,
    string CallerId,
    string GoalOrQuestion,
    string ScenarioContextSummary,
    string RouteScope,
    string QueryConstraints,
    IReadOnlyList<DataAgentGraphNodeManifest> NodeManifests,
    bool NoSqlAuthority,
    bool ReadOnly,
    bool FallbackAvailable,
    int TraceBudgetChars,
    int ProgressBudget);

public sealed record DataAgentGraphHandshakeProgress(
    string NodeName,
    DataAgentGraphHandshakeProgressStatus Status,
    string ReasonCode,
    string Message = "",
    IReadOnlyDictionary<string, string>? Facts = null);

public sealed record DataAgentGraphHandshakeResponse(
    string RequestId,
    bool Accepted,
    string ReasonCode,
    IReadOnlyList<string> SelectedNodes,
    IReadOnlyList<DataAgentGraphHandshakeProgress> NodeProgress,
    string TraceSummary,
    string ContextContribution,
    bool FallbackRequired,
    bool NoSqlAuthority,
    bool ReadOnly,
    IReadOnlyList<string> RequestedToolNames,
    bool RequestsCheckpointMutation,
    bool RequestsVisibleText);

public sealed record DataAgentGraphHandshakeValidationResult(
    bool Accepted,
    string ReasonCode);

public sealed record DataAgentGraphHandshakeOutcome(
    DataAgentGraphHandshakeStatus Status,
    string ReasonCode,
    bool FallbackRequired,
    DataAgentGraphHandshakeRequest? Request,
    DataAgentGraphHandshakeResponse? Response,
    DataAgentGraphHandshakeValidationResult Validation);
