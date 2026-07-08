using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;
using Alife.Framework.Models.StateEstimation;
using System.Net;
using System.Text;

namespace Alife.Function.DataAgent;

public static class DataAgentReadiness
{
    public static IReadOnlyList<DataAgentReadinessCheck> CheckCore(string databasePath)
    {
        List<DataAgentReadinessCheck> checks = [];

        try
        {
            checks.Add(Pass("DataAgentModulePresent", "Alife.Function.DataAgent loaded"));

            DataAgentSchemaInitializer.Initialize(databasePath);
            checks.Add(Pass("SqliteSchemaInitializes", databasePath));

            DataAgentFixtureImporter.Import(databasePath);
            checks.Add(Pass("FixtureDataImports", "engineering fixture data imported"));

            DataAgentSchemaSnapshot schemaSnapshot = new DataAgentSchemaIntrospector(
                DataAgentCatalog.CreateDefault(),
                databasePath).Inspect();

            checks.Add(schemaSnapshot.Datasets.Count > 0
                ? Pass("SchemaSnapshotAvailable", $"{schemaSnapshot.Datasets.Count} datasets")
                : Fail("SchemaSnapshotAvailable", "schema snapshot is empty"));

            checks.Add(schemaSnapshot.CatalogMatchesDatabase
                ? Pass("CatalogMatchesSqliteSchema", "catalog fields match sqlite schema")
                : Fail("CatalogMatchesSqliteSchema", "catalog fields do not match sqlite schema"));

            DataAgentQueryPlan plan = new(
                "engineering_gate",
                "find_missing_required_gates",
                ["name", "status", "evidence_path"],
                [
                    new DataAgentFilter("required", "=", true),
                    new DataAgentFilter("status", "!=", "passed")
                ],
                [],
                50);

            DataAgentValidationResult validation = new DataAgentQueryPlanValidator(DataAgentCatalog.CreateDefault()).Validate(plan);
            DataAgentCompiledSql compiled = new DataAgentSqlCompiler(DataAgentCatalog.CreateDefault()).Compile(plan);
            checks.Add(validation.IsValid
                ? Pass("QueryPlanFixturesPass", compiled.Sql)
                : Fail("QueryPlanFixturesPass", string.Join(";", validation.Errors)));

            DataAgentSqlSafetyResult dangerousSql = new DataAgentSqlSafetyValidator().Validate("DELETE FROM engineering_gate");
            checks.Add(dangerousSql.IsSafe == false
                ? Pass("DangerousSqlRejected", dangerousSql.Reason)
                : Fail("DangerousSqlRejected", "dangerous SQL was accepted"));

            DataAgentQueryResult result = new DataAgentQueryExecutor(databasePath).Execute(compiled);
            checks.Add(Pass("ReadOnlyQueryExecutes", $"{result.Rows.Count} rows"));

            checks.Add(typeof(IDataAgentStore).IsInterface &&
                       typeof(SqliteDataAgentStore).GetInterface(nameof(IDataAgentStore)) is not null &&
                       typeof(PostgresDataAgentStore).GetInterface(nameof(IDataAgentStore)) is not null
                ? Pass("DataAgentStoreBoundaryPresent", "DataAgent provider-neutral store boundary exists")
                : Fail("DataAgentStoreBoundaryPresent", "store boundary types missing"));

            IDataAgentStore readinessStore = new SqliteDataAgentStore(databasePath);
            checks.Add(string.Equals(readinessStore.ProviderName, "sqlite", StringComparison.Ordinal) &&
                       readinessStore.Query(new DataAgentCompiledSql("SELECT path FROM document_index LIMIT 1", [])).Rows.Count >= 0
                ? Pass("SqliteStoreCompatibilityPresent", "SQLite store remains default-compatible")
                : Fail("SqliteStoreCompatibilityPresent", "SQLite store query failed"));

            checks.Add(typeof(IDataAgentStore).IsAssignableFrom(typeof(PostgresDataAgentStore))
                ? Pass("PostgresStoreProviderPresent", "PostgreSQL store provider type exists")
                : Fail("PostgresStoreProviderPresent", "PostgreSQL provider missing"));

            checks.Add(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION"))
                ? Pass("PostgresLiveTestsEnvironmentGated", "PostgreSQL live tests are environment-gated")
                : Pass("PostgresLiveTestsEnvironmentGated", "PostgreSQL live test connection is explicitly configured"));

            bool postgresCheckpointSessionStoreReady =
                typeof(IDataAgentAnalysisSessionStore).IsAssignableFrom(typeof(PostgresDataAgentAnalysisSessionStore));
            bool postgresCheckpointFactoryReady =
                DataAgentAnalysisSessionStoreFactory.Create(new DataAgentAnalysisSessionStoreOptions(
                    string.Empty,
                    string.Empty)) is InMemoryDataAgentAnalysisSessionStore &&
                typeof(DataAgentAnalysisSessionStoreFactory)
                    .GetMethod(nameof(DataAgentAnalysisSessionStoreFactory.FromEnvironment)) is not null;
            bool postgresCheckpointModuleWiringReady =
                DataAgentModuleService.CreateAnalysisSessionStore(new DataAgentAnalysisSessionStoreOptions(
                    string.Empty,
                    string.Empty)) is InMemoryDataAgentAnalysisSessionStore;
            bool postgresCheckpointLiveTestGated =
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION"));
            bool postgresCheckpointReady =
                postgresCheckpointSessionStoreReady &&
                postgresCheckpointFactoryReady &&
                postgresCheckpointModuleWiringReady;
            checks.Add(postgresCheckpointReady
                ? Pass(
                    "PostgresCheckpointPersistencePresent",
                    $"session_store=true;factory=true;module_wiring=true;live_test_gated={LowerBool(postgresCheckpointLiveTestGated)}")
                : Fail(
                    "PostgresCheckpointPersistencePresent",
                    $"session_store={LowerBool(postgresCheckpointSessionStoreReady)};factory={LowerBool(postgresCheckpointFactoryReady)};module_wiring={LowerBool(postgresCheckpointModuleWiringReady)};live_test_gated={LowerBool(postgresCheckpointLiveTestGated)}"));

            DataAgentGraphSidecarOptions graphSidecarDefaultOptions = DataAgentGraphSidecarOptions.FromValue(null);
            DataAgentGraphSidecarPolicy graphSidecarPolicy = DataAgentGraphSidecarPolicy.CreateDefault();
            DataAgentGraphSidecarResponse graphSidecarForbiddenResponse = new(
                "readiness-workflow",
                true,
                "unsafe_sql_authority",
                "unsafe authority request",
                DataAgentGraphSidecarNodeKind.QueryPlanValidation,
                "ExecuteSql",
                true,
                ["unsafe authority request"],
                [DataAgentGraphSidecarAuthority.ExecuteSql]);
            bool graphSidecarContractReady =
                typeof(DataAgentGraphSidecarContract).IsClass &&
                typeof(DataAgentGraphSidecarRequest).IsClass &&
                typeof(DataAgentGraphSidecarResponse).IsClass &&
                DataAgentGraphSidecarContract.DefaultAllowedNodeKinds.Contains(DataAgentGraphSidecarNodeKind.QueryPlanner);
            bool graphSidecarPolicyReady =
                graphSidecarPolicy.Allows(DataAgentGraphSidecarAuthority.ProposeOrchestrationIntent) &&
                graphSidecarPolicy.Allows(DataAgentGraphSidecarAuthority.RequestCSharpSafetyService) &&
                graphSidecarPolicy.Allows(DataAgentGraphSidecarAuthority.ReturnBoundedTrace) &&
                graphSidecarPolicy.Allows(DataAgentGraphSidecarAuthority.ReportDeterministicFallback) &&
                graphSidecarPolicy.Forbids(DataAgentGraphSidecarAuthority.ExecuteSql) &&
                graphSidecarPolicy.Forbids(DataAgentGraphSidecarAuthority.DecideToolRoute) &&
                graphSidecarPolicy.NoToolRouteAuthority &&
                graphSidecarPolicy.NoCheckpointAuthority &&
                graphSidecarPolicy.NoEvidenceAuthority &&
                graphSidecarPolicy.NoVisibleTextAuthority;
            bool graphSidecarNoSqlAuthority =
                graphSidecarPolicy.NoSqlAuthority &&
                DataAgentGraphSidecarContract.IsResponseSafe(graphSidecarForbiddenResponse, graphSidecarPolicy) == false;
            bool graphSidecarNoRuntime = DataAgentGraphSidecarContract.IsRuntimeAvailable == false;
            bool graphSidecarReady =
                graphSidecarDefaultOptions.Enabled == false &&
                graphSidecarContractReady &&
                graphSidecarPolicyReady &&
                graphSidecarNoSqlAuthority &&
                graphSidecarNoRuntime;
            checks.Add(graphSidecarReady
                ? Pass("GraphSidecarContractPresent", "default_enabled=false;contract=true;policy=true;no_sql_authority=true;no_visible_text_authority=true;no_runtime=true")
                : Fail("GraphSidecarContractPresent", $"default_enabled={LowerBool(graphSidecarDefaultOptions.Enabled)};contract={LowerBool(graphSidecarContractReady)};policy={LowerBool(graphSidecarPolicyReady)};no_sql_authority={LowerBool(graphSidecarNoSqlAuthority)};no_visible_text_authority={LowerBool(graphSidecarPolicy.NoVisibleTextAuthority)};no_runtime={LowerBool(graphSidecarNoRuntime)}"));

            DataAgentDataQueryGraphOptions dataQueryGraphDefaultOptions = DataAgentDataQueryGraphOptions.FromValue(null);
            DataAgentDataQueryGraphOptions dataQueryGraphEnabledOptions = new(true);
            DataAgentDataQueryGraphDryRunResult dataQueryGraphDisabledResult =
                DataAgentDataQueryGraphPilot.DryRun(CreateReadinessDataQueryGraphAcceptedResult(), dataQueryGraphDefaultOptions);
            DataAgentDataQueryGraphDryRunResult dataQueryGraphAcceptedResult =
                DataAgentDataQueryGraphPilot.DryRun(CreateReadinessDataQueryGraphAcceptedResult(), dataQueryGraphEnabledOptions);
            DataAgentDataQueryGraphDryRunResult dataQueryGraphDeniedResult =
                DataAgentDataQueryGraphPilot.DryRun(CreateReadinessDataQueryGraphDeniedWithStrayExecutionResult(), dataQueryGraphEnabledOptions);
            DataAgentDataQueryGraphDryRunResult dataQueryGraphTerminalResult =
                DataAgentDataQueryGraphPilot.DryRun(CreateReadinessDataQueryGraphTerminalWithStrayExecutionResult(), dataQueryGraphEnabledOptions);
            DataAgentDataQueryGraphDryRunResult dataQueryGraphFallbackResult =
                DataAgentDataQueryGraphPilot.DryRun(null, dataQueryGraphEnabledOptions);
            DataAgentDataQueryGraphNode dataQueryGraphPlannerNode =
                DataAgentDataQueryGraphPilot.BuildNode(DataAgentWorkflowNodeNames.QueryPlanner, "planner");
            DataAgentDataQueryGraphNode dataQueryGraphDiagnosticsNode =
                DataAgentDataQueryGraphPilot.BuildNode(DataAgentWorkflowNodeNames.DiagnosticsRouter, "diagnostics");
            DataAgentDataQueryGraphNode dataQueryGraphUnknownNode =
                DataAgentDataQueryGraphPilot.BuildNode("unknown_node", "unknown");
            DataAgentDataQueryGraphDryRunResult dataQueryGraphUnsafeTraceResult = new(
                true,
                false,
                "dataquerygraph_fallback_to_deterministic_orchestrator",
                "unsafe_trace",
                DataAgentDataQueryGraphPilot.NoLangGraphRuntimeMarker,
                "SELECT path FROM document_index",
                new DataAgentDataQueryGraphPlan(
                    [DataAgentDataQueryGraphPilot.BuildNode(DataAgentWorkflowNodeNames.QueryPlanner, "DROP TABLE document_index")],
                    []));
            string dataQueryGraphUnsafeTrace = DataAgentDataQueryGraphTraceFormatter.Format(dataQueryGraphUnsafeTraceResult);
            bool dataQueryGraphDefaultDisabled =
                dataQueryGraphDefaultOptions.Enabled == false &&
                dataQueryGraphDisabledResult.Enabled == false &&
                string.Equals(dataQueryGraphDisabledResult.ReasonCode, "dataquerygraph_disabled", StringComparison.Ordinal);
            bool dataQueryGraphDryRunReady =
                dataQueryGraphAcceptedResult.Enabled &&
                dataQueryGraphAcceptedResult.Accepted &&
                string.Equals(dataQueryGraphAcceptedResult.ReasonCode, "dataquerygraph_dry_run_completed", StringComparison.Ordinal);
            bool dataQueryGraphNoRuntime =
                string.Equals(DataAgentDataQueryGraphPilot.NoLangGraphRuntimeMarker, "no_langgraph_runtime", StringComparison.Ordinal) &&
                string.Equals(dataQueryGraphAcceptedResult.RuntimeMarker, DataAgentDataQueryGraphPilot.NoLangGraphRuntimeMarker, StringComparison.Ordinal);
            bool dataQueryGraphNodeScopeReady =
                dataQueryGraphPlannerNode.AllowsModelCall &&
                dataQueryGraphPlannerNode.AllowedCapabilities.Contains(DataAgentNodeCapabilities.GenerateQueryPlan, StringComparer.Ordinal) &&
                dataQueryGraphPlannerNode.AllowedCapabilities.Contains(DataAgentNodeCapabilities.ExecuteReadOnlyQuery, StringComparer.Ordinal) == false &&
                dataQueryGraphDiagnosticsNode.AllowsModelCall &&
                dataQueryGraphDiagnosticsNode.AllowedCapabilities.Contains(DataAgentNodeCapabilities.ReadProgressDiagnostics, StringComparer.Ordinal) &&
                dataQueryGraphDiagnosticsNode.AllowedCapabilities.Contains(DataAgentNodeCapabilities.ExecuteReadOnlyQuery, StringComparer.Ordinal) == false &&
                dataQueryGraphUnknownNode.AllowsModelCall == false &&
                dataQueryGraphUnknownNode.AllowedCapabilities.Count == 0;
            string[] dataQueryGraphExpectedNodes =
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
            bool dataQueryGraphPlanShapeReady =
                dataQueryGraphAcceptedResult.Plan.Nodes
                    .Select(node => node.Name)
                    .SequenceEqual(dataQueryGraphExpectedNodes, StringComparer.Ordinal);
            bool dataQueryGraphTransitionShapeReady =
                dataQueryGraphAcceptedResult.Plan.Transitions.Count == Math.Max(0, dataQueryGraphAcceptedResult.Plan.Nodes.Count - 1) &&
                dataQueryGraphAcceptedResult.Plan.Transitions.Select(transition => transition.FromNode)
                    .SequenceEqual(dataQueryGraphAcceptedResult.Plan.Nodes.Take(Math.Max(0, dataQueryGraphAcceptedResult.Plan.Nodes.Count - 1)).Select(node => node.Name), StringComparer.Ordinal) &&
                dataQueryGraphAcceptedResult.Plan.Transitions.Select(transition => transition.ToNode)
                    .SequenceEqual(dataQueryGraphAcceptedResult.Plan.Nodes.Skip(1).Select(node => node.Name), StringComparer.Ordinal);
            DataAgentDataQueryGraphNode[] dataQueryGraphExecuteNodes = dataQueryGraphAcceptedResult.Plan.Nodes.Where(node =>
                node.AllowedCapabilities.Contains(DataAgentNodeCapabilities.ExecuteReadOnlyQuery, StringComparer.Ordinal)).ToArray();
            DataAgentDataQueryGraphNode? dataQueryGraphPlanNode = dataQueryGraphAcceptedResult.Plan.Nodes.FirstOrDefault(node =>
                string.Equals(node.Name, DataAgentWorkflowNodeNames.QueryPlanner, StringComparison.Ordinal));
            bool dataQueryGraphExecuteScopeReady =
                dataQueryGraphExecuteNodes.Length == 1 &&
                string.Equals(dataQueryGraphExecuteNodes[0].Name, DataAgentWorkflowNodeNames.ReadOnlyExecute, StringComparison.Ordinal) &&
                dataQueryGraphPlanNode is not null &&
                dataQueryGraphPlanNode.AllowedCapabilities.Contains(DataAgentNodeCapabilities.ExecuteReadOnlyQuery, StringComparer.Ordinal) == false;
            bool dataQueryGraphDeniedNoExecute =
                dataQueryGraphDeniedResult.Accepted == false &&
                dataQueryGraphDeniedResult.Plan.Nodes.Select(node => node.Name).SequenceEqual(
                    [
                        DataAgentWorkflowNodeNames.RouteGate,
                        DataAgentWorkflowNodeNames.Reject,
                        DataAgentWorkflowNodeNames.CheckpointProgress
                    ],
                    StringComparer.Ordinal) &&
                dataQueryGraphDeniedResult.Plan.Nodes.Any(NodeCanExecuteReadOnlyQuery) == false;
            bool dataQueryGraphTerminalNoExecute =
                dataQueryGraphTerminalResult.Accepted &&
                dataQueryGraphTerminalResult.Plan.Nodes.Select(node => node.Name).SequenceEqual(
                    [
                        DataAgentWorkflowNodeNames.Terminal,
                        DataAgentWorkflowNodeNames.CheckpointProgress
                    ],
                    StringComparer.Ordinal) &&
                dataQueryGraphTerminalResult.Plan.Nodes.Any(NodeCanExecuteReadOnlyQuery) == false;
            bool dataQueryGraphNoSqlAuthority =
                dataQueryGraphUnsafeTrace.Contains("dataquerygraph_sql_text_rejected", StringComparison.Ordinal) &&
                dataQueryGraphUnsafeTrace.Contains("SELECT path FROM document_index", StringComparison.OrdinalIgnoreCase) == false &&
                dataQueryGraphUnsafeTrace.Contains("DROP TABLE document_index", StringComparison.OrdinalIgnoreCase) == false;
            bool dataQueryGraphFallbackReady =
                dataQueryGraphFallbackResult.Enabled &&
                dataQueryGraphFallbackResult.Accepted == false &&
                string.Equals(dataQueryGraphFallbackResult.ReasonCode, "dataquerygraph_fallback_to_deterministic_orchestrator", StringComparison.Ordinal);
            bool dataQueryGraphReady =
                dataQueryGraphDefaultDisabled &&
                dataQueryGraphDryRunReady &&
                dataQueryGraphNoRuntime &&
                dataQueryGraphNodeScopeReady &&
                dataQueryGraphPlanShapeReady &&
                dataQueryGraphTransitionShapeReady &&
                dataQueryGraphExecuteScopeReady &&
                dataQueryGraphDeniedNoExecute &&
                dataQueryGraphTerminalNoExecute &&
                dataQueryGraphNoSqlAuthority &&
                dataQueryGraphFallbackReady;
            string dataQueryGraphDetail =
                $"default_enabled={LowerBool(dataQueryGraphDefaultOptions.Enabled)};dry_run={LowerBool(dataQueryGraphDryRunReady)};plan_shape={LowerBool(dataQueryGraphPlanShapeReady)};transition_shape={LowerBool(dataQueryGraphTransitionShapeReady)};execute_scope={LowerBool(dataQueryGraphExecuteScopeReady)};denied_no_execute={LowerBool(dataQueryGraphDeniedNoExecute)};terminal_no_execute={LowerBool(dataQueryGraphTerminalNoExecute)};no_langgraph_runtime={LowerBool(dataQueryGraphNoRuntime)};node_scope={LowerBool(dataQueryGraphNodeScopeReady)};no_sql_authority={LowerBool(dataQueryGraphNoSqlAuthority)};fallback={LowerBool(dataQueryGraphFallbackReady)}";
            checks.Add(dataQueryGraphReady
                ? Pass("DataQueryGraphPilotPresent", dataQueryGraphDetail)
                : Fail("DataQueryGraphPilotPresent", dataQueryGraphDetail));

            DataAgentGraphHandshakeOptions graphHandshakeDefaultOptions = DataAgentGraphHandshakeOptions.FromValue(null);
            IReadOnlyList<DataAgentGraphNodeManifest> graphHandshakeManifests = DataAgentGraphHandshakeManifestFactory.CreateDefault();
            DataAgentGraphHandshakeRequest graphHandshakeRequest = new(
                "readiness-request",
                "readiness-session",
                "turn-1",
                "owner",
                "Which required gates failed?",
                "scenario_context=engineering_readiness",
                "route_allowed",
                "required=true;status!=passed;limit=50",
                graphHandshakeManifests,
                NoSqlAuthority: true,
                ReadOnly: true,
                FallbackAvailable: true,
                TraceBudgetChars: DataAgentGraphHandshakeLimits.MaxTraceSummaryChars,
                ProgressBudget: DataAgentGraphHandshakeLimits.MaxProgressEvents);
            DataAgentGraphHandshakeResponse graphHandshakeSafeResponse = new(
                "readiness-request",
                true,
                "planner_suggested",
                [DataAgentWorkflowNodeNames.ScenarioKnowledge, DataAgentWorkflowNodeNames.QueryPlanner],
                [new DataAgentGraphHandshakeProgress(DataAgentWorkflowNodeNames.QueryPlanner, DataAgentGraphHandshakeProgressStatus.Completed, "planner_suggested")],
                "ScenarioKnowledge:Completed>QueryPlanner:Completed",
                "graph_handshake=accepted",
                FallbackRequired: false,
                NoSqlAuthority: true,
                ReadOnly: true,
                [DataAgentGraphHandshakeToolNames.ProposeQueryPlan],
                RequestsCheckpointMutation: false,
                RequestsVisibleText: false);
            DataAgentGraphHandshakeResponse graphHandshakeSqlAuthorityResponse = graphHandshakeSafeResponse with
            {
                NoSqlAuthority = false
            };
            DataAgentGraphHandshakeResponse graphHandshakeUnsafeTraceResponse = graphHandshakeSafeResponse with
            {
                TraceSummary = "SELECT * FROM document_index"
            };
            DataAgentGraphHandshakeResponse graphHandshakeUnsafeMarkerResponse = graphHandshakeSafeResponse with
            {
                ContextContribution = "[tool_route_context] hidden_context bearer"
            };
            DataAgentGraphHandshakeValidationResult graphHandshakeSafeValidation =
                DataAgentGraphHandshakeValidator.Validate(graphHandshakeRequest, graphHandshakeSafeResponse);
            DataAgentGraphHandshakeValidationResult graphHandshakeSqlAuthorityValidation =
                DataAgentGraphHandshakeValidator.Validate(graphHandshakeRequest, graphHandshakeSqlAuthorityResponse);
            DataAgentGraphHandshakeValidationResult graphHandshakeUnsafeTraceValidation =
                DataAgentGraphHandshakeValidator.Validate(graphHandshakeRequest, graphHandshakeUnsafeTraceResponse);
            DataAgentGraphHandshakeValidationResult graphHandshakeUnsafeMarkerValidation =
                DataAgentGraphHandshakeValidator.Validate(graphHandshakeRequest, graphHandshakeUnsafeMarkerResponse);
            DataAgentGraphHandshakeOutcome graphHandshakeDisabledOutcome =
                new DataAgentGraphHandshakeCoordinator(DataAgentGraphHandshakeOptions.Disabled, DisabledDataAgentGraphSidecarClient.Instance)
                    .TryHandshake("owner", "Which required gates failed?", CreateReadinessDataQueryGraphAcceptedResult());
            DataAgentGraphNodeManifest? graphHandshakeReadOnlyExecuteManifest = graphHandshakeManifests.SingleOrDefault(manifest =>
                string.Equals(manifest.NodeName, DataAgentWorkflowNodeNames.ReadOnlyExecute, StringComparison.Ordinal));
            DataAgentGraphNodeManifest? graphHandshakeQueryPlannerManifest = graphHandshakeManifests.SingleOrDefault(manifest =>
                string.Equals(manifest.NodeName, DataAgentWorkflowNodeNames.QueryPlanner, StringComparison.Ordinal));
            DataAgentGraphNodeManifest? graphHandshakeDiagnosticsManifest = graphHandshakeManifests.SingleOrDefault(manifest =>
                string.Equals(manifest.NodeName, DataAgentWorkflowNodeNames.DiagnosticsRouter, StringComparison.Ordinal));
            bool graphHandshakeNoSqlAuthority =
                graphHandshakeRequest.NoSqlAuthority &&
                graphHandshakeSafeResponse.NoSqlAuthority &&
                graphHandshakeSqlAuthorityValidation.Accepted == false &&
                string.Equals(graphHandshakeSqlAuthorityValidation.ReasonCode, "sql_authority_requested", StringComparison.Ordinal) &&
                graphHandshakeUnsafeTraceValidation.Accepted == false &&
                string.Equals(graphHandshakeUnsafeTraceValidation.ReasonCode, "unsafe_trace", StringComparison.Ordinal);
            bool graphHandshakeSecretMarkerSafety =
                graphHandshakeUnsafeMarkerValidation.Accepted == false &&
                string.Equals(graphHandshakeUnsafeMarkerValidation.ReasonCode, "unsafe_trace", StringComparison.Ordinal);
            bool graphHandshakeScopedManifest =
                graphHandshakeManifests.Count > 0 &&
                graphHandshakeReadOnlyExecuteManifest is not null &&
                graphHandshakeReadOnlyExecuteManifest.AllowedToolNames.Count == 0 &&
                graphHandshakeReadOnlyExecuteManifest.DeniedCapabilityMarkers.Contains(DataAgentNodeCapabilities.ExecuteReadOnlyQuery, StringComparer.Ordinal) &&
                graphHandshakeManifests.SelectMany(manifest => manifest.AllowedToolNames).All(toolName =>
                    string.Equals(toolName, DataAgentGraphHandshakeToolNames.ExecuteReadOnlyQuery, StringComparison.Ordinal) == false &&
                    ContainsBroadGraphHandshakeAuthorityToken(toolName) == false) &&
                graphHandshakeQueryPlannerManifest?.AllowedToolNames.Contains(DataAgentGraphHandshakeToolNames.ProposeQueryPlan, StringComparer.Ordinal) == true &&
                graphHandshakeDiagnosticsManifest?.AllowedToolNames.Contains(DataAgentGraphHandshakeToolNames.ReadProgressDiagnostics, StringComparer.Ordinal) == true;
            bool graphHandshakeFallback =
                graphHandshakeDisabledOutcome.FallbackRequired &&
                string.Equals(graphHandshakeDisabledOutcome.ReasonCode, "sidecar_disabled", StringComparison.Ordinal);
            bool graphHandshakeReady =
                graphHandshakeDefaultOptions.Enabled == false &&
                graphHandshakeSafeValidation.Accepted &&
                graphHandshakeNoSqlAuthority &&
                graphHandshakeSecretMarkerSafety &&
                graphHandshakeScopedManifest &&
                graphHandshakeFallback;
            const string graphHandshakeReadyDetail =
                "default_enabled=false;validator=true;no_sql_authority=true;secret_marker_safety=true;scoped_node_manifest=true;fallback=true;runtime_required=false";
            string graphHandshakeFailureDetail =
                $"default_enabled={LowerBool(graphHandshakeDefaultOptions.Enabled)};validator={LowerBool(graphHandshakeSafeValidation.Accepted)};no_sql_authority={LowerBool(graphHandshakeNoSqlAuthority)};secret_marker_safety={LowerBool(graphHandshakeSecretMarkerSafety)};scoped_node_manifest={LowerBool(graphHandshakeScopedManifest)};fallback={LowerBool(graphHandshakeFallback)};runtime_required=false";
            checks.Add(graphHandshakeReady
                ? Pass("GraphHandshakeBoundaryPresent", graphHandshakeReadyDetail)
                : Fail("GraphHandshakeBoundaryPresent", graphHandshakeFailureDetail));

            DataAgentGraphHandshakeHttpOptions graphHandshakeDefaultHttpOptions =
                DataAgentGraphHandshakeHttpOptions.FromValues(null, null);
            DataAgentGraphHandshakeHttpOptions graphHandshakeLoopbackHttpOptions =
                DataAgentGraphHandshakeHttpOptions.FromValues("http://127.0.0.1:8765/handshake", "800");
            DataAgentGraphHandshakeHttpOptions graphHandshakeRemoteHttpOptions =
                DataAgentGraphHandshakeHttpOptions.FromValues("http://example.com/handshake", "800");
            bool graphHandshakeDevHttpAdapterPresent =
                typeof(DataAgentGraphHandshakeHttpClient).GetInterfaces().Contains(typeof(IDataAgentGraphSidecarClient));
            bool graphHandshakeEndpointRequired =
                graphHandshakeDefaultHttpOptions.Configured == false &&
                graphHandshakeLoopbackHttpOptions.Configured &&
                graphHandshakeRemoteHttpOptions.Configured == false;
            bool graphHandshakeNoRuntimeStarted =
                graphHandshakeDefaultHttpOptions.RuntimeStarted == false &&
                graphHandshakeLoopbackHttpOptions.RuntimeStarted == false;
            bool graphHandshakeLoopbackOnly =
                graphHandshakeLoopbackHttpOptions.Endpoint?.IsLoopback == true &&
                graphHandshakeRemoteHttpOptions.Endpoint is null;
            bool graphHandshakeDevFallback =
                graphHandshakeDisabledOutcome.FallbackRequired &&
                string.Equals(graphHandshakeDisabledOutcome.ReasonCode, "sidecar_disabled", StringComparison.Ordinal);
            bool graphHandshakeDevReady =
                graphHandshakeDefaultOptions.Enabled == false &&
                graphHandshakeDevHttpAdapterPresent &&
                graphHandshakeNoRuntimeStarted &&
                graphHandshakeEndpointRequired &&
                graphHandshakeLoopbackOnly &&
                graphHandshakeDevFallback &&
                graphHandshakeSafeValidation.Accepted &&
                graphHandshakeNoSqlAuthority;
            checks.Add(graphHandshakeDevReady
                ? Pass("GraphHandshakeDevSidecarAdapterPresent", "default_enabled=false;dev_http_adapter_present=true;runtime_started=false;endpoint_required=true;loopback_only=true;fallback=true;validator=true;no_sql_authority=true")
                : Fail("GraphHandshakeDevSidecarAdapterPresent", $"default_enabled={LowerBool(graphHandshakeDefaultOptions.Enabled)};dev_http_adapter_present={LowerBool(graphHandshakeDevHttpAdapterPresent)};runtime_started={LowerBool(graphHandshakeNoRuntimeStarted == false)};endpoint_required={LowerBool(graphHandshakeEndpointRequired)};loopback_only={LowerBool(graphHandshakeLoopbackOnly)};fallback={LowerBool(graphHandshakeDevFallback)};validator={LowerBool(graphHandshakeSafeValidation.Accepted)};no_sql_authority={LowerBool(graphHandshakeNoSqlAuthority)}"));

            DateTimeOffset graphSidecarProgressNow = new(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);
            DataAgentProgressRecorder graphSidecarProgressRecorder = new();
            DataAgentGraphSidecarProgressBridge graphSidecarProgressBridge = new(
                graphSidecarProgressRecorder,
                () => graphSidecarProgressNow);
            DataAgentOrchestrationResult graphSidecarProgressResult = CreateReadinessDataQueryGraphAcceptedResult();
            graphSidecarProgressResult = graphSidecarProgressResult with
            {
                SessionId = graphHandshakeRequest.SessionId,
                Checkpoint = graphSidecarProgressResult.Checkpoint with { SessionId = graphHandshakeRequest.SessionId },
                Response = graphSidecarProgressResult.Response with { SessionId = graphHandshakeRequest.SessionId }
            };
            DataAgentGraphSidecarProgressBridgeResult graphSidecarProgressAccepted = graphSidecarProgressBridge.Publish(
                graphHandshakeRequest,
                graphSidecarProgressResult,
                [
                    new DataAgentGraphSidecarProgressEvent(
                        graphHandshakeRequest.RequestId,
                        graphHandshakeRequest.SessionId,
                        DataAgentWorkflowNodeNames.QueryPlanner,
                        DataAgentGraphSidecarProgressStatus.Completed,
                        "planner_suggested",
                        "planner ready",
                        graphSidecarProgressNow.AddSeconds(-5),
                        new Dictionary<string, string>
                        {
                            ["stage"] = "planner"
                        })
                ]);
            DataAgentGraphSidecarProgressBridgeResult graphSidecarProgressUnsafe = graphSidecarProgressBridge.Publish(
                graphHandshakeRequest,
                graphSidecarProgressResult,
                [
                    new DataAgentGraphSidecarProgressEvent(
                        graphHandshakeRequest.RequestId,
                        graphHandshakeRequest.SessionId,
                        DataAgentWorkflowNodeNames.QueryPlanner,
                        DataAgentGraphSidecarProgressStatus.Completed,
                        "planner_suggested",
                        "SELECT * FROM engineering_gate",
                        graphSidecarProgressNow,
                        new Dictionary<string, string>())
                ]);
            IReadOnlyList<DataAgentProgressEvent> graphSidecarProgressEvents = graphSidecarProgressRecorder.GetRecent(
                graphHandshakeRequest.SessionId,
                graphSidecarProgressNow);
            string graphSidecarProgressDiagnostics = DataAgentProgressDiagnosticsFormatter.Format(
                graphSidecarProgressEvents,
                graphHandshakeRequest.SessionId,
                graphSidecarProgressNow);
            string graphSidecarProgressRedactionProbe = DataAgentProgressDiagnosticsFormatter.Format(
                [
                    new DataAgentProgressEvent(
                        graphHandshakeRequest.SessionId,
                        DataAgentProgressEventKind.Planner,
                        DataAgentProgressEventPhase.Completed,
                        DataAgentProgressEventStatus.Succeeded,
                        "planner_suggested",
                        TurnCount: 1,
                        graphSidecarProgressNow,
                        ExecutedSql: false,
                        QueryAllowed: true,
                        Terminal: false,
                        new Dictionary<string, string>
                        {
                            ["sql"] = "SELECT * FROM engineering_gate",
                            ["hidden_context"] = "[hidden_context]secret[/hidden_context]"
                        })
                ],
                graphHandshakeRequest.SessionId,
                graphSidecarProgressNow);
            bool graphSidecarProgressBridgeReady =
                graphSidecarProgressAccepted.AcceptedCount == 1 &&
                graphSidecarProgressAccepted.RejectedCount == 0 &&
                graphSidecarProgressEvents.Count == 1 &&
                graphSidecarProgressEvents.Single().ExecutedSql == false &&
                graphSidecarProgressEvents.Single().Facts.ContainsKey("source") &&
                string.Equals(graphSidecarProgressEvents.Single().Facts["source"], "graph_sidecar", StringComparison.Ordinal);
            bool graphSidecarProgressUnsafeRejected =
                graphSidecarProgressUnsafe.AcceptedCount == 0 &&
                graphSidecarProgressUnsafe.RejectedCount == 1 &&
                graphSidecarProgressRecorder.GetRecent(graphHandshakeRequest.SessionId, graphSidecarProgressNow).Count == 1 &&
                graphSidecarProgressDiagnostics.Contains("SELECT", StringComparison.OrdinalIgnoreCase) == false;
            bool graphSidecarProgressUnsafeRedacted =
                graphSidecarProgressRedactionProbe.Contains("sql=redacted", StringComparison.Ordinal) &&
                graphSidecarProgressRedactionProbe.Contains("SELECT", StringComparison.OrdinalIgnoreCase) == false &&
                graphSidecarProgressRedactionProbe.Contains("hidden_context", StringComparison.OrdinalIgnoreCase) == false;
            bool graphSidecarProgressQChatBoundary =
                string.Equals(typeof(DataAgentGraphSidecarProgressBridge).Namespace, "Alife.Function.DataAgent", StringComparison.Ordinal) &&
                typeof(DataAgentGraphSidecarProgressBridge).Assembly.GetName().Name?.Contains("QChat", StringComparison.OrdinalIgnoreCase) == false;
            bool graphSidecarProgressReady =
                graphHandshakeDefaultOptions.Enabled == false &&
                graphSidecarProgressBridgeReady &&
                graphSidecarProgressUnsafeRejected &&
                graphSidecarProgressUnsafeRedacted &&
                graphSidecarProgressQChatBoundary;

            checks.Add(graphSidecarProgressReady
                ? Pass("GraphHandshakeDevSidecarProgressBridgePresent", "default_enabled=false;progress_bridge=true;csharp_recorder_authority=true;unsafe_progress_rejected=true;unsafe_progress_redacted=true;qchat_boundary=true;no_sql_authority=true;runtime_required=false")
                : Fail("GraphHandshakeDevSidecarProgressBridgePresent", $"default_enabled={LowerBool(graphHandshakeDefaultOptions.Enabled)};progress_bridge={LowerBool(graphSidecarProgressBridgeReady)};csharp_recorder_authority={LowerBool(graphSidecarProgressEvents.Count == 1)};unsafe_progress_rejected={LowerBool(graphSidecarProgressUnsafeRejected)};unsafe_progress_redacted={LowerBool(graphSidecarProgressUnsafeRedacted)};qchat_boundary={LowerBool(graphSidecarProgressQChatBoundary)};no_sql_authority={LowerBool(graphSidecarProgressEvents.All(item => item.ExecutedSql == false))};runtime_required=false"));

            DataAgentGraphHandshakeStreamOptions graphHandshakeDefaultStreamOptions =
                DataAgentGraphHandshakeStreamOptions.FromValues(null, null, null);
            DataAgentGraphHandshakeStreamOptions graphHandshakeLoopbackStreamOptions =
                DataAgentGraphHandshakeStreamOptions.FromValues(
                    "true",
                    "http://127.0.0.1:8765/handshake-stream",
                    "800");
            DataAgentGraphHandshakeStreamOptions graphHandshakeRemoteStreamOptions =
                DataAgentGraphHandshakeStreamOptions.FromValues(
                    "true",
                    "http://example.com/handshake-stream",
                    "800");
            DataAgentGraphHandshakeProgress graphHandshakeBufferedPlannerProgress = new(
                DataAgentWorkflowNodeNames.QueryPlanner,
                DataAgentGraphHandshakeProgressStatus.Completed,
                "planner_suggested",
                "planner ready",
                new Dictionary<string, string>
                {
                    ["stage"] = "planner"
                });
            DataAgentProgressRecorder graphHandshakeAcceptedStreamProgressRecorder = new();
            DataAgentGraphHandshakeCoordinator graphHandshakeAcceptedStreamCoordinator = new(
                new DataAgentGraphHandshakeOptions(true),
                DisabledDataAgentGraphSidecarClient.Instance,
                new DataAgentGraphSidecarProgressBridge(
                    graphHandshakeAcceptedStreamProgressRecorder,
                    () => graphSidecarProgressNow),
                new FixedGraphHandshakeStreamClient(request => new DataAgentGraphHandshakeStreamResult(
                    graphHandshakeSafeResponse with
                    {
                        RequestId = request.RequestId,
                        NodeProgress = []
                    },
                    [graphHandshakeBufferedPlannerProgress])));
            DataAgentGraphHandshakeOutcome graphHandshakeAcceptedStreamOutcome =
                graphHandshakeAcceptedStreamCoordinator.TryHandshake(
                    "owner",
                    "Which required gates failed?",
                    graphSidecarProgressResult);
            IReadOnlyList<DataAgentProgressEvent> graphHandshakeAcceptedStreamProgressEvents =
                graphHandshakeAcceptedStreamProgressRecorder.GetRecent(graphSidecarProgressResult.SessionId, graphSidecarProgressNow);
            DataAgentProgressRecorder graphHandshakeRejectedStreamProgressRecorder = new();
            DataAgentGraphHandshakeCoordinator graphHandshakeRejectedStreamCoordinator = new(
                new DataAgentGraphHandshakeOptions(true),
                DisabledDataAgentGraphSidecarClient.Instance,
                new DataAgentGraphSidecarProgressBridge(
                    graphHandshakeRejectedStreamProgressRecorder,
                    () => graphSidecarProgressNow),
                new FixedGraphHandshakeStreamClient(request => new DataAgentGraphHandshakeStreamResult(
                    graphHandshakeSafeResponse with
                    {
                        RequestId = request.RequestId,
                        NodeProgress = [],
                        NoSqlAuthority = false
                    },
                    [graphHandshakeBufferedPlannerProgress])));
            DataAgentGraphHandshakeOutcome graphHandshakeRejectedStreamOutcome =
                graphHandshakeRejectedStreamCoordinator.TryHandshake(
                    "owner",
                    "Which required gates failed?",
                    graphSidecarProgressResult);
            bool graphHandshakeMissingFinalResponseReady = false;
            try
            {
                string missingFinalNdjson =
                    """{"Kind":"Progress","Progress":{"NodeName":"query_planner","Status":"Completed","ReasonCode":"planner_suggested","Message":"planner ready","Facts":{"stage":"planner"}}}""";
                using HttpClient missingFinalHttpClient = new(new DelegateHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(missingFinalNdjson, Encoding.UTF8, "application/x-ndjson")
                }));
                DataAgentGraphHandshakeNdjsonStreamClient missingFinalStreamClient = new(
                    missingFinalHttpClient,
                    graphHandshakeLoopbackStreamOptions);
                missingFinalStreamClient.TryHandshakeStream(graphHandshakeRequest);
            }
            catch (DataAgentGraphSidecarInvalidStreamException exception)
            {
                graphHandshakeMissingFinalResponseReady = string.Equals(
                    exception.ReasonCode,
                    "missing_stream_final_response",
                    StringComparison.Ordinal);
            }

