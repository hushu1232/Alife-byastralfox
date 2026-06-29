using System.Diagnostics;
using System.Text.Json;

namespace Alife.Function.DataAgent;

public sealed class DataAgentService
{
    readonly DataAgentCatalog catalog = DataAgentCatalog.CreateDefault();
    readonly DataAgentSqlSafetyValidator safetyValidator = new();
    readonly IDataAgentStore store;
    readonly IDataAgentQueryPlanner planner;

    public DataAgentService(string databasePath)
        : this(new SqliteDataAgentStore(databasePath), new DeterministicDataAgentQueryPlanner())
    {
    }

    public DataAgentService(string databasePath, IDataAgentQueryPlanner planner)
        : this(new SqliteDataAgentStore(databasePath), planner)
    {
    }

    public DataAgentService(IDataAgentStore store)
        : this(store, new DeterministicDataAgentQueryPlanner())
    {
    }

    public DataAgentService(IDataAgentStore store, IDataAgentQueryPlanner planner)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(planner);

        this.store = store;
        this.planner = planner;
    }

    public DataAgentAnswer Answer(string question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        DataAgentQueryPlanEnvelope envelope = ValidateEnvelope(planner.Plan(new DataAgentQueryRequest(question, "developer", "zh-CN", false)));
        DataAgentPlannerExplanation explanation = envelope.Explanation;
        if (envelope.Clarification is not null)
            return Clarify(question, envelope.Clarification, explanation);

        DataAgentQueryPlan plan = envelope.Plan!;
        string queryPlanJson = JsonSerializer.Serialize(plan);
        DataAgentValidationResult planValidation = new DataAgentQueryPlanValidator(catalog).Validate(plan);

        if (planValidation.IsValid == false)
            return Reject(question, plan, explanation, queryPlanJson, string.Join(";", planValidation.Errors), string.Empty);

        DataAgentCompiledSql compiled = new DataAgentSqlCompiler(catalog).Compile(plan);
        DataAgentSqlSafetyResult safety = safetyValidator.Validate(compiled.Sql);
        if (safety.IsSafe == false)
            return Reject(question, plan, explanation, queryPlanJson, safety.Reason, compiled.Sql);

        Stopwatch stopwatch = Stopwatch.StartNew();
        DataAgentQueryResult result = store.Query(compiled);
        stopwatch.Stop();

        string summary = DataAgentResultSummarizer.Summarize(plan, result);
        string resultExplanation = DataAgentResultExplainer.ExplainAccepted(
            question,
            plan.Dataset,
            result.Rows.Count,
            summary,
            explanation);
        string context = DataAgentContextProvider.Build(
            question,
            plan.Dataset,
            compiled.Sql,
            result.Rows.Count,
            summary,
            result,
            explanation,
            resultExplanation);

        store.RecordAccepted(new DataAgentAcceptedAuditInput(
            question,
            plan.Dataset,
            queryPlanJson,
            compiled.Sql,
            result.Rows.Count,
            stopwatch.Elapsed));

        return new DataAgentAnswer(plan.Dataset, compiled.Sql, result.Rows.Count, summary, context, true, string.Empty, explanation);
    }

    DataAgentAnswer Reject(
        string question,
        DataAgentQueryPlan plan,
        DataAgentPlannerExplanation explanation,
        string queryPlanJson,
        string reason,
        string generatedSql)
    {
        store.RecordRejected(new DataAgentRejectedAuditInput(
            question,
            plan.Dataset,
            queryPlanJson,
            generatedSql,
            reason,
            TimeSpan.Zero));

        string summary = $"DataAgent query rejected: {reason}";
        string context = DataAgentContextProvider.BuildRejected(question, plan.Dataset, reason, explanation);
        return new DataAgentAnswer(plan.Dataset, generatedSql, 0, summary, context, false, reason, explanation);
    }

    DataAgentAnswer Clarify(
        string question,
        DataAgentClarificationRequest clarification,
        DataAgentPlannerExplanation explanation)
    {
        string queryPlanJson = JsonSerializer.Serialize(clarification);

        store.RecordRejected(new DataAgentRejectedAuditInput(
            question,
            string.Empty,
            queryPlanJson,
            string.Empty,
            "needs_clarification",
            TimeSpan.Zero));

        string summary = DataAgentResultExplainer.ExplainClarification(clarification);
        string context = DataAgentContextProvider.BuildClarification(question, clarification, explanation);
        return new DataAgentAnswer(string.Empty, string.Empty, 0, summary, context, false, "needs_clarification", explanation);
    }

    static DataAgentQueryPlanEnvelope ValidateEnvelope(DataAgentQueryPlanEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(envelope.Explanation);

        if ((envelope.Plan is null) == (envelope.Clarification is null))
            throw new ArgumentException("Planner envelope must include exactly one of plan or clarification.", nameof(envelope));

        DataAgentPlannerExplanation explanation = envelope.Explanation;
        ArgumentException.ThrowIfNullOrWhiteSpace(explanation.PlannerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(explanation.Intent);
        ArgumentException.ThrowIfNullOrWhiteSpace(explanation.Confidence);
        ArgumentException.ThrowIfNullOrWhiteSpace(explanation.Reason);
        ArgumentNullException.ThrowIfNull(explanation.Signals);

        foreach (string signal in explanation.Signals)
            ArgumentException.ThrowIfNullOrWhiteSpace(signal);

        if (envelope.Plan is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(explanation.Dataset);

            if (string.Equals(explanation.Dataset, envelope.Plan.Dataset, StringComparison.Ordinal) == false)
                throw new ArgumentException("Planner explanation dataset must match the query plan dataset.", nameof(envelope));

            if (string.Equals(explanation.Intent, envelope.Plan.Intent, StringComparison.Ordinal) == false)
                throw new ArgumentException("Planner explanation intent must match the query plan intent.", nameof(envelope));

            return envelope;
        }

        DataAgentClarificationRequest rawClarification = envelope.Clarification!;
        ArgumentNullException.ThrowIfNull(rawClarification.Options);

        if (rawClarification.Options.Count is < 2 or > 4)
            throw new ArgumentException("Clarification options must include 2 to 4 choices.", nameof(envelope));

        DataAgentClarificationRequest clarification = DataAgentClarificationSanitizer.Sanitize(rawClarification);
        ArgumentException.ThrowIfNullOrWhiteSpace(clarification.Question);
        ArgumentException.ThrowIfNullOrWhiteSpace(clarification.Reason);

        foreach (string option in clarification.Options)
            ArgumentException.ThrowIfNullOrWhiteSpace(option);

        return envelope with { Clarification = clarification };
    }
}

public sealed record DataAgentAnswer(
    string Dataset,
    string Sql,
    int RowCount,
    string Summary,
    string Context,
    bool Validated,
    string RejectedReason,
    DataAgentPlannerExplanation PlannerExplanation);
