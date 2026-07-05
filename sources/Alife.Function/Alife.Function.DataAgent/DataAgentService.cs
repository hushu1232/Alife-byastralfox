using System.Diagnostics;
using System.Text.Json;

namespace Alife.Function.DataAgent;

public sealed class DataAgentService
{
    readonly DataAgentCatalog catalog = DataAgentCatalog.CreateDefault();
    readonly DataAgentSqlSafetyValidator safetyValidator = new();
    readonly IDataAgentStore store;
    readonly IDataAgentQueryPlanner planner;
    readonly IDataAgentScenarioContextProvider scenarioContextProvider;

    public DataAgentService(string databasePath)
        : this(
            new SqliteDataAgentStore(databasePath),
            new DeterministicDataAgentQueryPlanner(),
            DataAgentScenarioContextProvider.CreateDefault())
    {
    }

    public DataAgentService(string databasePath, IDataAgentQueryPlanner planner)
        : this(new SqliteDataAgentStore(databasePath), planner, DataAgentScenarioContextProvider.CreateDefault())
    {
    }

    public DataAgentService(
        string databasePath,
        IDataAgentQueryPlanner planner,
        IDataAgentScenarioContextProvider scenarioContextProvider)
        : this(new SqliteDataAgentStore(databasePath), planner, scenarioContextProvider)
    {
    }

    public DataAgentService(IDataAgentStore store)
        : this(store, new DeterministicDataAgentQueryPlanner(), DataAgentScenarioContextProvider.CreateDefault())
    {
    }

    public DataAgentService(IDataAgentStore store, IDataAgentQueryPlanner planner)
        : this(store, planner, DataAgentScenarioContextProvider.CreateDefault())
    {
    }

    public DataAgentService(
        IDataAgentStore store,
        IDataAgentQueryPlanner planner,
        IDataAgentScenarioContextProvider scenarioContextProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(scenarioContextProvider);

        this.store = store;
        this.planner = planner;
        this.scenarioContextProvider = scenarioContextProvider;
    }

    public DataAgentAnswer Answer(string question)
    {
        return Answer(question, null, string.Empty, 0, () => DateTimeOffset.UtcNow);
    }

