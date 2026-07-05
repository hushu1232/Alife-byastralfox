using System.Text.RegularExpressions;

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
    const int MaxIdentityLength = 128;
    const int MaxQuestionLength = 2048;
    const int MaxScenarioContextLength = 4096;
    const int MaxTraceIdLength = 128;
    const int MaxCheckpointStatusLength = 64;
    const int MaxNodeKindCount = 16;
    const int MaxCapabilityNameCount = 16;
    const int MaxCapabilityNameLength = 128;
    const int MaxReasonCodeLength = 128;
    const int MaxMessageLength = 1024;
    const int MaxTraceEntryCount = 16;
    const int MaxTraceEntryLength = 256;

    static readonly Regex RawSqlMarkerPattern = new(
        @"```sql|\b(select|insert|update|delete|drop|alter|truncate)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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

    public static IReadOnlyList<string> DefaultAllowedCapabilityNames { get; } =
    [
        "DataAgentScenarioContextBuilder",
        "DataAgentQueryPlanner",
        "DataAgentQueryPlanValidator",
        "DataAgentSqlSafetyValidator",
        "DataAgentEvidencePackBuilder",
        "DataAgentTraceDiagnosticsFormatter",
        "DataAgentProgressDiagnosticsFormatter",
        "DataAgentDeterministicFallback"
    ];

    static readonly HashSet<string> DefaultAllowedCapabilityNameSet = new(
        DefaultAllowedCapabilityNames,
        StringComparer.Ordinal);

    public static bool IsRequestValid(DataAgentGraphSidecarRequest? request)
    {
        if (request is null)
            return false;

        return HasBoundedText(request.WorkflowId, MaxIdentityLength) &&
               HasBoundedText(request.SessionId, MaxIdentityLength) &&
               HasBoundedText(request.CallerId, MaxIdentityLength) &&
               HasBoundedText(request.Question, MaxQuestionLength) &&
               HasBoundedText(request.ScenarioContext, MaxScenarioContextLength) &&
               HasBoundedText(request.TraceId, MaxTraceIdLength) &&
               HasBoundedOptionalText(request.CheckpointSessionId, MaxIdentityLength) &&
               HasBoundedOptionalText(request.CheckpointStatus, MaxCheckpointStatusLength) &&
               IsAllowedNodeKindList(request.AllowedNodeKinds) &&
               IsAllowedCapabilityNameList(request.AllowedCapabilityNames);
    }

    public static bool IsResponseSafe(
        DataAgentGraphSidecarResponse? response,
        DataAgentGraphSidecarPolicy? policy)
    {
        if (response is null || policy is null)
            return false;

        if (HasBoundedText(response.WorkflowId, MaxIdentityLength) == false ||
            HasBoundedText(response.ReasonCode, MaxReasonCodeLength) == false ||
            HasBoundedText(response.Message, MaxMessageLength) == false)
            return false;

        if (response.ProposedNodeKind.HasValue &&
            DefaultAllowedNodeKinds.Contains(response.ProposedNodeKind.Value) == false)
            return false;

        if (IsResponseCapabilitySafe(response.RequestedCapabilityName, response.RequiresCSharpSafetyService) == false)
            return false;

        if (IsTraceSafe(response.Trace) == false)
            return false;

        if (ContainsRawSql(response.ReasonCode) ||
            ContainsRawSql(response.Message) ||
            ContainsRawSql(response.RequestedCapabilityName) ||
            response.Trace.Any(ContainsRawSql))
        {
            return false;
        }

        if (response.ClaimedAuthorities is null)
            return false;

        return response.ClaimedAuthorities.All(policy.Allows);
    }

    public static bool IsResponseSafe(
        DataAgentGraphSidecarResponse? response,
        DataAgentGraphSidecarPolicy? policy,
        DataAgentGraphSidecarRequest? request)
    {
        if (IsRequestValid(request) == false ||
            IsResponseSafe(response, policy) == false)
        {
            return false;
        }

        return HasBoundedText(response!.RequestedCapabilityName, MaxCapabilityNameLength) &&
               request!.AllowedCapabilityNames.Contains(response.RequestedCapabilityName, StringComparer.Ordinal);
    }

    static bool HasBoundedText(string? value, int maxLength)
    {
        return string.IsNullOrWhiteSpace(value) == false &&
               value.Length <= maxLength;
    }

    static bool HasBoundedOptionalText(string? value, int maxLength)
    {
        return value is null ||
               value.Length <= maxLength;
    }

    static bool IsAllowedNodeKindList(IReadOnlyList<DataAgentGraphSidecarNodeKind>? allowedNodeKinds)
    {
        if (allowedNodeKinds is null ||
            allowedNodeKinds.Count == 0 ||
            allowedNodeKinds.Count > MaxNodeKindCount)
        {
            return false;
        }

        return allowedNodeKinds.All(DefaultAllowedNodeKinds.Contains);
    }

    static bool IsAllowedCapabilityNameList(IReadOnlyList<string>? allowedCapabilityNames)
    {
        if (allowedCapabilityNames is null ||
            allowedCapabilityNames.Count == 0 ||
            allowedCapabilityNames.Count > MaxCapabilityNameCount)
        {
            return false;
        }

        return allowedCapabilityNames.All(IsDefaultAllowedCapabilityName);
    }

    static bool IsDefaultAllowedCapabilityName(string? value)
    {
        return HasBoundedText(value, MaxCapabilityNameLength) &&
               ContainsForbiddenCapability(value) == false &&
               DefaultAllowedCapabilityNameSet.Contains(value!);
    }

    static bool IsResponseCapabilitySafe(string? value, bool requiresCSharpSafetyService)
    {
        if (string.IsNullOrWhiteSpace(value))
            return requiresCSharpSafetyService == false;

        return IsDefaultAllowedCapabilityName(value);
    }

    static bool IsTraceSafe(IReadOnlyList<string>? trace)
    {
        if (trace is null ||
            trace.Count > MaxTraceEntryCount)
        {
            return false;
        }

        return trace.All(entry => entry is not null && entry.Length <= MaxTraceEntryLength);
    }

    static bool ContainsForbiddenCapability(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return ForbiddenCapabilityNames.Any(marker =>
            value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    static bool ContainsRawSql(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return RawSqlMarkerPattern.IsMatch(value);
    }
}
