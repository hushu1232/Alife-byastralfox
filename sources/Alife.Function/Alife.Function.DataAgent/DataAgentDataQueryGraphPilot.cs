using System.Text;
using System.Text.RegularExpressions;

namespace Alife.Function.DataAgent;

public sealed record DataAgentDataQueryGraphOptions(bool Enabled)
{
    public const string EnabledEnvironmentVariable = "ALIFE_DATAAGENT_DATAQUERYGRAPH_PILOT_ENABLED";

    public static DataAgentDataQueryGraphOptions Disabled { get; } = new(false);

    public static DataAgentDataQueryGraphOptions FromEnvironment()
    {
        return FromValue(Environment.GetEnvironmentVariable(EnabledEnvironmentVariable));
    }

    public static DataAgentDataQueryGraphOptions FromValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Disabled;

        return value.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "yes" => new DataAgentDataQueryGraphOptions(true),
            "false" or "0" or "no" => Disabled,
            _ => Disabled
        };
    }
}

public sealed record DataAgentDataQueryGraphNode(
    string Name,
    bool AllowsModelCall,
    IReadOnlyList<string> AllowedCapabilities,
    string ScopeReason,
    string Trace);

public sealed record DataAgentDataQueryGraphTransition(string FromNode, string ToNode);

public sealed record DataAgentDataQueryGraphPlan(
    IReadOnlyList<DataAgentDataQueryGraphNode> Nodes,
    IReadOnlyList<DataAgentDataQueryGraphTransition> Transitions)
{
    public static DataAgentDataQueryGraphPlan Empty { get; } = new([], []);
}

public sealed record DataAgentDataQueryGraphDryRunResult(
    bool Enabled,
    bool Accepted,
    string ReasonCode,
    string FallbackReason,
    string RuntimeMarker,
    string ComparedOrchestrationTrace,
    DataAgentDataQueryGraphPlan Plan);

public static class DataAgentDataQueryGraphPilot
{
    public const string NoLangGraphRuntimeMarker = "no_langgraph_runtime";

    public static DataAgentDataQueryGraphDryRunResult DryRun(DataAgentOrchestrationResult? result)
    {
        return DryRun(result, DataAgentDataQueryGraphOptions.FromEnvironment());
    }

    public static DataAgentDataQueryGraphDryRunResult DryRun(
        DataAgentOrchestrationResult? result,
        DataAgentDataQueryGraphOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Enabled == false)
        {
            return new DataAgentDataQueryGraphDryRunResult(
                false,
                false,
                "dataquerygraph_disabled",
                "pilot_disabled",
                NoLangGraphRuntimeMarker,
                string.Empty,
                DataAgentDataQueryGraphPlan.Empty);
        }

        if (result is null)
        {
            return new DataAgentDataQueryGraphDryRunResult(
                true,
                false,
                "dataquerygraph_fallback_to_deterministic_orchestrator",
                "orchestration_result_missing",
                NoLangGraphRuntimeMarker,
                string.Empty,
                DataAgentDataQueryGraphPlan.Empty);
        }

        DataAgentDataQueryGraphPlan plan = BuildPlan(result);
        string reasonCode = result.Response.Accepted
            ? "dataquerygraph_dry_run_accepted"
            : ResolveRejectedReason(result);

