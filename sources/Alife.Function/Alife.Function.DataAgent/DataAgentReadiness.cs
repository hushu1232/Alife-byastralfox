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

            string v311RepoRoot = FindRepositoryRoot();
            string v311DocPath = Path.Combine(v311RepoRoot, "docs", "dataagent", "dataagent-v3.11-real-langgraph-sidecar-skeleton.md");
            string v311SidecarPath = Path.Combine(v311RepoRoot, "tools", "dataagent-langgraph-sidecar", "server.py");
            string v311ReadmePath = Path.Combine(v311RepoRoot, "tools", "dataagent-langgraph-sidecar", "README.md");
            bool v311DocExists = File.Exists(v311DocPath);
            bool v311SidecarExists = File.Exists(v311SidecarPath);
            bool v311ReadmeExists = File.Exists(v311ReadmePath);
            string v311Doc = v311DocExists ? File.ReadAllText(v311DocPath) : string.Empty;
            string v311Sidecar = v311SidecarExists ? File.ReadAllText(v311SidecarPath) : string.Empty;
            string v311Readme = v311ReadmeExists ? File.ReadAllText(v311ReadmePath) : string.Empty;
            bool v311ManualOnly =
                v311Doc.Contains("manual_only=true", StringComparison.Ordinal) &&
                v311Readme.Contains("manual-only", StringComparison.OrdinalIgnoreCase);
            bool v311LoopbackOnly =
                v311Doc.Contains("loopback_only=true", StringComparison.Ordinal) &&
                v311Sidecar.Contains("Only loopback hosts are allowed.", StringComparison.Ordinal);
            bool v311DefaultDisabled =
                graphHandshakeDefaultOptions.Enabled == false &&
                v311Doc.Contains("default_enabled=false", StringComparison.Ordinal);
            bool v311RuntimeStarted =
                graphHandshakeDefaultHttpOptions.RuntimeStarted ||
                graphHandshakeDefaultStreamOptions.RuntimeStarted ||
                v311Doc.Contains("starts_runtime=true", StringComparison.Ordinal) ||
                v311Sidecar.Contains("Process.Start", StringComparison.Ordinal) ||
                v311Sidecar.Contains("subprocess", StringComparison.Ordinal);
            bool v311DefaultTestsLiveRuntime =
                v311Doc.Contains("default_tests_live_runtime=false", StringComparison.Ordinal) &&
                v311RuntimeStarted == false;
            bool v311NoSqlAuthority =
                v311Doc.Contains("no_sql_authority=true", StringComparison.Ordinal) &&
                v311Sidecar.Contains("\"NoSqlAuthority\": True", StringComparison.Ordinal) &&
                v311Sidecar.Contains("\"RequestedToolNames\": []", StringComparison.Ordinal);
            bool v311LangGraphHook =
                v311Sidecar.Contains("from langgraph.graph import END, StateGraph", StringComparison.Ordinal) &&
                v311Sidecar.Contains("StateGraph(dict)", StringComparison.Ordinal) &&
                v311Sidecar.Contains("workflow.compile()", StringComparison.Ordinal);
            bool v311Fallback =
                v311Doc.Contains("fallback_required=true", StringComparison.Ordinal) &&
                v311Sidecar.Contains("\"FallbackRequired\": True", StringComparison.Ordinal);
            bool v311SkeletonReady =
                v311DocExists &&
                v311SidecarExists &&
                v311ReadmeExists &&
                v311ManualOnly &&
                v311LoopbackOnly &&
                v311DefaultDisabled &&
                v311DefaultTestsLiveRuntime &&
                v311NoSqlAuthority &&
                v311LangGraphHook &&
                v311Fallback;
            checks.Add(v311SkeletonReady
                ? Pass("GraphHandshakeRealLangGraphSidecarSkeletonPresent", "manual_only=true;loopback_only=true;default_enabled=false;runtime_started=false;default_tests_live_runtime=false;no_sql_authority=true;langgraph_hook=true;fallback=true")
                : Fail("GraphHandshakeRealLangGraphSidecarSkeletonPresent", $"doc={LowerBool(v311DocExists)};sidecar={LowerBool(v311SidecarExists)};readme={LowerBool(v311ReadmeExists)};manual_only={LowerBool(v311ManualOnly)};loopback_only={LowerBool(v311LoopbackOnly)};default_enabled={LowerBool(v311DefaultDisabled)};runtime_started={LowerBool(v311RuntimeStarted)};default_tests_live_runtime={LowerBool(v311DefaultTestsLiveRuntime)};no_sql_authority={LowerBool(v311NoSqlAuthority)};langgraph_hook={LowerBool(v311LangGraphHook)};fallback={LowerBool(v311Fallback)}"));

            string v312DocPath = Path.Combine(v311RepoRoot, "docs", "dataagent", "dataagent-v3.12-replay-parity-shadow-comparison.md");
            bool v312DocExists = File.Exists(v312DocPath);
            string v312Doc = v312DocExists ? File.ReadAllText(v312DocPath) : string.Empty;
            bool v312ComparisonModelReady =
                typeof(DataAgentGraphHandshakeShadowComparison).IsClass &&
                typeof(DataAgentGraphHandshakeShadowComparisonStatus).IsEnum &&
                typeof(DataAgentGraphHandshakeShadowComparer).IsClass &&
                typeof(DataAgentGraphHandshakeShadowComparisonFormatter).IsClass;
            bool v312ReportReady =
                typeof(DataAgentGraphHandshakeShadowComparisonReport).IsClass &&
                typeof(DataAgentGraphHandshakeShadowComparisonReportFormatter).IsClass;
            bool v312CategoryReady =
                Enum.IsDefined(typeof(DataAgentGraphHandshakeShadowComparisonStatus), DataAgentGraphHandshakeShadowComparisonStatus.Match) &&
                Enum.IsDefined(typeof(DataAgentGraphHandshakeShadowComparisonStatus), DataAgentGraphHandshakeShadowComparisonStatus.AcceptedAdvisoryDifference) &&
                Enum.IsDefined(typeof(DataAgentGraphHandshakeShadowComparisonStatus), DataAgentGraphHandshakeShadowComparisonStatus.RejectedAuthorityClaim) &&
                Enum.IsDefined(typeof(DataAgentGraphHandshakeShadowComparisonStatus), DataAgentGraphHandshakeShadowComparisonStatus.FallbackUsed) &&
                Enum.IsDefined(typeof(DataAgentGraphHandshakeShadowComparisonStatus), DataAgentGraphHandshakeShadowComparisonStatus.InvalidSchema) &&
                Enum.IsDefined(typeof(DataAgentGraphHandshakeShadowComparisonStatus), DataAgentGraphHandshakeShadowComparisonStatus.TimeoutOrTransportFailure);
            bool v312ShadowOnly =
                v312Doc.Contains("shadow_only=true", StringComparison.Ordinal) &&
                v312Doc.Contains("default_result_changed=false", StringComparison.Ordinal);
            bool v312ReplayParity =
                v312Doc.Contains("replay_parity_required=true", StringComparison.Ordinal) &&
                v312Doc.Contains("accepted_advisory_difference", StringComparison.Ordinal) &&
                v312Doc.Contains("rejected_authority_claim", StringComparison.Ordinal) &&
                v312Doc.Contains("timeout_or_transport_failure", StringComparison.Ordinal);
            bool v312NoAuthority =
                v312Doc.Contains("no_sql_authority=true", StringComparison.Ordinal) &&
                v312Doc.Contains("no_checkpoint_mutation=true", StringComparison.Ordinal) &&
                v312Doc.Contains("no_visible_text=true", StringComparison.Ordinal) &&
                v312Doc.Contains("fallback_required=true", StringComparison.Ordinal);
            bool v312Ready =
                v312DocExists &&
                v312ComparisonModelReady &&
                v312ReportReady &&
                v312CategoryReady &&
                v312ShadowOnly &&
                v312ReplayParity &&
                v312NoAuthority;
            checks.Add(v312Ready
                ? Pass("GraphHandshakeReplayParityShadowComparisonPresent", "shadow_only=true;default_result_changed=false;replay_parity_required=true;categories=true;report=true;no_sql_authority=true;fallback=true")
                : Fail("GraphHandshakeReplayParityShadowComparisonPresent", $"doc={LowerBool(v312DocExists)};comparison_model={LowerBool(v312ComparisonModelReady)};report={LowerBool(v312ReportReady)};categories={LowerBool(v312CategoryReady)};shadow_only={LowerBool(v312ShadowOnly)};replay_parity={LowerBool(v312ReplayParity)};no_authority={LowerBool(v312NoAuthority)}"));

            string v313DocPath = Path.Combine(v311RepoRoot, "docs", "dataagent", "dataagent-v3.13-bounded-diagnostics-explanation.md");
            bool v313DocExists = File.Exists(v313DocPath);
            string v313Doc = v313DocExists ? File.ReadAllText(v313DocPath) : string.Empty;
            bool v313ModelReady =
                typeof(DataAgentGraphHandshakeDiagnosticExplanationResult).IsClass &&
                typeof(DataAgentGraphHandshakeDiagnosticExplanationValidator).IsClass &&
                typeof(DataAgentGraphHandshakeDiagnosticExplanationFormatter).IsClass;
            DataAgentGraphHandshakeDiagnosticExplanationResult v313SafeExplanation =
                DataAgentGraphHandshakeDiagnosticExplanationValidator.Validate("safe advisory explanation", "accepted_advisory_difference");
            DataAgentGraphHandshakeDiagnosticExplanationResult v313UnsafeExplanation =
                DataAgentGraphHandshakeDiagnosticExplanationValidator.Validate("SELECT * FROM hidden_context", "unsafe_case");
            bool v313BoundedExplanation =
                v313Doc.Contains("bounded_explanation=true", StringComparison.Ordinal) &&
                DataAgentGraphHandshakeDiagnosticExplanationValidator.MaxExplanationChars <= 320;
            bool v313AdvisoryOnly =
                v313Doc.Contains("advisory_only=true", StringComparison.Ordinal) &&
                v313SafeExplanation.DefaultResultChanged == false;
            bool v313AuthorityReady =
                v313Doc.Contains("csharp_write_authority=true", StringComparison.Ordinal) &&
                v313Doc.Contains("sidecar_write_authority=false", StringComparison.Ordinal) &&
                v313Doc.Contains("requests_visible_text=false", StringComparison.Ordinal) &&
                v313SafeExplanation.CSharpWriteAuthority &&
                v313SafeExplanation.SidecarWriteAuthority == false &&
                v313SafeExplanation.RequestsVisibleText == false;
            bool v313UnsafeRejected =
                v313Doc.Contains("unsafe_text_rejected=true", StringComparison.Ordinal) &&
                v313UnsafeExplanation.Accepted == false &&
                string.Equals(v313UnsafeExplanation.ReasonCode, "diagnostic_explanation_unsafe", StringComparison.Ordinal);
            bool v313Fallback =
                v313Doc.Contains("fallback_required=true", StringComparison.Ordinal);
            bool v313Ready =
                v313DocExists &&
                v313ModelReady &&
                v313BoundedExplanation &&
                v313AdvisoryOnly &&
                v313AuthorityReady &&
                v313UnsafeRejected &&
                v313Fallback;
            checks.Add(v313Ready
                ? Pass("GraphHandshakeBoundedDiagnosticsExplanationPresent", "bounded_explanation=true;advisory_only=true;csharp_write_authority=true;sidecar_write_authority=false;requests_visible_text=false;unsafe_text_rejected=true;fallback=true")
                : Fail("GraphHandshakeBoundedDiagnosticsExplanationPresent", $"doc={LowerBool(v313DocExists)};model={LowerBool(v313ModelReady)};bounded_explanation={LowerBool(v313BoundedExplanation)};advisory_only={LowerBool(v313AdvisoryOnly)};authority={LowerBool(v313AuthorityReady)};unsafe_text_rejected={LowerBool(v313UnsafeRejected)};fallback={LowerBool(v313Fallback)}"));

            string v314DocPath = Path.Combine(v311RepoRoot, "docs", "dataagent", "dataagent-v3.14-cross-module-planner-manifests.md");
            bool v314DocExists = File.Exists(v314DocPath);
            string v314Doc = v314DocExists ? File.ReadAllText(v314DocPath) : string.Empty;
            IReadOnlyList<DataAgentCrossModulePlannerManifest> v314Manifests =
                DataAgentCrossModulePlannerManifestFactory.CreateDefault();
            bool v314ManifestModelReady =
                typeof(DataAgentCrossModulePlannerManifest).IsClass &&
                typeof(DataAgentCrossModulePlannerManifestFactory).IsClass &&
                typeof(DataAgentCrossModulePlannerManifestValidator).IsClass;
            bool v314PlannerOnly =
                v314Doc.Contains("planner_only=true", StringComparison.Ordinal) &&
                v314Manifests.Count == 6 &&
                v314Manifests.All(manifest => manifest.PlannerOnly);
            bool v314CrossModuleAdvisory =
                v314Doc.Contains("cross_module_advisory=true", StringComparison.Ordinal) &&
                v314Manifests.Select(manifest => manifest.CapabilityName).SequenceEqual(
                    [
                        "qchat.intent_hint",
                        "memory.candidate_summary",
                        "browser.task_plan",
                        "desktop.task_plan",
                        "emotion.expression_hint",
                        "deskpet.expression_hint"
                    ],
                    StringComparer.Ordinal);
            bool v314AuthorityDenied =
                v314Doc.Contains("allows_execution=false", StringComparison.Ordinal) &&
                v314Doc.Contains("allows_state_write=false", StringComparison.Ordinal) &&
                v314Doc.Contains("allows_visible_text=false", StringComparison.Ordinal) &&
                v314Manifests.All(manifest =>
                    manifest.AllowsExecution == false &&
                    manifest.AllowsStateWrite == false &&
                    manifest.AllowsVisibleText == false);
            bool v314DeniedMarkers =
                v314Manifests.All(manifest =>
                    DataAgentCrossModulePlannerManifestValidator.Validate(manifest).Accepted &&
                    DataAgentCrossModulePlannerManifestValidator.RequiredDeniedCapabilityMarkers.All(required =>
                        manifest.DeniedCapabilityMarkers.Contains(required, StringComparer.Ordinal)));
            bool v314Fallback =
                v314Doc.Contains("fallback_required=true", StringComparison.Ordinal);
            bool v314Ready =
                v314DocExists &&
                v314ManifestModelReady &&
                v314PlannerOnly &&
                v314CrossModuleAdvisory &&
                v314AuthorityDenied &&
                v314DeniedMarkers &&
                v314Fallback;
            checks.Add(v314Ready
                ? Pass("GraphHandshakeCrossModulePlannerManifestsPresent", "planner_only=true;cross_module_advisory=true;allows_execution=false;allows_state_write=false;allows_visible_text=false;denied_markers=true;fallback=true")
                : Fail("GraphHandshakeCrossModulePlannerManifestsPresent", $"doc={LowerBool(v314DocExists)};model={LowerBool(v314ManifestModelReady)};planner_only={LowerBool(v314PlannerOnly)};cross_module_advisory={LowerBool(v314CrossModuleAdvisory)};authority_denied={LowerBool(v314AuthorityDenied)};denied_markers={LowerBool(v314DeniedMarkers)};fallback={LowerBool(v314Fallback)}"));

            string v315DocPath = Path.Combine(v311RepoRoot, "docs", "dataagent", "dataagent-v3.15-authority-fallback-regression.md");
            bool v315DocExists = File.Exists(v315DocPath);
            string v315Doc = v315DocExists ? File.ReadAllText(v315DocPath) : string.Empty;
            DataAgentGraphSidecarPolicy v315Policy = DataAgentGraphSidecarPolicy.CreateDefault();
            DataAgentGraphSidecarAuthority[] v315ForbiddenAuthorities =
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
            bool v315AuthorityRegression =
                v315Doc.Contains("authority_regression=true", StringComparison.Ordinal) &&
                v315ForbiddenAuthorities.All(authority => v315Policy.Forbids(authority));
            bool v315ForbiddenAuthoritiesRejected =
                v315Doc.Contains("forbidden_authorities_rejected=true", StringComparison.Ordinal) &&
                v315ForbiddenAuthorities.All(authority =>
                    DataAgentGraphSidecarContract.IsResponseSafe(
                        new DataAgentGraphSidecarResponse(
                            WorkflowId: "readiness-workflow",
                            Accepted: true,
                            ReasonCode: "authority_claimed",
                            Message: "advisory response",
                            ProposedNodeKind: DataAgentGraphSidecarNodeKind.QueryPlanner,
                            RequestedCapabilityName: null,
                            RequiresCSharpSafetyService: false,
                            Trace: ["QueryPlanner:AdvisoryOnly"],
                            ClaimedAuthorities: [authority]),
                        v315Policy) == false);
            DataAgentGraphHandshakeRequest v315Request = new(
                "readiness-request",
                "readiness-session",
                "turn-1",
                "owner",
                "Which gates failed?",
                "scenario_context=ready",
                "route_present=true",
                "status=Active",
                DataAgentGraphHandshakeManifestFactory.CreateDefault(),
                NoSqlAuthority: true,
                ReadOnly: true,
                FallbackAvailable: true,
                TraceBudgetChars: 128,
                ProgressBudget: 2);
            DataAgentGraphHandshakeResponse v315SafeResponse = new(
                RequestId: v315Request.RequestId,
                Accepted: true,
                ReasonCode: "handshake_accepted",
                SelectedNodes: [DataAgentWorkflowNodeNames.QueryPlanner],
                NodeProgress:
                [
                    new DataAgentGraphHandshakeProgress(
                        DataAgentWorkflowNodeNames.QueryPlanner,
                        DataAgentGraphHandshakeProgressStatus.Completed,
                        "planner_suggested")
                ],
                TraceSummary: "QueryPlanner:Completed",
                ContextContribution: "graph_handshake=accepted",
                FallbackRequired: false,
                NoSqlAuthority: true,
                ReadOnly: true,
                RequestedToolNames: [DataAgentGraphHandshakeToolNames.ProposeQueryPlan],
                RequestsCheckpointMutation: false,
                RequestsVisibleText: false);
            bool v315HandshakeRegression =
                DataAgentGraphHandshakeValidator.Validate(v315Request, v315SafeResponse with { NoSqlAuthority = false }).ReasonCode == "sql_authority_requested" &&
                DataAgentGraphHandshakeValidator.Validate(v315Request, v315SafeResponse with { ReadOnly = false }).ReasonCode == "sql_authority_requested" &&
                DataAgentGraphHandshakeValidator.Validate(v315Request, v315SafeResponse with { RequestsCheckpointMutation = true }).ReasonCode == "checkpoint_mutation_requested" &&
                DataAgentGraphHandshakeValidator.Validate(v315Request, v315SafeResponse with { RequestsVisibleText = true }).ReasonCode == "visible_text_requested";
            bool v315Fallback =
                v315Doc.Contains("fallback_required=true", StringComparison.Ordinal) &&
                v315Doc.Contains("default_result_changed=false", StringComparison.Ordinal);
            bool v315NoAuthority =
                v315Doc.Contains("no_sql_authority=true", StringComparison.Ordinal) &&
                v315Doc.Contains("no_visible_text=true", StringComparison.Ordinal) &&
                v315Policy.NoSqlAuthority &&
                v315Policy.NoVisibleTextAuthority;
            bool v315Ready =
                v315DocExists &&
                v315AuthorityRegression &&
                v315ForbiddenAuthoritiesRejected &&
                v315HandshakeRegression &&
                v315Fallback &&
                v315NoAuthority;
            checks.Add(v315Ready
                ? Pass("GraphHandshakeAuthorityFallbackRegressionPresent", "authority_regression=true;forbidden_authorities_rejected=true;fallback_required=true;default_result_changed=false;no_sql_authority=true;no_visible_text=true")
                : Fail("GraphHandshakeAuthorityFallbackRegressionPresent", $"doc={LowerBool(v315DocExists)};authority_regression={LowerBool(v315AuthorityRegression)};forbidden_authorities_rejected={LowerBool(v315ForbiddenAuthoritiesRejected)};handshake_regression={LowerBool(v315HandshakeRegression)};fallback_required={LowerBool(v315Fallback)};no_authority={LowerBool(v315NoAuthority)}"));

            string v316DocPath = Path.Combine(v311RepoRoot, "docs", "dataagent", "dataagent-v3.16-langgraph-live-smoke-readiness.md");
            bool v316DocExists = File.Exists(v316DocPath);
            string v316Doc = v316DocExists ? File.ReadAllText(v316DocPath) : string.Empty;
            bool v316OperatorRunbook =
                v316Doc.Contains("operator_runbook=true", StringComparison.Ordinal) &&
                v316Doc.Contains("how to start sidecar manually", StringComparison.Ordinal) &&
                v316Doc.Contains("how to verify loopback binding", StringComparison.Ordinal) &&
                v316Doc.Contains("how to run smoke tests", StringComparison.Ordinal) &&
                v316Doc.Contains("how to inspect diagnostics", StringComparison.Ordinal) &&
                v316Doc.Contains("how to stop sidecar", StringComparison.Ordinal) &&
                v316Doc.Contains("how to confirm fallback works", StringComparison.Ordinal) &&
                v316Doc.Contains("how to prove default chain is unchanged", StringComparison.Ordinal);
            bool v316ManualStart =
                v316Doc.Contains("manual_start=true", StringComparison.Ordinal) &&
                v311Doc.Contains("manual_only=true", StringComparison.Ordinal) &&
                v311Readme.Contains("manual-only", StringComparison.OrdinalIgnoreCase);
            bool v316LoopbackCheck =
                v316Doc.Contains("loopback_check=true", StringComparison.Ordinal) &&
                v311Doc.Contains("loopback_only=true", StringComparison.Ordinal) &&
                v311Sidecar.Contains("Only loopback hosts are allowed.", StringComparison.Ordinal);
            bool v316SmokeCoverage =
                v316Doc.Contains("smoke_valid_advisory=true", StringComparison.Ordinal) &&
                v316Doc.Contains("smoke_forbidden_authority_rejected=true", StringComparison.Ordinal) &&
                v316Doc.Contains("smoke_timeout_fallback=true", StringComparison.Ordinal) &&
                v316Doc.Contains("NoSqlAuthority=true", StringComparison.Ordinal) &&
                v316Doc.Contains("FallbackRequired=true", StringComparison.Ordinal);
            bool v316KillSwitch =
                v316Doc.Contains("kill_switch=true", StringComparison.Ordinal) &&
                graphHandshakeDefaultOptions.Enabled == false;
            bool v316DefaultTestsNoLiveRuntime =
                v316Doc.Contains("default_tests_live_runtime=false", StringComparison.Ordinal) &&
                v316Doc.Contains("starts_runtime=false", StringComparison.Ordinal) &&
                v316Doc.Contains("installs_dependencies=false", StringComparison.Ordinal) &&
                graphHandshakeDefaultHttpOptions.RuntimeStarted == false &&
                graphHandshakeDefaultStreamOptions.RuntimeStarted == false &&
                v311Sidecar.Contains("Process.Start", StringComparison.Ordinal) == false &&
                v311Sidecar.Contains("subprocess", StringComparison.Ordinal) == false &&
                v311Sidecar.Contains("pip install", StringComparison.Ordinal) == false;
            bool v316Ready =
                v316DocExists &&
                v316OperatorRunbook &&
                v316ManualStart &&
                v316LoopbackCheck &&
                v316SmokeCoverage &&
                v316KillSwitch &&
                v316DefaultTestsNoLiveRuntime;
            checks.Add(v316Ready
                ? Pass("GraphHandshakeLangGraphLiveSmokeReadinessPresent", "operator_runbook=true;manual_start=true;loopback_check=true;smoke_valid_advisory=true;smoke_forbidden_authority_rejected=true;smoke_timeout_fallback=true;kill_switch=true;default_tests_live_runtime=false;starts_runtime=false;installs_dependencies=false")
                : Fail("GraphHandshakeLangGraphLiveSmokeReadinessPresent", $"doc={LowerBool(v316DocExists)};operator_runbook={LowerBool(v316OperatorRunbook)};manual_start={LowerBool(v316ManualStart)};loopback_check={LowerBool(v316LoopbackCheck)};smoke_coverage={LowerBool(v316SmokeCoverage)};kill_switch={LowerBool(v316KillSwitch)};default_tests_live_runtime={LowerBool(v316DefaultTestsNoLiveRuntime)}"));

            string v317DocPath = Path.Combine(v311RepoRoot, "docs", "dataagent", "dataagent-v3.17-langgraph-manual-smoke.md");
            string v317ScriptPath = Path.Combine(v311RepoRoot, "tools", "run-dataagent-langgraph-manual-smoke.ps1");
            bool v317DocExists = File.Exists(v317DocPath);
            bool v317ScriptExists = File.Exists(v317ScriptPath);
            string v317Doc = v317DocExists ? File.ReadAllText(v317DocPath) : string.Empty;
            string v317Script = v317ScriptExists ? File.ReadAllText(v317ScriptPath) : string.Empty;
            bool v317OperatorOnly =
                v317Doc.Contains("manual_smoke=true", StringComparison.Ordinal) &&
                v317Doc.Contains("operator_only=true", StringComparison.Ordinal) &&
                v317Script.Contains("manual_only=true", StringComparison.Ordinal);
            bool v317DefaultUnchanged =
                v317Doc.Contains("default_result_changed=false", StringComparison.Ordinal) &&
                v317Script.Contains("starts_runtime=false", StringComparison.Ordinal) &&
                v317Script.Contains("installs_dependencies=false", StringComparison.Ordinal) &&
                v317Script.Contains("creates_venv=false", StringComparison.Ordinal) &&
                v317Script.Contains("binds_port=false", StringComparison.Ordinal);
            bool v317AuthorityBoundary =
                v317Doc.Contains("sidecar_write_authority=false", StringComparison.Ordinal) &&
                v317Doc.Contains("csharp_execution_authority=true", StringComparison.Ordinal) &&
                v317Doc.Contains("fallback_required=true", StringComparison.Ordinal);
            bool v317ManualOnly =
                v317Doc.Contains("manual_only=true", StringComparison.Ordinal) &&
                v317Script.Contains("manual_only=true", StringComparison.Ordinal) &&
                v317Script.Contains("Start-Process", StringComparison.Ordinal) == false &&
                v317Script.Contains("pip install", StringComparison.Ordinal) == false &&
                v317Script.Contains("python -m venv", StringComparison.Ordinal) == false &&
                v317Script.Contains("uvicorn", StringComparison.Ordinal) == false;
            bool v317LoopbackOnly =
                v317Doc.Contains("loopback_only=true", StringComparison.Ordinal) &&
                v317Script.Contains("loopback_only=true", StringComparison.Ordinal) &&
                v317Script.Contains("Only loopback hosts are allowed.", StringComparison.Ordinal);
            bool v317SmokeMarkers =
                v317Script.Contains("smoke_valid_advisory=true", StringComparison.Ordinal) &&
                v317Script.Contains("smoke_forbidden_authority_rejected=true", StringComparison.Ordinal) &&
                v317Script.Contains("smoke_timeout_fallback=true", StringComparison.Ordinal);
            bool v317Ready =
                v317DocExists &&
                v317ScriptExists &&
                v317OperatorOnly &&
                v317DefaultUnchanged &&
                v317AuthorityBoundary &&
                v317ManualOnly &&
                v317LoopbackOnly &&
                v317SmokeMarkers;
            checks.Add(v317Ready
                ? Pass("GraphHandshakeLangGraphManualSmokeHarnessPresent", "manual_smoke=true;operator_only=true;default_result_changed=false;sidecar_write_authority=false;csharp_execution_authority=true;fallback_required=true;manual_only=true;loopback_only=true")
                : Fail("GraphHandshakeLangGraphManualSmokeHarnessPresent", $"doc={LowerBool(v317DocExists)};script={LowerBool(v317ScriptExists)};operator_only={LowerBool(v317OperatorOnly)};default_unchanged={LowerBool(v317DefaultUnchanged)};authority_boundary={LowerBool(v317AuthorityBoundary)};manual_only={LowerBool(v317ManualOnly)};loopback_only={LowerBool(v317LoopbackOnly)};smoke_markers={LowerBool(v317SmokeMarkers)}"));

            string v318DocPath = Path.Combine(v311RepoRoot, "docs", "dataagent", "dataagent-v3.18-smoke-result-artifact.md");
            string v318ScriptPath = Path.Combine(v311RepoRoot, "tools", "format-dataagent-langgraph-smoke-result.ps1");
            bool v318DocExists = File.Exists(v318DocPath);
            bool v318ScriptExists = File.Exists(v318ScriptPath);
            string v318Doc = v318DocExists ? File.ReadAllText(v318DocPath) : string.Empty;
            string v318Script = v318ScriptExists ? File.ReadAllText(v318ScriptPath) : string.Empty;
            bool v318ArtifactFormatter =
                v318Doc.Contains("smoke_result_artifact=true", StringComparison.Ordinal) &&
                v318Doc.Contains("artifact_formatter=true", StringComparison.Ordinal) &&
                v318Script.Contains("artifact_formatter=true", StringComparison.Ordinal);
            bool v318ManualOnly =
                v318Doc.Contains("manual_only=true", StringComparison.Ordinal) &&
                v318Script.Contains("manual_only=true", StringComparison.Ordinal) &&
                v318Script.Contains("Start-Process", StringComparison.Ordinal) == false &&
                v318Script.Contains("pip install", StringComparison.Ordinal) == false &&
                v318Script.Contains("python -m venv", StringComparison.Ordinal) == false &&
                v318Script.Contains("uvicorn", StringComparison.Ordinal) == false;
            bool v318StorageBoundary =
                v318Doc.Contains("stores_secrets=false", StringComparison.Ordinal) &&
                v318Doc.Contains("stores_sql=false", StringComparison.Ordinal) &&
                v318Doc.Contains("stores_hidden_context=false", StringComparison.Ordinal) &&
                v318Script.Contains("stores_secrets=false", StringComparison.Ordinal) &&
                v318Script.Contains("stores_sql=false", StringComparison.Ordinal) &&
                v318Script.Contains("stores_hidden_context=false", StringComparison.Ordinal);
            bool v318SanitizesUnsafeText =
                v318Doc.Contains("sanitizes_unsafe_text=true", StringComparison.Ordinal) &&
                v318Script.Contains("sanitizes_unsafe_text=true", StringComparison.Ordinal) &&
                v318Script.Contains("unsafe_text_redacted", StringComparison.Ordinal) &&
                v318Script.Contains("redacted", StringComparison.Ordinal);
            bool v318DefaultUnchanged =
                v318Doc.Contains("default_result_changed=false", StringComparison.Ordinal);
            bool v318Ready =
                v318DocExists &&
                v318ScriptExists &&
                v318ArtifactFormatter &&
                v318ManualOnly &&
                v318StorageBoundary &&
                v318SanitizesUnsafeText &&
                v318DefaultUnchanged;
            checks.Add(v318Ready
                ? Pass("GraphHandshakeSmokeResultArtifactFormatterPresent", "smoke_result_artifact=true;artifact_formatter=true;manual_only=true;stores_secrets=false;stores_sql=false;stores_hidden_context=false;sanitizes_unsafe_text=true;default_result_changed=false")
                : Fail("GraphHandshakeSmokeResultArtifactFormatterPresent", $"doc={LowerBool(v318DocExists)};script={LowerBool(v318ScriptExists)};artifact_formatter={LowerBool(v318ArtifactFormatter)};manual_only={LowerBool(v318ManualOnly)};storage_boundary={LowerBool(v318StorageBoundary)};sanitizes_unsafe_text={LowerBool(v318SanitizesUnsafeText)};default_result_changed={LowerBool(v318DefaultUnchanged)}"));

            string v319DocPath = Path.Combine(v311RepoRoot, "docs", "dataagent", "dataagent-v3.19-replay-fixture-pack.md");
            string v319FixtureDirectory = Path.Combine(v311RepoRoot, "Tests", "Alife.Test.DataAgent", "Fixtures", "DataAgent", "V319Replay");
            bool v319DocExists = File.Exists(v319DocPath);
            string v319Doc = v319DocExists ? File.ReadAllText(v319DocPath) : string.Empty;
            string[] v319FixtureIds =
            [
                "successful_advisory",
                "rejected_authority",
                "timeout_fallback",
                "invalid_schema"
            ];
            bool v319FixturesExist = v319FixtureIds.All(id => File.Exists(Path.Combine(v319FixtureDirectory, $"{id}.json")));
            bool v319DocMarkers =
                v319Doc.Contains("replay_fixture_pack=true", StringComparison.Ordinal) &&
                v319Doc.Contains("successful_advisory=true", StringComparison.Ordinal) &&
                v319Doc.Contains("rejected_authority=true", StringComparison.Ordinal) &&
                v319Doc.Contains("timeout_fallback=true", StringComparison.Ordinal) &&
                v319Doc.Contains("invalid_schema=true", StringComparison.Ordinal);
            bool v319Boundary =
                v319Doc.Contains("default_result_changed=false", StringComparison.Ordinal) &&
                v319Doc.Contains("stores_secrets=false", StringComparison.Ordinal) &&
                v319Doc.Contains("stores_sql=false", StringComparison.Ordinal);
            bool v319FixtureMarkers = v319FixturesExist && v319FixtureIds.All(id =>
            {
                string fixtureText = File.ReadAllText(Path.Combine(v319FixtureDirectory, $"{id}.json"));
                return fixtureText.Contains("\"replay_fixture_pack\": true", StringComparison.Ordinal) &&
                       fixtureText.Contains("\"default_result_changed\": false", StringComparison.Ordinal) &&
                       fixtureText.Contains("\"no_sql_authority\": true", StringComparison.Ordinal) &&
                       fixtureText.Contains("\"fallback_required\": true", StringComparison.Ordinal) &&
                       fixtureText.Contains("SELECT", StringComparison.OrdinalIgnoreCase) == false &&
                       fixtureText.Contains("hidden_context", StringComparison.OrdinalIgnoreCase) == false &&
                       fixtureText.Contains("bearer", StringComparison.OrdinalIgnoreCase) == false &&
                       fixtureText.Contains("secret", StringComparison.OrdinalIgnoreCase) == false;
            });
            bool v319Ready =
                v319DocExists &&
                v319DocMarkers &&
                v319Boundary &&
                v319FixtureMarkers;
            checks.Add(v319Ready
                ? Pass("GraphHandshakeReplayFixturePackPresent", "replay_fixture_pack=true;successful_advisory=true;rejected_authority=true;timeout_fallback=true;invalid_schema=true;default_result_changed=false;stores_secrets=false;stores_sql=false")
                : Fail("GraphHandshakeReplayFixturePackPresent", $"doc={LowerBool(v319DocExists)};fixtures={LowerBool(v319FixturesExist)};doc_markers={LowerBool(v319DocMarkers)};boundary={LowerBool(v319Boundary)};fixture_markers={LowerBool(v319FixtureMarkers)}"));

            string v320DocPath = Path.Combine(v311RepoRoot, "docs", "dataagent", "dataagent-v3.20-shadow-replay-report.md");
            bool v320DocExists = File.Exists(v320DocPath);
            string v320Doc = v320DocExists ? File.ReadAllText(v320DocPath) : string.Empty;
            DataAgentGraphHandshakeReplayReport v320SampleReport =
                DataAgentGraphHandshakeReplayReportConsolidator.Create(
                    "v3.20-readiness",
                    [
                        new DataAgentGraphHandshakeReplayInput(
                            "successful_advisory",
                            new DataAgentGraphHandshakeOutcome(
                                DataAgentGraphHandshakeStatus.Disabled,
                                "sidecar_disabled",
                                true,
                                Request: null,
                                Response: null,
                                new DataAgentGraphHandshakeValidationResult(false, "sidecar_disabled")),
                            new DataAgentGraphHandshakeOutcome(
                                DataAgentGraphHandshakeStatus.Accepted,
                                "handshake_accepted",
                                false,
                                Request: null,
                                Response: null,
                                new DataAgentGraphHandshakeValidationResult(true, "handshake_accepted")))
                    ]);
            string v320SampleMarkdown = DataAgentGraphHandshakeReplayReportFormatter.FormatMarkdown(v320SampleReport);
            bool v320DocMarkers =
                v320Doc.Contains("shadow_replay_report=true", StringComparison.Ordinal) &&
                v320Doc.Contains("replay_fixture_pack=true", StringComparison.Ordinal) &&
                v320Doc.Contains("source_fixture_pack=v3.19", StringComparison.Ordinal) &&
                v320Doc.Contains("shadow_only=true", StringComparison.Ordinal);
            bool v320Boundary =
                v320Doc.Contains("default_result_changed=false", StringComparison.Ordinal) &&
                v320Doc.Contains("starts_runtime=false", StringComparison.Ordinal) &&
                v320Doc.Contains("stores_secrets=false", StringComparison.Ordinal) &&
                v320Doc.Contains("stores_sql=false", StringComparison.Ordinal) &&
                v320Doc.Contains("stores_hidden_context=false", StringComparison.Ordinal);
            bool v320ModelReady =
                typeof(DataAgentGraphHandshakeReplayInput).IsClass &&
                typeof(DataAgentGraphHandshakeReplayReport).IsClass &&
                typeof(DataAgentGraphHandshakeReplayReportConsolidator).IsClass &&
                typeof(DataAgentGraphHandshakeReplayReportFormatter).IsClass &&
                v320SampleReport.ComparisonCount == 1 &&
                v320SampleReport.DefaultResultChanged == false &&
                v320SampleReport.Passed;
            bool v320FormatterReady =
                v320SampleMarkdown.Contains("shadow_replay_report=true", StringComparison.Ordinal) &&
                v320SampleMarkdown.Contains("replay_fixture_pack=true", StringComparison.Ordinal) &&
                v320SampleMarkdown.Contains("source_fixture_pack=v3.19", StringComparison.Ordinal) &&
                v320SampleMarkdown.Contains("starts_runtime=false", StringComparison.Ordinal) &&
                v320SampleMarkdown.Contains("stores_secrets=false", StringComparison.Ordinal) &&
                v320SampleMarkdown.Contains("stores_sql=false", StringComparison.Ordinal) &&
                v320SampleMarkdown.Contains("fixture_successful_advisory=accepted_advisory_difference", StringComparison.Ordinal) &&
                v320SampleMarkdown.Contains("SELECT", StringComparison.OrdinalIgnoreCase) == false &&
                v320SampleMarkdown.Contains("bearer", StringComparison.OrdinalIgnoreCase) == false;
            bool v320Ready =
                v320DocExists &&
                v320DocMarkers &&
                v320Boundary &&
                v320ModelReady &&
                v320FormatterReady;
            checks.Add(v320Ready
                ? Pass("GraphHandshakeShadowReplayReportPresent", "shadow_replay_report=true;replay_fixture_pack=true;source_fixture_pack=v3.19;shadow_only=true;default_result_changed=false;starts_runtime=false;stores_secrets=false;stores_sql=false;stores_hidden_context=false")
                : Fail("GraphHandshakeShadowReplayReportPresent", $"doc={LowerBool(v320DocExists)};doc_markers={LowerBool(v320DocMarkers)};boundary={LowerBool(v320Boundary)};model={LowerBool(v320ModelReady)};formatter={LowerBool(v320FormatterReady)}"));

            string v321DocPath = Path.Combine(v311RepoRoot, "docs", "dataagent", "dataagent-v3.21-manual-replay-report-artifact.md");
            bool v321DocExists = File.Exists(v321DocPath);
            string v321Doc = v321DocExists ? File.ReadAllText(v321DocPath) : string.Empty;
            string v321ArtifactPath = Path.Combine(
                Path.GetDirectoryName(databasePath) ?? Path.GetTempPath(),
                $"dataagent-v321-readiness-{Guid.NewGuid():N}.md");
            DataAgentGraphHandshakeReplayReportArtifact? v321Artifact = null;
            string v321ArtifactText = string.Empty;
            try
            {
                v321Artifact = DataAgentGraphHandshakeReplayReportArtifactWriter.Write(v320SampleReport, v321ArtifactPath);
                v321ArtifactText = File.ReadAllText(v321ArtifactPath);
            }
            finally
            {
                if (File.Exists(v321ArtifactPath))
                    File.Delete(v321ArtifactPath);
            }

            bool v321DocMarkers =
                v321Doc.Contains("manual_replay_report_artifact=true", StringComparison.Ordinal) &&
                v321Doc.Contains("artifact_writer=true", StringComparison.Ordinal) &&
                v321Doc.Contains("manual_only=true", StringComparison.Ordinal);
            bool v321Boundary =
                v321Doc.Contains("starts_runtime=false", StringComparison.Ordinal) &&
                v321Doc.Contains("installs_dependencies=false", StringComparison.Ordinal) &&
                v321Doc.Contains("default_result_changed=false", StringComparison.Ordinal) &&
                v321Doc.Contains("stores_secrets=false", StringComparison.Ordinal) &&
                v321Doc.Contains("stores_sql=false", StringComparison.Ordinal) &&
                v321Doc.Contains("stores_hidden_context=false", StringComparison.Ordinal);
            bool v321ModelReady =
                typeof(DataAgentGraphHandshakeReplayReportArtifact).IsClass &&
                typeof(DataAgentGraphHandshakeReplayReportArtifactWriter).IsClass &&
                v321Artifact is not null &&
                v321Artifact.ManualOnly &&
                v321Artifact.StartsRuntime == false &&
                v321Artifact.InstallsDependencies == false &&
                v321Artifact.StoresSecrets == false &&
                v321Artifact.StoresSql == false &&
                v321Artifact.StoresHiddenContext == false &&
                v321Artifact.DefaultResultChanged == false &&
                v321Artifact.BytesWritten > 0;
            bool v321ArtifactMarkers =
                v321ArtifactText.Contains("manual_replay_report_artifact=true", StringComparison.Ordinal) &&
                v321ArtifactText.Contains("artifact_writer=true", StringComparison.Ordinal) &&
                v321ArtifactText.Contains("manual_only=true", StringComparison.Ordinal) &&
                v321ArtifactText.Contains("starts_runtime=false", StringComparison.Ordinal) &&
                v321ArtifactText.Contains("installs_dependencies=false", StringComparison.Ordinal) &&
                v321ArtifactText.Contains("stores_secrets=false", StringComparison.Ordinal) &&
                v321ArtifactText.Contains("stores_sql=false", StringComparison.Ordinal) &&
                v321ArtifactText.Contains("stores_hidden_context=false", StringComparison.Ordinal) &&
                v321ArtifactText.Contains("shadow_replay_report=true", StringComparison.Ordinal) &&
                v321ArtifactText.Contains("SELECT", StringComparison.OrdinalIgnoreCase) == false &&
                v321ArtifactText.Contains("bearer", StringComparison.OrdinalIgnoreCase) == false;
            bool v321Ready =
                v321DocExists &&
                v321DocMarkers &&
                v321Boundary &&
                v321ModelReady &&
                v321ArtifactMarkers;
            checks.Add(v321Ready
                ? Pass("GraphHandshakeManualReplayReportArtifactWriterPresent", "manual_replay_report_artifact=true;artifact_writer=true;manual_only=true;starts_runtime=false;installs_dependencies=false;default_result_changed=false;stores_secrets=false;stores_sql=false;stores_hidden_context=false")
                : Fail("GraphHandshakeManualReplayReportArtifactWriterPresent", $"doc={LowerBool(v321DocExists)};doc_markers={LowerBool(v321DocMarkers)};boundary={LowerBool(v321Boundary)};model={LowerBool(v321ModelReady)};artifact={LowerBool(v321ArtifactMarkers)}"));

            string v322DocPath = Path.Combine(v311RepoRoot, "docs", "dataagent", "dataagent-v3.22-manual-artifact-index.md");
            bool v322DocExists = File.Exists(v322DocPath);
            string v322Doc = v322DocExists ? File.ReadAllText(v322DocPath) : string.Empty;
            string v322IndexPath = Path.Combine(
                Path.GetDirectoryName(databasePath) ?? Path.GetTempPath(),
                $"dataagent-v322-readiness-{Guid.NewGuid():N}.md");
            DataAgentGraphHandshakeReplayReportArtifactIndex? v322Index = null;
            string v322IndexText = string.Empty;
            try
            {
                v322Index = DataAgentGraphHandshakeReplayReportArtifactIndexWriter.Write(v320SampleReport, v321Artifact!, v322IndexPath);
                v322IndexText = File.ReadAllText(v322IndexPath);
            }
            finally
            {
                if (File.Exists(v322IndexPath))
                    File.Delete(v322IndexPath);
            }

            bool v322DocMarkers =
                v322Doc.Contains("manual_artifact_index=true", StringComparison.Ordinal) &&
                v322Doc.Contains("manifest_writer=true", StringComparison.Ordinal) &&
                v322Doc.Contains("manual_only=true", StringComparison.Ordinal);
            bool v322Boundary =
                v322Doc.Contains("starts_runtime=false", StringComparison.Ordinal) &&
                v322Doc.Contains("installs_dependencies=false", StringComparison.Ordinal) &&
                v322Doc.Contains("default_result_changed=false", StringComparison.Ordinal) &&
                v322Doc.Contains("stores_secrets=false", StringComparison.Ordinal) &&
                v322Doc.Contains("stores_sql=false", StringComparison.Ordinal) &&
                v322Doc.Contains("stores_hidden_context=false", StringComparison.Ordinal);
            bool v322ModelReady =
                typeof(DataAgentGraphHandshakeReplayReportArtifactIndex).IsClass &&
                typeof(DataAgentGraphHandshakeReplayReportArtifactIndexWriter).IsClass &&
                v322Index is not null &&
                v322Index.ManualOnly &&
                v322Index.StartsRuntime == false &&
                v322Index.InstallsDependencies == false &&
                v322Index.StoresSecrets == false &&
                v322Index.StoresSql == false &&
                v322Index.StoresHiddenContext == false &&
                v322Index.DefaultResultChanged == false &&
                v322Index.ComparisonCount == v320SampleReport.ComparisonCount;
            bool v322IndexMarkers =
                v322IndexText.Contains("manual_artifact_index=true", StringComparison.Ordinal) &&
                v322IndexText.Contains("manifest_writer=true", StringComparison.Ordinal) &&
                v322IndexText.Contains("manual_only=true", StringComparison.Ordinal) &&
                v322IndexText.Contains("starts_runtime=false", StringComparison.Ordinal) &&
                v322IndexText.Contains("installs_dependencies=false", StringComparison.Ordinal) &&
                v322IndexText.Contains("stores_secrets=false", StringComparison.Ordinal) &&
                v322IndexText.Contains("stores_sql=false", StringComparison.Ordinal) &&
                v322IndexText.Contains("stores_hidden_context=false", StringComparison.Ordinal) &&
                v322IndexText.Contains("comparison_count=1", StringComparison.Ordinal) &&
                v322IndexText.Contains("default_result_changed=false", StringComparison.Ordinal) &&
                v322IndexText.Contains("SELECT", StringComparison.OrdinalIgnoreCase) == false &&
                v322IndexText.Contains("bearer", StringComparison.OrdinalIgnoreCase) == false;
            bool v322Ready =
                v322DocExists &&
                v322DocMarkers &&
                v322Boundary &&
                v322ModelReady &&
                v322IndexMarkers;
            checks.Add(v322Ready
                ? Pass("GraphHandshakeManualArtifactIndexPresent", "manual_artifact_index=true;manifest_writer=true;manual_only=true;starts_runtime=false;installs_dependencies=false;default_result_changed=false;stores_secrets=false;stores_sql=false;stores_hidden_context=false")
                : Fail("GraphHandshakeManualArtifactIndexPresent", $"doc={LowerBool(v322DocExists)};doc_markers={LowerBool(v322DocMarkers)};boundary={LowerBool(v322Boundary)};model={LowerBool(v322ModelReady)};index={LowerBool(v322IndexMarkers)}"));

            string v323DocPath = Path.Combine(v311RepoRoot, "docs", "dataagent", "dataagent-v3.23-manual-audit-bundle.md");
            bool v323DocExists = File.Exists(v323DocPath);
            string v323Doc = v323DocExists ? File.ReadAllText(v323DocPath) : string.Empty;
            string v323BundlePath = Path.Combine(
                Path.GetDirectoryName(databasePath) ?? Path.GetTempPath(),
                $"dataagent-v323-readiness-{Guid.NewGuid():N}.md");
            DataAgentGraphHandshakeManualAuditBundle? v323Bundle = null;
            string v323BundleText = string.Empty;
            try
            {
                v323Bundle = DataAgentGraphHandshakeManualAuditBundleWriter.Write(v320SampleReport, v321Artifact!, v322Index!, v323BundlePath);
                v323BundleText = File.ReadAllText(v323BundlePath);
            }
            finally
            {
                if (File.Exists(v323BundlePath))
                    File.Delete(v323BundlePath);
            }

            bool v323DocMarkers =
                v323Doc.Contains("manual_audit_bundle=true", StringComparison.Ordinal) &&
                v323Doc.Contains("bundle_writer=true", StringComparison.Ordinal) &&
                v323Doc.Contains("source_versions=v3.18-v3.22", StringComparison.Ordinal) &&
                v323Doc.Contains("manual_only=true", StringComparison.Ordinal);
            bool v323Boundary =
                v323Doc.Contains("starts_runtime=false", StringComparison.Ordinal) &&
                v323Doc.Contains("installs_dependencies=false", StringComparison.Ordinal) &&
                v323Doc.Contains("default_result_changed=false", StringComparison.Ordinal) &&
                v323Doc.Contains("stores_secrets=false", StringComparison.Ordinal) &&
                v323Doc.Contains("stores_sql=false", StringComparison.Ordinal) &&
                v323Doc.Contains("stores_hidden_context=false", StringComparison.Ordinal);
            bool v323ModelReady =
                typeof(DataAgentGraphHandshakeManualAuditBundle).IsClass &&
                typeof(DataAgentGraphHandshakeManualAuditBundleWriter).IsClass &&
                v323Bundle is not null &&
                v323Bundle.ManualOnly &&
                v323Bundle.StartsRuntime == false &&
                v323Bundle.InstallsDependencies == false &&
                v323Bundle.StoresSecrets == false &&
                v323Bundle.StoresSql == false &&
                v323Bundle.StoresHiddenContext == false &&
                v323Bundle.DefaultResultChanged == false &&
                v323Bundle.ComparisonCount == v320SampleReport.ComparisonCount &&
                v323Bundle.EvidenceItemCount == 5;
            bool v323BundleMarkers =
                v323BundleText.Contains("manual_audit_bundle=true", StringComparison.Ordinal) &&
                v323BundleText.Contains("bundle_writer=true", StringComparison.Ordinal) &&
                v323BundleText.Contains("source_versions=v3.18-v3.22", StringComparison.Ordinal) &&
                v323BundleText.Contains("manual_only=true", StringComparison.Ordinal) &&
                v323BundleText.Contains("starts_runtime=false", StringComparison.Ordinal) &&
                v323BundleText.Contains("installs_dependencies=false", StringComparison.Ordinal) &&
                v323BundleText.Contains("stores_secrets=false", StringComparison.Ordinal) &&
                v323BundleText.Contains("stores_sql=false", StringComparison.Ordinal) &&
                v323BundleText.Contains("stores_hidden_context=false", StringComparison.Ordinal) &&
                v323BundleText.Contains("default_result_changed=false", StringComparison.Ordinal) &&
                v323BundleText.Contains("comparison_count=1", StringComparison.Ordinal) &&
                v323BundleText.Contains("evidence_item_count=5", StringComparison.Ordinal) &&
                v323BundleText.Contains("includes_smoke_result_artifact=true", StringComparison.Ordinal) &&
                v323BundleText.Contains("includes_replay_fixture_pack=true", StringComparison.Ordinal) &&
                v323BundleText.Contains("includes_shadow_replay_report=true", StringComparison.Ordinal) &&
                v323BundleText.Contains("includes_manual_replay_report_artifact=true", StringComparison.Ordinal) &&
                v323BundleText.Contains("includes_manual_artifact_index=true", StringComparison.Ordinal) &&
                v323BundleText.Contains("SELECT", StringComparison.OrdinalIgnoreCase) == false &&
                v323BundleText.Contains("bearer", StringComparison.OrdinalIgnoreCase) == false;
            bool v323Ready =
                v323DocExists &&
                v323DocMarkers &&
                v323Boundary &&
                v323ModelReady &&
                v323BundleMarkers;
            checks.Add(v323Ready
                ? Pass("GraphHandshakeManualAuditBundlePresent", "manual_audit_bundle=true;bundle_writer=true;source_versions=v3.18-v3.22;manual_only=true;starts_runtime=false;installs_dependencies=false;default_result_changed=false;stores_secrets=false;stores_sql=false;stores_hidden_context=false")
                : Fail("GraphHandshakeManualAuditBundlePresent", $"doc={LowerBool(v323DocExists)};doc_markers={LowerBool(v323DocMarkers)};boundary={LowerBool(v323Boundary)};model={LowerBool(v323ModelReady)};bundle={LowerBool(v323BundleMarkers)}"));

            string v324DocPath = Path.Combine(v311RepoRoot, "docs", "dataagent", "dataagent-v3.24-agent-advisory-contract.md");
            bool v324DocExists = File.Exists(v324DocPath);
            string v324Doc = v324DocExists ? File.ReadAllText(v324DocPath) : string.Empty;
            string v324BoundaryPath = Path.Combine(v311RepoRoot, "docs", "engineering", "agent-harness-boundary.md");
            bool v324BoundaryDocExists = File.Exists(v324BoundaryPath);
            string v324BoundaryDoc = v324BoundaryDocExists ? File.ReadAllText(v324BoundaryPath) : string.Empty;
            DataAgentAgentAdvisoryRequest v324Request = new(
                ContractVersion: "v3.24",
                RunId: "v3.24-readiness",
                Task: "explain classified harness failure",
                CurrentState: "manual shadow run failed after loopback check",
                AllowedAdvisoryActions: ["explain_failure", "propose_manual_check", "summarize_artifact", "suggest_fixture", "compare_replay_diff"],
                ForbiddenAuthorities: ["start_runtime", "execute_sql", "write_state", "write_secret", "publish_visible_answer", "decide_tool_permission", "override_readiness"],
                LastSuccessfulStep: "loopback_check",
                FailureCategory: "timeout_or_transport_failure",
                EvidenceRefs: ["artifact_index:v3.23-manual-audit-bundle"],
                ArtifactIndexToken: "v3.23-manual-audit-bundle",
                ExpectedResponseSchema: "advisory_id,summary,reason_code,confidence,evidence_refs,proposed_next_steps,forbidden_authority_claims,requires_operator_action",
                AgentAdvisoryOnly: true,
                HarnessExecutionAuthority: true,
                CSharpValidationAuthority: true,
                DefaultResultChanged: false);
            DataAgentAgentAdvisoryResponse v324SafeResponse = new(
                AdvisoryId: "adv-1",
                Summary: "classified timeout can be retried manually after checking loopback health",
                ReasonCode: "timeout_or_transport_failure",
                Confidence: 0.8,
                EvidenceRefs: ["artifact_index:v3.23-manual-audit-bundle"],
                ProposedNextSteps: ["inspect_loopback"],
                ForbiddenAuthorityClaims: [],
                RequiresOperatorAction: true,
                RequestsExecution: false,
                RequestsStateWrite: false,
                RequestsVisibleText: false,
                DefaultResultChanged: false);
            DataAgentAgentAdvisoryValidationResult v324RequestValidation =
                DataAgentAgentAdvisoryContract.ValidateRequest(v324Request);
            DataAgentAgentAdvisoryValidationResult v324ResponseValidation =
                DataAgentAgentAdvisoryContract.ValidateResponse(v324Request, v324SafeResponse);
            DataAgentAgentAdvisoryValidationResult v324ForbiddenValidation =
                DataAgentAgentAdvisoryContract.ValidateResponse(
                    v324Request,
                    v324SafeResponse with
                    {
                        ForbiddenAuthorityClaims = ["execute_sql"],
                        RequestsExecution = true
                    });
            string v324Packet = DataAgentAgentAdvisoryFormatter.Format(v324Request, v324SafeResponse);
            bool v324DocMarkers =
                v324Doc.Contains("agent_advisory_contract=true", StringComparison.Ordinal) &&
                v324Doc.Contains("contract_version=v3.24", StringComparison.Ordinal) &&
                v324Doc.Contains("token_budget_context_layers=true", StringComparison.Ordinal) &&
                v324Doc.Contains("evidence_first_response=true", StringComparison.Ordinal) &&
                v324Doc.Contains("langgraph_provider_only=true", StringComparison.Ordinal);
            bool v324Boundary =
                v324Doc.Contains("agent_advisory_only=true", StringComparison.Ordinal) &&
                v324Doc.Contains("harness_execution_authority=true", StringComparison.Ordinal) &&
                v324Doc.Contains("csharp_validation_authority=true", StringComparison.Ordinal) &&
                v324Doc.Contains("starts_runtime=false", StringComparison.Ordinal) &&
                v324Doc.Contains("installs_dependencies=false", StringComparison.Ordinal) &&
                v324Doc.Contains("default_result_changed=false", StringComparison.Ordinal) &&
                v324Doc.Contains("stores_secrets=false", StringComparison.Ordinal) &&
                v324Doc.Contains("stores_sql=false", StringComparison.Ordinal) &&
                v324Doc.Contains("stores_hidden_context=false", StringComparison.Ordinal);
            bool v324EngineeringBoundary =
                v324BoundaryDoc.Contains("agent_harness_boundary=true", StringComparison.Ordinal) &&
                v324BoundaryDoc.Contains("loop_harness_reuse_required=true", StringComparison.Ordinal) &&
                v324BoundaryDoc.Contains("dataagent_is_testbed_not_destination=true", StringComparison.Ordinal);
            bool v324ModelReady =
                typeof(DataAgentAgentAdvisoryRequest).IsClass &&
                typeof(DataAgentAgentAdvisoryResponse).IsClass &&
                typeof(DataAgentAgentAdvisoryContract).IsClass &&
                typeof(DataAgentAgentAdvisoryFormatter).IsClass &&
                v324RequestValidation.Accepted &&
                v324ResponseValidation.Accepted &&
                v324ForbiddenValidation.Accepted == false &&
                string.Equals(v324ForbiddenValidation.ReasonCode, "advisory_forbidden_authority_claimed", StringComparison.Ordinal);
            bool v324PacketMarkers =
                v324Packet.Contains("agent_advisory_contract=true", StringComparison.Ordinal) &&
                v324Packet.Contains("contract_version=v3.24", StringComparison.Ordinal) &&
                v324Packet.Contains("token_budget_context_layers=true", StringComparison.Ordinal) &&
                v324Packet.Contains("evidence_first_response=true", StringComparison.Ordinal) &&
                v324Packet.Contains("agent_advisory_only=true", StringComparison.Ordinal) &&
                v324Packet.Contains("harness_execution_authority=true", StringComparison.Ordinal) &&
                v324Packet.Contains("csharp_validation_authority=true", StringComparison.Ordinal) &&
                v324Packet.Contains("default_result_changed=false", StringComparison.Ordinal) &&
                v324Packet.Contains("reason_code=timeout_or_transport_failure", StringComparison.Ordinal) &&
                v324Packet.Contains("SELECT", StringComparison.OrdinalIgnoreCase) == false &&
                v324Packet.Contains("bearer", StringComparison.OrdinalIgnoreCase) == false &&
                v324Packet.Contains("hidden_context", StringComparison.OrdinalIgnoreCase) == false;
            bool v324Ready =
                v324DocExists &&
                v324BoundaryDocExists &&
                v324DocMarkers &&
                v324Boundary &&
                v324EngineeringBoundary &&
                v324ModelReady &&
                v324PacketMarkers;
            checks.Add(v324Ready
                ? Pass("GraphHandshakeAgentAdvisoryContractPresent", "agent_advisory_contract=true;contract_version=v3.24;token_budget_context_layers=true;evidence_first_response=true;agent_advisory_only=true;harness_execution_authority=true;csharp_validation_authority=true;langgraph_provider_only=true;starts_runtime=false;installs_dependencies=false;default_result_changed=false;stores_secrets=false;stores_sql=false;stores_hidden_context=false")
                : Fail("GraphHandshakeAgentAdvisoryContractPresent", $"doc={LowerBool(v324DocExists)};engineering_doc={LowerBool(v324BoundaryDocExists)};doc_markers={LowerBool(v324DocMarkers)};boundary={LowerBool(v324Boundary)};engineering_boundary={LowerBool(v324EngineeringBoundary)};model={LowerBool(v324ModelReady)};packet={LowerBool(v324PacketMarkers)}"));

            string v325DocPath = Path.Combine(v311RepoRoot, "docs", "dataagent", "dataagent-v3.25-real-langgraph-manual-shadow-provider.md");
            bool v325DocExists = File.Exists(v325DocPath);
            string v325Doc = v325DocExists ? File.ReadAllText(v325DocPath) : string.Empty;
            DataAgentLangGraphManualShadowPayload v325Payload = new(
                ProviderName: "langgraph",
                CapturedByOperator: true,
                RuntimeStartedByAlife: false,
                DependenciesInstalledByAlife: false,
                SidecarCalledByAlife: false,
                v324SafeResponse);
            DataAgentLangGraphManualShadowResult v325Accepted =
                DataAgentLangGraphManualShadowProvider.Evaluate(v324Request, v325Payload);
            DataAgentLangGraphManualShadowResult v325Rejected =
                DataAgentLangGraphManualShadowProvider.Evaluate(
                    v324Request,
                    v325Payload with
                    {
                        Advisory = v324SafeResponse with
                        {
                            ForbiddenAuthorityClaims = ["execute_sql"],
                            RequestsExecution = true
                        }
                    });
            DataAgentLangGraphManualShadowResult v325Missing =
                DataAgentLangGraphManualShadowProvider.Evaluate(v324Request, null);
            string v325Packet = DataAgentLangGraphManualShadowFormatter.Format(v325Accepted);
            bool v325DocMarkers =
                v325Doc.Contains("real_langgraph_manual_shadow_provider=true", StringComparison.Ordinal) &&
                v325Doc.Contains("langgraph_provider_only=true", StringComparison.Ordinal) &&
                v325Doc.Contains("manual_shadow_only=true", StringComparison.Ordinal) &&
                v325Doc.Contains("agent_advisory_contract=v3.24", StringComparison.Ordinal);
            bool v325Boundary =
                v325Doc.Contains("starts_runtime=false", StringComparison.Ordinal) &&
                v325Doc.Contains("installs_dependencies=false", StringComparison.Ordinal) &&
                v325Doc.Contains("calls_sidecar=false", StringComparison.Ordinal) &&
                v325Doc.Contains("default_result_changed=false", StringComparison.Ordinal) &&
                v325Doc.Contains("stores_secrets=false", StringComparison.Ordinal) &&
                v325Doc.Contains("stores_sql=false", StringComparison.Ordinal) &&
                v325Doc.Contains("stores_hidden_context=false", StringComparison.Ordinal);
            bool v325ModelReady =
                typeof(DataAgentLangGraphManualShadowPayload).IsClass &&
                typeof(DataAgentLangGraphManualShadowResult).IsClass &&
                typeof(DataAgentLangGraphManualShadowProvider).IsClass &&
                typeof(DataAgentLangGraphManualShadowFormatter).IsClass &&
                v325Accepted.Accepted &&
                string.Equals(v325Accepted.ReasonCode, "langgraph_manual_shadow_advisory_accepted", StringComparison.Ordinal) &&
                v325Accepted.ManualShadowOnly &&
                v325Accepted.StartsRuntime == false &&
                v325Accepted.InstallsDependencies == false &&
                v325Accepted.CallsSidecar == false &&
                v325Accepted.DefaultResultChanged == false &&
                v325Accepted.StoresSecrets == false &&
                v325Accepted.StoresSql == false &&
                v325Accepted.StoresHiddenContext == false &&
                v325Rejected.Accepted == false &&
                string.Equals(v325Rejected.ReasonCode, "advisory_forbidden_authority_claimed", StringComparison.Ordinal) &&
                v325Rejected.FallbackRequired &&
                v325Missing.Accepted == false &&
                string.Equals(v325Missing.ReasonCode, "langgraph_manual_shadow_payload_missing", StringComparison.Ordinal);
            bool v325PacketMarkers =
                v325Packet.Contains("real_langgraph_manual_shadow_provider=true", StringComparison.Ordinal) &&
                v325Packet.Contains("langgraph_provider_only=true", StringComparison.Ordinal) &&
                v325Packet.Contains("manual_shadow_only=true", StringComparison.Ordinal) &&
                v325Packet.Contains("agent_advisory_contract=v3.24", StringComparison.Ordinal) &&
                v325Packet.Contains("starts_runtime=false", StringComparison.Ordinal) &&
                v325Packet.Contains("installs_dependencies=false", StringComparison.Ordinal) &&
                v325Packet.Contains("calls_sidecar=false", StringComparison.Ordinal) &&
                v325Packet.Contains("default_result_changed=false", StringComparison.Ordinal) &&
                v325Packet.Contains("stores_secrets=false", StringComparison.Ordinal) &&
                v325Packet.Contains("stores_sql=false", StringComparison.Ordinal) &&
                v325Packet.Contains("stores_hidden_context=false", StringComparison.Ordinal) &&
                v325Packet.Contains("SELECT", StringComparison.OrdinalIgnoreCase) == false &&
                v325Packet.Contains("bearer", StringComparison.OrdinalIgnoreCase) == false;
            bool v325Ready =
                v325DocExists &&
                v325DocMarkers &&
                v325Boundary &&
                v325ModelReady &&
                v325PacketMarkers;
            checks.Add(v325Ready
                ? Pass("GraphHandshakeRealLangGraphManualShadowProviderPresent", "real_langgraph_manual_shadow_provider=true;langgraph_provider_only=true;manual_shadow_only=true;agent_advisory_contract=v3.24;starts_runtime=false;installs_dependencies=false;calls_sidecar=false;default_result_changed=false;stores_secrets=false;stores_sql=false;stores_hidden_context=false")
                : Fail("GraphHandshakeRealLangGraphManualShadowProviderPresent", $"doc={LowerBool(v325DocExists)};doc_markers={LowerBool(v325DocMarkers)};boundary={LowerBool(v325Boundary)};model={LowerBool(v325ModelReady)};packet={LowerBool(v325PacketMarkers)}"));

            string v326DocPath = Path.Combine(v311RepoRoot, "docs", "dataagent", "dataagent-v3.26-harness-replay-diff-gate.md");
            bool v326DocExists = File.Exists(v326DocPath);
            string v326Doc = v326DocExists ? File.ReadAllText(v326DocPath) : string.Empty;
            DataAgentGraphHandshakeShadowComparison v326Comparison = new(
                DataAgentGraphHandshakeShadowComparisonStatus.TimeoutOrTransportFailure,
                "timeout_or_transport_failure",
                "sidecar_disabled",
                "timeout",
                DataAgentGraphHandshakeStatus.Disabled,
                DataAgentGraphHandshakeStatus.Timeout,
                DeterministicFallbackRequired: true,
                SidecarFallbackRequired: true,
                DefaultResultChanged: false);
            DataAgentGraphHandshakeReplayReport v326Report = new(
                "v3.26-harness-replay-diff-gate",
                [new DataAgentGraphHandshakeReplayFixtureResult("timeout_fallback", v326Comparison)],
                new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["timeout_or_transport_failure"] = 1
                },
                ComparisonCount: 1,
                DefaultResultChanged: false,
                Passed: true);
            DataAgentGraphHandshakeReplayReport v326ChangedReport = v326Report with
            {
                DefaultResultChanged = true,
                Passed = false
            };
            DataAgentLangGraphManualShadowResult v326MismatchAdvisory =
                DataAgentLangGraphManualShadowProvider.Evaluate(
                    v324Request with
                    {
                        FailureCategory = "invalid_schema"
                    },
                    v325Payload with
                    {
                        Advisory = v324SafeResponse with
                        {
                            ReasonCode = "invalid_schema"
                        }
                    });
            DataAgentHarnessReplayDiffGateResult v326Passed =
                DataAgentHarnessReplayDiffGate.Evaluate(new DataAgentHarnessReplayDiffGateInput(v326Report, v325Accepted));
            DataAgentHarnessReplayDiffGateResult v326Rejected =
                DataAgentHarnessReplayDiffGate.Evaluate(new DataAgentHarnessReplayDiffGateInput(v326Report, v325Rejected));
            DataAgentHarnessReplayDiffGateResult v326Changed =
                DataAgentHarnessReplayDiffGate.Evaluate(new DataAgentHarnessReplayDiffGateInput(v326ChangedReport, v325Accepted));
            DataAgentHarnessReplayDiffGateResult v326Mismatch =
                DataAgentHarnessReplayDiffGate.Evaluate(new DataAgentHarnessReplayDiffGateInput(v326Report, v326MismatchAdvisory));
            string v326Packet = DataAgentHarnessReplayDiffGateFormatter.Format(v326Passed);
            bool v326DocMarkers =
                v326Doc.Contains("harness_replay_diff_gate=true", StringComparison.Ordinal) &&
                v326Doc.Contains("agent_advisory_contract=v3.24", StringComparison.Ordinal) &&
                v326Doc.Contains("real_langgraph_manual_shadow_provider=true", StringComparison.Ordinal) &&
                v326Doc.Contains("harness_execution_authority=true", StringComparison.Ordinal) &&
                v326Doc.Contains("csharp_validation_authority=true", StringComparison.Ordinal) &&
                v326Doc.Contains("agent_advisory_only=true", StringComparison.Ordinal) &&
                v326Doc.Contains("gate_only=true", StringComparison.Ordinal) &&
                v326Doc.Contains("operator_decides=true", StringComparison.Ordinal);
            bool v326Boundary =
                v326Doc.Contains("default_result_changed=false", StringComparison.Ordinal) &&
                v326Doc.Contains("starts_runtime=false", StringComparison.Ordinal) &&
                v326Doc.Contains("installs_dependencies=false", StringComparison.Ordinal) &&
                v326Doc.Contains("calls_sidecar=false", StringComparison.Ordinal) &&
                v326Doc.Contains("stores_secrets=false", StringComparison.Ordinal) &&
                v326Doc.Contains("stores_sql=false", StringComparison.Ordinal) &&
                v326Doc.Contains("stores_hidden_context=false", StringComparison.Ordinal);
            bool v326ModelReady =
                typeof(DataAgentHarnessReplayDiffGateInput).IsClass &&
                typeof(DataAgentHarnessReplayDiffGateResult).IsClass &&
                typeof(DataAgentHarnessReplayDiffGate).IsClass &&
                typeof(DataAgentHarnessReplayDiffGateFormatter).IsClass &&
                v326Passed.GatePassed &&
                string.Equals(v326Passed.ReasonCode, "harness_replay_diff_gate_passed", StringComparison.Ordinal) &&
                v326Passed.ReplayEvidenceMatched &&
                v326Passed.AdvisoryReasonMatched &&
                v326Passed.FallbackRequired == false &&
                v326Passed.DefaultResultChanged == false &&
                v326Passed.StartsRuntime == false &&
                v326Passed.InstallsDependencies == false &&
                v326Passed.CallsSidecar == false &&
                v326Passed.StoresSecrets == false &&
                v326Passed.StoresSql == false &&
                v326Passed.StoresHiddenContext == false &&
                v326Rejected.GatePassed == false &&
                string.Equals(v326Rejected.ReasonCode, "advisory_forbidden_authority_claimed", StringComparison.Ordinal) &&
                v326Rejected.FallbackRequired &&
                v326Changed.GatePassed == false &&
                string.Equals(v326Changed.ReasonCode, "harness_replay_default_result_changed", StringComparison.Ordinal) &&
                v326Mismatch.GatePassed == false &&
                string.Equals(v326Mismatch.ReasonCode, "harness_replay_diff_reason_mismatch", StringComparison.Ordinal) &&
                v326Mismatch.OperatorRequired;
            bool v326PacketMarkers =
                v326Packet.Contains("harness_replay_diff_gate=true", StringComparison.Ordinal) &&
                v326Packet.Contains("agent_advisory_contract=v3.24", StringComparison.Ordinal) &&
                v326Packet.Contains("real_langgraph_manual_shadow_provider=true", StringComparison.Ordinal) &&
                v326Packet.Contains("harness_execution_authority=true", StringComparison.Ordinal) &&
                v326Packet.Contains("csharp_validation_authority=true", StringComparison.Ordinal) &&
                v326Packet.Contains("agent_advisory_only=true", StringComparison.Ordinal) &&
                v326Packet.Contains("gate_only=true", StringComparison.Ordinal) &&
                v326Packet.Contains("operator_decides=true", StringComparison.Ordinal) &&
                v326Packet.Contains("default_result_changed=false", StringComparison.Ordinal) &&
                v326Packet.Contains("starts_runtime=false", StringComparison.Ordinal) &&
                v326Packet.Contains("installs_dependencies=false", StringComparison.Ordinal) &&
                v326Packet.Contains("calls_sidecar=false", StringComparison.Ordinal) &&
                v326Packet.Contains("stores_secrets=false", StringComparison.Ordinal) &&
                v326Packet.Contains("stores_sql=false", StringComparison.Ordinal) &&
                v326Packet.Contains("stores_hidden_context=false", StringComparison.Ordinal) &&
                v326Packet.Contains("SELECT", StringComparison.OrdinalIgnoreCase) == false &&
                v326Packet.Contains("bearer", StringComparison.OrdinalIgnoreCase) == false;
            bool v326Ready =
                v326DocExists &&
                v326DocMarkers &&
                v326Boundary &&
                v326ModelReady &&
                v326PacketMarkers;
            checks.Add(v326Ready
                ? Pass("GraphHandshakeHarnessReplayDiffGatePresent", "harness_replay_diff_gate=true;agent_advisory_contract=v3.24;real_langgraph_manual_shadow_provider=true;harness_execution_authority=true;csharp_validation_authority=true;agent_advisory_only=true;gate_only=true;operator_decides=true;default_result_changed=false;starts_runtime=false;installs_dependencies=false;calls_sidecar=false;stores_secrets=false;stores_sql=false;stores_hidden_context=false")
                : Fail("GraphHandshakeHarnessReplayDiffGatePresent", $"doc={LowerBool(v326DocExists)};doc_markers={LowerBool(v326DocMarkers)};boundary={LowerBool(v326Boundary)};model={LowerBool(v326ModelReady)};packet={LowerBool(v326PacketMarkers)}"));

            string v327DocPath = Path.Combine(v311RepoRoot, "docs", "dataagent", "dataagent-v3.27-operator-evidence-pack.md");
            bool v327DocExists = File.Exists(v327DocPath);
            string v327Doc = v327DocExists ? File.ReadAllText(v327DocPath) : string.Empty;
            DataAgentOperatorEvidencePack v327PassedPack =
                DataAgentOperatorEvidencePackBuilder.Build(v326Report, v321Artifact!, v322Index!, v323Bundle!, v325Accepted, v326Passed);
            DataAgentOperatorEvidencePack v327FallbackPack =
                DataAgentOperatorEvidencePackBuilder.Build(v326Report, v321Artifact!, v322Index!, v323Bundle!, v325Rejected, v326Rejected);
            string v327Packet = DataAgentOperatorEvidencePackFormatter.Format(v327PassedPack);
            bool v327DocMarkers =
                v327Doc.Contains("operator_evidence_pack=true", StringComparison.Ordinal) &&
                v327Doc.Contains("source_versions=v3.18-v3.26", StringComparison.Ordinal) &&
                v327Doc.Contains("manual_audit_bundle=true", StringComparison.Ordinal) &&
                v327Doc.Contains("agent_advisory_contract=v3.24", StringComparison.Ordinal) &&
                v327Doc.Contains("real_langgraph_manual_shadow_provider=true", StringComparison.Ordinal) &&
                v327Doc.Contains("harness_replay_diff_gate=true", StringComparison.Ordinal) &&
                v327Doc.Contains("operator_decides=true", StringComparison.Ordinal);
            bool v327Boundary =
                v327Doc.Contains("default_result_changed=false", StringComparison.Ordinal) &&
                v327Doc.Contains("manual_only=true", StringComparison.Ordinal) &&
                v327Doc.Contains("starts_runtime=false", StringComparison.Ordinal) &&
                v327Doc.Contains("installs_dependencies=false", StringComparison.Ordinal) &&
                v327Doc.Contains("calls_sidecar=false", StringComparison.Ordinal) &&
                v327Doc.Contains("stores_secrets=false", StringComparison.Ordinal) &&
                v327Doc.Contains("stores_sql=false", StringComparison.Ordinal) &&
                v327Doc.Contains("stores_hidden_context=false", StringComparison.Ordinal);
            bool v327ModelReady =
                typeof(DataAgentOperatorEvidencePack).IsClass &&
                typeof(DataAgentOperatorEvidencePackBuilder).IsClass &&
                typeof(DataAgentOperatorEvidencePackFormatter).IsClass &&
                string.Equals(v327PassedPack.PackId, "v3.27-operator-evidence-pack", StringComparison.Ordinal) &&
                v327PassedPack.GatePassed &&
                v327PassedPack.AdvisoryAccepted &&
                v327PassedPack.FallbackRequired == false &&
                v327PassedPack.OperatorRequired == false &&
                v327PassedPack.ManualOnly &&
                v327PassedPack.AgentAdvisoryOnly &&
                v327PassedPack.HarnessExecutionAuthority &&
                v327PassedPack.CSharpValidationAuthority &&
                v327PassedPack.OperatorDecides &&
                v327PassedPack.DefaultResultChanged == false &&
                v327PassedPack.StartsRuntime == false &&
                v327PassedPack.InstallsDependencies == false &&
                v327PassedPack.CallsSidecar == false &&
                v327PassedPack.StoresSecrets == false &&
                v327PassedPack.StoresSql == false &&
                v327PassedPack.StoresHiddenContext == false &&
                v327FallbackPack.GatePassed == false &&
                v327FallbackPack.AdvisoryAccepted == false &&
                v327FallbackPack.FallbackRequired &&
                v327FallbackPack.OperatorRequired;
            bool v327PacketMarkers =
                v327Packet.Contains("operator_evidence_pack=true", StringComparison.Ordinal) &&
                v327Packet.Contains("source_versions=v3.18-v3.26", StringComparison.Ordinal) &&
                v327Packet.Contains("manual_audit_bundle=true", StringComparison.Ordinal) &&
                v327Packet.Contains("agent_advisory_contract=v3.24", StringComparison.Ordinal) &&
                v327Packet.Contains("real_langgraph_manual_shadow_provider=true", StringComparison.Ordinal) &&
                v327Packet.Contains("harness_replay_diff_gate=true", StringComparison.Ordinal) &&
                v327Packet.Contains("operator_decides=true", StringComparison.Ordinal) &&
                v327Packet.Contains("agent_advisory_only=true", StringComparison.Ordinal) &&
                v327Packet.Contains("harness_execution_authority=true", StringComparison.Ordinal) &&
                v327Packet.Contains("csharp_validation_authority=true", StringComparison.Ordinal) &&
                v327Packet.Contains("default_result_changed=false", StringComparison.Ordinal) &&
                v327Packet.Contains("manual_only=true", StringComparison.Ordinal) &&
                v327Packet.Contains("starts_runtime=false", StringComparison.Ordinal) &&
                v327Packet.Contains("installs_dependencies=false", StringComparison.Ordinal) &&
                v327Packet.Contains("calls_sidecar=false", StringComparison.Ordinal) &&
                v327Packet.Contains("stores_secrets=false", StringComparison.Ordinal) &&
                v327Packet.Contains("stores_sql=false", StringComparison.Ordinal) &&
                v327Packet.Contains("stores_hidden_context=false", StringComparison.Ordinal) &&
                v327Packet.Contains("SELECT", StringComparison.OrdinalIgnoreCase) == false &&
                v327Packet.Contains("bearer", StringComparison.OrdinalIgnoreCase) == false &&
                v327Packet.Contains("unsafe SELECT hidden_context bearer secret", StringComparison.OrdinalIgnoreCase) == false;
            bool v327Ready =
                v327DocExists &&
                v327DocMarkers &&
                v327Boundary &&
                v327ModelReady &&
                v327PacketMarkers;
            checks.Add(v327Ready
                ? Pass("GraphHandshakeOperatorEvidencePackPresent", "operator_evidence_pack=true;source_versions=v3.18-v3.26;manual_audit_bundle=true;agent_advisory_contract=v3.24;real_langgraph_manual_shadow_provider=true;harness_replay_diff_gate=true;operator_decides=true;agent_advisory_only=true;harness_execution_authority=true;csharp_validation_authority=true;default_result_changed=false;manual_only=true;starts_runtime=false;installs_dependencies=false;calls_sidecar=false;stores_secrets=false;stores_sql=false;stores_hidden_context=false")
                : Fail("GraphHandshakeOperatorEvidencePackPresent", $"doc={LowerBool(v327DocExists)};doc_markers={LowerBool(v327DocMarkers)};boundary={LowerBool(v327Boundary)};model={LowerBool(v327ModelReady)};packet={LowerBool(v327PacketMarkers)}"));
            string dataAgentEndToEndContractPath = Path.Combine(
                FindRepositoryRoot(AppContext.BaseDirectory),
                "Tests",
                "Alife.Test.DataAgent",
                "DataAgentEndToEndChainContractTests.cs");
            string dataAgentEndToEndContractSource = File.Exists(dataAgentEndToEndContractPath)
                ? File.ReadAllText(dataAgentEndToEndContractPath)
                : string.Empty;
            string ExtractTestMethodBlock(string methodName)
            {
                string signature = $"public void {methodName}(";
                int methodStart = dataAgentEndToEndContractSource.IndexOf(signature, StringComparison.Ordinal);
                if (methodStart < 0)
                    return string.Empty;

                int nextTestStart = dataAgentEndToEndContractSource.IndexOf(
                    "\n    [Test]",
                    methodStart + signature.Length,
                    StringComparison.Ordinal);
                return nextTestStart < 0
                    ? dataAgentEndToEndContractSource[methodStart..]
                    : dataAgentEndToEndContractSource[methodStart..nextTestStart];
            }
            bool ContractContains(string source, string marker) =>
                source.Contains(marker, StringComparison.Ordinal);
            bool ContractDoesNotContainComposedForbiddenToken(string source, string prefix, string suffix) =>
                source.Contains(prefix + suffix, StringComparison.Ordinal) == false;
            int firstTestStart = dataAgentEndToEndContractSource.IndexOf("\n    [Test]", StringComparison.Ordinal);
            string dataAgentEndToEndClassConstantsSource = firstTestStart < 0
                ? dataAgentEndToEndContractSource
                : dataAgentEndToEndContractSource[..firstTestStart];
            string dataAgentEndToEndRouteBoundarySource = ExtractTestMethodBlock("ToolBrokerRoutesDataAgentToolsOnlyForTrustedOwnerPrivateSurface");
            string dataAgentEndToEndXmlPolicySource = ExtractTestMethodBlock("XmlExecutionPolicyEnforcesRouteAndSessionScopeForDataAgentTools");
            string dataAgentEndToEndAcceptedAnalysisSource = ExtractTestMethodBlock("AcceptedAnalysisPublishesSessionStateAndAllDiagnostics");
            string dataAgentEndToEndRouteDeniedSource = ExtractTestMethodBlock("RouteDeniedAnalysisDoesNotExecuteSql");
            string dataAgentEndToEndTerminalActionsSource = ExtractTestMethodBlock("TerminalAnalysisActionsDoNotExecuteSqlAndRemainSessionScoped");
            string dataAgentEndToEndOfflineBoundarySource =
                dataAgentEndToEndClassConstantsSource +
                ExtractTestMethodBlock("OfflineBoundaryMarkersLockNoLiveRuntimeAndNoSidecarAuthority");
            bool dataAgentEndToEndRouteBoundaryReady =
                typeof(DataAgentAnalysisToolHandler).IsClass &&
                typeof(DataAgentService).IsClass &&
                typeof(ToolCapabilityRouter).IsClass &&
                ContractContains(dataAgentEndToEndRouteBoundarySource, "ToolBrokerRoutesDataAgentToolsOnlyForTrustedOwnerPrivateSurface");
            bool dataAgentEndToEndXmlPolicyReady =
                typeof(XmlFunctionExecutionPolicy).IsClass &&
                ContractContains(dataAgentEndToEndXmlPolicySource, "XmlExecutionPolicyEnforcesRouteAndSessionScopeForDataAgentTools");
            bool dataAgentEndToEndSessionStateReady =
                typeof(DataAgentAnalysisOrchestrator).IsClass &&
                typeof(DataAgentAnalysisService).IsClass &&
                typeof(XmlFunctionCaller).IsClass &&
                ContractContains(dataAgentEndToEndAcceptedAnalysisSource, "AcceptedAnalysisPublishesSessionStateAndAllDiagnostics") &&
                ContractContains(dataAgentEndToEndAcceptedAnalysisSource, "UpdateDataAgentAnalysisRouteSessionFromContext");
            bool dataAgentEndToEndDiagnosticsClosureReady =
                typeof(DataAgentEvidenceDiagnosticsFormatter).IsClass &&
                typeof(DataAgentTraceDiagnosticsFormatter).IsClass &&
                typeof(DataAgentProgressDiagnosticsFormatter).IsClass &&
                typeof(DataAgentDataQueryGraphTraceFormatter).IsClass &&
                typeof(DataAgentGraphHandshakeDiagnosticsFormatter).IsClass &&
                ContractContains(dataAgentEndToEndAcceptedAnalysisSource, "QChatDiagnosticsService") &&
                ContractContains(dataAgentEndToEndAcceptedAnalysisSource, "AcceptedAnalysisPublishesSessionStateAndAllDiagnostics");
            bool dataAgentEndToEndRouteDeniedNoExecuteReady =
                ContractContains(dataAgentEndToEndRouteDeniedSource, "RouteDeniedAnalysisDoesNotExecuteSql") &&
                ContractContains(dataAgentEndToEndRouteDeniedSource, "DataAgentOrchestrationNodeKind.RouteGate") &&
                ContractContains(dataAgentEndToEndRouteDeniedSource, "DataAgentOrchestrationNodeKind.Reject") &&
                ContractContains(dataAgentEndToEndRouteDeniedSource, "DataAgentOrchestrationNodeKind.Checkpoint") &&
                ContractContains(dataAgentEndToEndRouteDeniedSource, "DataAgentOrchestrationNodeKind.Execute), Is.False") &&
                ContractContains(dataAgentEndToEndRouteDeniedSource, "step.ExecutedSql), Is.False") &&
                (ContractContains(dataAgentEndToEndRouteDeniedSource, "QueryCount, Is.Zero") ||
                 ContractContains(dataAgentEndToEndRouteDeniedSource, "QueryCount, Is.EqualTo(0)")) &&
                ContractContains(dataAgentEndToEndRouteDeniedSource, "AcceptedAudit, Is.Empty") &&
                ContractContains(dataAgentEndToEndRouteDeniedSource, "RejectedAudit, Is.Empty") &&
                ContractContains(dataAgentEndToEndRouteDeniedSource, "owner_private_required");
            bool dataAgentEndToEndTerminalNoExecuteReady =
                ContractContains(dataAgentEndToEndTerminalActionsSource, "TerminalAnalysisActionsDoNotExecuteSqlAndRemainSessionScoped") &&
                ContractContains(dataAgentEndToEndTerminalActionsSource, "dataagent_analysis_summarize") &&
                ContractContains(dataAgentEndToEndTerminalActionsSource, "dataagent_analysis_end") &&
                ContractContains(dataAgentEndToEndTerminalActionsSource, "summary.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.Execute), Is.False") &&
                ContractContains(dataAgentEndToEndTerminalActionsSource, "summary.Steps.Any(step => step.ExecutedSql), Is.False") &&
                ContractContains(dataAgentEndToEndTerminalActionsSource, "end.Steps.Any(step => step.Node == DataAgentOrchestrationNodeKind.Execute), Is.False") &&
                ContractContains(dataAgentEndToEndTerminalActionsSource, "end.Steps.Any(step => step.ExecutedSql), Is.False") &&
                ContractContains(dataAgentEndToEndTerminalActionsSource, "queryCountAfterStart, Is.EqualTo(1)") &&
                ContractContains(dataAgentEndToEndTerminalActionsSource, "dataStore.QueryCount, Is.EqualTo(1)") &&
                ContractContains(dataAgentEndToEndTerminalActionsSource, "summarizeMissingSession.IsAllowed, Is.False") &&
                ContractContains(dataAgentEndToEndTerminalActionsSource, "summarizeWrongSession.IsAllowed, Is.False") &&
                ContractContains(dataAgentEndToEndTerminalActionsSource, "summarizeMatchingSession.IsAllowed, Is.True") &&
                ContractContains(dataAgentEndToEndTerminalActionsSource, "endMissingSession.IsAllowed, Is.False") &&
                ContractContains(dataAgentEndToEndTerminalActionsSource, "endWrongSession.IsAllowed, Is.False") &&
                ContractContains(dataAgentEndToEndTerminalActionsSource, "endMatchingSession.IsAllowed, Is.True") &&
                ContractContains(dataAgentEndToEndTerminalActionsSource, "tool_route_required") &&
                ContractContains(dataAgentEndToEndTerminalActionsSource, "tool_session_not_allowed_in_current_route");
            bool dataAgentEndToEndSidecarAuthorityBoundaryReady =
                ContractContains(dataAgentEndToEndOfflineBoundarySource, "sidecar_authority=false") &&
                ContractContains(dataAgentEndToEndAcceptedAnalysisSource, "DataAgentGraphHandshakeOptions.Disabled") &&
                ContractContains(dataAgentEndToEndAcceptedAnalysisSource, "DisabledDataAgentGraphSidecarClient");
            bool dataAgentEndToEndDefaultTestsLiveRuntimeBoundaryReady =
                ContractContains(dataAgentEndToEndOfflineBoundarySource, "default_tests_live_runtime=false") &&
                ContractContains(dataAgentEndToEndOfflineBoundarySource, "Does.Not.Contain(\"Invoke-\" + \"WebRequest\")") &&
                ContractContains(dataAgentEndToEndOfflineBoundarySource, "Does.Not.Contain(\"Start-\" + \"Process\")") &&
                ContractContains(dataAgentEndToEndOfflineBoundarySource, "Does.Not.Contain(\"uvi\" + \"corn\")") &&
                ContractContains(dataAgentEndToEndOfflineBoundarySource, "Does.Not.Contain(\"127.0.0.\" + \"1:8765\")") &&
                ContractContains(dataAgentEndToEndOfflineBoundarySource, "Does.Not.Contain(\"Event\" + \"Source\")") &&
                ContractDoesNotContainComposedForbiddenToken(dataAgentEndToEndContractSource, "Invoke-", "WebRequest") &&
                ContractDoesNotContainComposedForbiddenToken(dataAgentEndToEndContractSource, "Start-", "Process") &&
                ContractDoesNotContainComposedForbiddenToken(dataAgentEndToEndContractSource, "uvi", "corn") &&
                ContractDoesNotContainComposedForbiddenToken(dataAgentEndToEndContractSource, "127.0.0.", "1:8765") &&
                ContractDoesNotContainComposedForbiddenToken(dataAgentEndToEndContractSource, "Event", "Source");
            bool dataAgentEndToEndChainContractReady =
                dataAgentEndToEndRouteBoundaryReady &&
                dataAgentEndToEndXmlPolicyReady &&
                dataAgentEndToEndSessionStateReady &&
                dataAgentEndToEndDiagnosticsClosureReady &&
                dataAgentEndToEndRouteDeniedNoExecuteReady &&
                dataAgentEndToEndTerminalNoExecuteReady &&
                dataAgentEndToEndSidecarAuthorityBoundaryReady &&
                dataAgentEndToEndDefaultTestsLiveRuntimeBoundaryReady;
            const string dataAgentEndToEndChainContractPassDetail =
                "route_boundary=true;xml_policy=true;session_state=true;diagnostics_closure=true;route_denied_no_execute=true;terminal_no_execute=true;sidecar_authority=false;default_tests_live_runtime=false";
            string dataAgentEndToEndChainContractFailDetail =
                $"route_boundary={LowerBool(dataAgentEndToEndRouteBoundaryReady)};xml_policy={LowerBool(dataAgentEndToEndXmlPolicyReady)};session_state={LowerBool(dataAgentEndToEndSessionStateReady)};diagnostics_closure={LowerBool(dataAgentEndToEndDiagnosticsClosureReady)};route_denied_no_execute={LowerBool(dataAgentEndToEndRouteDeniedNoExecuteReady)};terminal_no_execute={LowerBool(dataAgentEndToEndTerminalNoExecuteReady)};sidecar_authority={LowerBool(dataAgentEndToEndSidecarAuthorityBoundaryReady == false)};default_tests_live_runtime={LowerBool(dataAgentEndToEndDefaultTestsLiveRuntimeBoundaryReady == false)}";
            checks.Add(dataAgentEndToEndChainContractReady
                ? Pass("DataAgentEndToEndChainContractPresent", dataAgentEndToEndChainContractPassDetail)
                : Fail("DataAgentEndToEndChainContractPresent", dataAgentEndToEndChainContractFailDetail));

            string dataAgentReplayRepoRoot = FindRepositoryRoot(AppContext.BaseDirectory);
            string dataAgentReplayWrapperPath = Path.Combine(dataAgentReplayRepoRoot, "tools", "replay-dataagent-chain.ps1");
            string dataAgentReplayProjectPath = Path.Combine(dataAgentReplayRepoRoot, "tools", "dataagent-replay", "Alife.Tools.DataAgentReplay.csproj");
            string dataAgentReplayFixturePath = Path.Combine(dataAgentReplayRepoRoot, "Tests", "Alife.Test.DataAgent", "Fixtures", "DataAgentReplay", "v3.9-owner-readiness-analysis.json");
            string dataAgentReplayRunnerPath = Path.Combine(dataAgentReplayRepoRoot, "tools", "dataagent-replay", "DataAgentReplayRunner.cs");
            string dataAgentReplayFormatterPath = Path.Combine(dataAgentReplayRepoRoot, "tools", "dataagent-replay", "DataAgentReplayReportFormatter.cs");
            string dataAgentReplayTestsPath = Path.Combine(dataAgentReplayRepoRoot, "Tests", "Alife.Test.DataAgent", "DataAgentReplayRunbookTests.cs");
            string dataAgentReplayWrapperSource = File.Exists(dataAgentReplayWrapperPath)
                ? File.ReadAllText(dataAgentReplayWrapperPath)
                : string.Empty;
            string dataAgentReplayRunnerSource = File.Exists(dataAgentReplayRunnerPath)
                ? File.ReadAllText(dataAgentReplayRunnerPath)
                : string.Empty;
            string dataAgentReplayFormatterSource = File.Exists(dataAgentReplayFormatterPath)
                ? File.ReadAllText(dataAgentReplayFormatterPath)
                : string.Empty;
            bool dataAgentReplayCliReady =
                File.Exists(dataAgentReplayWrapperPath) &&
                File.Exists(dataAgentReplayProjectPath) &&
                File.Exists(dataAgentReplayTestsPath) &&
                ContractContains(dataAgentReplayWrapperSource, "v3.9-owner-readiness-analysis.json") &&
                ContractContains(dataAgentReplayWrapperSource, @"C:\Users\hu shu\.dotnet\dotnet.exe") &&
                ContractContains(dataAgentReplayWrapperSource, "$Format") &&
                ContractContains(dataAgentReplayWrapperSource, "markdown") &&
                ContractContains(dataAgentReplayWrapperSource, "json") &&
                ContractContains(dataAgentReplayWrapperSource, "Unsupported format") &&
                ContractContains(dataAgentReplayWrapperSource, "--no-restore") &&
                ContractContains(dataAgentReplayWrapperSource, "Alife.Tools.DataAgentReplay.csproj");
            bool dataAgentReplayFixtureReady = File.Exists(dataAgentReplayFixturePath);
            bool dataAgentReplayRealChainReady =
                ContractContains(dataAgentReplayRunnerSource, "ToolCapabilityRouter.CreateDefault") &&
                ContractContains(dataAgentReplayRunnerSource, "XmlFunctionExecutionPolicy") &&
                ContractContains(dataAgentReplayRunnerSource, "XmlPolicyDataAgentToolRouteContextAccessor") &&
                ContractContains(dataAgentReplayRunnerSource, "DataAgentAnalysisToolHandler") &&
                ContractContains(dataAgentReplayRunnerSource, "QChatDiagnosticsService") &&
                ContractContains(dataAgentReplayRunnerSource, "FixedRouteContextAccessor") == false;
            bool dataAgentReplayMarkdownReady =
                ContractContains(dataAgentReplayFormatterSource, "FormatMarkdown") &&
                ContractContains(dataAgentReplayFormatterSource, "# DataAgent Replay:") &&
                ContractContains(dataAgentReplayFormatterSource, "## Expected Markers");
            bool dataAgentReplayJsonReady =
                ContractContains(dataAgentReplayFormatterSource, "FormatJson") &&
                ContractContains(dataAgentReplayFormatterSource, "JsonSerializer.Serialize");
            bool dataAgentReplayExpectedMarkersReady =
                ContractContains(dataAgentReplayRunnerSource, "ExpectedMarkers") &&
                ContractContains(dataAgentReplayRunnerSource, "DataAgentReplayExpectedMarker") &&
                ContractContains(dataAgentReplayRunnerSource, "combined.Contains(marker, StringComparison.Ordinal)") &&
                ContractContains(dataAgentReplayRunnerSource, "All(marker => marker.Passed)");
            bool dataAgentReplaySidecarAuthorityBoundaryReady =
                ContractContains(dataAgentReplayRunnerSource, "DataAgentGraphHandshakeOptions.Disabled") &&
                ContractContains(dataAgentReplayRunnerSource, "DisabledDataAgentGraphSidecarClient.Instance") &&
                ContractContains(dataAgentReplayRunnerSource, "sidecar_disabled") &&
                ContractContains(dataAgentReplayRunnerSource, "SidecarAuthority: disabledSidecarObserved == false || liveRuntimeObserved") &&
                ContractContains(dataAgentReplayRunnerSource, "offlineBoundary.SidecarAuthority == false");
            bool dataAgentReplayDefaultTestsLiveRuntimeBoundaryReady =
                ContractContains(dataAgentReplayRunnerSource, "offlineBoundary.DefaultTestsLiveRuntime == false") &&
                ContractContains(dataAgentReplayRunnerSource, "ContainsLiveRuntimeMarker(replayEvidence)") &&
                ContractContains(dataAgentReplayRunnerSource, "DefaultTestsLiveRuntime: liveRuntimeObserved") &&
                ContractContains(dataAgentReplayRunnerSource, "\"http://\"") &&
                ContractContains(dataAgentReplayRunnerSource, "\"https://\"") &&
                ContractContains(dataAgentReplayRunnerSource, "\"127.0.0.1\"") &&
                ContractContains(dataAgentReplayRunnerSource, "\"localhost\"") &&
                ContractContains(dataAgentReplayRunnerSource, "\"uvicorn\"") &&
                ContractContains(dataAgentReplayRunnerSource, "\"Start-Process\"") &&
                ContractContains(dataAgentReplayRunnerSource, "\"DataAgentGraphHandshakeHttpClient\"") &&
                ContractContains(dataAgentReplayWrapperSource, "dotnet run --no-restore --project") &&
                ContractContains(dataAgentReplayWrapperSource, "Start-Process") == false &&
                ContractContains(dataAgentReplayWrapperSource, "Invoke-WebRequest") == false &&
                ContractContains(dataAgentReplayWrapperSource, "uvicorn") == false &&
                ContractContains(dataAgentReplayWrapperSource, "127.0.0.1") == false &&
                ContractContains(dataAgentReplayWrapperSource, "localhost") == false;
            bool dataAgentReplaySidecarAuthorityObserved = dataAgentReplaySidecarAuthorityBoundaryReady == false;
            bool dataAgentReplayDefaultTestsLiveRuntimeObserved = dataAgentReplayDefaultTestsLiveRuntimeBoundaryReady == false;
            bool dataAgentReplayRunbookReady =
                dataAgentReplayCliReady &&
                dataAgentReplayFixtureReady &&
                dataAgentReplayRealChainReady &&
                dataAgentReplayMarkdownReady &&
                dataAgentReplayJsonReady &&
                dataAgentReplayExpectedMarkersReady &&
                dataAgentReplaySidecarAuthorityObserved == false &&
                dataAgentReplayDefaultTestsLiveRuntimeObserved == false;
            const string dataAgentReplayRunbookPassDetail =
                "cli=true;fixture=true;real_chain=true;markdown=true;json=true;expected_markers=true;sidecar_authority=false;default_tests_live_runtime=false";
            string dataAgentReplayRunbookFailDetail =
                $"cli={LowerBool(dataAgentReplayCliReady)};fixture={LowerBool(dataAgentReplayFixtureReady)};real_chain={LowerBool(dataAgentReplayRealChainReady)};markdown={LowerBool(dataAgentReplayMarkdownReady)};json={LowerBool(dataAgentReplayJsonReady)};expected_markers={LowerBool(dataAgentReplayExpectedMarkersReady)};sidecar_authority={LowerBool(dataAgentReplaySidecarAuthorityObserved)};default_tests_live_runtime={LowerBool(dataAgentReplayDefaultTestsLiveRuntimeObserved)}";
            checks.Add(dataAgentReplayRunbookReady
                ? Pass("DataAgentReplayRunbookPresent", dataAgentReplayRunbookPassDetail)
                : Fail("DataAgentReplayRunbookPresent", dataAgentReplayRunbookFailDetail));

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

            string v328RepoRoot = FindRepositoryRoot(AppContext.BaseDirectory);
            string v328DocPath = Path.Combine(v328RepoRoot, "docs", "dataagent", "dataagent-v3.28-final-readiness-freeze.md");
            string v3LedgerPath = Path.Combine(v328RepoRoot, "docs", "dataagent", "dataagent-v3-closure-ledger.md");
            string readinessScriptPath = Path.Combine(v328RepoRoot, "tools", "check-dataagent-readiness.ps1");
            bool v328DocExists = File.Exists(v328DocPath);
            string v328Doc = v328DocExists ? File.ReadAllText(v328DocPath) : string.Empty;
            string v3Ledger = File.Exists(v3LedgerPath) ? File.ReadAllText(v3LedgerPath) : string.Empty;
            string readinessScript = File.Exists(readinessScriptPath) ? File.ReadAllText(readinessScriptPath) : string.Empty;
            IReadOnlyList<DataAgentV3MilestoneEvidence> v3Manifest = DataAgentV3ClosureManifest.CreateDefault();
            DataAgentV3LedgerParseResult parsedV3Ledger = DataAgentV3ClosureManifest.ParseLedger(v3Ledger);
            DataAgentV3FrozenReadinessSnapshot v3Snapshot = DataAgentV3ClosureManifest.CanonicalReadinessSnapshot;
            IReadOnlySet<string> existingV3EvidencePaths = v3Manifest
                .Select(entry => entry.EvidencePath)
                .Where(path => File.Exists(Path.Combine(v328RepoRoot, path.Replace('/', Path.DirectorySeparatorChar))) )
                .ToHashSet(StringComparer.Ordinal);
            IReadOnlyList<string> v3StaticCheckNames = DataAgentV3ClosureManifest.ParseStaticCheckNames(readinessScript)
                .Where(v3Snapshot.ExpectedStaticCheckNames.Contains)
                .ToArray();
            DataAgentV3ClosureResult v3Closure = DataAgentV3ClosureValidator.Validate(
                v3Snapshot, v3Manifest, checks, parsedV3Ledger, v3StaticCheckNames, existingV3EvidencePaths);
            DataAgentV3FinalReadinessFreeze v328Freeze =
                DataAgentV3FinalReadinessFreezeBuilder.Build(v3Closure);
            string v328Packet = DataAgentV3FinalReadinessFreezeFormatter.Format(v328Freeze);
            bool v328DocMarkers =
                v328Doc.Contains("v3_final_readiness_freeze=true", StringComparison.Ordinal) &&
                v328Doc.Contains("final_v3_version=v3.28", StringComparison.Ordinal) &&
                v328Doc.Contains("source_versions=v3.0-v3.27", StringComparison.Ordinal) &&
                v328Doc.Contains("frozen_required_check_count=111", StringComparison.Ordinal) &&
                v328Doc.Contains("frozen_core_check_count=95", StringComparison.Ordinal) &&
                v328Doc.Contains("missing_milestone_count=0", StringComparison.Ordinal) &&
                v328Doc.Contains("missing_evidence_path_count=0", StringComparison.Ordinal) &&
                v328Doc.Contains("missing_required_check_count=0", StringComparison.Ordinal) &&
                v328Doc.Contains("failed_required_check_count=0", StringComparison.Ordinal) &&
                v328Doc.Contains("duplicate_required_check_count=0", StringComparison.Ordinal) &&
                v328Doc.Contains("unexpected_check_count=0", StringComparison.Ordinal) &&
                v328Doc.Contains("operator_evidence_pack_present=true", StringComparison.Ordinal) &&
                v328Doc.Contains("readiness_gates_frozen=true", StringComparison.Ordinal) &&
                v328Doc.Contains("operator_decides=true", StringComparison.Ordinal);
            bool v328Boundary =
                v328Doc.Contains("agent_advisory_only=true", StringComparison.Ordinal) &&
                v328Doc.Contains("harness_execution_authority=true", StringComparison.Ordinal) &&
                v328Doc.Contains("csharp_validation_authority=true", StringComparison.Ordinal) &&
                v328Doc.Contains("default_result_changed=false", StringComparison.Ordinal) &&
                v328Doc.Contains("manual_only=true", StringComparison.Ordinal) &&
                v328Doc.Contains("starts_runtime=false", StringComparison.Ordinal) &&
                v328Doc.Contains("installs_dependencies=false", StringComparison.Ordinal) &&
                v328Doc.Contains("calls_sidecar=false", StringComparison.Ordinal) &&
                v328Doc.Contains("stores_secrets=false", StringComparison.Ordinal) &&
                v328Doc.Contains("stores_sql=false", StringComparison.Ordinal) &&
                v328Doc.Contains("stores_hidden_context=false", StringComparison.Ordinal);
            bool v328ModelReady =
                typeof(DataAgentV3FinalReadinessFreeze).IsClass &&
                typeof(DataAgentV3FinalReadinessFreezeBuilder).IsClass &&
                typeof(DataAgentV3FinalReadinessFreezeFormatter).IsClass &&
                string.Equals(v328Freeze.FreezeId, "v3.28-final-readiness-freeze", StringComparison.Ordinal) &&
                string.Equals(v328Freeze.FinalV3Version, "v3.28", StringComparison.Ordinal) &&
                string.Equals(v328Freeze.SourceVersions, "v3.0-v3.27", StringComparison.Ordinal) &&
                v328Freeze.FrozenRequiredCheckCount == 111 &&
                v328Freeze.FrozenCoreCheckCount == 95 &&
                v328Freeze.AllFrozenChecksPassed &&
                v328Freeze.MissingMilestoneCount == 0 &&
                v328Freeze.MissingEvidencePathCount == 0 &&
                v328Freeze.MissingRequiredCheckCount == 0 &&
                v328Freeze.FailedRequiredCheckCount == 0 &&
                v328Freeze.DuplicateRequiredCheckCount == 0 &&
                v328Freeze.UnexpectedCheckCount == 0 &&
                v328Freeze.OperatorEvidencePackPresent &&
                v328Freeze.ReadinessGatesFrozen &&
                v328Freeze.OperatorDecides &&
                v328Freeze.AgentAdvisoryOnly &&
                v328Freeze.HarnessExecutionAuthority &&
                v328Freeze.CSharpValidationAuthority &&
                v328Freeze.DefaultResultChanged == false &&
                v328Freeze.ManualOnly &&
                v328Freeze.StartsRuntime == false &&
                v328Freeze.InstallsDependencies == false &&
                v328Freeze.CallsSidecar == false &&
                v328Freeze.StoresSecrets == false &&
                v328Freeze.StoresSql == false &&
                v328Freeze.StoresHiddenContext == false;
            bool v328PacketMarkers =
                v328Packet.Contains("v3_final_readiness_freeze=true", StringComparison.Ordinal) &&
                v328Packet.Contains("final_v3_version=v3.28", StringComparison.Ordinal) &&
                v328Packet.Contains("source_versions=v3.0-v3.27", StringComparison.Ordinal) &&
                v328Packet.Contains("frozen_required_check_count=111", StringComparison.Ordinal) &&
                v328Packet.Contains("frozen_core_check_count=95", StringComparison.Ordinal) &&
                v328Packet.Contains("missing_milestone_count=0", StringComparison.Ordinal) &&
                v328Packet.Contains("missing_evidence_path_count=0", StringComparison.Ordinal) &&
                v328Packet.Contains("missing_required_check_count=0", StringComparison.Ordinal) &&
                v328Packet.Contains("failed_required_check_count=0", StringComparison.Ordinal) &&
                v328Packet.Contains("duplicate_required_check_count=0", StringComparison.Ordinal) &&
                v328Packet.Contains("unexpected_check_count=0", StringComparison.Ordinal) &&
                v328Packet.Contains("all_frozen_checks_passed=true", StringComparison.Ordinal) &&
                v328Packet.Contains("operator_evidence_pack_present=true", StringComparison.Ordinal) &&
                v328Packet.Contains("readiness_gates_frozen=true", StringComparison.Ordinal) &&
                v328Packet.Contains("operator_decides=true", StringComparison.Ordinal) &&
                v328Packet.Contains("agent_advisory_only=true", StringComparison.Ordinal) &&
                v328Packet.Contains("harness_execution_authority=true", StringComparison.Ordinal) &&
                v328Packet.Contains("csharp_validation_authority=true", StringComparison.Ordinal) &&
                v328Packet.Contains("default_result_changed=false", StringComparison.Ordinal) &&
                v328Packet.Contains("manual_only=true", StringComparison.Ordinal) &&
                v328Packet.Contains("starts_runtime=false", StringComparison.Ordinal) &&
                v328Packet.Contains("installs_dependencies=false", StringComparison.Ordinal) &&
                v328Packet.Contains("calls_sidecar=false", StringComparison.Ordinal) &&
                v328Packet.Contains("stores_secrets=false", StringComparison.Ordinal) &&
                v328Packet.Contains("stores_sql=false", StringComparison.Ordinal) &&
                v328Packet.Contains("stores_hidden_context=false", StringComparison.Ordinal) &&
                v328Packet.Contains("SELECT", StringComparison.OrdinalIgnoreCase) == false &&
                v328Packet.Contains("bearer", StringComparison.OrdinalIgnoreCase) == false;
            bool v328Ready =
                v3Closure.Accepted &&
                v328DocExists &&
                v328DocMarkers &&
                v328Boundary &&
                v328ModelReady &&
                v328PacketMarkers;
            checks.Add(v328Ready
                ? Pass("GraphHandshakeFinalV3ReadinessFreezePresent", "v3_final_readiness_freeze=true;final_v3_version=v3.28;source_versions=v3.0-v3.27;frozen_required_check_count=111;frozen_core_check_count=95;missing_milestone_count=0;missing_evidence_path_count=0;missing_required_check_count=0;failed_required_check_count=0;duplicate_required_check_count=0;unexpected_check_count=0;operator_evidence_pack_present=true;readiness_gates_frozen=true;operator_decides=true;agent_advisory_only=true;harness_execution_authority=true;csharp_validation_authority=true;default_result_changed=false;manual_only=true;starts_runtime=false;installs_dependencies=false;calls_sidecar=false;stores_secrets=false;stores_sql=false;stores_hidden_context=false")
                : Fail("GraphHandshakeFinalV3ReadinessFreezePresent", $"closure={LowerBool(v3Closure.Accepted)};doc={LowerBool(v328DocExists)};doc_markers={LowerBool(v328DocMarkers)};boundary={LowerBool(v328Boundary)};model={LowerBool(v328ModelReady)};packet={LowerBool(v328PacketMarkers)};missing_milestone_count={v328Freeze.MissingMilestoneCount};missing_evidence_path_count={v328Freeze.MissingEvidencePathCount};missing_required_check_count={v328Freeze.MissingRequiredCheckCount};failed_required_check_count={v328Freeze.FailedRequiredCheckCount};duplicate_required_check_count={v328Freeze.DuplicateRequiredCheckCount};unexpected_check_count={v328Freeze.UnexpectedCheckCount}"));

            string v40DocPath = Path.Combine(v328RepoRoot, "docs", "dataagent", "dataagent-v4.0-real-langgraph-manual-shadow-integration.md");
            string v40HarnessScriptPath = Path.Combine(v328RepoRoot, "tools", "run-dataagent-v4-manual-shadow.ps1");
            string v40TestPath = Path.Combine(v328RepoRoot, "Tests", "Alife.Test.DataAgent", "DataAgentV40RealLangGraphManualShadowIntegrationTests.cs");
            bool v40DocExists = File.Exists(v40DocPath);
            bool v40HarnessScriptExists = File.Exists(v40HarnessScriptPath);
            bool v40TestExists = File.Exists(v40TestPath);
            string v40Doc = v40DocExists ? File.ReadAllText(v40DocPath) : string.Empty;
            string v40HarnessScript = v40HarnessScriptExists ? File.ReadAllText(v40HarnessScriptPath) : string.Empty;
            string v40Tests = v40TestExists ? File.ReadAllText(v40TestPath) : string.Empty;
            DataAgentRealLangGraphManualShadowInput v40FallbackInput = new(
                SourceReplayId: "v4.0-readiness-gate",
                OperatorStartedRuntime: true,
                LoopbackOnly: true,
                RuntimeStartedByAlife: false,
                DependenciesInstalledByAlife: false,
                SidecarCalledByAlife: false,
                ContextLayers:
                [
                    new DataAgentRealLangGraphManualShadowContextLayer("layer_1_route", "route=allowed;source_baseline=v3.28"),
                    new DataAgentRealLangGraphManualShadowContextLayer("layer_2_evidence", "reason_code=timeout_or_transport_failure"),
                    new DataAgentRealLangGraphManualShadowContextLayer("layer_3_excerpt", "bounded_failure_excerpt=timeout_or_transport_failure")
                ],
                ManualShadowResult: v325Rejected,
                DiffGateResult: v326Rejected);
            DataAgentRealLangGraphManualShadowResult v40Fallback =
                DataAgentRealLangGraphManualShadowIntegration.Evaluate(v40FallbackInput);
            string v40Packet = DataAgentRealLangGraphManualShadowFormatter.Format(v40Fallback);
            bool v40DocMarkers =
                v40Doc.Contains("real_langgraph_manual_shadow_integration=true", StringComparison.Ordinal) &&
                v40Doc.Contains("source_baseline=v3.28", StringComparison.Ordinal) &&
                v40Doc.Contains("manual_only=true", StringComparison.Ordinal) &&
                v40Doc.Contains("operator_started_runtime=true", StringComparison.Ordinal) &&
                v40Doc.Contains("loopback_only=true", StringComparison.Ordinal) &&
                v40Doc.Contains("calls_sidecar=false", StringComparison.Ordinal);
            bool v40Boundary =
                v40Doc.Contains("manual shadow integration", StringComparison.OrdinalIgnoreCase) &&
                v40Doc.Contains("not automatic startup", StringComparison.OrdinalIgnoreCase) &&
                v40Doc.Contains("not a C# sidecar call path", StringComparison.OrdinalIgnoreCase) &&
                v40Doc.Contains("operator manually starts", StringComparison.OrdinalIgnoreCase) &&
                v40Doc.Contains("manual harness shadow request", StringComparison.OrdinalIgnoreCase) &&
                v40Doc.Contains("C# validation", StringComparison.Ordinal) &&
                v40Doc.Contains("advisory only", StringComparison.OrdinalIgnoreCase) &&
                v40Doc.Contains("default DataAgent result remains unchanged", StringComparison.Ordinal) &&
                v40Doc.Contains("fallback", StringComparison.OrdinalIgnoreCase) &&
                v40Doc.Contains("No secrets", StringComparison.Ordinal) &&
                v40Doc.Contains("SQL text", StringComparison.Ordinal) &&
                v40Doc.Contains("hidden context", StringComparison.Ordinal);
            bool v40ModelReady =
                typeof(DataAgentRealLangGraphManualShadowContextLayer).IsClass &&
                typeof(DataAgentRealLangGraphManualShadowInput).IsClass &&
                typeof(DataAgentRealLangGraphManualShadowResult).IsClass &&
                typeof(DataAgentRealLangGraphManualShadowIntegration).IsClass &&
                typeof(DataAgentRealLangGraphManualShadowFormatter).IsClass &&
                typeof(DataAgentRealLangGraphManualShadowArtifactWriter).IsClass &&
                string.Equals(v40Fallback.SourceBaseline, "v3.28", StringComparison.Ordinal) &&
                v40Fallback.Accepted == false &&
                v40Fallback.ManualOnly &&
                v40Fallback.OperatorStartedRuntime &&
                v40Fallback.LoopbackOnly &&
                v40Fallback.AgentAdvisoryOnly &&
                v40Fallback.HarnessExecutionAuthority &&
                v40Fallback.CSharpValidationAuthority &&
                v40Fallback.DefaultResultChanged == false &&
                v40Fallback.FallbackRequired &&
                v40Fallback.StartsRuntime == false &&
                v40Fallback.InstallsDependencies == false &&
                v40Fallback.CallsSidecar == false &&
                v40Fallback.StoresSecrets == false &&
                v40Fallback.StoresSql == false &&
                v40Fallback.StoresHiddenContext == false;
            bool v40PacketMarkers =
                v40Packet.Contains("real_langgraph_manual_shadow_integration=true", StringComparison.Ordinal) &&
                v40Packet.Contains("source_baseline=v3.28", StringComparison.Ordinal) &&
                v40Packet.Contains("manual_only=true", StringComparison.Ordinal) &&
                v40Packet.Contains("operator_started_runtime=true", StringComparison.Ordinal) &&
                v40Packet.Contains("loopback_only=true", StringComparison.Ordinal) &&
                v40Packet.Contains("agent_advisory_only=true", StringComparison.Ordinal) &&
                v40Packet.Contains("harness_execution_authority=true", StringComparison.Ordinal) &&
                v40Packet.Contains("csharp_validation_authority=true", StringComparison.Ordinal) &&
                v40Packet.Contains("default_result_changed=false", StringComparison.Ordinal) &&
                v40Packet.Contains("fallback_required=true", StringComparison.Ordinal) &&
                v40Packet.Contains("starts_runtime=false", StringComparison.Ordinal) &&
                v40Packet.Contains("installs_dependencies=false", StringComparison.Ordinal) &&
                v40Packet.Contains("calls_sidecar=false", StringComparison.Ordinal) &&
                v40Packet.Contains("stores_secrets=false", StringComparison.Ordinal) &&
                v40Packet.Contains("stores_sql=false", StringComparison.Ordinal) &&
                v40Packet.Contains("stores_hidden_context=false", StringComparison.Ordinal) &&
                v40Packet.Contains("SELECT", StringComparison.OrdinalIgnoreCase) == false &&
                v40Packet.Contains("bearer", StringComparison.OrdinalIgnoreCase) == false;
            bool v40HarnessMarkers =
                v40HarnessScript.Contains("real_langgraph_manual_shadow_integration=true", StringComparison.Ordinal) &&
                v40HarnessScript.Contains("source_baseline=v3.28", StringComparison.Ordinal) &&
                v40HarnessScript.Contains("manual_only=true", StringComparison.Ordinal) &&
                v40HarnessScript.Contains("operator_started_runtime=true", StringComparison.Ordinal) &&
                v40HarnessScript.Contains("loopback_only=true", StringComparison.Ordinal) &&
                v40HarnessScript.Contains("agent_advisory_only=true", StringComparison.Ordinal) &&
                v40HarnessScript.Contains("harness_execution_authority=true", StringComparison.Ordinal) &&
                v40HarnessScript.Contains("csharp_validation_authority=true", StringComparison.Ordinal) &&
                v40HarnessScript.Contains("default_result_changed=false", StringComparison.Ordinal) &&
                v40HarnessScript.Contains("fallback_required=true", StringComparison.Ordinal) &&
                v40HarnessScript.Contains("starts_runtime=false", StringComparison.Ordinal) &&
                v40HarnessScript.Contains("installs_dependencies=false", StringComparison.Ordinal) &&
                v40HarnessScript.Contains("calls_sidecar=false", StringComparison.Ordinal) &&
                v40HarnessScript.Contains("stores_secrets=false", StringComparison.Ordinal) &&
                v40HarnessScript.Contains("stores_sql=false", StringComparison.Ordinal) &&
                v40HarnessScript.Contains("stores_hidden_context=false", StringComparison.Ordinal) &&
                v40HarnessScript.Contains("Assert-LoopbackBaseUri", StringComparison.Ordinal) &&
                v40HarnessScript.Contains("Invoke-WebRequest", StringComparison.Ordinal) &&
                v40HarnessScript.Contains("Start-Process", StringComparison.Ordinal) == false &&
                v40HarnessScript.Contains("pip install", StringComparison.Ordinal) == false;
            bool v40ArtifactWriterReady =
                string.Equals(DataAgentRealLangGraphManualShadowArtifactWriter.FileName, "dataagent-v4.0-real-langgraph-manual-shadow.txt", StringComparison.Ordinal);
            bool v40TestsReady =
                v40Tests.Contains("IntegrationAcceptsManualLangGraphAdvisoryThroughReplayDiffGate", StringComparison.Ordinal) &&
                v40Tests.Contains("ManualHarnessScriptDeclaresOperatorOnlyLoopbackBoundary", StringComparison.Ordinal) &&
                v40Tests.Contains("ManualHarnessArtifactOutputDoesNotLeakAbsoluteDirectoryAndJsonUsesMarkerSchema", StringComparison.Ordinal) &&
                v40Tests.Contains("CallsSidecar", StringComparison.Ordinal) &&
                v40Tests.Contains("calls_sidecar=false", StringComparison.Ordinal) &&
                v40Tests.Contains("source_baseline", StringComparison.Ordinal) &&
                v40Tests.Contains("Does.Not.Contain(\"source_baseline\")", StringComparison.Ordinal);
            bool v40Ready =
                v40DocExists &&
                v40HarnessScriptExists &&
                v40TestExists &&
                v40DocMarkers &&
                v40Boundary &&
                v40ModelReady &&
                v40PacketMarkers &&
                v40HarnessMarkers &&
                v40ArtifactWriterReady &&
                v40TestsReady;
            checks.Add(v40Ready
                ? Pass("GraphHandshakeRealLangGraphManualShadowIntegrationPresent", "real_langgraph_manual_shadow_integration=true;source_baseline=v3.28;manual_only=true;operator_started_runtime=true;loopback_only=true;agent_advisory_only=true;harness_execution_authority=true;csharp_validation_authority=true;default_result_changed=false;fallback_required=true;starts_runtime=false;installs_dependencies=false;calls_sidecar=false;stores_secrets=false;stores_sql=false;stores_hidden_context=false")
                : Fail("GraphHandshakeRealLangGraphManualShadowIntegrationPresent", $"doc={LowerBool(v40DocExists)};script={LowerBool(v40HarnessScriptExists)};tests={LowerBool(v40TestExists)};doc_markers={LowerBool(v40DocMarkers)};boundary={LowerBool(v40Boundary)};model={LowerBool(v40ModelReady)};packet={LowerBool(v40PacketMarkers)};harness={LowerBool(v40HarnessMarkers)};artifact_writer={LowerBool(v40ArtifactWriterReady)};test_markers={LowerBool(v40TestsReady)}"));

            string v41DocPath = Path.Combine(v328RepoRoot, "docs", "dataagent", "dataagent-v4.1-token-budgeted-manual-shadow-context.md");
            bool v41DocExists = File.Exists(v41DocPath);
            string v41Doc = v41DocExists ? File.ReadAllText(v41DocPath) : string.Empty;
            DataAgentRealLangGraphManualShadowContextEnvelope v41Envelope =
                DataAgentRealLangGraphManualShadowContextBudgetBuilder.Build(
                    [
                        new DataAgentRealLangGraphManualShadowContextLayer("layer_1_route", "fixture=v4.1-manual-shadow;route=allowed;node=manual_shadow"),
                        new DataAgentRealLangGraphManualShadowContextLayer("layer_2_evidence", "reason_code=manual_shadow_review;evidence_ref=v3.28-final-readiness-freeze"),
                        new DataAgentRealLangGraphManualShadowContextLayer("layer_3_excerpt", "bounded_failure_excerpt=operator_review_required")
                    ],
                    new DataAgentRealLangGraphManualShadowContextBudgetOptions(
                        MaxEnvelopeChars: 1200,
                        MaxLayerChars: 400,
                        RequiredLayerNames:
                        [
                            "layer_1_route",
                            "layer_2_evidence",
                            "layer_3_excerpt"
                        ]));
            string v41Packet = DataAgentRealLangGraphManualShadowContextBudgetFormatter.Format(v41Envelope);
            bool v41Markers =
                v41Doc.Contains("manual_shadow_context_budget=true", StringComparison.Ordinal) &&
                v41Doc.Contains("source_baseline=v4.0", StringComparison.Ordinal) &&
                v41Doc.Contains("max_envelope_chars=1200", StringComparison.Ordinal) &&
                v41Doc.Contains("max_layer_chars=400", StringComparison.Ordinal) &&
                v41Doc.Contains("required_layer_count=3", StringComparison.Ordinal) &&
                v41Doc.Contains("agent_advisory_only=true", StringComparison.Ordinal) &&
                v41Doc.Contains("harness_execution_authority=true", StringComparison.Ordinal) &&
                v41Doc.Contains("csharp_validation_authority=true", StringComparison.Ordinal) &&
                v41Doc.Contains("default_result_changed=false", StringComparison.Ordinal) &&
                v41Doc.Contains("starts_runtime=false", StringComparison.Ordinal) &&
                v41Doc.Contains("installs_dependencies=false", StringComparison.Ordinal) &&
                v41Doc.Contains("calls_sidecar=false", StringComparison.Ordinal) &&
                v41Doc.Contains("stores_secrets=false", StringComparison.Ordinal) &&
                v41Doc.Contains("stores_sql=false", StringComparison.Ordinal) &&
                v41Doc.Contains("stores_hidden_context=false", StringComparison.Ordinal) &&
                v41Packet.Contains("manual_shadow_context_budget=true", StringComparison.Ordinal) &&
                v41Packet.Contains("accepted=true", StringComparison.Ordinal) &&
                v41Packet.Contains("layer_count=3", StringComparison.Ordinal) &&
                v41Packet.Contains("stores_hidden_context=false", StringComparison.Ordinal) &&
                v41Packet.Contains("SELECT", StringComparison.OrdinalIgnoreCase) == false &&
                v41Packet.Contains("FROM hidden_context", StringComparison.OrdinalIgnoreCase) == false &&
                v41Packet.Contains("bearer", StringComparison.OrdinalIgnoreCase) == false;
            bool v41Ready =
                v41DocExists &&
                v41Envelope.Accepted &&
                v41Envelope.TotalIncludedChars <= 1200 &&
                v41Envelope.Layers.All(layer => layer.IncludedChars <= 400) &&
                v41Markers;
            checks.Add(v41Ready
                ? Pass("GraphHandshakeRealLangGraphManualShadowContextBudgetPresent", "manual_shadow_context_budget=true;source_baseline=v4.0;max_envelope_chars=1200;max_layer_chars=400;required_layer_count=3;agent_advisory_only=true;harness_execution_authority=true;csharp_validation_authority=true;default_result_changed=false;starts_runtime=false;installs_dependencies=false;calls_sidecar=false;stores_secrets=false;stores_sql=false;stores_hidden_context=false")
                : Fail("GraphHandshakeRealLangGraphManualShadowContextBudgetPresent", $"doc={LowerBool(v41DocExists)};envelope={LowerBool(v41Envelope.Accepted)};markers={LowerBool(v41Markers)}"));
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

    static string FindRepositoryRoot()
    {
        foreach (string start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            DirectoryInfo? directory = new(start);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "docs")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "tools")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        return Environment.CurrentDirectory;
    }

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