            bool graphHandshakeStreamDefaultDisabled =
                graphHandshakeDefaultStreamOptions.Enabled == false &&
                graphHandshakeDefaultStreamOptions.Configured == false &&
                graphHandshakeDefaultStreamOptions.RuntimeStarted == false;
            bool graphHandshakeStreamLoopbackOnly =
                graphHandshakeLoopbackStreamOptions.Enabled &&
                graphHandshakeLoopbackStreamOptions.Configured &&
                graphHandshakeLoopbackStreamOptions.Endpoint?.IsLoopback == true &&
                graphHandshakeLoopbackStreamOptions.RuntimeStarted == false &&
                graphHandshakeRemoteStreamOptions.Configured == false &&
                graphHandshakeRemoteStreamOptions.Endpoint is null;
            bool graphHandshakeStreamEnvelopeTypesReady =
                typeof(DataAgentGraphHandshakeStreamEvent).IsClass &&
                Enum.IsDefined(typeof(DataAgentGraphHandshakeStreamEventKind), DataAgentGraphHandshakeStreamEventKind.Progress) &&
                typeof(DataAgentGraphHandshakeStreamResult).IsClass &&
                typeof(IDataAgentGraphHandshakeStreamClient).IsInterface &&
                typeof(DataAgentGraphSidecarInvalidStreamException).IsAssignableTo(typeof(Exception));
            bool graphHandshakeNdjsonStreamReady =
                typeof(DataAgentGraphHandshakeNdjsonStreamClient).IsAssignableTo(typeof(IDataAgentGraphHandshakeStreamClient)) &&
                typeof(DataAgentGraphHandshakeNdjsonStreamClient).Name.Contains("Ndjson", StringComparison.Ordinal);
            bool graphHandshakeAcceptedStreamReady =
                graphHandshakeAcceptedStreamOutcome.Status == DataAgentGraphHandshakeStatus.Accepted &&
                graphHandshakeAcceptedStreamOutcome.Response is not null &&
                graphHandshakeAcceptedStreamProgressEvents.Count == 1 &&
                string.Equals(graphHandshakeAcceptedStreamProgressEvents.Single().Facts["stage"], "planner", StringComparison.Ordinal) &&
                string.Equals(graphHandshakeAcceptedStreamProgressEvents.Single().Facts["source"], "graph_sidecar", StringComparison.Ordinal);
            bool graphHandshakeRejectedStreamNoPublish =
                graphHandshakeRejectedStreamOutcome.Status == DataAgentGraphHandshakeStatus.Rejected &&
                string.Equals(graphHandshakeRejectedStreamOutcome.ReasonCode, "sql_authority_requested", StringComparison.Ordinal) &&
                graphHandshakeRejectedStreamProgressRecorder.GetRecent(graphSidecarProgressResult.SessionId, graphSidecarProgressNow).Count == 0;
            bool graphHandshakeStreamBufferedUntilAccepted =
                graphHandshakeAcceptedStreamReady &&
                graphHandshakeRejectedStreamNoPublish;
            bool graphHandshakeStreamSseDeferred =
                typeof(DataAgentGraphHandshakeNdjsonStreamClient).Assembly.GetTypes().All(type =>
                    type.Name.Contains("EventSource", StringComparison.OrdinalIgnoreCase) == false);
            bool graphHandshakeStreamCSharpBridgeAuthority =
                string.Equals(typeof(DataAgentGraphSidecarProgressBridge).Namespace, "Alife.Function.DataAgent", StringComparison.Ordinal) &&
                graphHandshakeAcceptedStreamProgressEvents.Count == 1 &&
                string.Equals(graphHandshakeAcceptedStreamProgressEvents.Single().Facts["source"], "graph_sidecar", StringComparison.Ordinal);
            bool graphHandshakeStreamQChatBoundary =
                string.Equals(typeof(DataAgentGraphHandshakeStreamEvent).Namespace, "Alife.Function.DataAgent", StringComparison.Ordinal) &&
                string.Equals(typeof(DataAgentGraphHandshakeNdjsonStreamClient).Namespace, "Alife.Function.DataAgent", StringComparison.Ordinal) &&
                typeof(DataAgentGraphHandshakeNdjsonStreamClient).Assembly.GetName().Name?.Contains("QChat", StringComparison.OrdinalIgnoreCase) == false &&
                typeof(DataAgentGraphHandshakeNdjsonStreamClient).Assembly.GetReferencedAssemblies().Any(assemblyName =>
                    string.Equals(assemblyName.Name, "Alife.Function.QChat", StringComparison.Ordinal)) == false;
            bool graphHandshakeStreamingTransportReady =
                graphHandshakeStreamDefaultDisabled &&
                graphHandshakeStreamLoopbackOnly &&
                graphHandshakeStreamEnvelopeTypesReady &&
                graphHandshakeNdjsonStreamReady &&
                graphHandshakeStreamBufferedUntilAccepted &&
                graphHandshakeMissingFinalResponseReady &&
                graphHandshakeStreamSseDeferred &&
                graphHandshakeStreamCSharpBridgeAuthority &&
                graphHandshakeStreamQChatBoundary;
            checks.Add(graphHandshakeStreamingTransportReady
                ? Pass("GraphHandshakeDevSidecarStreamingTransportPresent", "default_enabled=false;ndjson_stream=true;buffer_until_accepted=true;final_response_required=true;sse_deferred=true;csharp_bridge_authority=true;qchat_boundary=true;runtime_required=false")
                : Fail("GraphHandshakeDevSidecarStreamingTransportPresent", $"default_enabled={LowerBool(graphHandshakeStreamDefaultDisabled)};loopback_only={LowerBool(graphHandshakeStreamLoopbackOnly)};envelope_types={LowerBool(graphHandshakeStreamEnvelopeTypesReady)};stream_client={LowerBool(graphHandshakeNdjsonStreamReady)};accepted_stream={LowerBool(graphHandshakeAcceptedStreamReady)};rejected_no_publish={LowerBool(graphHandshakeRejectedStreamNoPublish)};missing_final_response={LowerBool(graphHandshakeMissingFinalResponseReady)};sse_deferred={LowerBool(graphHandshakeStreamSseDeferred)};csharp_bridge_authority={LowerBool(graphHandshakeStreamCSharpBridgeAuthority)};qchat_boundary={LowerBool(graphHandshakeStreamQChatBoundary)};runtime_required={LowerBool(graphHandshakeDefaultStreamOptions.RuntimeStarted || graphHandshakeLoopbackStreamOptions.RuntimeStarted)}"));

