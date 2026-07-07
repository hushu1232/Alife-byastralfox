using System.Text.RegularExpressions;

namespace Alife.Function.DataAgent;

public static class DataAgentGraphHandshakeValidator
{
    static readonly Regex RawSqlPattern = new(
        @"```sql|\b(select|insert|update|delete|drop|alter|truncate)\b|\bcreate\b|\bwith\s+(?:recursive\s+)?[A-Za-z_][A-Za-z0-9_]*\s+as\s*\(|\bexecute\s+[A-Za-z_][A-Za-z0-9_.]*\b|\bcall\b\s*(?:[A-Za-z_][A-Za-z0-9_.]*\s*)?\(|\bmerge\s+into\b|\bgrant\s+[A-Za-z]+\b|\brevoke\s+[A-Za-z]+\b|\bpragma\s+[A-Za-z_][A-Za-z0-9_]*\b|\bbegin(?:\s+(?:transaction|work))?\b|\bcommit\b|\brollback\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly Regex MachineTokenPattern = new(
        @"^[A-Za-z0-9][A-Za-z0-9_.-]*$",
        RegexOptions.CultureInvariant);

    static readonly string[] ForbiddenToolMarkers =
    [
        "qchat",
        "qq",
        "browser",
        "desktop",
        "voice",
        "tts",
        "file",
        "rag.manage",
        "checkpoint.write"
    ];

    static readonly HashSet<string> ExecutionAuthorityToolNames = new(
        [
            DataAgentGraphHandshakeToolNames.ExecuteReadOnlyQuery,
            DataAgentNodeCapabilities.ExecuteReadOnlyQuery
        ],
        StringComparer.Ordinal);

    public static DataAgentGraphHandshakeValidationResult Validate(
        DataAgentGraphHandshakeRequest? request,
        DataAgentGraphHandshakeResponse? response)
    {
        if (request is null || response is null)
            return Reject("invalid_response_schema");

        if (IsValidRequest(request) == false)
            return Reject("invalid_request_schema");

        if (string.Equals(request.RequestId, response.RequestId, StringComparison.Ordinal) == false)
            return Reject("request_id_mismatch");

        if (response.NoSqlAuthority == false || response.ReadOnly == false)
            return Reject("sql_authority_requested");

        if (response.RequestsCheckpointMutation)
            return Reject("checkpoint_mutation_requested");

        if (response.RequestsVisibleText)
            return Reject("visible_text_requested");

        if (IsReasonCodeSafe(response.ReasonCode) == false)
            return Reject("invalid_response_schema");

        if (response.TraceSummary is null ||
            response.ContextContribution is null ||
            response.TraceSummary.Length > request.TraceBudgetChars ||
            response.TraceSummary.Length > DataAgentGraphHandshakeLimits.MaxTraceSummaryChars ||
            response.ContextContribution.Length > DataAgentGraphHandshakeLimits.MaxContextContributionChars ||
            ContainsRawSql(response.TraceSummary) ||
            ContainsRawSql(response.ContextContribution))
        {
            return Reject("unsafe_trace");
        }

        HashSet<string> manifestNodeNames = request.NodeManifests
            .Select(manifest => manifest.NodeName)
            .ToHashSet(StringComparer.Ordinal);
        if (response.SelectedNodes is null ||
            response.SelectedNodes.Count == 0 ||
            response.SelectedNodes.Any(node => manifestNodeNames.Contains(node) == false))
        {
            return Reject("unknown_node");
        }

        if (IsProgressValid(request, response.NodeProgress, manifestNodeNames) == false)
            return Reject("progress_invalid");

        HashSet<string> selectedNodeNames = response.SelectedNodes.ToHashSet(StringComparer.Ordinal);
        HashSet<string> allowedToolNames = request.NodeManifests
            .Where(manifest => selectedNodeNames.Contains(manifest.NodeName))
            .SelectMany(manifest => manifest.AllowedToolNames)
            .ToHashSet(StringComparer.Ordinal);
        if (response.RequestedToolNames is null ||
            response.RequestedToolNames.Any(tool => IsAllowedTool(tool, allowedToolNames) == false))
        {
            return Reject("unknown_tool");
        }

        return new DataAgentGraphHandshakeValidationResult(response.Accepted, response.Accepted ? "handshake_accepted" : response.ReasonCode);
    }

