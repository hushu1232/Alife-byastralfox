namespace Alife.Function.DataAgent;

public static class DataAgentResultSummarizer
{
    public static string Summarize(DataAgentQueryPlan plan, DataAgentQueryResult result)
    {
        return plan.Dataset switch
        {
            "engineering_gate" => SummarizeEngineeringGate(plan, result),
            "runtime_readiness_check" => SummarizeRuntimeReadiness(result),
            "test_run" => SummarizeTestRun(result),
            "document_index" => SummarizeDocuments(result),
            _ => $"{result.Rows.Count} rows returned from {plan.Dataset}."
        };
    }

    static string SummarizeEngineeringGate(DataAgentQueryPlan plan, DataAgentQueryResult result)
    {
        if (plan.Intent == "find_missing_required_gates" && result.Rows.Count == 0)
            return "0 required gate missing in the local DataAgent fixture.";

        return string.Join(
            "; ",
            result.Rows.Select(row => $"{Value(row, "name")} is {Value(row, "status")} ({Value(row, "evidence_path")})"));
    }

    static string SummarizeRuntimeReadiness(DataAgentQueryResult result)
    {
        return string.Join(
            "; ",
            result.Rows.Select(row => $"{Value(row, "capability")} is {Value(row, "status")} ({Value(row, "evidence_path")})"));
    }

    static string SummarizeTestRun(DataAgentQueryResult result)
    {
        if (result.Rows.Count == 0)
            return "No test run rows found.";

        IReadOnlyDictionary<string, object?> row = result.Rows[0];
        return $"{Value(row, "suite_name")}: {Value(row, "passed")} passed, {Value(row, "failed")} failed, {Value(row, "skipped")} skipped, {Value(row, "total")} total.";
    }

    static string SummarizeDocuments(DataAgentQueryResult result)
    {
        return string.Join(
            "; ",
            result.Rows.Select(row => $"{Value(row, "title")} ({Value(row, "path")})"));
    }

    static string Value(IReadOnlyDictionary<string, object?> row, string field)
    {
        return row.TryGetValue(field, out object? value) ? Convert.ToString(value) ?? string.Empty : string.Empty;
    }
}
