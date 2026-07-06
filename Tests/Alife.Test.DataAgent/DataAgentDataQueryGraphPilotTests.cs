using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentDataQueryGraphPilotTests
{
    [Test]
    public void OptionsDefaultDisabledAndParseOnlyExplicitTrueLikeValues()
    {
        string?[] disabledValues =
        [
            null,
            string.Empty,
            "   ",
            "false",
            "FALSE",
            "0",
            "no",
            "unexpected"
        ];

        string[] enabledValues =
        [
            "true",
            "TRUE",
            "1",
            "yes",
            " YES "
        ];

        Assert.Multiple(() =>
        {
            Assert.That(DataAgentDataQueryGraphOptions.Disabled.Enabled, Is.False);
            Assert.That(
                DataAgentDataQueryGraphOptions.EnabledEnvironmentVariable,
                Is.EqualTo("ALIFE_DATAAGENT_DATAQUERYGRAPH_PILOT_ENABLED"));

            foreach (string? value in disabledValues)
                Assert.That(DataAgentDataQueryGraphOptions.FromValue(value).Enabled, Is.False, $"Expected disabled for '{value}'.");

            foreach (string value in enabledValues)
                Assert.That(DataAgentDataQueryGraphOptions.FromValue(value).Enabled, Is.True, $"Expected enabled for '{value}'.");
        });
    }

    [Test]
    [NonParallelizable]
    public void ExplicitEnableOnlyEnablesDryRunAndNoLangGraphRuntimeMarker()
    {
        string? previous = Environment.GetEnvironmentVariable(DataAgentDataQueryGraphOptions.EnabledEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(DataAgentDataQueryGraphOptions.EnabledEnvironmentVariable, null);
            DataAgentDataQueryGraphOptions defaultOptions = DataAgentDataQueryGraphOptions.FromEnvironment();

            Environment.SetEnvironmentVariable(DataAgentDataQueryGraphOptions.EnabledEnvironmentVariable, "true");
            DataAgentDataQueryGraphOptions enabledOptions = DataAgentDataQueryGraphOptions.FromEnvironment();
            DataAgentDataQueryGraphDryRunResult result = DataAgentDataQueryGraphPilot.DryRun(
                AcceptedResult(),
                enabledOptions);

            Assert.Multiple(() =>
            {
                Assert.That(defaultOptions.Enabled, Is.False);
                Assert.That(enabledOptions.Enabled, Is.True);
                Assert.That(DataAgentDataQueryGraphPilot.NoLangGraphRuntimeMarker, Is.EqualTo("no_langgraph_runtime"));
                Assert.That(result.Enabled, Is.True);
                Assert.That(result.RuntimeMarker, Is.EqualTo(DataAgentDataQueryGraphPilot.NoLangGraphRuntimeMarker));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable(DataAgentDataQueryGraphOptions.EnabledEnvironmentVariable, previous);
        }
    }

    [Test]
    public void DisabledPilotReturnsDisabledReasonWithoutNodes()
    {
        DataAgentDataQueryGraphDryRunResult result = DataAgentDataQueryGraphPilot.DryRun(
            AcceptedResult(),
            DataAgentDataQueryGraphOptions.Disabled);

        Assert.Multiple(() =>
        {
            Assert.That(result.Enabled, Is.False);
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("dataquerygraph_disabled"));
            Assert.That(result.FallbackReason, Is.EqualTo("pilot_disabled"));
            Assert.That(result.Plan.Nodes, Is.Empty);
            Assert.That(result.Plan.Transitions, Is.Empty);
        });
    }

    [Test]
    public void EnabledPilotBuildsAcceptedQueryGraphWithCanonicalNodeScopes()
    {
        DataAgentDataQueryGraphDryRunResult result = DataAgentDataQueryGraphPilot.DryRun(
            AcceptedResult(),
            EnabledOptions);

        string[] expectedNodes =
        [
            DataAgentWorkflowNodeNames.RouteGate,
            DataAgentWorkflowNodeNames.ScenarioKnowledge,
            DataAgentWorkflowNodeNames.QueryPlanner,
            DataAgentWorkflowNodeNames.QueryPlanValidator,
            DataAgentWorkflowNodeNames.SqlCompiler,
            DataAgentWorkflowNodeNames.SqlSafety,
            DataAgentWorkflowNodeNames.ReadOnlyExecute,
            DataAgentWorkflowNodeNames.ResultExplainer,
            DataAgentWorkflowNodeNames.EvidenceAudit,
            DataAgentWorkflowNodeNames.CheckpointProgress
        ];

        Assert.Multiple(() =>
        {
            Assert.That(result.Enabled, Is.True);
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.ReasonCode, Is.EqualTo("dataquerygraph_dry_run_completed"));
            Assert.That(result.Plan.Nodes.Select(node => node.Name), Is.EqualTo(expectedNodes));
            Assert.That(result.Plan.Transitions.Select(transition => (transition.FromNode, transition.ToNode)), Is.EqualTo(new[]
            {
                (DataAgentWorkflowNodeNames.RouteGate, DataAgentWorkflowNodeNames.ScenarioKnowledge),
                (DataAgentWorkflowNodeNames.ScenarioKnowledge, DataAgentWorkflowNodeNames.QueryPlanner),
                (DataAgentWorkflowNodeNames.QueryPlanner, DataAgentWorkflowNodeNames.QueryPlanValidator),
                (DataAgentWorkflowNodeNames.QueryPlanValidator, DataAgentWorkflowNodeNames.SqlCompiler),
                (DataAgentWorkflowNodeNames.SqlCompiler, DataAgentWorkflowNodeNames.SqlSafety),
                (DataAgentWorkflowNodeNames.SqlSafety, DataAgentWorkflowNodeNames.ReadOnlyExecute),
                (DataAgentWorkflowNodeNames.ReadOnlyExecute, DataAgentWorkflowNodeNames.ResultExplainer),
                (DataAgentWorkflowNodeNames.ResultExplainer, DataAgentWorkflowNodeNames.EvidenceAudit),
                (DataAgentWorkflowNodeNames.EvidenceAudit, DataAgentWorkflowNodeNames.CheckpointProgress)
            }));
            Assert.That(
                result.Plan.Nodes.Single(node => node.Name == DataAgentWorkflowNodeNames.ReadOnlyExecute).AllowedCapabilities,
                Is.EqualTo(new[] { DataAgentNodeCapabilities.ExecuteReadOnlyQuery }));
            Assert.That(
                result.Plan.Nodes.Single(node => node.Name == DataAgentWorkflowNodeNames.QueryPlanner).AllowedCapabilities,
                Does.Not.Contain(DataAgentNodeCapabilities.ExecuteReadOnlyQuery));
        });
    }

    [Test]
    public void RouteDeniedGraphRejectsAndDoesNotReachReadOnlyExecute()
    {
        DataAgentDataQueryGraphDryRunResult result = DataAgentDataQueryGraphPilot.DryRun(
            RouteDeniedResult(),
            EnabledOptions);

        Assert.Multiple(() =>
        {
            Assert.That(result.Enabled, Is.True);
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("dataquerygraph_route_rejected"));
            Assert.That(result.Plan.Nodes.Select(node => node.Name), Is.EqualTo(new[]
            {
                DataAgentWorkflowNodeNames.RouteGate,
                DataAgentWorkflowNodeNames.Reject,
                DataAgentWorkflowNodeNames.CheckpointProgress
            }));
            Assert.That(result.Plan.Nodes.Any(node => node.Name == DataAgentWorkflowNodeNames.ReadOnlyExecute), Is.False);
        });
    }

    [Test]
    public void TerminalGraphCheckpointsWithoutQueryExecution()
    {
        DataAgentDataQueryGraphDryRunResult result = DataAgentDataQueryGraphPilot.DryRun(
            TerminalResult(),
            EnabledOptions);

        Assert.Multiple(() =>
        {
            Assert.That(result.Enabled, Is.True);
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.ReasonCode, Is.EqualTo("dataquerygraph_dry_run_completed"));
            Assert.That(result.Plan.Nodes.Select(node => node.Name), Is.EqualTo(new[]
            {
                DataAgentWorkflowNodeNames.Terminal,
                DataAgentWorkflowNodeNames.CheckpointProgress
            }));
            Assert.That(result.Plan.Nodes.Any(node => node.Name == DataAgentWorkflowNodeNames.ReadOnlyExecute), Is.False);
            Assert.That(result.Plan.Nodes.Any(node => node.AllowedCapabilities.Contains(DataAgentNodeCapabilities.ExecuteReadOnlyQuery)), Is.False);
        });
    }

    [Test]
    public void ClarificationResponseAcceptedByOrchestratorIsNotGraphAccepted()
    {
        DataAgentDataQueryGraphDryRunResult result = DataAgentDataQueryGraphPilot.DryRun(
            ClarificationResult(),
            EnabledOptions);

        Assert.Multiple(() =>
        {
            Assert.That(result.Enabled, Is.True);
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("needs_clarification"));
            Assert.That(result.Plan.Nodes.Any(node => node.Name == DataAgentWorkflowNodeNames.ReadOnlyExecute), Is.False);
            Assert.That(result.Plan.Nodes.Any(node => node.AllowedCapabilities.Contains(DataAgentNodeCapabilities.ExecuteReadOnlyQuery)), Is.False);
        });
    }

    [Test]
    public void InvalidAnswerAcceptedByOrchestratorIsNotGraphAccepted()
    {
        DataAgentDataQueryGraphDryRunResult result = DataAgentDataQueryGraphPilot.DryRun(
            InvalidAcceptedResponseResult(),
            EnabledOptions);

        Assert.Multiple(() =>
        {
            Assert.That(result.Enabled, Is.True);
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("unsupported_operator:starts_with"));
            Assert.That(result.Plan.Nodes.Any(node => node.Name == DataAgentWorkflowNodeNames.ReadOnlyExecute), Is.False);
            Assert.That(result.Plan.Nodes.Any(node => node.AllowedCapabilities.Contains(DataAgentNodeCapabilities.ExecuteReadOnlyQuery)), Is.False);
        });
    }

    [Test]
    public void UnknownNodeFailsClosedWithNoCapabilitiesAndNoModelCall()
    {
        DataAgentDataQueryGraphNode node = DataAgentDataQueryGraphPilot.BuildNode("unknown_node", "planner trace");

        Assert.Multiple(() =>
        {
            Assert.That(node.Name, Is.EqualTo("unknown_node"));
            Assert.That(node.AllowsModelCall, Is.False);
            Assert.That(node.AllowedCapabilities, Is.Empty);
            Assert.That(node.ScopeReason, Is.EqualTo("unknown_node_fail_closed"));
        });
    }

    [Test]
    public void PlannerAndDiagnosticsScopesCannotExecuteReadOnlyQuery()
    {
        DataAgentDataQueryGraphNode planner = DataAgentDataQueryGraphPilot.BuildNode(
            DataAgentWorkflowNodeNames.QueryPlanner,
            "planner");
        DataAgentDataQueryGraphNode diagnostics = DataAgentDataQueryGraphPilot.BuildNode(
            DataAgentWorkflowNodeNames.DiagnosticsRouter,
            "diagnostics");

        Assert.Multiple(() =>
        {
            Assert.That(planner.AllowsModelCall, Is.True);
            Assert.That(planner.AllowedCapabilities, Does.Not.Contain(DataAgentNodeCapabilities.ExecuteReadOnlyQuery));
            Assert.That(diagnostics.AllowsModelCall, Is.True);
            Assert.That(diagnostics.AllowedCapabilities, Does.Not.Contain(DataAgentNodeCapabilities.ExecuteReadOnlyQuery));
        });
    }

    [Test]
    public void NullResultFallsBackToDeterministicOrchestratorWhenEnabled()
    {
        DataAgentDataQueryGraphDryRunResult result = DataAgentDataQueryGraphPilot.DryRun(null, EnabledOptions);

        Assert.Multiple(() =>
        {
            Assert.That(result.Enabled, Is.True);
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("dataquerygraph_fallback_to_deterministic_orchestrator"));
            Assert.That(result.FallbackReason, Is.EqualTo("orchestration_result_missing"));
            Assert.That(result.Plan.Nodes, Is.Empty);
            Assert.That(result.Plan.Transitions, Is.Empty);
        });
    }

    [Test]
    public void FormatterRejectsAndRedactsSqlLikeComparedTraceText()
    {
        DataAgentDataQueryGraphDryRunResult result = new(
            Enabled: true,
            Accepted: false,
            ReasonCode: "dataquerygraph_fallback_to_deterministic_orchestrator",
            FallbackReason: "compared_trace_contains_select_from_document_index",
            RuntimeMarker: DataAgentDataQueryGraphPilot.NoLangGraphRuntimeMarker,
            ComparedOrchestrationTrace: "SELECT path FROM document_index LIMIT 20",
            Plan: new DataAgentDataQueryGraphPlan(
                [DataAgentDataQueryGraphPilot.BuildNode(DataAgentWorkflowNodeNames.QueryPlanner, "DROP TABLE document_index")],
                []));

        string formatted = DataAgentDataQueryGraphTraceFormatter.Format(result);

        Assert.Multiple(() =>
        {
            Assert.That(formatted, Does.Contain("dataquerygraph_sql_text_rejected"));
            Assert.That(formatted, Does.Not.Contain("SELECT path FROM document_index LIMIT 20"));
            Assert.That(formatted, Does.Not.Contain("DROP TABLE document_index"));
            Assert.That(formatted, Does.Not.Contain("compared_trace_contains_select_from_document_index"));
        });
    }

    [Test]
    public void FormatterRejectsSqlLikeRuntimeNodeScopeReasonAndTransitionEndpointText()
    {
        const string runtimeMarker = "runtime SELECT token FROM secret_table";
        const string nodeName = "node SELECT name FROM secret_node";
        const string scopeReason = "scope DELETE FROM secret_scope";
        const string transitionEndpoint = "transition DROP TABLE secret_transition";

        DataAgentDataQueryGraphDryRunResult result = new(
            Enabled: true,
            Accepted: true,
            ReasonCode: "dataquerygraph_dry_run_completed",
            FallbackReason: string.Empty,
            RuntimeMarker: runtimeMarker,
            ComparedOrchestrationTrace: "RouteGate:Succeeded",
            Plan: new DataAgentDataQueryGraphPlan(
                [
                    new DataAgentDataQueryGraphNode(
                        nodeName,
                        AllowsModelCall: false,
                        AllowedCapabilities: [],
                        scopeReason,
                        "safe_trace")
                ],
                [
                    new DataAgentDataQueryGraphTransition(
                        transitionEndpoint,
                        DataAgentWorkflowNodeNames.CheckpointProgress)
                ]));

        string formatted = DataAgentDataQueryGraphTraceFormatter.Format(result);

        Assert.Multiple(() =>
        {
            Assert.That(formatted, Does.Contain("dataquerygraph_sql_text_rejected"));
            Assert.That(formatted, Does.Not.Contain(runtimeMarker));
            Assert.That(formatted, Does.Not.Contain(nodeName));
            Assert.That(formatted, Does.Not.Contain(scopeReason));
            Assert.That(formatted, Does.Not.Contain(transitionEndpoint));
        });
    }

    static DataAgentDataQueryGraphOptions EnabledOptions => new(true);

    static DataAgentOrchestrationResult AcceptedResult()
    {
        return Result(
            DataAgentAnalysisSessionStatus.Active,
            true,
            string.Empty,
            [
                Step(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Succeeded, "route_allowed", false),
                Step(DataAgentOrchestrationNodeKind.SchemaContext, DataAgentOrchestrationStepStatus.Succeeded, "dataagent_catalog_available", false),
                Step(DataAgentOrchestrationNodeKind.Plan, DataAgentOrchestrationStepStatus.Succeeded, "planner_response_received", false),
                Step(DataAgentOrchestrationNodeKind.Validate, DataAgentOrchestrationStepStatus.Succeeded, "validated", false),
                Step(DataAgentOrchestrationNodeKind.Execute, DataAgentOrchestrationStepStatus.Succeeded, "read_only_query_executed", true),
                Step(DataAgentOrchestrationNodeKind.Explain, DataAgentOrchestrationStepStatus.Succeeded, "result_explained", false),
                Step(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
            ]);
    }

    static DataAgentOrchestrationResult RouteDeniedResult()
    {
        return Result(
            DataAgentAnalysisSessionStatus.Rejected,
            false,
            "tool_route_required",
            [
                Step(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Rejected, "tool_route_required", false),
                Step(DataAgentOrchestrationNodeKind.Reject, DataAgentOrchestrationStepStatus.Rejected, "tool_route_required", false),
                Step(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
            ]);
    }

    static DataAgentOrchestrationResult TerminalResult()
    {
        return Result(
            DataAgentAnalysisSessionStatus.Ended,
            true,
            string.Empty,
            [
                Step(DataAgentOrchestrationNodeKind.End, DataAgentOrchestrationStepStatus.Succeeded, "terminal_end", false),
                Step(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
            ],
            DataAgentAnalysisTurnIntent.End);
    }

    static DataAgentOrchestrationResult ClarificationResult()
    {
        return Result(
            DataAgentAnalysisSessionStatus.Active,
            true,
            string.Empty,
            [
                Step(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Succeeded, "route_allowed", false),
                Step(DataAgentOrchestrationNodeKind.Clarification, DataAgentOrchestrationStepStatus.Succeeded, "needs_clarification", false),
                Step(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
            ],
            answer: RejectedAnswer("needs_clarification"));
    }

    static DataAgentOrchestrationResult InvalidAcceptedResponseResult()
    {
        return Result(
            DataAgentAnalysisSessionStatus.Active,
            true,
            string.Empty,
            [
                Step(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Succeeded, "route_allowed", false),
                Step(DataAgentOrchestrationNodeKind.SchemaContext, DataAgentOrchestrationStepStatus.Succeeded, "dataagent_catalog_available", false),
                Step(DataAgentOrchestrationNodeKind.Plan, DataAgentOrchestrationStepStatus.Succeeded, "planner_response_received", false),
                Step(DataAgentOrchestrationNodeKind.Validate, DataAgentOrchestrationStepStatus.Rejected, "unsupported_operator:starts_with", false),
                Step(DataAgentOrchestrationNodeKind.Reject, DataAgentOrchestrationStepStatus.Rejected, "unsupported_operator:starts_with", false),
                Step(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
            ],
            answer: RejectedAnswer("unsupported_operator:starts_with"));
    }

    static DataAgentOrchestrationResult Result(
        DataAgentAnalysisSessionStatus status,
        bool accepted,
        string rejectedReason,
        IReadOnlyList<DataAgentOrchestrationStep> steps,
        DataAgentAnalysisTurnIntent intent = DataAgentAnalysisTurnIntent.NewQuestion,
        DataAgentAnswer? answer = null)
    {
        DataAgentAnalysisResponse response = new(
            "session-1",
            status,
            intent,
            answer ?? (accepted && ProducesQueryForTest(intent) ? AcceptedAnswer() : null),
            accepted ? "ok" : string.Empty,
            string.Empty,
            accepted,
            rejectedReason);

        DataAgentOrchestrationCheckpoint checkpoint = new(
            "session-1",
            status,
            "document_index",
            1,
            CanContinue: status is not DataAgentAnalysisSessionStatus.Ended and not DataAgentAnalysisSessionStatus.Rejected,
            CanSummarize: true,
            Terminal: status is DataAgentAnalysisSessionStatus.Ended or DataAgentAnalysisSessionStatus.Rejected);

        return new DataAgentOrchestrationResult(
            "session-1",
            status,
            steps,
            checkpoint,
            response);
    }

    static bool ProducesQueryForTest(DataAgentAnalysisTurnIntent intent)
    {
        return intent is DataAgentAnalysisTurnIntent.NewQuestion
            or DataAgentAnalysisTurnIntent.Continue
            or DataAgentAnalysisTurnIntent.RefinePrevious
            or DataAgentAnalysisTurnIntent.AnswerClarification;
    }

    static DataAgentOrchestrationStep Step(
        DataAgentOrchestrationNodeKind node,
        DataAgentOrchestrationStepStatus status,
        string reason,
        bool executedSql)
    {
        return new DataAgentOrchestrationStep(node, status, reason, executedSql);
    }

    static DataAgentAnswer AcceptedAnswer()
    {
        return new DataAgentAnswer(
            "document_index",
            "SELECT path FROM document_index LIMIT 20",
            2,
            "Found DataAgent documentation.",
            "[data_agent_context]\nsql_status=validated\n[/data_agent_context]",
            true,
            string.Empty,
            new DataAgentPlannerExplanation(
                "TestPlanner",
                "find_documents",
                "document_index",
                "high",
                ["test"],
                "test accepted answer"));
    }

    static DataAgentAnswer RejectedAnswer(string rejectedReason)
    {
        return new DataAgentAnswer(
            "document_index",
            string.Empty,
            0,
            string.Empty,
            "[data_agent_context]\nsql_status=rejected\n[/data_agent_context]",
            false,
            rejectedReason,
            new DataAgentPlannerExplanation(
                "TestPlanner",
                "rejected_query",
                "document_index",
                "low",
                ["test"],
                "test rejected answer"));
    }
}
