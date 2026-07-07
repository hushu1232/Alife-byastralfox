using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace Alife.Function.DataAgent;

public sealed class DataAgentGraphSidecarProgressBridge
{
    const int MaxNodeNameLength = 128;
    const int MaxMessageLength = 240;
    const int MaxFactCount = 8;
    const int MaxFactKeyLength = 64;
    const int MaxFactValueLength = 160;

    static readonly Regex MachineTokenPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9_.-]*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    static readonly string[] UnsafeFactKeyFragments =
    [
        "hidden",
        "tool_route",
        "evidence_pack",
        "connection",
        "authorization",
        "token",
        "password",
        "pwd",
        "secret",
        "credential",
        "api",
        "key",
        "sql",
        "query",
        "dataset",
        "table"
    ];

    readonly IDataAgentProgressSink? progressSink;
    readonly Func<DateTimeOffset> clock;

    public DataAgentGraphSidecarProgressBridge(
        IDataAgentProgressSink? progressSink = null,
        Func<DateTimeOffset>? clock = null)
    {
        this.progressSink = progressSink;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public DataAgentGraphSidecarProgressBridgeResult PublishHandshakeProgress(
        DataAgentGraphHandshakeRequest request,
        DataAgentOrchestrationResult result,
        IReadOnlyList<DataAgentGraphHandshakeProgress>? progress)
    {
        if (progress is null)
            return Publish(request, result, []);

        List<DataAgentGraphSidecarProgressEvent> events = new(progress.Count);
        foreach (DataAgentGraphHandshakeProgress item in progress)
        {
            if (item is null)
            {
                events.Add(null!);
                continue;
            }

            events.Add(new DataAgentGraphSidecarProgressEvent(
                request.RequestId,
                request.SessionId,
                item.NodeName,
                MapStatus(item.Status),
                item.ReasonCode,
                string.Empty,
                clock(),
                new Dictionary<string, string>()));
        }

        return Publish(request, result, events);
    }

    public DataAgentGraphSidecarProgressBridgeResult Publish(
        DataAgentGraphHandshakeRequest request,
        DataAgentOrchestrationResult result,
        IReadOnlyList<DataAgentGraphSidecarProgressEvent>? events)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);

        if (events is null || events.Count == 0)
            return new DataAgentGraphSidecarProgressBridgeResult(0, 0);

        if (events.Count > request.ProgressBudget ||
            events.Count > DataAgentGraphHandshakeLimits.MaxProgressEvents)
        {
            return new DataAgentGraphSidecarProgressBridgeResult(0, events.Count);
        }

        HashSet<string> manifestNodeNames = request.NodeManifests
            .Select(manifest => manifest.NodeName)
            .ToHashSet(StringComparer.Ordinal);
        int accepted = 0;
        int rejected = 0;

        foreach (DataAgentGraphSidecarProgressEvent progressEvent in events)
        {
            if (TryMap(request, result, manifestNodeNames, progressEvent, clock(), out DataAgentProgressEvent? mapped))
            {
                accepted++;
                progressSink?.Publish(mapped);
            }
            else
            {
                rejected++;
            }
        }

        return new DataAgentGraphSidecarProgressBridgeResult(accepted, rejected);
    }

    static bool TryMap(
        DataAgentGraphHandshakeRequest request,
        DataAgentOrchestrationResult result,
        HashSet<string> manifestNodeNames,
        DataAgentGraphSidecarProgressEvent? progressEvent,
        DateTimeOffset now,
        out DataAgentProgressEvent? mapped)
    {
        mapped = null;

        if (progressEvent is null ||
            IsIdentityMatch(request.RequestId, progressEvent.RequestId, DataAgentGraphHandshakeLimits.MaxRequestIdLength) == false ||
            IsIdentityMatch(request.SessionId, progressEvent.SessionId, DataAgentGraphHandshakeLimits.MaxSessionIdLength) == false ||
            HasBoundedText(progressEvent.NodeName, MaxNodeNameLength) == false ||
            manifestNodeNames.Contains(progressEvent.NodeName) == false ||
            Enum.IsDefined(typeof(DataAgentGraphSidecarProgressStatus), progressEvent.Status) == false ||
            IsMachineToken(progressEvent.ReasonCode, DataAgentGraphHandshakeLimits.MaxReasonCodeLength) == false ||
            IsSafeOptionalText(progressEvent.Message, MaxMessageLength) == false ||
            TryBuildFacts(progressEvent, out IReadOnlyDictionary<string, string>? facts) == false)
        {
            return false;
        }

        mapped = new DataAgentProgressEvent(
            progressEvent.SessionId.Trim(),
            MapKind(progressEvent.NodeName),
            MapPhase(progressEvent.Status),
            MapProgressStatus(progressEvent.Status),
            progressEvent.ReasonCode.Trim(),
            result.Checkpoint.TurnCount,
            now,
            ExecutedSql: false,
            QueryAllowed: result.RouteContext?.AllowsQuery == true,
            Terminal: result.Checkpoint.Terminal || string.Equals(progressEvent.NodeName, DataAgentWorkflowNodeNames.Terminal, StringComparison.Ordinal),
            Facts: facts!);

        return true;
    }

    static bool TryBuildFacts(
        DataAgentGraphSidecarProgressEvent progressEvent,
        out IReadOnlyDictionary<string, string>? facts)
    {
        facts = null;
        if (progressEvent.Facts is null ||
            progressEvent.Facts.Count > MaxFactCount)
        {
            return false;
        }

        Dictionary<string, string> safeFacts = new(StringComparer.Ordinal)
        {
            ["source"] = "graph_sidecar",
            ["node"] = progressEvent.NodeName.Trim(),
            ["request_id"] = progressEvent.RequestId.Trim()
        };

        if (string.IsNullOrWhiteSpace(progressEvent.Message) == false)
            safeFacts["message"] = DataAgentContextFieldSanitizer.Sanitize(progressEvent.Message.Trim(), MaxMessageLength);

        foreach (KeyValuePair<string, string> fact in progressEvent.Facts)
        {
            if (TryNormalizeFact(fact, out string? key, out string? value) == false ||
                safeFacts.ContainsKey(key!))
            {
                return false;
            }

            safeFacts[key!] = value!;
        }

        facts = new ReadOnlyDictionary<string, string>(safeFacts);
        return true;
    }

    static bool TryNormalizeFact(
        KeyValuePair<string, string> fact,
        out string? key,
        out string? value)
    {
        key = null;
        value = null;

        if (IsMachineToken(fact.Key, MaxFactKeyLength) == false ||
            UnsafeFactKeyFragments.Any(fragment => fact.Key.Contains(fragment, StringComparison.OrdinalIgnoreCase)) ||
            IsSafeOptionalText(fact.Value, MaxFactValueLength) == false ||
            string.IsNullOrWhiteSpace(fact.Value))
        {
            return false;
        }

        key = fact.Key.Trim();
        value = DataAgentContextFieldSanitizer.Sanitize(fact.Value.Trim(), MaxFactValueLength);
        return true;
    }

    static bool IsIdentityMatch(string expected, string actual, int maxLength)
    {
        return HasBoundedText(actual, maxLength) &&
               string.Equals(expected, actual.Trim(), StringComparison.Ordinal);
    }

    static bool HasBoundedText(string? value, int maxLength)
    {
        return string.IsNullOrWhiteSpace(value) == false &&
               value.Length <= maxLength;
    }

    static bool IsSafeOptionalText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        return value.Length <= maxLength &&
               DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText(value) == false;
    }

    static bool IsMachineToken(string? value, int maxLength)
    {
        return HasBoundedText(value, maxLength) &&
               MachineTokenPattern.IsMatch(value!);
    }

    static DataAgentProgressEventKind MapKind(string nodeName)
    {
        return nodeName switch
        {
            DataAgentWorkflowNodeNames.RouteGate => DataAgentProgressEventKind.RouteGate,
            DataAgentWorkflowNodeNames.ScenarioKnowledge => DataAgentProgressEventKind.SchemaContext,
            DataAgentWorkflowNodeNames.QueryPlanner => DataAgentProgressEventKind.Planner,
            DataAgentWorkflowNodeNames.QueryPlanValidator => DataAgentProgressEventKind.Validate,
            DataAgentWorkflowNodeNames.SqlSafety => DataAgentProgressEventKind.SqlSafety,
            DataAgentWorkflowNodeNames.ReadOnlyExecute => DataAgentProgressEventKind.Execute,
            DataAgentWorkflowNodeNames.ResultExplainer => DataAgentProgressEventKind.Explain,
            DataAgentWorkflowNodeNames.DiagnosticsRouter => DataAgentProgressEventKind.Explain,
            DataAgentWorkflowNodeNames.CheckpointProgress => DataAgentProgressEventKind.Checkpoint,
            DataAgentWorkflowNodeNames.Terminal => DataAgentProgressEventKind.End,
            DataAgentWorkflowNodeNames.Reject => DataAgentProgressEventKind.Reject,
            _ => DataAgentProgressEventKind.Explain
        };
    }

    static DataAgentProgressEventPhase MapPhase(DataAgentGraphSidecarProgressStatus status)
    {
        return status == DataAgentGraphSidecarProgressStatus.Started
            ? DataAgentProgressEventPhase.Started
            : DataAgentProgressEventPhase.Completed;
    }

    static DataAgentProgressEventStatus MapProgressStatus(DataAgentGraphSidecarProgressStatus status)
    {
        return status switch
        {
            DataAgentGraphSidecarProgressStatus.Started => DataAgentProgressEventStatus.Running,
            DataAgentGraphSidecarProgressStatus.Completed => DataAgentProgressEventStatus.Succeeded,
            DataAgentGraphSidecarProgressStatus.Skipped => DataAgentProgressEventStatus.Skipped,
            DataAgentGraphSidecarProgressStatus.Rejected => DataAgentProgressEventStatus.Rejected,
            DataAgentGraphSidecarProgressStatus.Failed => DataAgentProgressEventStatus.Failed,
            _ => DataAgentProgressEventStatus.Failed
        };
    }

    static DataAgentGraphSidecarProgressStatus MapStatus(DataAgentGraphHandshakeProgressStatus status)
    {
        return status switch
        {
            DataAgentGraphHandshakeProgressStatus.Started => DataAgentGraphSidecarProgressStatus.Started,
            DataAgentGraphHandshakeProgressStatus.Completed => DataAgentGraphSidecarProgressStatus.Completed,
            DataAgentGraphHandshakeProgressStatus.Skipped => DataAgentGraphSidecarProgressStatus.Skipped,
            DataAgentGraphHandshakeProgressStatus.Rejected => DataAgentGraphSidecarProgressStatus.Rejected,
            DataAgentGraphHandshakeProgressStatus.Failed => DataAgentGraphSidecarProgressStatus.Failed,
            _ => (DataAgentGraphSidecarProgressStatus)999
        };
    }
}
