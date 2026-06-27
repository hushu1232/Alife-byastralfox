using System.Diagnostics;
using System.Text.Json;

namespace Alife.Function.DataAgent;

public sealed class DataAgentService
{
    readonly DataAgentCatalog catalog = DataAgentCatalog.CreateDefault();
    readonly DataAgentSqlSafetyValidator safetyValidator = new();
    readonly string databasePath;
    readonly IDataAgentQueryPlanner planner;

    public DataAgentService(string databasePath)
        : this(databasePath, new DeterministicDataAgentQueryPlanner())
    {
    }

    public DataAgentService(string databasePath, IDataAgentQueryPlanner planner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(planner);

        this.databasePath = databasePath;
        this.planner = planner;
    }

    public DataAgentAnswer Answer(string question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        DataAgentQueryPlan plan = planner.Plan(new DataAgentQueryRequest(question, "developer", "zh-CN", false));
        string queryPlanJson = JsonSerializer.Serialize(plan);
        DataAgentValidationResult planValidation = new DataAgentQueryPlanValidator(catalog).Validate(plan);

        if (planValidation.IsValid == false)
            return Reject(question, plan, queryPlanJson, string.Join(";", planValidation.Errors), string.Empty);

        DataAgentCompiledSql compiled = new DataAgentSqlCompiler(catalog).Compile(plan);
        DataAgentSqlSafetyResult safety = safetyValidator.Validate(compiled.Sql);
        if (safety.IsSafe == false)
            return Reject(question, plan, queryPlanJson, safety.Reason, compiled.Sql);

        Stopwatch stopwatch = Stopwatch.StartNew();
        DataAgentQueryResult result = new DataAgentQueryExecutor(databasePath).Execute(compiled);
        stopwatch.Stop();

        string summary = DataAgentResultSummarizer.Summarize(plan, result);
        string context = DataAgentContextProvider.Build(question, plan.Dataset, compiled.Sql, result.Rows.Count, summary, result);

        new DataAgentAuditLog(databasePath).RecordAccepted(
            question,
            plan.Dataset,
            queryPlanJson,
            compiled.Sql,
            result.Rows.Count,
            stopwatch.Elapsed);

        return new DataAgentAnswer(plan.Dataset, compiled.Sql, result.Rows.Count, summary, context, true, string.Empty);
    }

    DataAgentAnswer Reject(string question, DataAgentQueryPlan plan, string queryPlanJson, string reason, string generatedSql)
    {
        new DataAgentAuditLog(databasePath).RecordRejected(
            question,
            plan.Dataset,
            queryPlanJson,
            generatedSql,
            reason,
            TimeSpan.Zero);

        string summary = $"DataAgent query rejected: {reason}";
        string context = DataAgentContextProvider.BuildRejected(question, plan.Dataset, reason);
        return new DataAgentAnswer(plan.Dataset, generatedSql, 0, summary, context, false, reason);
    }
}

public sealed record DataAgentAnswer(
    string Dataset,
    string Sql,
    int RowCount,
    string Summary,
    string Context,
    bool Validated,
    string RejectedReason);
