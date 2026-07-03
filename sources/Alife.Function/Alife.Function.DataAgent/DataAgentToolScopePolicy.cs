namespace Alife.Function.DataAgent;

public static class DataAgentWorkflowNodeNames
{
    public const string RouteGate = "route_gate";
    public const string ScenarioKnowledge = "scenario_knowledge";
    public const string QueryPlanner = "query_planner";
    public const string QueryPlanValidator = "query_plan_validator";
    public const string SqlCompiler = "sql_compiler";
    public const string SqlSafety = "sql_safety";
    public const string ReadOnlyExecute = "read_only_execute";
    public const string ResultExplainer = "result_explainer";
    public const string EvidenceAudit = "evidence_audit";
    public const string CheckpointProgress = "checkpoint_progress";
    public const string DiagnosticsRouter = "diagnostics_router";
}

public static class DataAgentNodeCapabilities
{
    public const string ReadCatalog = "dataagent.catalog.read";
    public const string ReadScenarioPack = "dataagent.scenario_pack.read";
    public const string GenerateQueryPlan = "dataagent.query_plan.generate";
    public const string ValidateQueryPlan = "dataagent.query_plan.validate";
    public const string CompileSql = "dataagent.sql.compile";
    public const string ValidateSqlSafety = "dataagent.sql.safety_validate";
    public const string ExecuteReadOnlyQuery = "dataagent.query.execute_readonly";
    public const string ExplainResult = "dataagent.result.explain";
    public const string BuildEvidencePack = "dataagent.evidence.build";
    public const string WriteAudit = "dataagent.audit.write";
    public const string PublishProgress = "dataagent.progress.publish";
    public const string ReadProgressDiagnostics = "dataagent.diagnostics.progress.read";
    public const string ReadTraceDiagnostics = "dataagent.diagnostics.trace.read";
    public const string ReadEvidenceDiagnostics = "dataagent.diagnostics.evidence.read";
}

public sealed record DataAgentNodeToolScope
{
    public DataAgentNodeToolScope(
        string NodeName,
        bool AllowsModelCall,
        IReadOnlyList<string> AllowedCapabilities,
        string Reason)
    {
        ArgumentNullException.ThrowIfNull(NodeName);
        ArgumentNullException.ThrowIfNull(AllowedCapabilities);
        ArgumentNullException.ThrowIfNull(Reason);

        this.NodeName = NodeName;
        this.AllowsModelCall = AllowsModelCall;
        this.AllowedCapabilities = Array.AsReadOnly(AllowedCapabilities.ToArray());
        this.Reason = Reason;
    }

    public string NodeName { get; }
    public bool AllowsModelCall { get; }
    public IReadOnlyList<string> AllowedCapabilities { get; }
    public string Reason { get; }
}

public static class DataAgentToolScopePolicy
{
    const string UnknownNodeReason = "unknown_node_fail_closed";

    static readonly IReadOnlyList<DataAgentNodeToolScope> DefaultScopes = CreateDefaultScopes();
    static readonly IReadOnlyDictionary<string, DataAgentNodeToolScope> ScopesByNodeName =
        DefaultScopes.ToDictionary(scope => scope.NodeName, StringComparer.Ordinal);

    public static IReadOnlyList<DataAgentNodeToolScope> CreateDefault()
    {
        return Array.AsReadOnly(DefaultScopes.Select(Copy).ToArray());
    }

    public static DataAgentNodeToolScope ForNode(string nodeName)
    {
        if (string.IsNullOrWhiteSpace(nodeName))
        {
            return FailClosed(nodeName ?? string.Empty);
        }

        return ScopesByNodeName.TryGetValue(nodeName, out DataAgentNodeToolScope? scope)
            ? Copy(scope)
            : FailClosed(nodeName);
    }

    static IReadOnlyList<DataAgentNodeToolScope> CreateDefaultScopes()
    {
        return Array.AsReadOnly(new[]
        {
            Scope(
                DataAgentWorkflowNodeNames.RouteGate,
                false,
                "route_gate_uses_tool_broker_state"),
            Scope(
                DataAgentWorkflowNodeNames.ScenarioKnowledge,
                false,
                "scenario_knowledge_reads_catalog_and_scenario_pack",
                DataAgentNodeCapabilities.ReadCatalog,
                DataAgentNodeCapabilities.ReadScenarioPack),
            Scope(
                DataAgentWorkflowNodeNames.QueryPlanner,
                true,
                "query_planner_generates_plan_from_catalog_and_scenario_pack",
                DataAgentNodeCapabilities.ReadCatalog,
                DataAgentNodeCapabilities.ReadScenarioPack,
                DataAgentNodeCapabilities.GenerateQueryPlan),
            Scope(
                DataAgentWorkflowNodeNames.QueryPlanValidator,
                false,
                "query_plan_validator_validates_plan",
                DataAgentNodeCapabilities.ValidateQueryPlan),
            Scope(
                DataAgentWorkflowNodeNames.SqlCompiler,
                false,
                "sql_compiler_compiles_validated_plan",
                DataAgentNodeCapabilities.CompileSql),
            Scope(
                DataAgentWorkflowNodeNames.SqlSafety,
                false,
                "sql_safety_validates_compiled_sql",
                DataAgentNodeCapabilities.ValidateSqlSafety),
            Scope(
                DataAgentWorkflowNodeNames.ReadOnlyExecute,
                false,
                "read_only_execute_runs_read_only_query",
                DataAgentNodeCapabilities.ExecuteReadOnlyQuery),
            Scope(
                DataAgentWorkflowNodeNames.ResultExplainer,
                true,
                "result_explainer_explains_result",
                DataAgentNodeCapabilities.ExplainResult),
            Scope(
                DataAgentWorkflowNodeNames.EvidenceAudit,
                false,
                "evidence_audit_builds_evidence_and_writes_audit",
                DataAgentNodeCapabilities.BuildEvidencePack,
                DataAgentNodeCapabilities.WriteAudit),
            Scope(
                DataAgentWorkflowNodeNames.CheckpointProgress,
                false,
                "checkpoint_progress_publishes_progress",
                DataAgentNodeCapabilities.PublishProgress),
            Scope(
                DataAgentWorkflowNodeNames.DiagnosticsRouter,
                true,
                "diagnostics_router_reads_diagnostics",
                DataAgentNodeCapabilities.ReadProgressDiagnostics,
                DataAgentNodeCapabilities.ReadTraceDiagnostics,
                DataAgentNodeCapabilities.ReadEvidenceDiagnostics)
        });
    }

    static DataAgentNodeToolScope Scope(
        string nodeName,
        bool allowsModelCall,
        string reason,
        params string[] allowedCapabilities)
    {
        return new DataAgentNodeToolScope(nodeName, allowsModelCall, allowedCapabilities, reason);
    }

    static DataAgentNodeToolScope Copy(DataAgentNodeToolScope scope)
    {
        return new DataAgentNodeToolScope(
            scope.NodeName,
            scope.AllowsModelCall,
            scope.AllowedCapabilities,
            scope.Reason);
    }

    static DataAgentNodeToolScope FailClosed(string nodeName)
    {
        return new DataAgentNodeToolScope(
            nodeName,
            false,
            Array.Empty<string>(),
            UnknownNodeReason);
    }
}
