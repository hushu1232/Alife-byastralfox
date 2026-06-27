namespace Alife.Function.DataAgent;

public sealed class DeterministicDataAgentQueryPlanner : IDataAgentQueryPlanner
{
    public DataAgentQueryPlanEnvelope Plan(DataAgentQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Question);

        string question = request.Question;

        if (question.Contains("readiness", StringComparison.OrdinalIgnoreCase) &&
            (question.Contains("TTS", StringComparison.OrdinalIgnoreCase) ||
             question.Contains("vision", StringComparison.OrdinalIgnoreCase) ||
             question.Contains("视图", StringComparison.OrdinalIgnoreCase) ||
             question.Contains("瑙嗗浘", StringComparison.OrdinalIgnoreCase)))
        {
            return Envelope(
                new DataAgentQueryPlan(
                    "runtime_readiness_check",
                    "find_qchat_tts_readiness",
                    ["capability", "status", "failure_reason", "evidence_path"],
                    [new DataAgentFilter("capability", "contains", "Tts")],
                    [],
                    50),
                "high",
                ["readiness", "tts", "vision"],
                "question mentions QChat TTS or vision readiness");
        }

        if (question.Contains("runtime readiness", StringComparison.OrdinalIgnoreCase) &&
            question.Contains("required", StringComparison.OrdinalIgnoreCase))
        {
            return Envelope(
                new DataAgentQueryPlan(
                    "engineering_gate",
                    "find_runtime_readiness_required_evidence",
                    ["name", "status", "evidence_path"],
                    [new DataAgentFilter("name", "contains", "Runtime readiness")],
                    [],
                    10),
                "high",
                ["runtime", "readiness", "required"],
                "question mentions runtime readiness required evidence");
        }

        bool asksAboutTests = question.Contains("test", StringComparison.OrdinalIgnoreCase) ||
                              question.Contains("测试", StringComparison.OrdinalIgnoreCase) ||
                              question.Contains("娴嬭瘯", StringComparison.OrdinalIgnoreCase);
        bool asksAboutTestResult = question.Contains("pass", StringComparison.OrdinalIgnoreCase) ||
                                   question.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
                                   question.Contains("通过", StringComparison.OrdinalIgnoreCase) ||
                                   question.Contains("失败", StringComparison.OrdinalIgnoreCase) ||
                                   question.Contains("閫氳繃", StringComparison.OrdinalIgnoreCase) ||
                                   question.Contains("澶辫触", StringComparison.OrdinalIgnoreCase);

        if (asksAboutTests && asksAboutTestResult)
        {
            return Envelope(
                new DataAgentQueryPlan(
                    "test_run",
                    "latest_test_run_summary",
                    ["suite_name", "passed", "failed", "skipped", "total", "command"],
                    [],
                    [new DataAgentOrderBy("ran_at", "desc")],
                    1),
                "high",
                ["test", "result"],
                "question asks for latest test results");
        }

        if (question.Contains("DataAgent", StringComparison.OrdinalIgnoreCase) ||
            question.Contains("NL2SQL", StringComparison.OrdinalIgnoreCase))
        {
            return Envelope(
                new DataAgentQueryPlan(
                    "document_index",
                    "find_dataagent_documents",
                    ["path", "title", "summary"],
                    [new DataAgentFilter("tags", "contains", "dataagent")],
                    [new DataAgentOrderBy("updated_at", "desc")],
                    20),
                "high",
                ["dataagent", "nl2sql", "document"],
                "question asks for DataAgent or NL2SQL documentation");
        }

        return Envelope(
            new DataAgentQueryPlan(
                "engineering_gate",
                "find_missing_required_gates",
                ["name", "status", "evidence_path"],
                [
                    new DataAgentFilter("required", "=", true),
                    new DataAgentFilter("status", "!=", "passed")
                ],
                [],
                50),
            "low",
            ["fallback"],
            "fallback to missing required engineering gates");
    }

    static DataAgentQueryPlanEnvelope Envelope(
        DataAgentQueryPlan plan,
        string confidence,
        IReadOnlyList<string> signals,
        string reason)
    {
        return new DataAgentQueryPlanEnvelope(
            plan,
            new DataAgentPlannerExplanation(
                nameof(DeterministicDataAgentQueryPlanner),
                plan.Intent,
                plan.Dataset,
                confidence,
                signals,
                reason));
    }
}
