using System.Diagnostics;
using System.Text.Json;

namespace Alife.Function.DataAgent;

public sealed class DataAgentService(string databasePath)
{
    readonly DataAgentCatalog catalog = DataAgentCatalog.CreateDefault();
    readonly DataAgentSqlSafetyValidator safetyValidator = new();

    public DataAgentAnswer Answer(string question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        DataAgentQueryPlan plan = ResolvePlan(question);
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

    static DataAgentQueryPlan ResolvePlan(string question)
    {
        if (question.Contains("readiness", StringComparison.OrdinalIgnoreCase) &&
            (question.Contains("TTS", StringComparison.OrdinalIgnoreCase) || question.Contains("视图", StringComparison.OrdinalIgnoreCase)))
        {
            return new DataAgentQueryPlan(
                "runtime_readiness_check",
                "find_qchat_tts_readiness",
                ["capability", "status", "failure_reason", "evidence_path"],
                [new DataAgentFilter("capability", "contains", "Tts")],
                [],
                50);
        }

        if (question.Contains("runtime readiness", StringComparison.OrdinalIgnoreCase) &&
            question.Contains("required", StringComparison.OrdinalIgnoreCase))
        {
            return new DataAgentQueryPlan(
                "engineering_gate",
                "find_runtime_readiness_required_evidence",
                ["name", "status", "evidence_path"],
                [new DataAgentFilter("name", "contains", "Runtime readiness")],
                [],
                10);
        }

        if (question.Contains("测试", StringComparison.OrdinalIgnoreCase) &&
            (question.Contains("通过", StringComparison.OrdinalIgnoreCase) || question.Contains("失败", StringComparison.OrdinalIgnoreCase)))
        {
            return new DataAgentQueryPlan(
                "test_run",
                "latest_test_run_summary",
                ["suite_name", "passed", "failed", "skipped", "total", "command"],
                [],
                [new DataAgentOrderBy("ran_at", "desc")],
                1);
        }

        if (question.Contains("DataAgent", StringComparison.OrdinalIgnoreCase) ||
            question.Contains("NL2SQL", StringComparison.OrdinalIgnoreCase))
        {
            return new DataAgentQueryPlan(
                "document_index",
                "find_dataagent_documents",
                ["path", "title", "summary"],
                [new DataAgentFilter("tags", "contains", "dataagent")],
                [new DataAgentOrderBy("updated_at", "desc")],
                20);
        }

        return new DataAgentQueryPlan(
            "engineering_gate",
            "find_missing_required_gates",
            ["name", "status", "evidence_path"],
            [
                new DataAgentFilter("required", "=", true),
                new DataAgentFilter("status", "!=", "passed")
            ],
            [],
            50);
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