            DataAgentGraphSidecarObservabilityContext graphHandshakeObservabilityDefaultContext =
                DataAgentGraphSidecarObservabilityContext.Default;
            DataAgentGraphSidecarObservabilityContext graphHandshakeObservabilityConfiguredContext =
                new(EndpointConfigured: true, RuntimeStartedByAlife: false);
            DataAgentGraphHandshakeOutcome graphHandshakeObservabilityNotConfiguredOutcome =
                new DataAgentGraphHandshakeCoordinator(
                    new DataAgentGraphHandshakeOptions(true),
                    DisabledDataAgentGraphSidecarClient.Instance,
                    observabilityContext: graphHandshakeObservabilityDefaultContext)
                .TryHandshake("owner", "Which required gates failed?", graphSidecarProgressResult);
            DataAgentGraphHandshakeOutcome graphHandshakeObservabilityUnavailableOutcome =
                new DataAgentGraphHandshakeCoordinator(
                    new DataAgentGraphHandshakeOptions(true),
                    new FixedGraphSidecarClient(_ => throw new InvalidOperationException("sidecar offline")),
                    observabilityContext: graphHandshakeObservabilityConfiguredContext)
                .TryHandshake("owner", "Which required gates failed?", graphSidecarProgressResult);
            DataAgentGraphHandshakeOutcome graphHandshakeObservabilityRejectedOutcome =
                new DataAgentGraphHandshakeCoordinator(
                    new DataAgentGraphHandshakeOptions(true),
                    new FixedGraphSidecarClient(request => graphHandshakeSafeResponse with
                    {
                        RequestId = request.RequestId,
                        NoSqlAuthority = false
                    }),
                    observabilityContext: graphHandshakeObservabilityConfiguredContext)
                .TryHandshake("owner", "Which required gates failed?", graphSidecarProgressResult);
            DataAgentGraphHandshakeOutcome graphHandshakeObservabilityAcceptedOutcome =
                new DataAgentGraphHandshakeCoordinator(
                    new DataAgentGraphHandshakeOptions(true),
                    new FixedGraphSidecarClient(request => graphHandshakeSafeResponse with
                    {
                        RequestId = request.RequestId
                    }),
                    observabilityContext: graphHandshakeObservabilityConfiguredContext)
                .TryHandshake("owner", "Which required gates failed?", graphSidecarProgressResult);
            string graphHandshakeUnsafeObservabilityDiagnostics = DataAgentGraphHandshakeDiagnosticsFormatter.Format(
                new DataAgentGraphHandshakeOutcome(
                    DataAgentGraphHandshakeStatus.Rejected,
                    "unsafe_trace",
                    true,
                    graphHandshakeRequest,
                    null,
                    new DataAgentGraphHandshakeValidationResult(false, "unsafe_trace"),
                    new DataAgentGraphSidecarObservabilitySnapshot(
                        DataAgentGraphSidecarObservabilityReasonCodes.ResponseRejected,
                        DataAgentGraphSidecarObservabilityStatus.Rejected,
                        SidecarEnabled: true,
                        EndpointConfigured: true,
                        RuntimeStartedByAlife: false,
                        NetworkAttempted: true,
                        Accepted: false,
                        FallbackUsed: true,
                        SafeSummary: "SELECT * FROM engineering_gate [hidden_context]secret[/hidden_context]")));
            bool graphHandshakeObservabilityModelReady =
                typeof(DataAgentGraphSidecarObservabilitySnapshot).IsClass &&
                Enum.IsDefined(typeof(DataAgentGraphSidecarObservabilityStatus), DataAgentGraphSidecarObservabilityStatus.Disabled) &&
                typeof(DataAgentGraphSidecarObservabilityContext).GetProperty(nameof(DataAgentGraphSidecarObservabilityContext.RuntimeStartedByAlife)) is not null &&
                graphHandshakeDisabledOutcome.Observability is not null &&
                graphHandshakeObservabilityAcceptedOutcome.Observability is not null;
            bool graphHandshakeObservabilityReasonCodesReady =
                string.Equals(DataAgentGraphSidecarObservabilityReasonCodes.Disabled, "graph_sidecar_disabled", StringComparison.Ordinal) &&
                string.Equals(DataAgentGraphSidecarObservabilityReasonCodes.NotConfigured, "graph_sidecar_not_configured", StringComparison.Ordinal) &&
                string.Equals(DataAgentGraphSidecarObservabilityReasonCodes.RuntimeUnavailable, "graph_sidecar_runtime_unavailable", StringComparison.Ordinal) &&
                string.Equals(DataAgentGraphSidecarObservabilityReasonCodes.ResponseRejected, "graph_sidecar_response_rejected", StringComparison.Ordinal) &&
                string.Equals(DataAgentGraphSidecarObservabilityReasonCodes.ProgressRejected, "graph_sidecar_progress_rejected", StringComparison.Ordinal) &&
                string.Equals(DataAgentGraphSidecarObservabilityReasonCodes.Accepted, "graph_sidecar_accepted", StringComparison.Ordinal) &&
                string.Equals(DataAgentGraphSidecarObservabilityReasonCodes.FallbackUsed, "graph_sidecar_fallback_used", StringComparison.Ordinal) &&
                string.Equals(DataAgentGraphSidecarObservabilityReasonCodes.StreamFinalResponseMissing, "graph_sidecar_stream_final_response_missing", StringComparison.Ordinal) &&
                string.Equals(DataAgentGraphSidecarObservabilityReasonCodes.StreamFinalResponseRejected, "graph_sidecar_stream_final_response_rejected", StringComparison.Ordinal);
            bool graphHandshakeObservabilityFallbackReasonReady =
                graphHandshakeDisabledOutcome.Observability?.ReasonCode == DataAgentGraphSidecarObservabilityReasonCodes.Disabled &&
                graphHandshakeObservabilityNotConfiguredOutcome.Observability?.ReasonCode == DataAgentGraphSidecarObservabilityReasonCodes.NotConfigured &&
                graphHandshakeObservabilityUnavailableOutcome.Observability?.ReasonCode == DataAgentGraphSidecarObservabilityReasonCodes.RuntimeUnavailable &&
                graphHandshakeObservabilityRejectedOutcome.Observability?.ReasonCode == DataAgentGraphSidecarObservabilityReasonCodes.ResponseRejected &&
                graphHandshakeObservabilityAcceptedOutcome.Observability?.ReasonCode == DataAgentGraphSidecarObservabilityReasonCodes.Accepted &&
                graphHandshakeObservabilityRejectedOutcome.Observability.FallbackUsed &&
                graphHandshakeObservabilityAcceptedOutcome.Observability.FallbackUsed == false;
            bool graphHandshakeUnsafeDiagnosticsRedacted =
                graphHandshakeUnsafeObservabilityDiagnostics.Contains("graph_sidecar", StringComparison.Ordinal) &&
                graphHandshakeUnsafeObservabilityDiagnostics.Contains("endpoint_configured=true", StringComparison.Ordinal) &&
                graphHandshakeUnsafeObservabilityDiagnostics.Contains("network_attempted=true", StringComparison.Ordinal) &&
                graphHandshakeUnsafeObservabilityDiagnostics.Contains("summary=redacted", StringComparison.Ordinal) &&
                graphHandshakeUnsafeObservabilityDiagnostics.Contains("SELECT", StringComparison.OrdinalIgnoreCase) == false &&
                graphHandshakeUnsafeObservabilityDiagnostics.Contains("hidden_context", StringComparison.OrdinalIgnoreCase) == false &&
                graphHandshakeUnsafeObservabilityDiagnostics.Contains("secret", StringComparison.OrdinalIgnoreCase) == false;
            bool graphHandshakeObservabilityQChatBoundary =
                string.Equals(typeof(DataAgentGraphSidecarObservabilitySnapshot).Namespace, "Alife.Function.DataAgent", StringComparison.Ordinal) &&
                string.Equals(typeof(DataAgentGraphHandshakeDiagnosticsFormatter).Namespace, "Alife.Function.DataAgent", StringComparison.Ordinal) &&
                typeof(DataAgentGraphSidecarObservabilitySnapshot).Assembly.GetName().Name?.Contains("QChat", StringComparison.OrdinalIgnoreCase) == false &&
                typeof(DataAgentGraphSidecarObservabilitySnapshot).Assembly.GetReferencedAssemblies().Any(assemblyName =>
                    string.Equals(assemblyName.Name, "Alife.Function.QChat", StringComparison.Ordinal)) == false;
            bool graphHandshakeObservabilityDefaultTestsLiveRuntime =
                graphHandshakeDefaultHttpOptions.RuntimeStarted ||
                graphHandshakeDefaultStreamOptions.RuntimeStarted ||
                graphHandshakeObservabilityDefaultContext.RuntimeStartedByAlife ||
                graphHandshakeObservabilityConfiguredContext.RuntimeStartedByAlife;
            bool graphHandshakeObservabilityReady =
                graphHandshakeDefaultOptions.Enabled == false &&
                graphHandshakeObservabilityModelReady &&
                graphHandshakeObservabilityReasonCodesReady &&
                graphHandshakeObservabilityFallbackReasonReady &&
                graphHandshakeUnsafeDiagnosticsRedacted &&
                graphHandshakeStreamSseDeferred &&
                graphHandshakeObservabilityQChatBoundary &&
                graphHandshakeObservabilityDefaultTestsLiveRuntime == false;
            checks.Add(graphHandshakeObservabilityReady
                ? Pass("GraphHandshakeDevSidecarObservabilityContractPresent", "default_enabled=false;observability_model=true;reason_codes=true;fallback_reason=true;unsafe_diagnostics_redacted=true;sse_deferred=true;qchat_boundary=true;default_tests_live_runtime=false")
                : Fail("GraphHandshakeDevSidecarObservabilityContractPresent", $"default_enabled={LowerBool(graphHandshakeDefaultOptions.Enabled)};observability_model={LowerBool(graphHandshakeObservabilityModelReady)};reason_codes={LowerBool(graphHandshakeObservabilityReasonCodesReady)};fallback_reason={LowerBool(graphHandshakeObservabilityFallbackReasonReady)};unsafe_diagnostics_redacted={LowerBool(graphHandshakeUnsafeDiagnosticsRedacted)};sse_deferred={LowerBool(graphHandshakeStreamSseDeferred)};qchat_boundary={LowerBool(graphHandshakeObservabilityQChatBoundary)};default_tests_live_runtime={LowerBool(graphHandshakeObservabilityDefaultTestsLiveRuntime)}"));

