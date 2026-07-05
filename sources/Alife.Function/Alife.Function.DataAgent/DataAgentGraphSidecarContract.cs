namespace Alife.Function.DataAgent;

public sealed record DataAgentGraphSidecarOptions(bool Enabled)
{
    public const string EnabledEnvironmentVariable = "ALIFE_DATAAGENT_GRAPH_SIDECAR_ENABLED";

    public static DataAgentGraphSidecarOptions Disabled { get; } = new(false);

    public static DataAgentGraphSidecarOptions FromEnvironment()
    {
        return FromValue(Environment.GetEnvironmentVariable(EnabledEnvironmentVariable));
    }

    public static DataAgentGraphSidecarOptions FromValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Disabled;

        return value.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "yes" => new DataAgentGraphSidecarOptions(true),
            "false" or "0" or "no" => Disabled,
            _ => Disabled
        };
    }
}

public enum DataAgentGraphSidecarNodeKind
{
    ScenarioContext,
    QueryPlanner,
    QueryPlanValidation,
    SqlSafetyValidation,
    ReadOnlyExecution,
    Evidence,
    Checkpoint,
    Diagnostics,
    Terminal
}

public enum DataAgentGraphSidecarAuthority
{
    ProposeOrchestrationIntent,
    RequestCSharpSafetyService,
    ReturnBoundedTrace,
    ReportDeterministicFallback,
    AuthorizeDataset,
    AuthorizeField,
    AuthorizeOperator,
    AuthorizeLimit,
    ProvideExecutableSql,
    ExecuteSql,
    DecideToolRoute,
    MutateCheckpoint,
    WriteEvidence,
    WriteAudit,
    WriteProgress,
    WriteDiagnostics,
    SendVisibleQChatText,
    OwnQqIngress
}

public sealed record DataAgentGraphSidecarRequest(
    string WorkflowId,
    string SessionId,
    string CallerId,
    string Question,
    string ScenarioContext,
    IReadOnlyList<DataAgentGraphSidecarNodeKind> AllowedNodeKinds,
    IReadOnlyList<string> AllowedCapabilityNames,
    string? CheckpointSessionId,
    string? CheckpointStatus,
    string TraceId);

public sealed record DataAgentGraphSidecarResponse(
    string WorkflowId,
    bool Accepted,
    string ReasonCode,
    string Message,
    DataAgentGraphSidecarNodeKind? ProposedNodeKind,
    string? RequestedCapabilityName,
    bool RequiresCSharpSafetyService,
    IReadOnlyList<string> Trace,
    IReadOnlyList<DataAgentGraphSidecarAuthority> ClaimedAuthorities);

public sealed class DataAgentGraphSidecarPolicy
{
    static readonly DataAgentGraphSidecarAuthority[] AllowedAuthority =
    [
        DataAgentGraphSidecarAuthority.ProposeOrchestrationIntent,
        DataAgentGraphSidecarAuthority.RequestCSharpSafetyService,
        DataAgentGraphSidecarAuthority.ReturnBoundedTrace,
        DataAgentGraphSidecarAuthority.ReportDeterministicFallback
    ];

    static readonly DataAgentGraphSidecarAuthority[] ForbiddenAuthority =
    [
        DataAgentGraphSidecarAuthority.AuthorizeDataset,
        DataAgentGraphSidecarAuthority.AuthorizeField,
        DataAgentGraphSidecarAuthority.AuthorizeOperator,
        DataAgentGraphSidecarAuthority.AuthorizeLimit,
        DataAgentGraphSidecarAuthority.ProvideExecutableSql,
        DataAgentGraphSidecarAuthority.ExecuteSql,
        DataAgentGraphSidecarAuthority.DecideToolRoute,
        DataAgentGraphSidecarAuthority.MutateCheckpoint,
        DataAgentGraphSidecarAuthority.WriteEvidence,
        DataAgentGraphSidecarAuthority.WriteAudit,
        DataAgentGraphSidecarAuthority.WriteProgress,
        DataAgentGraphSidecarAuthority.WriteDiagnostics,
        DataAgentGraphSidecarAuthority.SendVisibleQChatText,
        DataAgentGraphSidecarAuthority.OwnQqIngress
    ];

    readonly HashSet<DataAgentGraphSidecarAuthority> allowed;
    readonly HashSet<DataAgentGraphSidecarAuthority> forbidden;

    DataAgentGraphSidecarPolicy(
        IEnumerable<DataAgentGraphSidecarAuthority> allowed,
        IEnumerable<DataAgentGraphSidecarAuthority> forbidden)
    {
        this.allowed = new HashSet<DataAgentGraphSidecarAuthority>(allowed);
        this.forbidden = new HashSet<DataAgentGraphSidecarAuthority>(forbidden);
    }