    static bool IsValidRequest(DataAgentGraphHandshakeRequest request)
    {
        return HasBoundedText(request.RequestId, DataAgentGraphHandshakeLimits.MaxRequestIdLength) &&
               HasBoundedText(request.SessionId, DataAgentGraphHandshakeLimits.MaxSessionIdLength) &&
               HasBoundedText(request.TurnId, DataAgentGraphHandshakeLimits.MaxTurnIdLength) &&
               HasBoundedText(request.CallerId, DataAgentGraphHandshakeLimits.MaxCallerIdLength) &&
               HasBoundedText(request.GoalOrQuestion, DataAgentGraphHandshakeLimits.MaxQuestionLength) &&
               HasBoundedText(request.ScenarioContextSummary, DataAgentGraphHandshakeLimits.MaxScenarioContextLength) &&
               HasBoundedText(request.RouteScope, DataAgentGraphHandshakeLimits.MaxRouteScopeLength) &&
               HasBoundedText(request.QueryConstraints, DataAgentGraphHandshakeLimits.MaxQueryConstraintsLength) &&
               request.NoSqlAuthority &&
               request.ReadOnly &&
               request.FallbackAvailable &&
               request.TraceBudgetChars is > 0 and <= DataAgentGraphHandshakeLimits.MaxTraceSummaryChars &&
               request.ProgressBudget is > 0 and <= DataAgentGraphHandshakeLimits.MaxProgressEvents &&
               request.NodeManifests is { Count: > 0 } &&
               request.NodeManifests.Count <= DataAgentGraphHandshakeLimits.MaxNodeManifests &&
               request.NodeManifests.All(IsManifestSafe);
    }

    static bool IsManifestSafe(DataAgentGraphNodeManifest manifest)
    {
        return manifest is not null &&
               manifest.AllowedToolNames is not null &&
               manifest.DeniedCapabilityMarkers is not null &&
               manifest.BusinessTerms is not null &&
               HasBoundedText(manifest.NodeName, 128) &&
               HasBoundedText(manifest.Purpose, 512) &&
               HasBoundedText(manifest.InputShape, 256) &&
               HasBoundedText(manifest.OutputShape, 256) &&
               HasBoundedText(manifest.SafetyNotes, 512) &&
               manifest.AllowedToolNames.Count <= DataAgentGraphHandshakeLimits.MaxToolNamesPerNode &&
               manifest.DeniedCapabilityMarkers.Count <= DataAgentGraphHandshakeLimits.MaxDeniedMarkersPerNode &&
               manifest.BusinessTerms.Count <= DataAgentGraphHandshakeLimits.MaxDeniedMarkersPerNode &&
               manifest.BusinessTerms.All(term => IsMachineToken(term, 128)) &&
               manifest.AllowedToolNames.All(tool => IsExecutionAuthorityToolName(tool) == false) &&
               manifest.AllowedToolNames.All(tool => IsForbiddenToolName(tool) == false);
    }

    static bool IsProgressValid(
        DataAgentGraphHandshakeRequest request,
        IReadOnlyList<DataAgentGraphHandshakeProgress>? progress,
        HashSet<string> manifestNodeNames)
    {
        if (progress is null ||
            progress.Count > request.ProgressBudget ||
            progress.Count > DataAgentGraphHandshakeLimits.MaxProgressEvents)
        {
            return false;
        }

        return progress.All(item =>
            item is not null &&
            manifestNodeNames.Contains(item.NodeName) &&
            Enum.IsDefined(typeof(DataAgentGraphHandshakeProgressStatus), item.Status) &&
            IsReasonCodeSafe(item.ReasonCode));
    }

    static bool IsAllowedTool(string? toolName, HashSet<string> allowedToolNames)
    {
        return HasBoundedText(toolName, 128) &&
               IsExecutionAuthorityToolName(toolName) == false &&
               IsForbiddenToolName(toolName) == false &&
               allowedToolNames.Contains(toolName!);
    }

    static bool IsExecutionAuthorityToolName(string? toolName)
    {
        return string.IsNullOrWhiteSpace(toolName) == false &&
               ExecutionAuthorityToolNames.Contains(toolName);
    }

    static bool IsForbiddenToolName(string? toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            return true;

        return ForbiddenToolMarkers.Any(marker =>
            toolName.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    static bool HasBoundedText(string? value, int maxLength)
    {
        return string.IsNullOrWhiteSpace(value) == false &&
               value.Length <= maxLength;
    }

    static bool IsReasonCodeSafe(string? value)
    {
        return IsMachineToken(value, DataAgentGraphHandshakeLimits.MaxReasonCodeLength);
    }

    static bool IsMachineToken(string? value, int maxLength)
    {
        return HasBoundedText(value, maxLength) &&
               MachineTokenPattern.IsMatch(value!);
    }

    static bool ContainsRawSql(string? value)
    {
        return string.IsNullOrWhiteSpace(value) == false &&
               RawSqlPattern.IsMatch(value);
    }

    static DataAgentGraphHandshakeValidationResult Reject(string reasonCode)
    {
        return new DataAgentGraphHandshakeValidationResult(false, reasonCode);
    }
}
