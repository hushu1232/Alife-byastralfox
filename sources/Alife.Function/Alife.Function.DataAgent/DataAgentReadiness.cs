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
        }
        catch (Exception ex)
        {
            checks.Add(Fail("DataAgentReadinessException", ex.Message));
        }

        return checks;
    }

    static DataAgentReadinessCheck Pass(string name, string detail) => new(name, true, detail);

    static DataAgentReadinessCheck Fail(string name, string detail) => new(name, false, detail);
}

public sealed record DataAgentReadinessCheck(string Name, bool Passed, string Detail);
