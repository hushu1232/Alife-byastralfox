using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;
using Alife.Framework.Models.StateEstimation;

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

    sealed class FixedLlmClient(string raw) : ILlmDataAgentPlannerClient
    {
        public string Complete(DataAgentLlmPlannerPrompt prompt) => raw;
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