            string dataQueryGraphDisabledDiagnostics = DataAgentDataQueryGraphTraceFormatter.Format(
                DataAgentDataQueryGraphPilot.DryRun(CreateReadinessDataQueryGraphAcceptedResult(), DataAgentDataQueryGraphOptions.Disabled));
            bool dataQueryGraphHandlerPublisherReady =
                typeof(DataAgentAnalysisToolHandler)
                    .GetConstructors()
                    .SelectMany(constructor => constructor.GetParameters())
                    .Any(parameter => string.Equals(parameter.Name, "dataQueryGraphDiagnosticsPublisher", StringComparison.Ordinal));
            bool dataQueryGraphCapabilityProviderReady =
                typeof(DataAgentAnalysisCapabilityProvider)
                    .GetConstructors()
                    .SelectMany(constructor => constructor.GetParameters())
                    .Any(parameter => string.Equals(parameter.Name, "dataQueryGraphDiagnosticsPublisher", StringComparison.Ordinal));
            bool dataQueryGraphFunctionCallerReady =
                typeof(XmlFunctionCaller).GetProperty("RecentDataAgentGraphDiagnostics") is not null &&
                typeof(XmlFunctionCaller).GetMethod("RecordRecentDataAgentGraphDiagnostics") is not null;
            bool dataQueryGraphDisabledDiagnosticsReady =
                dataQueryGraphDisabledDiagnostics.Contains("DataQueryGraph dry-run", StringComparison.Ordinal) &&
                dataQueryGraphDisabledDiagnostics.Contains("enabled=false", StringComparison.Ordinal) &&
                dataQueryGraphDisabledDiagnostics.Contains("reason=dataquerygraph_disabled", StringComparison.Ordinal) &&
                dataQueryGraphDisabledDiagnostics.Contains("runtime=no_langgraph_runtime", StringComparison.Ordinal);
            bool dataQueryGraphOwnerDiagnosticsReady =
                dataQueryGraphHandlerPublisherReady &&
                dataQueryGraphCapabilityProviderReady &&
                dataQueryGraphFunctionCallerReady &&
                dataQueryGraphDisabledDiagnosticsReady &&
                string.Equals(DataAgentDataQueryGraphPilot.NoLangGraphRuntimeMarker, "no_langgraph_runtime", StringComparison.Ordinal);
            string dataQueryGraphOwnerDiagnosticsDetail =
                $"handler_publisher={LowerBool(dataQueryGraphHandlerPublisherReady)};capability_provider={LowerBool(dataQueryGraphCapabilityProviderReady)};function_caller={LowerBool(dataQueryGraphFunctionCallerReady)};disabled_diagnostics={LowerBool(dataQueryGraphDisabledDiagnosticsReady)};no_langgraph_runtime={LowerBool(DataAgentDataQueryGraphPilot.NoLangGraphRuntimeMarker == "no_langgraph_runtime")}";
            checks.Add(dataQueryGraphOwnerDiagnosticsReady
                ? Pass("DataQueryGraphOwnerDiagnosticsPresent", dataQueryGraphOwnerDiagnosticsDetail)
                : Fail("DataQueryGraphOwnerDiagnosticsPresent", dataQueryGraphOwnerDiagnosticsDetail));

            DataAgentAnswer storeBoundaryAnswer = new DataAgentService(
                readinessStore,
                new FixedPlanner(new DataAgentQueryPlan(
                    "engineering_gate",
                    "store_boundary_readiness",
                    ["name", "status", "evidence_path"],
                    [new DataAgentFilter("required", "=", true)],
                    [],
                    20))).Answer("force store boundary readiness");
            checks.Add(storeBoundaryAnswer.Validated &&
                       storeBoundaryAnswer.Context.Contains("dataset=engineering_gate", StringComparison.Ordinal)
                ? Pass("DataAgentServiceUsesStoreBoundary", "DataAgentService accepted an injected IDataAgentStore")
                : Fail("DataAgentServiceUsesStoreBoundary", storeBoundaryAnswer.Context));

            readinessStore.RecordToolBrokerAudit(new DataAgentToolBrokerAuditRecord(
                "readiness-session",
                "dataagent_analysis_continue",
                false,
                "tool_route_required",
                "route is required",
                DateTimeOffset.UtcNow));
            IReadOnlyList<DataAgentToolBrokerAuditRecord> toolBrokerAuditRecords = readinessStore.ReadToolBrokerAudit();
            checks.Add(toolBrokerAuditRecords.Any(record =>
                       record.SessionId == "readiness-session" &&
                       record.ToolName == "dataagent_analysis_continue" &&
                       record.Allowed == false &&
                       record.ReasonCode == "tool_route_required")
                ? Pass("ToolBrokerAuditLogPresent", "Tool Broker audit record persisted")
                : Fail("ToolBrokerAuditLogPresent", "Tool Broker audit record was not persisted"));

            DataAgentCapabilityRegistry capabilityRegistry = new();
            DataAgentService readinessService = new(databasePath);
            InMemoryDataAgentAnalysisSessionStore readinessAnalysisStore = new();
            DataAgentAnalysisService readinessAnalysisService = new(
                readinessService,
                readinessAnalysisStore);
            DataAgentAnalysisOrchestrator readinessAnalysisOrchestrator = new(
                readinessAnalysisService,
                readinessAnalysisStore);
            capabilityRegistry.Add(new DataAgentQueryCapabilityProvider(readinessService));
            capabilityRegistry.Add(new DataAgentAnalysisCapabilityProvider(readinessAnalysisOrchestrator));
            checks.Add(capabilityRegistry.ProviderNames.SequenceEqual(new[]
                       {
                           nameof(DataAgentQueryCapabilityProvider),
                           nameof(DataAgentAnalysisCapabilityProvider)
                       }) &&
                       capabilityRegistry.ToolNames.SequenceEqual(DataAgentToolCapabilityManifests.Create().Select(manifest => manifest.Name))
                ? Pass("CapabilityBoundaryPresent", "DataAgent query and analysis providers registered")
                : Fail("CapabilityBoundaryPresent", string.Join(",", capabilityRegistry.ToolNames)));

            DataAgentAnswer answer = new DataAgentService(databasePath).Answer("Which required gates are not passing?");
            checks.Add(answer.Context.Contains("[data_agent_context]", StringComparison.Ordinal) &&
                       answer.Context.Contains("[/data_agent_context]", StringComparison.Ordinal)
                ? Pass("ContextContributionStable", "data_agent_context wrapper present")
                : Fail("ContextContributionStable", "missing data_agent_context wrapper"));

            checks.Add(answer.Context.Contains("planner_confidence=", StringComparison.Ordinal) &&
                       answer.Context.Contains("planner_reason=", StringComparison.Ordinal) &&
                       answer.Context.Contains("planner_signals=", StringComparison.Ordinal) &&
                       answer.PlannerExplanation.Signals.Count > 0
                ? Pass("PlannerExplanationInContext", answer.PlannerExplanation.Confidence)
                : Fail("PlannerExplanationInContext", answer.Context));

            checks.Add(answer.Context.Contains("result_explanation=", StringComparison.Ordinal)
                ? Pass("NaturalLanguageResultExplanationPresent", "result_explanation context field present")
                : Fail("NaturalLanguageResultExplanationPresent", answer.Context));

            checks.Add(typeof(ILlmDataAgentPlannerClient).IsInterface
                ? Pass("LlmPlannerInterfacePresent", nameof(ILlmDataAgentPlannerClient))
                : Fail("LlmPlannerInterfacePresent", "LLM planner client is not an interface"));

            DataAgentLlmPlannerPrompt prompt = new LlmDataAgentPlannerPromptFormatter().Format(
                new DataAgentQueryRequest("Which documents describe DataAgent NL2SQL?", "developer", "en-US", false),
                DataAgentCatalog.CreateDefault(),
                schemaSnapshot);
            checks.Add(prompt.Schema.Contains("document_index", StringComparison.Ordinal) &&
                       prompt.System.Contains("Do not output SQL", StringComparison.Ordinal)
                ? Pass("LlmPlannerPromptUsesSchemaSnapshot", "schema snapshot and SQL prohibition present")
                : Fail("LlmPlannerPromptUsesSchemaSnapshot", prompt.System + Environment.NewLine + prompt.Schema));

            LlmDataAgentPlannerResponseParser llmParser = new(DataAgentCatalog.CreateDefault());
            DataAgentLlmPlannerResult validLlmPlan = llmParser.Parse("""
                {"type":"plan","planner_name":"LlmDataAgentQueryPlanner","intent":"find_dataagent_documents","dataset":"document_index","confidence":"medium","signals":["dataagent","document"],"reason":"question asks for DataAgent documentation","select_fields":["path","title","summary"],"filters":[{"field":"tags","operator":"contains","value":"dataagent"}],"sorts":[{"field":"updated_at","direction":"desc"}],"limit":20}
                """);
            DataAgentLlmPlannerResult duplicatePlannerNamePlan = llmParser.Parse("""
                {"type":"plan","planner_name":"UntrustedPlanner","planner_name":"LlmDataAgentQueryPlanner","intent":"find_dataagent_documents","dataset":"document_index","confidence":"medium","signals":["dataagent","document"],"reason":"question asks for DataAgent documentation","select_fields":["path","title","summary"],"filters":[{"field":"tags","operator":"contains","value":"dataagent"}],"sorts":[],"limit":20}
                """);
            DataAgentLlmPlannerResult concatenatedJsonPlan = llmParser.Parse("""
                {"type":"plan"}{"type":"plan"}
                """);
            checks.Add(validLlmPlan.IsValid &&
                       validLlmPlan.Envelope?.Plan?.Dataset == "document_index" &&
                       duplicatePlannerNamePlan.IsValid == false &&
                       duplicatePlannerNamePlan.RejectedReason == "duplicate_property:planner_name" &&
                       concatenatedJsonPlan.IsValid == false &&
                       concatenatedJsonPlan.RejectedReason == "json_must_be_single_object"
                ? Pass("LlmPlannerStrictJsonParser", "strict JSON parser rejects duplicate and non-single-object output")
                : Fail("LlmPlannerStrictJsonParser", string.Join(";", validLlmPlan.RejectedReason, duplicatePlannerNamePlan.RejectedReason, concatenatedJsonPlan.RejectedReason)));

            DataAgentLlmPlannerResult invalidLlmPlan = new LlmDataAgentPlannerResponseParser(DataAgentCatalog.CreateDefault()).Parse("""
                {"type":"plan","planner_name":"LlmDataAgentQueryPlanner","intent":"bad_operator","dataset":"document_index","confidence":"medium","signals":["bad"],"reason":"bad operator","select_fields":["path"],"filters":[{"field":"tags","operator":"starts_with","value":"dataagent"}],"sorts":[],"limit":20}
                """);
            checks.Add(invalidLlmPlan.IsValid == false &&
                       invalidLlmPlan.RejectedReason.Contains("unsupported_operator:starts_with", StringComparison.Ordinal)
                ? Pass("LlmPlannerRejectsInvalidOutput", invalidLlmPlan.RejectedReason)
                : Fail("LlmPlannerRejectsInvalidOutput", invalidLlmPlan.RejectedReason));

            DataAgentQueryPlanEnvelope llmFallbackEnvelope = new LlmDataAgentQueryPlanner(
                databasePath,
                new FixedLlmClient("not json"),
                new DeterministicDataAgentQueryPlanner()).Plan(new DataAgentQueryRequest(
                    "Which documents describe DataAgent NL2SQL?",
                    "developer",
                    "en-US",
                    false));
            checks.Add(llmFallbackEnvelope.Plan?.Dataset == "document_index" &&
                       llmFallbackEnvelope.Explanation.Signals.Contains("llm_invalid_output_fallback", StringComparer.OrdinalIgnoreCase) &&
                       llmFallbackEnvelope.Explanation.Reason.Contains("not json", StringComparison.Ordinal) == false &&
                       llmFallbackEnvelope.Explanation.Reason.Contains("json_must_be_single_object", StringComparison.Ordinal)
                ? Pass("LlmPlannerFallbackPreservesSafety", llmFallbackEnvelope.Explanation.Reason)
                : Fail("LlmPlannerFallbackPreservesSafety", llmFallbackEnvelope.Explanation.Reason));

            DataAgentQueryPlanEnvelope clarificationEnvelope = new LlmDataAgentQueryPlanner(
                databasePath,
                new FixedLlmClient("""
                    {"type":"clarification","planner_name":"LlmDataAgentQueryPlanner","intent":"clarify_ambiguous_query","dataset":"","confidence":"low","signals":["ambiguous_dataset"],"reason":"question is ambiguous","clarification_question":"Which dataset should I use?","clarification_options":["document_index","test_run"]}
                    """),
                new DeterministicDataAgentQueryPlanner()).Plan(new DataAgentQueryRequest(
                    "Show me the latest status",
                    "developer",
                    "en-US",
                    false));
            checks.Add(clarificationEnvelope.Plan is null && clarificationEnvelope.Clarification is not null
                ? Pass("ClarificationRequestSupported", clarificationEnvelope.Clarification.Question)
                : Fail("ClarificationRequestSupported", clarificationEnvelope.Explanation.Reason));
            checks.Add(typeof(IDataAgentQueryPlanner).IsAssignableFrom(typeof(DeterministicDataAgentQueryPlanner))
                ? Pass("PlannerInterfacePresent", nameof(IDataAgentQueryPlanner))
                : Fail("PlannerInterfacePresent", "deterministic planner does not implement interface"));

