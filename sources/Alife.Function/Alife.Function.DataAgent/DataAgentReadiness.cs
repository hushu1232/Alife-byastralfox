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

            DataAgentAnswer answer = new DataAgentService(databasePath).Answer("当前还有哪些 required gate 没通过？");
            checks.Add(answer.Context.Contains("[data_agent_context]", StringComparison.Ordinal) &&
                       answer.Context.Contains("[/data_agent_context]", StringComparison.Ordinal)
                ? Pass("ContextContributionStable", "data_agent_context wrapper present")
                : Fail("ContextContributionStable", "missing data_agent_context wrapper"));

            checks.Add(answer.Context.Contains("planner_confidence=", StringComparison.Ordinal) &&
                       answer.Context.Contains("planner_reason=", StringComparison.Ordinal) &&
                       answer.PlannerExplanation.Signals.Count > 0
                ? Pass("PlannerExplanationInContext", answer.PlannerExplanation.Confidence)
                : Fail("PlannerExplanationInContext", answer.Context));

            checks.Add(typeof(IDataAgentQueryPlanner).IsAssignableFrom(typeof(DeterministicDataAgentQueryPlanner))
                ? Pass("PlannerInterfacePresent", nameof(IDataAgentQueryPlanner))
                : Fail("PlannerInterfacePresent", "deterministic planner does not implement interface"));

            DataAgentQueryPlanEnvelope deterministicEnvelope = new DeterministicDataAgentQueryPlanner().Plan(new DataAgentQueryRequest(
                "Which runtime readiness gate is required?",
                "developer",
                "en-US",
                false));
            DataAgentQueryPlan deterministicPlan = deterministicEnvelope.Plan;
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
        }
        catch (Exception ex)
        {
            checks.Add(Fail("DataAgentReadinessException", ex.Message));
        }

        return checks;
    }

    static DataAgentReadinessCheck Pass(string name, string detail) => new(name, true, detail);

    static DataAgentReadinessCheck Fail(string name, string detail) => new(name, false, detail);

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
}

public sealed record DataAgentReadinessCheck(string Name, bool Passed, string Detail);