        return new DataAgentDataQueryGraphDryRunResult(
            true,
            result.Response.Accepted,
            reasonCode,
            string.Empty,
            NoLangGraphRuntimeMarker,
            BuildComparedTrace(result),
            plan);
    }

    public static DataAgentDataQueryGraphNode BuildNode(string nodeName, string trace)
    {
        DataAgentNodeToolScope scope = DataAgentToolScopePolicy.ForNode(nodeName);
        return new DataAgentDataQueryGraphNode(
            scope.NodeName,
            scope.AllowsModelCall,
            scope.AllowedCapabilities,
            scope.Reason,
            trace);
    }

    static DataAgentDataQueryGraphPlan BuildPlan(DataAgentOrchestrationResult result)
    {
        bool routeDenied = IsRouteDenied(result);
        bool terminal = IsTerminal(result);
        List<DataAgentDataQueryGraphNode> nodes = [];

        foreach (DataAgentOrchestrationStep step in result.Steps)
        {
            if (terminal && step.Node is DataAgentOrchestrationNodeKind.Checkpoint)
            {
                AddNode(nodes, DataAgentWorkflowNodeNames.CheckpointProgress, step);
                continue;
            }

            switch (step.Node)
            {
                case DataAgentOrchestrationNodeKind.RouteGate:
                    if (terminal == false)
                        AddNode(nodes, DataAgentWorkflowNodeNames.RouteGate, step);
                    break;
                case DataAgentOrchestrationNodeKind.SchemaContext:
                    AddNode(nodes, DataAgentWorkflowNodeNames.ScenarioKnowledge, step);
                    break;
                case DataAgentOrchestrationNodeKind.Plan:
                    AddNode(nodes, DataAgentWorkflowNodeNames.QueryPlanner, step);
                    break;
                case DataAgentOrchestrationNodeKind.Validate:
                    AddNode(nodes, DataAgentWorkflowNodeNames.QueryPlanValidator, step);
                    if (step.Status == DataAgentOrchestrationStepStatus.Succeeded && HasExecuteStep(result))
                    {
                        AddNode(nodes, DataAgentWorkflowNodeNames.SqlCompiler, step);
                        AddNode(nodes, DataAgentWorkflowNodeNames.SqlSafety, step);
                    }
                    break;
                case DataAgentOrchestrationNodeKind.Execute:
                    AddNode(nodes, DataAgentWorkflowNodeNames.ReadOnlyExecute, step);
                    break;
                case DataAgentOrchestrationNodeKind.Explain:
                    AddNode(nodes, DataAgentWorkflowNodeNames.ResultExplainer, step);
                    break;
                case DataAgentOrchestrationNodeKind.Clarification:
                case DataAgentOrchestrationNodeKind.Summarize:
                case DataAgentOrchestrationNodeKind.End:
                    AddNode(nodes, DataAgentWorkflowNodeNames.Terminal, step);
                    break;
                case DataAgentOrchestrationNodeKind.Reject:
                    AddNode(nodes, DataAgentWorkflowNodeNames.Reject, step);
                    break;
                case DataAgentOrchestrationNodeKind.Checkpoint:
                    if (routeDenied == false && terminal == false && ShouldAuditEvidence(result))
                        AddNode(nodes, DataAgentWorkflowNodeNames.EvidenceAudit, step);
                    AddNode(nodes, DataAgentWorkflowNodeNames.CheckpointProgress, step);
                    break;
            }
        }

        return new DataAgentDataQueryGraphPlan(
            Array.AsReadOnly(nodes.ToArray()),
            BuildTransitions(nodes));
    }

    static void AddNode(
        List<DataAgentDataQueryGraphNode> nodes,
        string nodeName,
        DataAgentOrchestrationStep step)
    {
        if (nodes.Count > 0 && string.Equals(nodes[^1].Name, nodeName, StringComparison.Ordinal))
            return;

        nodes.Add(BuildNode(nodeName, BuildNodeTrace(step)));
    }

    static IReadOnlyList<DataAgentDataQueryGraphTransition> BuildTransitions(
        IReadOnlyList<DataAgentDataQueryGraphNode> nodes)
    {
        if (nodes.Count < 2)
            return Array.Empty<DataAgentDataQueryGraphTransition>();

        DataAgentDataQueryGraphTransition[] transitions = new DataAgentDataQueryGraphTransition[nodes.Count - 1];
        for (int i = 0; i < nodes.Count - 1; i++)
            transitions[i] = new DataAgentDataQueryGraphTransition(nodes[i].Name, nodes[i + 1].Name);

        return Array.AsReadOnly(transitions);
    }

    static bool IsRouteDenied(DataAgentOrchestrationResult result)
    {
        return result.Steps.Any(step =>
            step.Node == DataAgentOrchestrationNodeKind.RouteGate &&
            step.Status == DataAgentOrchestrationStepStatus.Rejected);
    }

    static bool IsTerminal(DataAgentOrchestrationResult result)
    {
        return result.Steps.Any(step =>
            step.Node is DataAgentOrchestrationNodeKind.Summarize or DataAgentOrchestrationNodeKind.End);
    }

    static bool HasExecuteStep(DataAgentOrchestrationResult result)
    {
        return result.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.Execute);
    }

    static bool ShouldAuditEvidence(DataAgentOrchestrationResult result)
    {
        return result.Steps.Any(step =>
            step.Node is DataAgentOrchestrationNodeKind.Execute or DataAgentOrchestrationNodeKind.Reject);
    }

    static string BuildNodeTrace(DataAgentOrchestrationStep step)
    {
        return $"{step.Node}:{step.Status}:reason={step.Reason}:executed_sql={LowerBool(step.ExecutedSql)}";
    }

    static string BuildComparedTrace(DataAgentOrchestrationResult result)
    {
        return string.Join(
            ">",
            result.Steps.Select(step => $"{step.Node}:{step.Status}"));
    }

    static string ResolveRejectedReason(DataAgentOrchestrationResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Response.RejectedReason) == false)
            return result.Response.RejectedReason;

        return result.Steps.FirstOrDefault(step => step.Status == DataAgentOrchestrationStepStatus.Rejected)?.Reason
            ?? "dataquerygraph_fallback_to_deterministic_orchestrator";
    }

    static string LowerBool(bool value)
    {
        return value ? "true" : "false";
    }
}

