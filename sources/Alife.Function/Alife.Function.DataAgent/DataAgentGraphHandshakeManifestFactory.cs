namespace Alife.Function.DataAgent;

public static class DataAgentGraphHandshakeManifestFactory
{
    public static IReadOnlyList<DataAgentGraphNodeManifest> CreateDefault()
    {
        return
        [
            Manifest(
                DataAgentWorkflowNodeNames.ScenarioKnowledge,
                "Map deterministic scenario knowledge and business vocabulary.",
                [DataAgentGraphHandshakeToolNames.ReadScenarioContext],
                [DataAgentNodeCapabilities.ExecuteReadOnlyQuery, DataAgentNodeCapabilities.CompileSql],
                "goal_or_question + scenario pack summary",
                "business_terms + dataset vocabulary",
                ["dataset", "field", "operator", "limit"],
                "No SQL generation or execution."),
            Manifest(
                DataAgentWorkflowNodeNames.RouteGate,
                "Read current route scope and explain permission state.",
                [DataAgentGraphHandshakeToolNames.ReadRouteScope],
                [DataAgentNodeCapabilities.ExecuteReadOnlyQuery, DataAgentNodeCapabilities.WriteAudit],
                "Tool Broker route state",
                "allow_or_deny + reason_code",
                ["owner", "private", "route"],
                "Cannot decide Tool Broker route; C# policy remains authority."),
            Manifest(
                DataAgentWorkflowNodeNames.QueryPlanner,
                "Suggest a QueryPlan-shaped intent candidate.",
                [DataAgentGraphHandshakeToolNames.ProposeQueryPlan],
                [DataAgentNodeCapabilities.ExecuteReadOnlyQuery, DataAgentNodeCapabilities.CompileSql],
                "goal_or_question + allowed dataset vocabulary",
                "query_plan_candidate",
                ["dataset", "field", "filter", "sort", "limit"],
                "Candidate only; C# validator decides."),
            Manifest(
                DataAgentWorkflowNodeNames.QueryPlanValidator,
                "Read validation status for C# QueryPlan checks.",
                [DataAgentGraphHandshakeToolNames.ReadQueryPlanValidationStatus],
                [DataAgentNodeCapabilities.ExecuteReadOnlyQuery],
                "query_plan_candidate",
                "validation_status_summary",
                ["field", "operator", "limit"],
                "Cannot override validator."),
            Manifest(
                DataAgentWorkflowNodeNames.SqlSafety,
                "Read SQL safety status produced by C#.",
                [DataAgentGraphHandshakeToolNames.ReadSqlSafetyStatus],
                [DataAgentNodeCapabilities.ExecuteReadOnlyQuery, DataAgentNodeCapabilities.CompileSql],
                "compiled_sql_status",
                "safe_or_rejected_summary",
                ["read_only", "parameterized", "single_statement"],
                "Cannot see executable SQL text."),
            Manifest(
                DataAgentWorkflowNodeNames.ReadOnlyExecute,
                "Represent the C# read-only execution boundary.",
                [],
                [DataAgentNodeCapabilities.ExecuteReadOnlyQuery, DataAgentGraphHandshakeToolNames.ExecuteReadOnlyQuery],
                "validated_query_plan",
                "execution_boundary_status",
                ["read_only"],
                "Sidecar cannot execute or request execution."),
            Manifest(
                DataAgentWorkflowNodeNames.ResultExplainer,
                "Interpret already controlled result state.",
                [DataAgentGraphHandshakeToolNames.InterpretControlledResult],
                [DataAgentNodeCapabilities.ExecuteReadOnlyQuery],
                "controlled_result_summary",
                "result_interpretation_summary",
                ["result_summary", "evidence"],
                "Cannot fetch new data."),
            Manifest(
                DataAgentWorkflowNodeNames.DiagnosticsRouter,
                "Summarize owner diagnostics availability.",
                [
                    DataAgentGraphHandshakeToolNames.ReadEvidenceDiagnostics,
                    DataAgentGraphHandshakeToolNames.ReadTraceDiagnostics,
                    DataAgentGraphHandshakeToolNames.ReadProgressDiagnostics
                ],
                [DataAgentNodeCapabilities.ExecuteReadOnlyQuery, DataAgentNodeCapabilities.WriteAudit],
                "diagnostics_state",
                "diagnostics_summary",
                ["evidence", "trace", "progress"],
                "Diagnostics only; no hidden context leakage."),
            Manifest(
                DataAgentWorkflowNodeNames.CheckpointProgress,
                "Represent C# checkpoint and progress ownership.",
                [],
                [DataAgentNodeCapabilities.PublishProgress, DataAgentGraphSidecarAuthority.MutateCheckpoint.ToString()],
                "checkpoint_status",
                "checkpoint_boundary_status",
                ["checkpoint", "progress"],
                "Sidecar cannot mutate checkpoints or publish progress."),
            Manifest(
                DataAgentWorkflowNodeNames.Terminal,
                "Represent summarize/end terminal states.",
                [],
                [DataAgentNodeCapabilities.ExecuteReadOnlyQuery],
                "terminal_request",
                "terminal_status",
                ["summary", "end"],
                "No query execution."),
            Manifest(
                DataAgentWorkflowNodeNames.Reject,
                "Represent fail-closed rejection states.",
                [],
                [DataAgentNodeCapabilities.ExecuteReadOnlyQuery],
                "rejection_reason",
                "fallback_required",
                ["reason_code"],
                "No query execution.")
        ];
    }

    static DataAgentGraphNodeManifest Manifest(
        string nodeName,
        string purpose,
        IReadOnlyList<string> allowedToolNames,
        IReadOnlyList<string> deniedCapabilityMarkers,
        string inputShape,
        string outputShape,
        IReadOnlyList<string> businessTerms,
        string safetyNotes)
    {
        return new DataAgentGraphNodeManifest(
            nodeName,
            purpose,
            Array.AsReadOnly(allowedToolNames.ToArray()),
            Array.AsReadOnly(deniedCapabilityMarkers.ToArray()),
            inputShape,
            outputShape,
            Array.AsReadOnly(businessTerms.ToArray()),
            safetyNotes);
    }
}