    public static DataAgentGraphSidecarPolicy CreateDefault()
    {
        return new DataAgentGraphSidecarPolicy(AllowedAuthority, ForbiddenAuthority);
    }

    public bool Allows(DataAgentGraphSidecarAuthority authority)
    {
        return allowed.Contains(authority);
    }

    public bool Forbids(DataAgentGraphSidecarAuthority authority)
    {
        return forbidden.Contains(authority);
    }

    public bool NoSqlAuthority =>
        Forbids(DataAgentGraphSidecarAuthority.ProvideExecutableSql) &&
        Forbids(DataAgentGraphSidecarAuthority.ExecuteSql) &&
        Forbids(DataAgentGraphSidecarAuthority.AuthorizeDataset) &&
        Forbids(DataAgentGraphSidecarAuthority.AuthorizeField) &&
        Forbids(DataAgentGraphSidecarAuthority.AuthorizeOperator) &&
        Forbids(DataAgentGraphSidecarAuthority.AuthorizeLimit);

    public bool NoToolRouteAuthority =>
        Forbids(DataAgentGraphSidecarAuthority.DecideToolRoute);

    public bool NoCheckpointAuthority =>
        Forbids(DataAgentGraphSidecarAuthority.MutateCheckpoint);

    public bool NoEvidenceAuthority =>
        Forbids(DataAgentGraphSidecarAuthority.WriteEvidence) &&
        Forbids(DataAgentGraphSidecarAuthority.WriteAudit) &&
        Forbids(DataAgentGraphSidecarAuthority.WriteProgress) &&
        Forbids(DataAgentGraphSidecarAuthority.WriteDiagnostics);

    public bool NoVisibleTextAuthority =>
        Forbids(DataAgentGraphSidecarAuthority.SendVisibleQChatText) &&
        Forbids(DataAgentGraphSidecarAuthority.OwnQqIngress);
}

public static class DataAgentGraphSidecarContract
{
    static readonly string[] RawSqlMarkers =
    [
        "select ",
        "insert ",
        "update ",
        "delete ",
        "drop ",
        "alter ",
        "truncate ",
        "```sql",
        ";"
    ];

    static readonly string[] ForbiddenCapabilityNames =
    [
        "ExecuteSql",
        "ProvideExecutableSql",
        "DataAgentQueryExecutor",
        "IDataAgentStore.Query"
    ];

    public static bool IsRuntimeAvailable => false;

    public static IReadOnlyList<DataAgentGraphSidecarNodeKind> DefaultAllowedNodeKinds { get; } =
    [
        DataAgentGraphSidecarNodeKind.ScenarioContext,
        DataAgentGraphSidecarNodeKind.QueryPlanner,
        DataAgentGraphSidecarNodeKind.QueryPlanValidation,
        DataAgentGraphSidecarNodeKind.SqlSafetyValidation,
        DataAgentGraphSidecarNodeKind.ReadOnlyExecution,
        DataAgentGraphSidecarNodeKind.Evidence,
        DataAgentGraphSidecarNodeKind.Checkpoint,
        DataAgentGraphSidecarNodeKind.Diagnostics,
        DataAgentGraphSidecarNodeKind.Terminal
    ];

    public static bool IsRequestValid(DataAgentGraphSidecarRequest request)
    {
        return HasText(request.WorkflowId) &&
               HasText(request.SessionId) &&
               HasText(request.CallerId) &&
               HasText(request.TraceId) &&
               request.AllowedNodeKinds.Count > 0 &&
               request.AllowedNodeKinds.All(DefaultAllowedNodeKinds.Contains) &&
               request.AllowedCapabilityNames.All(HasText);
    }

    public static bool IsResponseSafe(
        DataAgentGraphSidecarResponse response,
        DataAgentGraphSidecarPolicy policy)
    {
        if (HasText(response.WorkflowId) == false)
            return false;

        if (response.ProposedNodeKind.HasValue &&
            DefaultAllowedNodeKinds.Contains(response.ProposedNodeKind.Value) == false)
            return false;

        if (response.RequiresCSharpSafetyService && HasText(response.RequestedCapabilityName) == false)
            return false;

        if (ContainsForbiddenCapability(response.RequestedCapabilityName))
            return false;

        if (ContainsRawSql(response.ReasonCode) ||
            ContainsRawSql(response.Message) ||
            ContainsRawSql(response.RequestedCapabilityName) ||
            response.Trace.Any(ContainsRawSql))
        {
            return false;
        }

        return response.ClaimedAuthorities.All(policy.Allows);
    }

    static bool HasText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) == false;
    }

    static bool ContainsForbiddenCapability(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return ForbiddenCapabilityNames.Any(marker =>
            string.Equals(value.Trim(), marker, StringComparison.OrdinalIgnoreCase));
    }

    static bool ContainsRawSql(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string normalized = value.Trim().ToLowerInvariant();
        return RawSqlMarkers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal));
    }
}