public static class DataAgentDataQueryGraphTraceFormatter
{
    const string TruncationSuffix = "...";
    const string SqlRejectedReason = "dataquerygraph_sql_text_rejected";

    static readonly Regex RawSqlMarkerPattern = new(
        @"```sql|\b(select|insert|update|delete|drop|alter|truncate)\b|\bcreate\b|\bwith\s+(?:recursive\s+)?[A-Za-z_][A-Za-z0-9_]*\s+as\s*\(|\bexecute\s+[A-Za-z_][A-Za-z0-9_.]*\b|\bcall\s+[A-Za-z_][A-Za-z0-9_.]*\s*\(|\bmerge\s+into\b|\bgrant\s+[A-Za-z]+\b|\brevoke\s+[A-Za-z]+\b|\bpragma\s+[A-Za-z_][A-Za-z0-9_]*\b|\bbegin(?:\s+(?:transaction|work))?\b|\bcommit\b|\brollback\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string Format(DataAgentDataQueryGraphDryRunResult? result, int maxChars = 1800)
    {
        if (result is null)
            return Bound("DataQueryGraph dry-run\nreason=dataquerygraph_trace_unavailable", maxChars);

        if (ContainsUnsafeSqlText(result))
            return Bound($"DataQueryGraph dry-run\nreason={SqlRejectedReason}", maxChars);

        StringBuilder builder = new();
        builder.AppendLine("DataQueryGraph dry-run");
        builder.AppendLine($"enabled={LowerBool(result.Enabled)}");
        builder.AppendLine($"accepted={LowerBool(result.Accepted)}");
        builder.AppendLine($"reason={Safe(result.ReasonCode)}");
        builder.AppendLine($"fallback={Safe(result.FallbackReason)}");
        builder.AppendLine($"runtime={Safe(result.RuntimeMarker)}");
        builder.AppendLine($"compared_trace={Safe(result.ComparedOrchestrationTrace)}");
        builder.Append("nodes=");
        builder.Append(string.Join(",", result.Plan.Nodes.Select(node => node.Name)));

        return Bound(builder.ToString().TrimEnd(), maxChars);
    }

    static bool ContainsUnsafeSqlText(DataAgentDataQueryGraphDryRunResult result)
    {
        return ContainsRawSql(result.ReasonCode) ||
            ContainsRawSql(result.FallbackReason) ||
            ContainsRawSql(result.ComparedOrchestrationTrace) ||
            result.Plan.Nodes.Any(node => ContainsRawSql(node.Trace));
    }

    static bool ContainsRawSql(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return RawSqlMarkerPattern.IsMatch(value);
    }

    static string Safe(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "empty"
            : value.ReplaceLineEndings(" ").Replace('=', ':').Trim();
    }

    static string Bound(string value, int maxChars)
    {
        if (maxChars <= 0 || value.Length <= maxChars)
            return maxChars <= 0 ? string.Empty : value;

        if (maxChars <= TruncationSuffix.Length)
            return TruncationSuffix[..maxChars];

        return value[..(maxChars - TruncationSuffix.Length)].TrimEnd() + TruncationSuffix;
    }

    static string LowerBool(bool value)
    {
        return value ? "true" : "false";
    }
}