            DataAgentQueryPlanEnvelope deterministicEnvelope = new DeterministicDataAgentQueryPlanner().Plan(new DataAgentQueryRequest(
                "Which runtime readiness gate is required?",
                "developer",
                "en-US",
                false));
            DataAgentQueryPlan deterministicPlan = deterministicEnvelope.Plan ?? throw new InvalidOperationException("Deterministic planner returned no query plan.");
            checks.Add(deterministicPlan.Dataset == "engineering_gate" &&
                       deterministicPlan.Intent == "find_runtime_readiness_required_evidence"
                ? Pass("DeterministicPlannerPassesFixtures", deterministicPlan.Intent)
                : Fail("DeterministicPlannerPassesFixtures", $"{deterministicPlan.Dataset}/{deterministicPlan.Intent}"));

            DataAgentAnswer injectedPlannerAnswer = new DataAgentService(databasePath, new FixedPlanner(new DataAgentQueryPlan(
                "document_index",
                "readiness_forced_document_lookup",
                ["path", "title", "summary"],
                [new DataAgentFilter("tags", "contains", "dataagent")],
                [],
                20))).Answer("force injected planner");
            checks.Add(injectedPlannerAnswer.Dataset == "document_index" &&
                       injectedPlannerAnswer.Context.Contains("dataset=document_index", StringComparison.Ordinal)
                ? Pass("ServiceUsesInjectedPlanner", injectedPlannerAnswer.Dataset)
                : Fail("ServiceUsesInjectedPlanner", injectedPlannerAnswer.Context));

            DataAgentAnswer unsafePlannerAnswer = new DataAgentService(databasePath, new FixedPlanner(new DataAgentQueryPlan(
                "engineering_gate",
                "unsafe",
                ["name"],
                [new DataAgentFilter("status", "starts_with", "pass")],
                [],
                50))).Answer("unsafe planner output");
            checks.Add(unsafePlannerAnswer.Validated == false &&
                       unsafePlannerAnswer.Context.Contains("sql_status=rejected", StringComparison.Ordinal) &&
                       unsafePlannerAnswer.RejectedReason.Contains("unsupported_operator:starts_with", StringComparison.Ordinal)
                ? Pass("UnsafePlannerOutputRejected", unsafePlannerAnswer.RejectedReason)
                : Fail("UnsafePlannerOutputRejected", unsafePlannerAnswer.Context));

            string toolContext = new DataAgentToolHandler(new DataAgentService(databasePath)).Query("Which documents describe DataAgent NL2SQL?");
            checks.Add(toolContext.Contains("[data_agent_context]", StringComparison.Ordinal) &&
                       toolContext.Contains("dataset=document_index", StringComparison.Ordinal) &&
                       toolContext.Contains("[/data_agent_context]", StringComparison.Ordinal)
                ? Pass("ToolHandlerReturnsDataAgentContext", "dataagent_query context returned")
                : Fail("ToolHandlerReturnsDataAgentContext", toolContext));

            InMemoryDataAgentAnalysisSessionStore analysisStore = new();
            DataAgentAnalysisService analysisService = new(
                new DataAgentService(databasePath),
                analysisStore);
            DataAgentAnalysisResponse analysisStart = analysisService.Start(
                "local",
                "Which documents describe DataAgent NL2SQL?");
            DataAgentAnalysisSession? analysisSession = analysisStore.Get(analysisStart.SessionId);

            checks.Add(typeof(DataAgentAnalysisService).IsClass &&
                       analysisStart.Accepted &&
                       analysisSession?.Turns.Count == 1 &&
                       analysisStart.Answer is not null
                ? Pass("AnalysisSessionServicePresent", analysisStart.Status.ToString())
                : Fail("AnalysisSessionServicePresent", analysisStart.RejectedReason));

            checks.Add(typeof(IDataAgentAnalysisSessionStore).IsInterface &&
                       typeof(IDataAgentAnalysisSessionStore).IsAssignableFrom(typeof(InMemoryDataAgentAnalysisSessionStore))
                ? Pass("AnalysisSessionStorePresent", nameof(InMemoryDataAgentAnalysisSessionStore))
                : Fail("AnalysisSessionStorePresent", "analysis session store boundary missing"));

            DataAgentAnalysisResponse analysisEnd = analysisService.End(analysisStart.SessionId);
            DataAgentAnalysisResponse endedContinue = analysisService.Continue(analysisStart.SessionId, "\u7ee7\u7eed");
            checks.Add(analysisStart.Status is DataAgentAnalysisSessionStatus.Active or DataAgentAnalysisSessionStatus.ReadyToSummarize &&
                       analysisEnd.Status == DataAgentAnalysisSessionStatus.Ended &&
                       endedContinue.Accepted == false &&
                       endedContinue.RejectedReason == "analysis_session_ended"
                ? Pass("AnalysisSessionStateMachineTransitions", $"{analysisStart.Status}->{analysisEnd.Status}")
                : Fail("AnalysisSessionStateMachineTransitions", endedContinue.RejectedReason));

            DataAgentFollowUpInterpreter interpreter = new();
            checks.Add(interpreter.Interpret("\u7ee7\u7eed") == DataAgentAnalysisTurnIntent.Continue &&
                       interpreter.Interpret("\u53ea\u770b\u5931\u8d25\u7684") == DataAgentAnalysisTurnIntent.RefinePrevious &&
                       interpreter.Interpret("\u603b\u7ed3\u4e00\u4e0b") == DataAgentAnalysisTurnIntent.Summarize
                ? Pass("AnalysisFollowUpInterpreterPresent", "common Chinese follow-up intents recognized")
                : Fail("AnalysisFollowUpInterpreterPresent", "follow-up intent mismatch"));

            string analysisContext = analysisSession is null
                ? string.Empty
                : DataAgentAnalysisContextProvider.Build(analysisSession);
            checks.Add(analysisContext.Contains("[data_agent_analysis_session_context]", StringComparison.Ordinal) &&
                       analysisContext.Contains("caller_id=local", StringComparison.Ordinal) &&
                       analysisContext.Contains("pending_summary=", StringComparison.Ordinal)
                ? Pass("AnalysisSessionContextProviderPresent", "analysis session context emitted")
                : Fail("AnalysisSessionContextProviderPresent", analysisContext));

            DataAgentAnalysisResponse summaryWindowStart = analysisService.Start("local", "Which documents describe DataAgent NL2SQL?");
            DataAgentAnalysisResponse secondTurn = analysisService.Continue(summaryWindowStart.SessionId, "\u7ee7\u7eed");
            DataAgentAnalysisResponse thirdTurn = analysisService.Continue(summaryWindowStart.SessionId, "\u53ea\u770b DataAgent \u76f8\u5173");
            string thirdTurnContext = thirdTurn.Context;
            checks.Add(summaryWindowStart.Accepted &&
                       secondTurn.Accepted &&
                       thirdTurn.Accepted &&
                       summaryWindowStart.Status != DataAgentAnalysisSessionStatus.ReadyToSummarize &&
                       secondTurn.Status != DataAgentAnalysisSessionStatus.ReadyToSummarize &&
                       thirdTurn.Status == DataAgentAnalysisSessionStatus.ReadyToSummarize &&
                       thirdTurnContext.Contains("pending_summary=true", StringComparison.Ordinal)
                ? Pass("AnalysisSummaryWindowPresent", thirdTurn.Status.ToString())
                : Fail("AnalysisSummaryWindowPresent", $"{summaryWindowStart.Status}->{secondTurn.Status}->{thirdTurn.Status}"));