    public DataAgentAnswer Answer(
        string question,
        IDataAgentProgressSink? progressSink,
        string sessionId,
        int turnCount,
        Func<DateTimeOffset> clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(clock);

        Publish(
            progressSink,
            sessionId,
            DataAgentProgressEventKind.Planner,
            DataAgentProgressEventPhase.Started,
            DataAgentProgressEventStatus.Running,
            "planner_started",
            turnCount,
            clock(),
            executedSql: false,
            queryAllowed: true,
            terminal: false);
        DataAgentScenarioContext scenarioContext = scenarioContextProvider.Build(catalog, question);
        DataAgentQueryPlanEnvelope envelope = ValidateEnvelope(planner.Plan(new DataAgentQueryRequest(
            question,
            "developer",
            "zh-CN",
            false,
            scenarioContext)));
        Publish(
            progressSink,
            sessionId,
            DataAgentProgressEventKind.Planner,
            DataAgentProgressEventPhase.Completed,
            DataAgentProgressEventStatus.Succeeded,
            "planner_response_received",
            turnCount,
            clock(),
            executedSql: false,
            queryAllowed: true,
            terminal: false);
        DataAgentPlannerExplanation explanation = envelope.Explanation;
        if (envelope.Clarification is not null)
        {
            Publish(
                progressSink,
                sessionId,
                DataAgentProgressEventKind.Validate,
                DataAgentProgressEventPhase.Completed,
                DataAgentProgressEventStatus.Skipped,
                "needs_clarification",
                turnCount,
                clock(),
                executedSql: false,
                queryAllowed: true,
                terminal: false);
            Publish(
                progressSink,
                sessionId,
                DataAgentProgressEventKind.Clarification,
                DataAgentProgressEventPhase.Completed,
                DataAgentProgressEventStatus.Succeeded,
                "needs_clarification",
                turnCount,
                clock(),
                executedSql: false,
                queryAllowed: true,
                terminal: false);
            return Clarify(question, envelope.Clarification, explanation);
        }

        DataAgentQueryPlan plan = envelope.Plan!;
        string queryPlanJson = JsonSerializer.Serialize(plan);
        DataAgentValidationResult planValidation = new DataAgentQueryPlanValidator(catalog).Validate(plan);

        if (planValidation.IsValid == false)
        {
            string reason = string.Join(";", planValidation.Errors);
            Publish(
                progressSink,
                sessionId,
                DataAgentProgressEventKind.Validate,
                DataAgentProgressEventPhase.Completed,
                DataAgentProgressEventStatus.Rejected,
                reason,
                turnCount,
                clock(),
                executedSql: false,
                queryAllowed: true,
                terminal: false);
            PublishReject(progressSink, sessionId, reason, turnCount, clock, queryAllowed: true);
            return Reject(question, plan, explanation, queryPlanJson, reason, string.Empty);
        }

        Publish(
            progressSink,
            sessionId,
            DataAgentProgressEventKind.Validate,
            DataAgentProgressEventPhase.Completed,
            DataAgentProgressEventStatus.Succeeded,
            "validated",
            turnCount,
            clock(),
            executedSql: false,
            queryAllowed: true,
            terminal: false);

        DataAgentCompiledSql compiled = new DataAgentSqlCompiler(catalog).Compile(plan);
        DataAgentSqlSafetyResult safety = safetyValidator.Validate(compiled.Sql);
        if (safety.IsSafe == false)
        {
            Publish(
                progressSink,
                sessionId,
                DataAgentProgressEventKind.SqlSafety,
                DataAgentProgressEventPhase.Completed,
                DataAgentProgressEventStatus.Rejected,
                safety.Reason,
                turnCount,
                clock(),
                executedSql: false,
                queryAllowed: true,
                terminal: false);
            PublishReject(progressSink, sessionId, safety.Reason, turnCount, clock, queryAllowed: true);
            return Reject(question, plan, explanation, queryPlanJson, safety.Reason, compiled.Sql);
        }

        Publish(
            progressSink,
            sessionId,
            DataAgentProgressEventKind.SqlSafety,
            DataAgentProgressEventPhase.Completed,
            DataAgentProgressEventStatus.Succeeded,
            "read_only_sql_safe",
            turnCount,
            clock(),
            executedSql: false,
            queryAllowed: true,
            terminal: false);
        Publish(
            progressSink,
            sessionId,
            DataAgentProgressEventKind.Execute,
            DataAgentProgressEventPhase.Started,
            DataAgentProgressEventStatus.Running,
            "execute_started",
            turnCount,
            clock(),
            executedSql: false,
            queryAllowed: true,
            terminal: false);

        Stopwatch stopwatch = Stopwatch.StartNew();
        DataAgentQueryResult result = store.Query(compiled);
        stopwatch.Stop();
        Publish(
            progressSink,
            sessionId,
            DataAgentProgressEventKind.Execute,
            DataAgentProgressEventPhase.Completed,
            DataAgentProgressEventStatus.Succeeded,
            "read_only_query_executed",
            turnCount,
            clock(),
            executedSql: true,
            queryAllowed: true,
            terminal: false,
            new Dictionary<string, string>
            {
                ["rows"] = result.Rows.Count.ToString(),
                ["sql"] = "redacted"
            });

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

        Publish(
            progressSink,
            sessionId,
            DataAgentProgressEventKind.Explain,
            DataAgentProgressEventPhase.Completed,
            DataAgentProgressEventStatus.Succeeded,
            "result_explained",
            turnCount,
            clock(),
            executedSql: false,
            queryAllowed: true,
            terminal: false,
            new Dictionary<string, string>
            {
                ["rows"] = result.Rows.Count.ToString()
            });

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

    static void PublishReject(
        IDataAgentProgressSink? progressSink,
        string sessionId,
        string reason,
        int turnCount,
        Func<DateTimeOffset> clock,
        bool queryAllowed)
    {
        Publish(
            progressSink,
            sessionId,
            DataAgentProgressEventKind.Reject,
            DataAgentProgressEventPhase.Completed,
            DataAgentProgressEventStatus.Rejected,
            reason,
            turnCount,
            clock(),
            executedSql: false,
            queryAllowed,
            terminal: false);
    }

    static void Publish(
        IDataAgentProgressSink? progressSink,
        string sessionId,
        DataAgentProgressEventKind kind,
        DataAgentProgressEventPhase phase,
        DataAgentProgressEventStatus status,
        string reasonCode,
        int turnCount,
        DateTimeOffset createdAt,
        bool executedSql,
        bool queryAllowed,
        bool terminal,
        IReadOnlyDictionary<string, string>? facts = null)
    {
        progressSink?.Publish(new DataAgentProgressEvent(
            sessionId,
            kind,
            phase,
            status,
            reasonCode,
            turnCount,
            createdAt,
            executedSql,
            queryAllowed,
            terminal,
            facts ?? new Dictionary<string, string>()));
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