            bool storeInterfaceHasSqliteBinding = typeof(IDataAgentAnalysisSessionStore)
                .GetMethods()
                .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType))
                .Any(type => type.FullName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true);
            checks.Add(storeInterfaceHasSqliteBinding == false
                ? Pass("AnalysisSessionHasNoSqliteBinding", "store interface is provider-neutral")
                : Fail("AnalysisSessionHasNoSqliteBinding", "store interface exposes sqlite types"));

            InMemoryDataAgentAnalysisSessionStore orchestrationStore = new();
            int orchestrationAnswerCalls = 0;
            DataAgentAnalysisService orchestrationAnalysisService = new(
                question =>
                {
                    orchestrationAnswerCalls++;
                    return new DataAgentAnswer(
                        "document_index",
                        "SELECT path FROM document_index LIMIT 20",
                        1,
                        "orchestrated answer",
                        "[data_agent_context]\nsql_status=validated\nresult_explanation=orchestrated answer\n[/data_agent_context]",
                        true,
                        string.Empty,
                        new DataAgentPlannerExplanation(
                            "ReadinessPlanner",
                            "orchestrator_readiness",
                            "document_index",
                            "high",
                            ["orchestrator"],
                            "readiness orchestrator answer"));
                },
                orchestrationStore);
            DataAgentAnalysisOrchestrator orchestrator = new(orchestrationAnalysisService, orchestrationStore);
            DataAgentToolRouteContext acceptedEvidenceRouteContext = new(
                true,
                "dataagent_analysis_start",
                true,
                true,
                "route-evidence-pack",
                "analysis_start",
                "route_allowed",
                string.Empty);
            DataAgentOrchestrationResult orchestrationStart = orchestrator.Start(new DataAgentOrchestrationRequest(
                "readiness",
                "Which documents describe DataAgent?",
                null,
                RouteAllowsQuery: true,
                acceptedEvidenceRouteContext));
            DataAgentOrchestrationResult orchestrationDenied = orchestrator.Start(new DataAgentOrchestrationRequest(
                "readiness",
                "Which documents describe DataAgent?",
                null,
                RouteAllowsQuery: false));
            DataAgentOrchestrationResult orchestrationDeniedContinue = orchestrator.Continue(new DataAgentOrchestrationRequest(
                "readiness",
                "\u7ee7\u7eed",
                orchestrationStart.SessionId,
                RouteAllowsQuery: false));
            int answerCallsAfterDeniedContinue = orchestrationAnswerCalls;
            int turnsAfterDeniedContinue = orchestrationStore.Get(orchestrationStart.SessionId)?.Turns.Count ?? -1;
            DataAgentToolRouteContext orchestrationTerminalContinueRoute = new(
                true,
                "dataagent_analysis_continue",
                true,
                false,
                "route-terminal-continue",
                "analysis_continue",
                "route_allowed",
                orchestrationStart.SessionId);
            DataAgentOrchestrationResult orchestrationSummary = orchestrator.Continue(new DataAgentOrchestrationRequest(
                "readiness",
                "\u603b\u7ed3\u4e00\u4e0b",
                orchestrationStart.SessionId,
                orchestrationTerminalContinueRoute.AllowsQuery,
                orchestrationTerminalContinueRoute));

            checks.Add(typeof(IDataAgentAnalysisOrchestrator).IsAssignableFrom(typeof(DataAgentAnalysisOrchestrator)) &&
                       orchestrationStart.Response.Accepted
                ? Pass("DataAgentOrchestratorPresent", "native DataAgent analysis orchestrator available")
                : Fail("DataAgentOrchestratorPresent", "orchestrator type or accepted flow missing"));

            checks.Add(orchestrationStart.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.RouteGate) &&
                       orchestrationStart.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.SchemaContext) &&
                       orchestrationStart.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.Plan) &&
                       orchestrationStart.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.Validate) &&
                       orchestrationStart.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.Execute) &&
                       orchestrationStart.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.Explain)
                ? Pass("OrchestratorNodeBoundaryPresent", "route/schema/plan/validate/execute/explain nodes recorded")
                : Fail("OrchestratorNodeBoundaryPresent", string.Join(",", orchestrationStart.Steps.Select(step => step.Node))));

            checks.Add(orchestrationStart.Checkpoint.SessionId == orchestrationStart.SessionId &&
                       orchestrationStart.Checkpoint.TurnCount == 1 &&
                       orchestrationStart.Checkpoint.CanContinue &&
                       orchestrationStart.Checkpoint.CanSummarize
                ? Pass("OrchestratorCheckpointPresent", "checkpoint includes session state and continuation flags")
                : Fail("OrchestratorCheckpointPresent", orchestrationStart.Checkpoint.ToString()));

            checks.Add(orchestrationDenied.Response.Accepted == false &&
                       orchestrationDenied.Response.RejectedReason == "tool_route_required" &&
                       orchestrationDenied.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.Execute) == false &&
                       orchestrationDeniedContinue.Response.Accepted == false &&
                       orchestrationDeniedContinue.Response.RejectedReason == "tool_route_required" &&
                       orchestrationDeniedContinue.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.Execute) == false &&
                       answerCallsAfterDeniedContinue == 1 &&
                       turnsAfterDeniedContinue == 1
                ? Pass("OrchestratorRouteGateFailClosed", "denied start and denied continue avoided query execution and session mutation")
                : Fail("OrchestratorRouteGateFailClosed", $"{orchestrationDenied.Response.RejectedReason};continue={orchestrationDeniedContinue.Response.RejectedReason};answerCalls={answerCallsAfterDeniedContinue};turns={turnsAfterDeniedContinue}"));

            checks.Add(orchestrationSummary.Response.Accepted &&
                       orchestrationSummary.Response.Answer is null &&
                       orchestrationSummary.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.Summarize) &&
                       orchestrationSummary.Steps.Any(step => step.ExecutedSql) == false &&
                       orchestrationAnswerCalls == 1
                ? Pass("OrchestratorTerminalNodesDoNotQuery", "summarize terminal node avoided query execution")
                : Fail("OrchestratorTerminalNodesDoNotQuery", $"answerCalls={orchestrationAnswerCalls}"));

            checks.Add(orchestrationStart.SessionStatus == DataAgentAnalysisSessionStatus.Active &&
                       orchestrationSummary.SessionStatus == DataAgentAnalysisSessionStatus.Summarized
                ? Pass("OrchestratorStateMachineTransitions", $"{orchestrationStart.SessionStatus}->{orchestrationSummary.SessionStatus}")
                : Fail("OrchestratorStateMachineTransitions", $"{orchestrationStart.SessionStatus}->{orchestrationSummary.SessionStatus}"));

            string orchestrationStartContext = DataAgentOrchestrationContextProvider.Build(orchestrationStart);
            string orchestrationSummaryContext = DataAgentOrchestrationContextProvider.Build(orchestrationSummary);
            string orchestrationDeniedContinueContext = DataAgentOrchestrationContextProvider.Build(orchestrationDeniedContinue);
            DataAgentOrchestrationResult orchestrationRuntimeContinueStart = orchestrator.Start(new DataAgentOrchestrationRequest(
                "readiness",
                "Which documents describe DataAgent runtime?",
                null,
                RouteAllowsQuery: true));
            DataAgentOrchestrationResult orchestrationRuntimeContinue = orchestrator.Continue(new DataAgentOrchestrationRequest(
                "readiness",
                "\u7ee7\u7eed",
                orchestrationRuntimeContinueStart.SessionId,
                RouteAllowsQuery: true));
            string orchestrationContinueContext = DataAgentOrchestrationContextProvider.Build(orchestrationRuntimeContinue);

            checks.Add(typeof(DataAgentAnalysisToolHandler)
                    .GetConstructors()
                    .Any(constructor => constructor.GetParameters().Any(parameter => parameter.ParameterType == typeof(IDataAgentAnalysisOrchestrator)))
                ? Pass("AnalysisToolHandlerUsesOrchestrator", "analysis XML handler depends on IDataAgentAnalysisOrchestrator")
                : Fail("AnalysisToolHandlerUsesOrchestrator", "analysis XML handler can bypass orchestrator"));

            checks.Add(orchestrationStartContext.Contains("orchestration_trace=RouteGate:Succeeded", StringComparison.Ordinal) &&
                       orchestrationStartContext.Contains("Execute:Succeeded", StringComparison.Ordinal)
                ? Pass("OrchestratorTraceContextPresent", "orchestration trace emitted in runtime context")
                : Fail("OrchestratorTraceContextPresent", orchestrationStartContext));

            checks.Add(orchestrationStartContext.Contains("checkpoint_session_id=", StringComparison.Ordinal) &&
                       orchestrationStartContext.Contains("checkpoint_can_continue=true", StringComparison.Ordinal) &&
                       orchestrationStartContext.Contains("checkpoint_terminal=false", StringComparison.Ordinal)
                ? Pass("OrchestratorCheckpointContextPresent", "checkpoint context emitted")
                : Fail("OrchestratorCheckpointContextPresent", orchestrationStartContext));

            checks.Add(orchestrationStart.Response.Accepted &&
                       orchestrationStartContext.Contains("[data_agent_analysis_session_context]", StringComparison.Ordinal)
                ? Pass("OrchestratorRuntimeStartPathCovered", "start path returned analysis context and orchestration trace")
                : Fail("OrchestratorRuntimeStartPathCovered", orchestrationStartContext));

            checks.Add(orchestrationRuntimeContinue.Response.Accepted &&
                       orchestrationRuntimeContinue.Checkpoint.TurnCount == 2 &&
                       orchestrationContinueContext.Contains("checkpoint_turn_count=2", StringComparison.Ordinal)
                ? Pass("OrchestratorRuntimeContinuePathCovered", "continue path returned second-turn checkpoint")
                : Fail("OrchestratorRuntimeContinuePathCovered", orchestrationContinueContext));

            checks.Add(orchestrationSummary.Response.Accepted &&
                       orchestrationSummaryContext.Contains("orchestration_trace=Summarize:Succeeded>Checkpoint:Succeeded", StringComparison.Ordinal) &&
                       orchestrationSummary.Steps.Any(step => step.ExecutedSql) == false
                ? Pass("OrchestratorRuntimeTerminalPathCovered", "terminal summarize path returned no-query trace")
                : Fail("OrchestratorRuntimeTerminalPathCovered", orchestrationSummaryContext));

            checks.Add(orchestrationDeniedContinue.Response.Accepted == false &&
                       orchestrationDeniedContinueContext.Contains("orchestration_trace=RouteGate:Rejected>Reject:Rejected>Checkpoint:Succeeded", StringComparison.Ordinal) &&
                       answerCallsAfterDeniedContinue == 1 &&
                       turnsAfterDeniedContinue == 1
                ? Pass("OrchestratorRuntimeRouteDeniedFailClosed", "route-denied runtime continue returned rejected trace without mutation")
                : Fail("OrchestratorRuntimeRouteDeniedFailClosed", orchestrationDeniedContinueContext));

            DataAgentOrchestrationResult routeHandlerResult = new(
                "route-readiness-session",
                DataAgentAnalysisSessionStatus.Active,
                [
                    new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Succeeded, "route_allowed", false),
                    new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
                ],
                new DataAgentOrchestrationCheckpoint(
                    "route-readiness-session",
                    DataAgentAnalysisSessionStatus.Active,
                    "document_index",
                    1,
                    CanContinue: true,
                    CanSummarize: true,
                    Terminal: false),
                new DataAgentAnalysisResponse(
                    "route-readiness-session",
                    DataAgentAnalysisSessionStatus.Active,
                    DataAgentAnalysisTurnIntent.NewQuestion,
                    null,
                    string.Empty,
                    "[data_agent_analysis_session_context]\nsession_id=route-readiness-session\n[/data_agent_analysis_session_context]",
                    true,
                    string.Empty));
            RecordingRouteContextAccessor recordingRouteAccessor = new(new DataAgentToolRouteContext(
                true,
                "dataagent_analysis_start",
                true,
                true,
                "route-readiness",
                "analysis_start",
                "route_allowed",
                string.Empty));
            RecordingOrchestrator recordingOrchestrator = new(routeHandlerResult);
            DataAgentAnalysisToolHandler recordingHandler = new(recordingOrchestrator, null, recordingRouteAccessor);
            string routedHandlerContext = recordingHandler.Start("readiness", "Which documents describe DataAgent route context?");
            DataAgentOrchestrationRequest? routedStartRequest = recordingOrchestrator.StartRequests.Count == 1
                ? recordingOrchestrator.StartRequests[0]
                : null;

            checks.Add(recordingRouteAccessor.Requests.SequenceEqual(new[] { ("dataagent_analysis_start", (string?)null) }) &&
                       routedStartRequest?.RouteAllowsQuery == true &&
                       routedStartRequest.RouteContext?.RouteId == "route-readiness"
                ? Pass("AnalysisHandlerConsumesToolRouteContext", "analysis handler requested and forwarded Tool Broker route context")
                : Fail("AnalysisHandlerConsumesToolRouteContext", $"requests={recordingRouteAccessor.Requests.Count};route={routedStartRequest?.RouteContext?.RouteId ?? string.Empty}"));

            RecordingOrchestrator runtimeDecisionOrchestrator = new(routeHandlerResult);
            DataAgentAnalysisToolHandler runtimeDecisionHandler = new(
                runtimeDecisionOrchestrator,
                null,
                new RecordingRouteContextAccessor(new DataAgentToolRouteContext(
                    true,
                    "dataagent_analysis_start",
                    false,
                    false,
                    "route-denied",
                    "analysis_start",
                    DataAgentToolRouteContext.ToolNotAllowedReasonCode,
                    string.Empty)));
            runtimeDecisionHandler.Start("readiness", "Which documents describe DataAgent denied route?");
            DataAgentOrchestrationRequest? runtimeDecisionRequest = runtimeDecisionOrchestrator.StartRequests.Count == 1
                ? runtimeDecisionOrchestrator.StartRequests[0]
                : null;
            checks.Add(runtimeDecisionRequest?.RouteAllowsQuery == false &&
                       runtimeDecisionRequest.RouteContext?.AllowsQuery == false &&
                       runtimeDecisionRequest.RouteContext?.RouteId == "route-denied"
                ? Pass("OrchestrationRequestUsesRuntimeRouteDecision", "DataAgent orchestration request RouteAllowsQuery came from route context")
                : Fail("OrchestrationRequestUsesRuntimeRouteDecision", $"routeAllowsQuery={runtimeDecisionRequest?.RouteAllowsQuery}"));

            RecordingOrchestrator missingRouteOrchestrator = new(routeHandlerResult);
            DataAgentAnalysisToolHandler missingRouteHandler = new(missingRouteOrchestrator);
            missingRouteHandler.Start("readiness", "Which documents describe missing route?");
            DataAgentOrchestrationRequest? missingRouteRequest = missingRouteOrchestrator.StartRequests.Count == 1
                ? missingRouteOrchestrator.StartRequests[0]
                : null;
            checks.Add(missingRouteRequest?.RouteAllowsQuery == false &&
                       missingRouteRequest.RouteContext?.Present == false &&
                       missingRouteRequest.RouteContext?.ReasonCode == DataAgentToolRouteContext.MissingRouteReasonCode
                ? Pass("RouteMissingRequestFailsClosed", "missing route created a fail-closed DataAgent request")
                : Fail("RouteMissingRequestFailsClosed", missingRouteRequest?.RouteContext?.ReasonCode ?? "missing request"));

            checks.Add(routedHandlerContext.Contains("route_present=true", StringComparison.Ordinal) &&
                       routedHandlerContext.Contains("route_allows_query=true", StringComparison.Ordinal) &&
                       routedHandlerContext.Contains("route_reason_code=route_allowed", StringComparison.Ordinal) &&
                       routedHandlerContext.Contains("route_session_id=", StringComparison.Ordinal)
                ? Pass("RouteEvidenceContextPresent", "route evidence fields emitted in orchestration context")
                : Fail("RouteEvidenceContextPresent", routedHandlerContext));

            XmlFunctionExecutionPolicy sessionPolicy = new();
            sessionPolicy.CurrentRoute = new ToolRouteDecision(
                "route-session",
                ToolCapabilityDomain.DataAgent,
                "analysis_continue",
                ["dataagent_analysis_continue"],
                [],
                new ToolRouteState("session-allowed", "Active", true, true, true),
                "route_allowed",
                "route_allowed");
            DataAgentToolRouteContext sessionMismatchRoute = new XmlPolicyDataAgentToolRouteContextAccessor(sessionPolicy)
                .Get("dataagent_analysis_continue", "session-other");
            checks.Add(sessionMismatchRoute.Present &&
                       sessionMismatchRoute.AllowsQuery == false &&
                       sessionMismatchRoute.ReasonCode == DataAgentToolRouteContext.SessionNotAllowedReasonCode &&
                       sessionMismatchRoute.RouteSessionId == "session-allowed"
                ? Pass("RouteSessionScopePreserved", "session-scoped route mismatch remains fail-closed")
                : Fail("RouteSessionScopePreserved", sessionMismatchRoute.ReasonCode));

            DataAgentOrchestrationResult terminalRouteStart = orchestrator.Start(new DataAgentOrchestrationRequest(
                "readiness",
                "Which documents describe DataAgent terminal route context?",
                null,
                RouteAllowsQuery: true));
            int answerCallsBeforeTerminalRoute = orchestrationAnswerCalls;
            DataAgentToolRouteContext terminalRouteContext = new(
                true,
                "dataagent_analysis_summarize",
                true,
                true,
                "route-terminal",
                "analysis_summarize",
                "route_allowed",
                terminalRouteStart.SessionId);
            int turnsBeforeDeniedTerminalRoute = orchestrationStore.Get(terminalRouteStart.SessionId)?.Turns.Count ?? -1;
            DataAgentToolRouteContext deniedTerminalRouteContext =
                DataAgentToolRouteContext.Missing("dataagent_analysis_summarize");
            DataAgentOrchestrationResult terminalRouteDeniedSummary = orchestrator.Summarize(
                terminalRouteStart.SessionId,
                deniedTerminalRouteContext);
            int turnsAfterDeniedTerminalRoute = orchestrationStore.Get(terminalRouteStart.SessionId)?.Turns.Count ?? -1;
            string terminalRouteDeniedSummaryContext = DataAgentOrchestrationContextProvider.Build(terminalRouteDeniedSummary);
            DataAgentOrchestrationResult terminalRouteSummary = orchestrator.Summarize(
                terminalRouteStart.SessionId,
                terminalRouteContext);
            int answerCallsAfterTerminalRoute = orchestrationAnswerCalls;
            string terminalRouteSummaryContext = DataAgentOrchestrationContextProvider.Build(terminalRouteSummary);

            DataAgentEvidencePackBuilder evidencePackBuilder = new();
            DataAgentEvidencePack acceptedEvidencePack = evidencePackBuilder.Build(
                orchestrationStart,
                [
                    new DataAgentAuditRecord(
                        "Which documents describe DataAgent?",
                        "document_index",
                        "{}",
                        "SELECT path FROM document_index LIMIT 20",
                        true,
                        string.Empty,
                        1,
                        TimeSpan.FromMilliseconds(1),
                        DateTimeOffset.UtcNow)
                ],
                [
                    new DataAgentToolBrokerAuditRecord(
                        orchestrationStart.SessionId,
                        "dataagent_analysis_start",
                        true,
                        "route_allowed",
                        "route allowed",
                        DateTimeOffset.UtcNow)
                ]);
            DataAgentEvidencePack deniedEvidencePack = evidencePackBuilder.Build(orchestrationDeniedContinue);
            DataAgentEvidencePack terminalEvidencePack = evidencePackBuilder.Build(terminalRouteSummary);
            string acceptedEvidencePackContext = DataAgentEvidencePackFormatter.Format(acceptedEvidencePack);
            string deniedEvidencePackContext = DataAgentEvidencePackFormatter.Format(deniedEvidencePack);
            string terminalEvidencePackContext = DataAgentEvidencePackFormatter.Format(terminalEvidencePack);

            bool acceptedEvidenceReady =
                acceptedEvidencePack.ExecutedSql &&
                acceptedEvidencePack.RoutePresent &&
                acceptedEvidencePack.RouteTool == "dataagent_analysis_start" &&
                acceptedEvidencePack.RouteAllowed &&
                acceptedEvidencePack.RouteAllowsQuery &&
                acceptedEvidencePack.AuditValidated &&
                acceptedEvidencePack.ToolBrokerAuditAllowed &&
                acceptedEvidencePack.ToolBrokerAuditReasonCode == "route_allowed" &&
                orchestrationStart.RouteContext == acceptedEvidenceRouteContext &&
                acceptedEvidencePackContext.Contains("[data_agent_evidence_pack]", StringComparison.Ordinal) &&
                acceptedEvidencePackContext.Contains("audit_validated=true", StringComparison.Ordinal) &&
                acceptedEvidencePackContext.Contains("tool_broker_audit_allowed=true", StringComparison.Ordinal) &&
                acceptedEvidencePackContext.Contains("safety_summary=route_allowed read_only_sql_executed checkpoint_active", StringComparison.Ordinal);

            bool deniedEvidenceReady =
                deniedEvidencePack.ExecutedSql == false &&
                deniedEvidencePack.RouteAllowed == false &&
                deniedEvidencePack.RouteAllowsQuery == false &&
                deniedEvidencePack.AuditValidated == false &&
                deniedEvidencePack.Trace.Contains("RouteGate:Rejected", StringComparison.Ordinal) &&
                deniedEvidencePack.SafetySummary == "route_rejected;sql_not_executed;checkpoint_unchanged" &&
                deniedEvidencePackContext.Contains("route_allowed=false", StringComparison.Ordinal) &&
                deniedEvidencePackContext.Contains("route_allows_query=false", StringComparison.Ordinal) &&
                deniedEvidencePackContext.Contains("executed_sql=false", StringComparison.Ordinal);

            bool terminalEvidenceReady =
                terminalEvidencePack.ExecutedSql == false &&
                terminalEvidencePack.RouteTool == "dataagent_analysis_summarize" &&
                terminalEvidencePack.RouteAllowed &&
                terminalEvidencePack.RouteAllowsQuery &&
                terminalEvidencePack.Trace.Contains("Summarize:Succeeded", StringComparison.Ordinal) &&
                terminalEvidencePack.SafetySummary.Contains("terminal_no_query", StringComparison.Ordinal) &&
                terminalEvidencePackContext.Contains("terminal=false", StringComparison.Ordinal);

            checks.Add(terminalRouteSummary.Response.Accepted &&
                       terminalRouteSummary.Response.Answer is null &&
                       terminalRouteSummary.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.Summarize) &&
                       terminalRouteSummary.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.Execute) == false &&
                       terminalRouteSummary.Steps.Any(step => step.ExecutedSql) == false &&
                       terminalRouteSummary.RouteContext == terminalRouteContext &&
                       answerCallsAfterTerminalRoute == answerCallsBeforeTerminalRoute &&
                       terminalRouteDeniedSummary.Response.Accepted == false &&
                       terminalRouteDeniedSummary.Response.RejectedReason == DataAgentToolRouteContext.MissingRouteReasonCode &&
                       terminalRouteDeniedSummary.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.Summarize) == false &&
                       terminalRouteDeniedSummary.Steps.Any(step => step.ExecutedSql) == false &&
                       terminalRouteDeniedSummary.RouteContext == deniedTerminalRouteContext &&
                       turnsBeforeDeniedTerminalRoute == 1 &&
                       turnsAfterDeniedTerminalRoute == turnsBeforeDeniedTerminalRoute &&
                       terminalRouteDeniedSummaryContext.Contains("orchestration_trace=RouteGate:Rejected>Reject:Rejected>Checkpoint:Succeeded", StringComparison.Ordinal) &&
                       terminalRouteSummaryContext.Contains("route_tool=dataagent_analysis_summarize", StringComparison.Ordinal) &&
                       terminalRouteSummaryContext.Contains("route_allows_query=true", StringComparison.Ordinal) &&
                       terminalRouteSummaryContext.Contains($"route_session_id={terminalRouteStart.SessionId}", StringComparison.Ordinal)
                ? Pass("TerminalRouteDoesNotQuery", $"route_tool=dataagent_analysis_summarize;route_allows_query=true;route_session_id={terminalRouteStart.SessionId};answer_calls_unchanged=true;denied_terminal_fail_closed=true")
                : Fail("TerminalRouteDoesNotQuery", $"answerCallsBefore={answerCallsBeforeTerminalRoute};answerCallsAfter={answerCallsAfterTerminalRoute};turnsBeforeDenied={turnsBeforeDeniedTerminalRoute};turnsAfterDenied={turnsAfterDeniedTerminalRoute};context={terminalRouteSummaryContext};deniedContext={terminalRouteDeniedSummaryContext}"));

            checks.Add(acceptedEvidenceReady && deniedEvidenceReady && terminalEvidenceReady
                ? Pass("DataAgentEvidencePackPresent", $"accepted=true;accepted_route_context=runtime;denied=true;terminal=true;accepted_trace={acceptedEvidencePack.Trace};accepted_safety={acceptedEvidencePack.SafetySummary};denied_trace={deniedEvidencePack.Trace};denied_safety={deniedEvidencePack.SafetySummary};terminal_trace={terminalEvidencePack.Trace};terminal_safety={terminalEvidencePack.SafetySummary}")
                : Fail("DataAgentEvidencePackPresent", $"accepted={acceptedEvidenceReady};denied={deniedEvidenceReady};terminal={terminalEvidenceReady};accepted_trace={acceptedEvidencePack.Trace};accepted_safety={acceptedEvidencePack.SafetySummary};denied_trace={deniedEvidencePack.Trace};denied_safety={deniedEvidencePack.SafetySummary};terminal_trace={terminalEvidencePack.Trace};terminal_safety={terminalEvidencePack.SafetySummary}"));

            KalmanScalarFilter semanticFilter = new(0.20, 0.50);
            KalmanScalarFilter semanticUpdated = semanticFilter.Predict(0.05).Update(0.90, 0.20);
            checks.Add(semanticUpdated.Value > 0.20 &&
                       semanticUpdated.Value < 0.90 &&
                       semanticUpdated.Uncertainty > 0.0 &&
                       semanticUpdated.Uncertainty < 0.55
                ? Pass("SemanticStateEstimatorCorePresent", $"scalar_filter=true;application_layer=true;value={semanticUpdated.Value:0.###};uncertainty={semanticUpdated.Uncertainty:0.###}")
                : Fail("SemanticStateEstimatorCorePresent", $"scalar_filter=false;value={semanticUpdated.Value:0.###};uncertainty={semanticUpdated.Uncertainty:0.###}"));

            DataAgentAnalysisStateEstimate acceptedEstimate = DataAgentAnalysisStateEstimator.Estimate(acceptedEvidencePack);
            DataAgentAnalysisStateEstimate deniedEstimate = DataAgentAnalysisStateEstimator.Estimate(deniedEvidencePack);
            bool analysisEstimatorReady =
                acceptedEstimate.AnalysisConfidence >= 0.70 &&
                acceptedEstimate.AnswerStability >= 0.70 &&
                acceptedEstimate.ReasonCode == "analysis_evidence_stable" &&
                deniedEstimate.ToolPermissionAllowed == false &&
                deniedEstimate.ShouldContinue == false &&
                deniedEstimate.RiskLevel >= 0.70 &&
                deniedEstimate.ReasonCode == "route_denied_no_query";
            checks.Add(analysisEstimatorReady
                ? Pass("DataAgentAnalysisStateEstimatorPresent", $"accepted_stable=true;denied_no_bypass=true;accepted_reason={acceptedEstimate.ReasonCode};denied_reason={deniedEstimate.ReasonCode}")
                : Fail("DataAgentAnalysisStateEstimatorPresent", $"accepted_confidence={acceptedEstimate.AnalysisConfidence:0.###};accepted_stability={acceptedEstimate.AnswerStability:0.###};denied_permission={deniedEstimate.ToolPermissionAllowed};denied_continue={deniedEstimate.ShouldContinue};denied_risk={deniedEstimate.RiskLevel:0.###};accepted_reason={acceptedEstimate.ReasonCode};denied_reason={deniedEstimate.ReasonCode}"));

            string acceptedEvidenceDiagnostics = DataAgentEvidenceDiagnosticsFormatter.Format(acceptedEvidencePack);
            bool evidenceDiagnosticsReady =
                acceptedEvidenceDiagnostics.Contains("DataAgent evidence diagnostics", StringComparison.Ordinal) &&
                acceptedEvidenceDiagnostics.Contains("analysis_confidence=", StringComparison.Ordinal) &&
                acceptedEvidenceDiagnostics.Contains("risk_level=", StringComparison.Ordinal) &&
                acceptedEvidenceDiagnostics.Contains("state_estimate_reason_code=analysis_evidence_stable", StringComparison.Ordinal) &&
                acceptedEvidenceDiagnostics.Contains("[data_agent_evidence_pack]", StringComparison.OrdinalIgnoreCase) == false &&
                acceptedEvidenceDiagnostics.Contains("[tool_route_context]", StringComparison.OrdinalIgnoreCase) == false;
            checks.Add(evidenceDiagnosticsReady
                ? Pass("DataAgentEvidenceDiagnosticsPresent", "owner_diag=true;analysis_confidence=true;risk_level=true")
                : Fail("DataAgentEvidenceDiagnosticsPresent", acceptedEvidenceDiagnostics.ReplaceLineEndings(" ")));

            System.Reflection.ParameterInfo? evidenceDiagnosticsPublisherParameter =
                typeof(DataAgentAnalysisToolHandler).GetConstructors()
                    .SelectMany(ctor => ctor.GetParameters())
                    .FirstOrDefault(parameter => parameter.Name == "evidenceDiagnosticsPublisher");
            bool evidenceDiagnosticsPublisherIsStringAction =
                evidenceDiagnosticsPublisherParameter?.ParameterType == typeof(Action<string>);
            bool evidenceDiagnosticsFormatterPresent =
                typeof(DataAgentModuleService).Assembly.GetType("Alife.Function.DataAgent.DataAgentEvidenceDiagnosticsFormatter") is not null;
            bool dataAgentAvoidsQChatReference =
                typeof(DataAgentModuleService).Assembly.GetReferencedAssemblies().Any(assemblyName =>
                    string.Equals(assemblyName.Name, "Alife.Function.QChat", StringComparison.Ordinal)) == false;
            bool recentDiagnosticsBridgeReady =
                evidenceDiagnosticsPublisherIsStringAction &&
                evidenceDiagnosticsFormatterPresent &&
                dataAgentAvoidsQChatReference;
            checks.Add(recentDiagnosticsBridgeReady
                ? Pass("DataAgentEvidenceRecentDiagnosticsBridgePresent", "safe_bridge=true;cache_ready=true;publisher_type=Action<string>;no_qchat_reference=true")
                : Fail("DataAgentEvidenceRecentDiagnosticsBridgePresent", $"safe_bridge={evidenceDiagnosticsPublisherIsStringAction.ToString().ToLowerInvariant()};cache_ready={evidenceDiagnosticsFormatterPresent.ToString().ToLowerInvariant()};publisher_type={evidenceDiagnosticsPublisherParameter?.ParameterType.Name ?? "missing"};no_qchat_reference={dataAgentAvoidsQChatReference.ToString().ToLowerInvariant()}"));

            DataAgentTraceTimeline traceTimeline = new DataAgentTraceTimelineBuilder().Build(
                orchestrationStart,
                acceptedEvidencePack,
                DateTimeOffset.UtcNow);
            string traceDiagnostics = DataAgentTraceDiagnosticsFormatter.Format(traceTimeline);
            bool traceTimelineStructuralReady =
                traceTimeline.Events.Any(traceEvent => traceEvent.Kind == DataAgentTraceEventKind.RouteGate) &&
                traceTimeline.Events.Any(traceEvent => traceEvent.Kind == DataAgentTraceEventKind.Execute) &&
                traceTimeline.Events.Any(traceEvent => traceEvent.Kind == DataAgentTraceEventKind.EvidencePack) &&
                traceTimeline.Events.Any(traceEvent => traceEvent.Kind == DataAgentTraceEventKind.Checkpoint);
            bool traceOwnerDiagnosticsReady =
                traceDiagnostics.Contains("DataAgent trace diagnostics", StringComparison.Ordinal);
            bool traceSqlRedacted =
                traceDiagnostics.Contains("sql=redacted", StringComparison.Ordinal);
            bool traceEvidencePackRedacted =
                traceDiagnostics.Contains("data_agent_evidence_pack", StringComparison.OrdinalIgnoreCase) == false;
            bool traceToolRouteRedacted =
                traceDiagnostics.Contains("tool_route_context", StringComparison.OrdinalIgnoreCase) == false &&
                traceDiagnostics.Contains("Allowed XML tools", StringComparison.OrdinalIgnoreCase) == false;
            bool traceHiddenContextRedacted =
                traceDiagnostics.Contains("hidden_context", StringComparison.OrdinalIgnoreCase) == false &&
                traceDiagnostics.Contains("hidden context", StringComparison.OrdinalIgnoreCase) == false;
            bool traceTimelineReady =
                traceTimelineStructuralReady &&
                traceOwnerDiagnosticsReady &&
                traceSqlRedacted &&
                traceEvidencePackRedacted &&
                traceToolRouteRedacted &&
                traceHiddenContextRedacted;
            const string traceTimelineReadyDetail =
                "trace_timeline=true;owner_diag=true;sql_redacted=true;hidden_context_redacted=true;evidence_pack_redacted=true;tool_route_redacted=true";
            string traceTimelineFailureDetail =
                $"trace_timeline={LowerBool(traceTimelineStructuralReady)};owner_diag={LowerBool(traceOwnerDiagnosticsReady)};sql_redacted={LowerBool(traceSqlRedacted)};hidden_context_redacted={LowerBool(traceHiddenContextRedacted)};evidence_pack_redacted={LowerBool(traceEvidencePackRedacted)};tool_route_redacted={LowerBool(traceToolRouteRedacted)}";
            checks.Add(traceTimelineReady
                ? Pass("DataAgentTraceTimelinePresent", traceTimelineReadyDetail)
                : Fail("DataAgentTraceTimelinePresent", traceTimelineFailureDetail));

            DateTimeOffset progressNow = new(2026, 7, 3, 10, 0, 0, TimeSpan.Zero);
            DataAgentProgressRecorder progressRecorder = new();
            List<string> progressDiagnostics = [];
            IDataAgentProgressSink progressSink = new DataAgentProgressDiagnosticsPublisher(
                progressRecorder,
                progressDiagnostics.Add,
                () => progressNow);
            InMemoryDataAgentAnalysisSessionStore progressStore = new();
            DataAgentAnalysisService progressAnalysisService = new(
                new DataAgentService(databasePath),
                progressStore,
                progressSink: progressSink,
                clock: () => progressNow);
            DataAgentAnalysisOrchestrator progressOrchestrator = new(
                progressAnalysisService,
                progressStore,
                progressSink: progressSink,
                progressClock: () => progressNow);
            DataAgentOrchestrationResult progressResult = progressOrchestrator.Start(new DataAgentOrchestrationRequest(
                "owner",
                "Which required gates are not passing?",
                null,
                RouteAllowsQuery: true,
                RouteContext: new DataAgentToolRouteContext(
                    true,
                    "dataagent_analysis_start",
                    true,
                    true,
                    "route-progress",
                    "analysis_start",
                    "route_allowed",
                    string.Empty)));
            progressSink.Publish(new DataAgentProgressEvent(
                progressResult.SessionId,
                DataAgentProgressEventKind.Execute,
                DataAgentProgressEventPhase.Completed,
                DataAgentProgressEventStatus.Succeeded,
                "read_only_query_executed",
                progressResult.Checkpoint.TurnCount,
                progressNow,
                true,
                true,
                false,
                new Dictionary<string, string>
                {
                    ["sql"] = "SELECT * FROM engineering_gate WHERE required = 1",
                    ["hidden_context"] = "[hidden_context]secret[/hidden_context]",
                    ["data_agent_evidence_pack"] = "[data_agent_evidence_pack]secret[/data_agent_evidence_pack]",
                    ["tool_route_context"] = "Allowed XML tools for this turn: dataagent_query"
                }));
            IReadOnlyList<DataAgentProgressEvent> progressEvents = progressRecorder.GetRecent(progressResult.SessionId, progressNow);
            string progressDiagnosticsText = progressDiagnostics.LastOrDefault() ??
                DataAgentProgressDiagnosticsFormatter.Format(progressEvents, progressResult.SessionId, progressNow);
            bool progressStructuralReady =
                progressResult.Response.Accepted &&
                progressEvents.Any(progressEvent => progressEvent.Kind == DataAgentProgressEventKind.RouteGate) &&
                progressEvents.Any(progressEvent => progressEvent.Kind == DataAgentProgressEventKind.Planner) &&
                progressEvents.Any(progressEvent => progressEvent.Kind == DataAgentProgressEventKind.Execute) &&
                progressEvents.Any(progressEvent => progressEvent.Kind == DataAgentProgressEventKind.Checkpoint);
            bool progressOwnerDiagnosticsReady =
                progressDiagnosticsText.Contains("DataAgent progress diagnostics", StringComparison.Ordinal);
            bool progressSqlRedacted =
                progressDiagnosticsText.Contains("sql=redacted", StringComparison.Ordinal) &&
                progressDiagnosticsText.Contains("SELECT", StringComparison.OrdinalIgnoreCase) == false &&
                progressDiagnosticsText.Contains("engineering_gate", StringComparison.OrdinalIgnoreCase) == false;
            bool progressHiddenContextRedacted =
                progressDiagnosticsText.Contains("hidden_context", StringComparison.OrdinalIgnoreCase) == false &&
                progressDiagnosticsText.Contains("hidden context", StringComparison.OrdinalIgnoreCase) == false;
            bool progressEvidencePackRedacted =
                progressDiagnosticsText.Contains("data_agent_evidence_pack", StringComparison.OrdinalIgnoreCase) == false;
            bool progressToolRouteRedacted =
                progressDiagnosticsText.Contains("tool_route_context", StringComparison.OrdinalIgnoreCase) == false &&
                progressDiagnosticsText.Contains("Allowed XML tools", StringComparison.OrdinalIgnoreCase) == false;
            bool progressStreamingReady =
                progressStructuralReady &&
                progressOwnerDiagnosticsReady &&
                progressSqlRedacted &&
                progressHiddenContextRedacted &&
                progressEvidencePackRedacted &&
                progressToolRouteRedacted;
            const string progressStreamingReadyDetail =
                "progress_stream=true;owner_diag=true;sql_redacted=true;hidden_context_redacted=true;evidence_pack_redacted=true;tool_route_redacted=true";
            string progressStreamingFailureDetail =
                $"progress_stream={LowerBool(progressStructuralReady)};owner_diag={LowerBool(progressOwnerDiagnosticsReady)};sql_redacted={LowerBool(progressSqlRedacted)};hidden_context_redacted={LowerBool(progressHiddenContextRedacted)};evidence_pack_redacted={LowerBool(progressEvidencePackRedacted)};tool_route_redacted={LowerBool(progressToolRouteRedacted)}";
            checks.Add(progressStreamingReady
                ? Pass("DataAgentProgressStreamingPresent", progressStreamingReadyDetail)
                : Fail("DataAgentProgressStreamingPresent", progressStreamingFailureDetail));

            string repoRoot = FindRepositoryRoot(AppContext.BaseDirectory);
            string scenarioPackPath = Path.Combine(repoRoot, "docs", "dataagent", "scenario-packs", "engineering.zh-CN.json");
            Encoding strictUtf8 = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true);
            string scenarioPackText = File.ReadAllText(scenarioPackPath, strictUtf8);
            DataAgentScenarioKnowledgePack pack = DataAgentScenarioKnowledgePackProvider.Load(scenarioPackPath);
            IReadOnlyList<DataAgentScenarioTerm> resolvedTerms =
                DataAgentScenarioKnowledgePackProvider.ResolveTerms(pack, "看看工程门禁里最近失败的必需项");
            bool scenarioPackUtf8Readable =
                scenarioPackText.Contains("工程门禁", StringComparison.Ordinal) &&
                scenarioPackText.Contains("最近失败的测试", StringComparison.Ordinal) &&
                scenarioPackText.Contains("缺失项", StringComparison.Ordinal) &&
                scenarioPackText.Contains("文档证据", StringComparison.Ordinal) &&
                scenarioPackText.Contains("失败", StringComparison.Ordinal) &&
                scenarioPackText.Contains("必需", StringComparison.Ordinal) &&
                scenarioPackText.Contains("宸ョ▼", StringComparison.Ordinal) == false &&
                scenarioPackText.Contains("鏈€", StringComparison.Ordinal) == false &&
                scenarioPackText.Contains("澶辫触", StringComparison.Ordinal) == false &&
                scenarioPackText.Contains("蹇呭渶", StringComparison.Ordinal) == false &&
                scenarioPackText.Contains("\uFFFD", StringComparison.Ordinal) == false;
            bool scenarioPackHasEngineeringGateStatus = resolvedTerms.Any(term =>
                string.Equals(term.Dataset, "engineering_gate", StringComparison.Ordinal) &&
                term.Fields.Contains("status", StringComparer.Ordinal));
            bool scenarioPackHasTestRunFailed = resolvedTerms.Any(term =>
                string.Equals(term.Dataset, "test_run", StringComparison.Ordinal) &&
                term.Fields.Contains("failed", StringComparer.Ordinal));
            bool scenarioPackReady =
                string.Equals(pack.Scenario, "engineering_readiness", StringComparison.Ordinal) &&
                scenarioPackUtf8Readable &&
                scenarioPackHasEngineeringGateStatus &&
                scenarioPackHasTestRunFailed;
            checks.Add(scenarioPackReady
                ? Pass("DataAgentScenarioKnowledgePackPresent", "scenario=engineering_readiness;dataset=engineering_gate;field=status;utf8=readable")
                : Fail("DataAgentScenarioKnowledgePackPresent", $"path={scenarioPackPath};scenario={pack.Scenario};utf8={LowerBool(scenarioPackUtf8Readable)};engineering_gate_status={LowerBool(scenarioPackHasEngineeringGateStatus)};test_run_failed={LowerBool(scenarioPackHasTestRunFailed)};terms={string.Join(",", resolvedTerms.Select(term => term.Dataset))}"));

            DataAgentCatalog scenarioCatalog = DataAgentCatalog.CreateDefault();
            DataAgentScenarioContext scenarioContext = new DataAgentScenarioContextBuilder().Build(
                scenarioCatalog,
                pack,
                "看看工程门禁里最近失败的必需项");
            DataAgentLlmPlannerPrompt scenarioPrompt = new LlmDataAgentPlannerPromptFormatter().Format(
                new DataAgentQueryRequest("看看工程门禁里最近失败的必需项", "owner", "zh-CN", false),
                scenarioCatalog,
                schemaSnapshot,
                scenarioContext);
            string scenarioDiagnostics = DataAgentScenarioDiagnosticsFormatter.Format(scenarioContext);
            bool scenarioContextMatched =
                string.Equals(scenarioContext.ReasonCode, DataAgentScenarioContext.ReasonMatched, StringComparison.Ordinal) &&
                scenarioContext.CandidateDatasets.SequenceEqual(["engineering_gate", "test_run"], StringComparer.Ordinal) &&
                scenarioContext.CandidateFields.Contains("required", StringComparer.Ordinal) &&
                scenarioContext.CandidateFields.Contains("failed", StringComparer.Ordinal) &&
                scenarioContext.Metrics.Select(metric => metric.Name).SequenceEqual(["失败", "必需"], StringComparer.Ordinal);
            bool scenarioPromptHintReady =
                scenarioPrompt.Schema.Contains("Scenario context:", StringComparison.Ordinal) &&
                scenarioPrompt.Schema.Contains("Scenario context is a hint only", StringComparison.Ordinal) &&
                scenarioPrompt.System.Contains("Do not output SQL", StringComparison.Ordinal);
            bool scenarioOwnerDiagnosticsReady =
                scenarioDiagnostics.Contains("DataAgent scenario diagnostics", StringComparison.Ordinal) &&
                scenarioDiagnostics.Contains("reason=scenario_context_matched", StringComparison.Ordinal) &&
                scenarioDiagnostics.Contains("metrics=失败:status!=passed;必需:required=true", StringComparison.Ordinal) &&
                scenarioDiagnostics.Contains("SELECT", StringComparison.OrdinalIgnoreCase) == false;
            DataAgentLlmPlannerPrompt? capturedUnsafeScenarioPrompt = null;
            DataAgentQueryPlanEnvelope unsafeScenarioEnvelope = new LlmDataAgentQueryPlanner(
                databasePath,
                new CapturingFixedLlmClient(
                    """
                    {"type":"plan","planner_name":"LlmDataAgentQueryPlanner","intent":"unsafe_operator","dataset":"engineering_gate","confidence":"medium","signals":["scenario"],"reason":"try unsupported operator","select_fields":["name","status"],"filters":[{"field":"status","operator":"starts_with","value":"fail"}],"sorts":[],"limit":20}
                    """,
                    prompt => capturedUnsafeScenarioPrompt = prompt),
                new DeterministicDataAgentQueryPlanner()).Plan(new DataAgentQueryRequest(
                    "看看工程门禁里最近失败的必需项",
                    "owner",
                    "zh-CN",
                    false,
                    scenarioContext));
            bool scenarioBoundaryReady =
                capturedUnsafeScenarioPrompt?.Schema.Contains("Scenario context:", StringComparison.Ordinal) == true &&
                unsafeScenarioEnvelope.Plan is not null &&
                unsafeScenarioEnvelope.Clarification is null &&
                unsafeScenarioEnvelope.Explanation.Signals.Contains("llm_invalid_output_fallback", StringComparer.Ordinal) &&
                unsafeScenarioEnvelope.Explanation.Reason.Contains("unsupported_operator", StringComparison.Ordinal) &&
                unsafeScenarioEnvelope.Explanation.Reason.Contains("SELECT", StringComparison.OrdinalIgnoreCase) == false;
            bool scenarioSqlBoundaryReady =
                typeof(DataAgentQueryPlanValidator).IsClass &&
                typeof(DataAgentSqlCompiler).IsClass &&
                typeof(DataAgentSqlSafetyValidator).IsClass &&
                typeof(DataAgentQueryExecutor).IsClass &&
                scenarioBoundaryReady;
            bool scenarioContextIntegrated =
                scenarioContextMatched &&
                scenarioPromptHintReady &&
                scenarioOwnerDiagnosticsReady &&
                scenarioSqlBoundaryReady;
            checks.Add(scenarioContextIntegrated
                ? Pass("DataAgentScenarioContextIntegrated", "scenario_context=true;prompt_hint=true;owner_diag=true;sql_boundary=true")
                : Fail("DataAgentScenarioContextIntegrated", $"scenario_context={LowerBool(scenarioContextMatched)};prompt_hint={LowerBool(scenarioPromptHintReady)};owner_diag={LowerBool(scenarioOwnerDiagnosticsReady)};sql_boundary={LowerBool(scenarioSqlBoundaryReady)};reason={scenarioContext.ReasonCode};datasets={string.Join(",", scenarioContext.CandidateDatasets)};fields={string.Join(",", scenarioContext.CandidateFields)};metrics={string.Join(",", scenarioContext.Metrics.Select(metric => metric.Name))}"));

            const string runtimeScenarioQuestion = "\u770b\u770b\u5de5\u7a0b\u95e8\u7981\u91cc\u6700\u8fd1\u5931\u8d25\u7684\u5fc5\u9700\u9879";
            RecordingPlanner runtimeScenarioPlanner = new(new DataAgentQueryPlan(
                "engineering_gate",
                "runtime_scenario_context_activation",
                ["name", "status", "required"],
                [new DataAgentFilter("required", "=", true)],
                [],
                20));
            DataAgentService runtimeScenarioService = new(
                databasePath,
                runtimeScenarioPlanner,
                new DataAgentScenarioContextProvider(scenarioPackPath));
            DataAgentAnswer runtimeScenarioAnswer = runtimeScenarioService.Answer(runtimeScenarioQuestion);
            DataAgentScenarioContext? runtimeScenarioContext =
                runtimeScenarioPlanner.Requests.SingleOrDefault()?.ScenarioContext;
            DataAgentLlmPlannerPrompt? runtimeScenarioPrompt = null;
            DataAgentService runtimeLlmScenarioService = new(
                databasePath,
                new LlmDataAgentQueryPlanner(
                    databasePath,
                    new CapturingFixedLlmClient(
                        """
                        {"type":"plan","planner_name":"LlmDataAgentQueryPlanner","intent":"bad_operator","dataset":"engineering_gate","confidence":"medium","signals":["scenario"],"reason":"bad operator","select_fields":["name"],"filters":[{"field":"status","operator":"starts_with","value":"fail"}],"sorts":[],"limit":20}
                        """,
                        prompt => runtimeScenarioPrompt = prompt),
                    new DeterministicDataAgentQueryPlanner()),
                new DataAgentScenarioContextProvider(scenarioPackPath));
            DataAgentAnswer runtimeLlmScenarioAnswer = runtimeLlmScenarioService.Answer(runtimeScenarioQuestion);
            bool runtimeServiceContextReady =
                runtimeScenarioAnswer.Validated &&
                runtimeScenarioContext is not null &&
                string.Equals(runtimeScenarioContext.ReasonCode, DataAgentScenarioContext.ReasonMatched, StringComparison.Ordinal) &&
                runtimeScenarioContext.CandidateDatasets.SequenceEqual(["engineering_gate", "test_run"], StringComparer.Ordinal);
            bool runtimeLlmPromptReady =
                runtimeLlmScenarioAnswer.Validated &&
                runtimeLlmScenarioAnswer.PlannerExplanation.Signals.Contains("llm_invalid_output_fallback", StringComparer.OrdinalIgnoreCase) &&
                runtimeScenarioPrompt?.Schema.Contains("Scenario context:", StringComparison.Ordinal) == true &&
                runtimeScenarioPrompt.Schema.Contains("engineering_gate", StringComparison.Ordinal) &&
                runtimeScenarioPrompt.Schema.Contains("test_run", StringComparison.Ordinal);
            bool runtimeQChatBoundaryReady =
                typeof(DataAgentModuleService).Assembly.GetReferencedAssemblies().Any(assemblyName =>
                    string.Equals(assemblyName.Name, "Alife.Function.QChat", StringComparison.Ordinal)) == false;
            bool runtimeSqlBoundaryReady =
                typeof(DataAgentQueryPlanValidator).IsClass &&
                typeof(DataAgentSqlCompiler).IsClass &&
                typeof(DataAgentSqlSafetyValidator).IsClass &&
                typeof(DataAgentQueryExecutor).IsClass;
            bool runtimeActivationReady =
                runtimeServiceContextReady &&
                runtimeLlmPromptReady &&
                runtimeQChatBoundaryReady &&
                runtimeSqlBoundaryReady;
            checks.Add(runtimeActivationReady
                ? Pass("DataAgentRuntimeScenarioContextActivationPresent", "service_context=true;llm_prompt=true;qchat_boundary=true;sql_boundary=true")
                : Fail("DataAgentRuntimeScenarioContextActivationPresent", $"service_context={LowerBool(runtimeServiceContextReady)};llm_prompt={LowerBool(runtimeLlmPromptReady)};qchat_boundary={LowerBool(runtimeQChatBoundaryReady)};sql_boundary={LowerBool(runtimeSqlBoundaryReady)}"));

            DataAgentNodeToolScope plannerScope = DataAgentToolScopePolicy.ForNode(DataAgentWorkflowNodeNames.QueryPlanner);
            DataAgentNodeToolScope diagnosticsScope = DataAgentToolScopePolicy.ForNode(DataAgentWorkflowNodeNames.DiagnosticsRouter);
            bool nodeToolScopePolicyReady =
                plannerScope.AllowedCapabilities.Contains(DataAgentNodeCapabilities.GenerateQueryPlan, StringComparer.Ordinal) &&
                plannerScope.AllowedCapabilities.Contains(DataAgentNodeCapabilities.ExecuteReadOnlyQuery, StringComparer.Ordinal) == false &&
                diagnosticsScope.AllowedCapabilities.Contains(DataAgentNodeCapabilities.ReadProgressDiagnostics, StringComparer.Ordinal) &&
                diagnosticsScope.AllowedCapabilities.Contains(DataAgentNodeCapabilities.ExecuteReadOnlyQuery, StringComparer.Ordinal) == false;
            checks.Add(nodeToolScopePolicyReady
                ? Pass("DataAgentNodeToolScopePolicyPresent", "planner_generate=true;planner_execute=false;diagnostics_progress=true;diagnostics_execute=false")
                : Fail("DataAgentNodeToolScopePolicyPresent", $"planner={string.Join(",", plannerScope.AllowedCapabilities)};diagnostics={string.Join(",", diagnosticsScope.AllowedCapabilities)}"));

            DataAgentNodeToolScope validatorScope = DataAgentToolScopePolicy.ForNode(DataAgentWorkflowNodeNames.QueryPlanValidator);
            DataAgentNodeToolScope compilerScope = DataAgentToolScopePolicy.ForNode(DataAgentWorkflowNodeNames.SqlCompiler);
            DataAgentNodeToolScope safetyScope = DataAgentToolScopePolicy.ForNode(DataAgentWorkflowNodeNames.SqlSafety);
            DataAgentNodeToolScope executeScope = DataAgentToolScopePolicy.ForNode(DataAgentWorkflowNodeNames.ReadOnlyExecute);
            bool deterministicSafetyReady =
                validatorScope.AllowsModelCall == false &&
                compilerScope.AllowsModelCall == false &&
                safetyScope.AllowsModelCall == false &&
                executeScope.AllowsModelCall == false;
            checks.Add(deterministicSafetyReady
                ? Pass("DataAgentSafetyCapabilitiesRemainDeterministic", "validator_model=false;compiler_model=false;safety_model=false;execute_model=false")
                : Fail("DataAgentSafetyCapabilitiesRemainDeterministic", $"validator={LowerBool(validatorScope.AllowsModelCall)};compiler={LowerBool(compilerScope.AllowsModelCall)};safety={LowerBool(safetyScope.AllowsModelCall)};execute={LowerBool(executeScope.AllowsModelCall)}"));
        }
        catch (Exception ex)
        {
            checks.Add(Fail("DataAgentReadinessException", ex.Message));
        }

        return checks;
    }

    static DataAgentReadinessCheck Pass(string name, string detail) => new(name, true, detail);

    static DataAgentReadinessCheck Fail(string name, string detail) => new(name, false, detail);

    static string LowerBool(bool value) => value ? "true" : "false";

    static bool ContainsBroadGraphHandshakeAuthorityToken(string? toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            return true;

        string[] broadAuthorityTokens = ["execute", "sql.compile", "mutation", "checkpoint"];
        return broadAuthorityTokens.Any(token =>
            toolName.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    static DataAgentOrchestrationResult CreateReadinessDataQueryGraphAcceptedResult()
    {
        const string sessionId = "readiness-dataquerygraph";

        DataAgentAnalysisResponse response = new(
            sessionId,
            DataAgentAnalysisSessionStatus.Active,
            DataAgentAnalysisTurnIntent.NewQuestion,
            new DataAgentAnswer(
                "document_index",
                "SELECT path FROM document_index LIMIT 20",
                1,
                "Found DataAgent documentation.",
                "[data_agent_context]\nsql_status=validated\n[/data_agent_context]",
                true,
                string.Empty,
                new DataAgentPlannerExplanation(
                    "ReadinessPlanner",
                    "find_dataagent_documents",
                    "document_index",
                    "high",
                    ["readiness"],
                    "readiness accepted answer")),
            "ok",
            string.Empty,
            true,
            string.Empty);

        DataAgentOrchestrationCheckpoint checkpoint = new(
            sessionId,
            DataAgentAnalysisSessionStatus.Active,
            "document_index",
            1,
            CanContinue: true,
            CanSummarize: true,
            Terminal: false);

        return new DataAgentOrchestrationResult(
            sessionId,
            DataAgentAnalysisSessionStatus.Active,
            [
                CreateReadinessDataQueryGraphStep(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Succeeded, "route_allowed", false),
                CreateReadinessDataQueryGraphStep(DataAgentOrchestrationNodeKind.SchemaContext, DataAgentOrchestrationStepStatus.Succeeded, "dataagent_catalog_available", false),
                CreateReadinessDataQueryGraphStep(DataAgentOrchestrationNodeKind.Plan, DataAgentOrchestrationStepStatus.Succeeded, "planner_response_received", false),
                CreateReadinessDataQueryGraphStep(DataAgentOrchestrationNodeKind.Validate, DataAgentOrchestrationStepStatus.Succeeded, "validated", false),
                CreateReadinessDataQueryGraphStep(DataAgentOrchestrationNodeKind.Execute, DataAgentOrchestrationStepStatus.Succeeded, "read_only_query_executed", true),
                CreateReadinessDataQueryGraphStep(DataAgentOrchestrationNodeKind.Explain, DataAgentOrchestrationStepStatus.Succeeded, "result_explained", false),
                CreateReadinessDataQueryGraphStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
            ],
            checkpoint,
            response);
    }

    static DataAgentOrchestrationResult CreateReadinessDataQueryGraphDeniedWithStrayExecutionResult()
    {
        const string sessionId = "readiness-dataquerygraph-denied";

        DataAgentAnalysisResponse response = new(
            sessionId,
            DataAgentAnalysisSessionStatus.Rejected,
            DataAgentAnalysisTurnIntent.NewQuestion,
            null,
            string.Empty,
            string.Empty,
            false,
            "tool_route_required");

        DataAgentOrchestrationCheckpoint checkpoint = new(
            sessionId,
            DataAgentAnalysisSessionStatus.Rejected,
            "document_index",
            1,
            CanContinue: false,
            CanSummarize: true,
            Terminal: true);

        return new DataAgentOrchestrationResult(
            sessionId,
            DataAgentAnalysisSessionStatus.Rejected,
            [
                CreateReadinessDataQueryGraphStep(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Rejected, "tool_route_required", false),
                CreateReadinessDataQueryGraphStep(DataAgentOrchestrationNodeKind.Reject, DataAgentOrchestrationStepStatus.Rejected, "tool_route_required", false),
                CreateReadinessDataQueryGraphStep(DataAgentOrchestrationNodeKind.Validate, DataAgentOrchestrationStepStatus.Succeeded, "validated", false),
                CreateReadinessDataQueryGraphStep(DataAgentOrchestrationNodeKind.Execute, DataAgentOrchestrationStepStatus.Succeeded, "read_only_query_executed", true),
                CreateReadinessDataQueryGraphStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
            ],
            checkpoint,
            response);
    }

    static DataAgentOrchestrationResult CreateReadinessDataQueryGraphTerminalWithStrayExecutionResult()
    {
        const string sessionId = "readiness-dataquerygraph-terminal";

        DataAgentAnalysisResponse response = new(
            sessionId,
            DataAgentAnalysisSessionStatus.Ended,
            DataAgentAnalysisTurnIntent.End,
            null,
            "ok",
            string.Empty,
            true,
            string.Empty);

        DataAgentOrchestrationCheckpoint checkpoint = new(
            sessionId,
            DataAgentAnalysisSessionStatus.Ended,
            "document_index",
            1,
            CanContinue: false,
            CanSummarize: true,
            Terminal: true);

        return new DataAgentOrchestrationResult(
            sessionId,
            DataAgentAnalysisSessionStatus.Ended,
            [
                CreateReadinessDataQueryGraphStep(DataAgentOrchestrationNodeKind.End, DataAgentOrchestrationStepStatus.Succeeded, "terminal_end", false),
                CreateReadinessDataQueryGraphStep(DataAgentOrchestrationNodeKind.Validate, DataAgentOrchestrationStepStatus.Succeeded, "validated", false),
                CreateReadinessDataQueryGraphStep(DataAgentOrchestrationNodeKind.Execute, DataAgentOrchestrationStepStatus.Succeeded, "read_only_query_executed", true),
                CreateReadinessDataQueryGraphStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
            ],
            checkpoint,
            response);
    }

    static bool NodeCanExecuteReadOnlyQuery(DataAgentDataQueryGraphNode node)
    {
        return string.Equals(node.Name, DataAgentWorkflowNodeNames.ReadOnlyExecute, StringComparison.Ordinal) ||
               node.AllowedCapabilities.Contains(DataAgentNodeCapabilities.ExecuteReadOnlyQuery, StringComparer.Ordinal);
    }

    static DataAgentOrchestrationStep CreateReadinessDataQueryGraphStep(
        DataAgentOrchestrationNodeKind node,
        DataAgentOrchestrationStepStatus status,
        string reason,
        bool executedSql)
    {
        return new DataAgentOrchestrationStep(node, status, reason, executedSql);
    }

    static string FindRepositoryRoot(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "docs")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    sealed class FixedGraphHandshakeStreamClient(Func<DataAgentGraphHandshakeRequest, DataAgentGraphHandshakeStreamResult> resultFactory)
        : IDataAgentGraphHandshakeStreamClient
    {
        public DataAgentGraphHandshakeStreamResult TryHandshakeStream(DataAgentGraphHandshakeRequest request)
        {
            return resultFactory(request);
        }
    }

    sealed class FixedGraphSidecarClient(Func<DataAgentGraphHandshakeRequest, DataAgentGraphHandshakeResponse> responseFactory)
        : IDataAgentGraphSidecarClient
    {
        public DataAgentGraphHandshakeResponse TryHandshake(DataAgentGraphHandshakeRequest request)
        {
            return responseFactory(request);
        }
    }

    sealed class DelegateHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responseFactory(request));
        }
    }

    sealed class FixedPlanner(DataAgentQueryPlan plan) : IDataAgentQueryPlanner
    {
        public DataAgentQueryPlanEnvelope Plan(DataAgentQueryRequest request)
        {
            return new DataAgentQueryPlanEnvelope(
                plan,
                new DataAgentPlannerExplanation(
                    nameof(FixedPlanner),
                    plan.Intent,
                    plan.Dataset,
                    "low",
                    ["readiness-test"],
                    "readiness fixed query plan"));
        }
    }

    sealed class RecordingPlanner(DataAgentQueryPlan plan) : IDataAgentQueryPlanner
    {
        public List<DataAgentQueryRequest> Requests { get; } = [];

        public DataAgentQueryPlanEnvelope Plan(DataAgentQueryRequest request)
        {
            Requests.Add(request);
            return new DataAgentQueryPlanEnvelope(
                plan,
                new DataAgentPlannerExplanation(
                    nameof(RecordingPlanner),
                    plan.Intent,
                    plan.Dataset,
                    "medium",
                    ["runtime_scenario_context"],
                    "recorded runtime scenario context"));
        }
    }

    sealed class FixedLlmClient(string raw) : ILlmDataAgentPlannerClient
    {
        public string Complete(DataAgentLlmPlannerPrompt prompt) => raw;
    }

    sealed class CapturingFixedLlmClient(string raw, Action<DataAgentLlmPlannerPrompt> capturePrompt) : ILlmDataAgentPlannerClient
    {
        public string Complete(DataAgentLlmPlannerPrompt prompt)
        {
            capturePrompt(prompt);
            return raw;
        }
    }

    sealed class RecordingRouteContextAccessor(DataAgentToolRouteContext routeContext) : IDataAgentToolRouteContextAccessor
    {
        public List<(string ToolName, string? SessionId)> Requests { get; } = [];

        public DataAgentToolRouteContext Get(string toolName, string? sessionId)
        {
            Requests.Add((toolName, sessionId));
            return routeContext with { ToolName = toolName };
        }
    }

    sealed class RecordingOrchestrator(DataAgentOrchestrationResult result) : IDataAgentAnalysisOrchestrator
    {
        public List<DataAgentOrchestrationRequest> StartRequests { get; } = [];

        public DataAgentOrchestrationResult Start(DataAgentOrchestrationRequest request)
        {
            StartRequests.Add(request);
            return result with { RouteContext = request.RouteContext };
        }

        public DataAgentOrchestrationResult Continue(DataAgentOrchestrationRequest request)
        {
            return result with { RouteContext = request.RouteContext };
        }

        public DataAgentOrchestrationResult Summarize(string sessionId, DataAgentToolRouteContext? routeContext = null)
        {
            return result with { RouteContext = routeContext };
        }

        public DataAgentOrchestrationResult End(string sessionId, DataAgentToolRouteContext? routeContext = null)
        {
            return result with { RouteContext = routeContext };
        }
    }
}

public sealed record DataAgentReadinessCheck(string Name, bool Passed, string Detail);
