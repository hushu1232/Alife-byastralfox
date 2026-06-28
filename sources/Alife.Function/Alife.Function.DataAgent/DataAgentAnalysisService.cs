using System.Text;

namespace Alife.Function.DataAgent;

public sealed class DataAgentAnalysisService
{
    const int SummaryWindowValidatedTurns = 3;
    const int MaxStoredQuestionLength = 240;
    const int MaxPendingClarificationQuestionLength = 240;
    const string NonQueryTerminalTurnRejectedReason = "non_query_terminal_turn";

    readonly Func<string, DataAgentAnswer> answer;
    readonly IDataAgentAnalysisSessionStore store;
    readonly DataAgentFollowUpInterpreter followUpInterpreter;
    readonly Func<DateTimeOffset> clock;

    public DataAgentAnalysisService(
        DataAgentService dataAgentService,
        IDataAgentAnalysisSessionStore store)
        : this((dataAgentService ?? throw new ArgumentNullException(nameof(dataAgentService))).Answer,
            store,
            new DataAgentFollowUpInterpreter(),
            () => DateTimeOffset.UtcNow)
    {
    }

    public DataAgentAnalysisService(
        Func<string, DataAgentAnswer> answer,
        IDataAgentAnalysisSessionStore store,
        DataAgentFollowUpInterpreter? followUpInterpreter = null,
        Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(store);

        this.answer = answer;
        this.store = store;
        this.followUpInterpreter = followUpInterpreter ?? new DataAgentFollowUpInterpreter();
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public DataAgentAnalysisResponse Start(string goalOrQuestion)
    {
        return Start("local", goalOrQuestion);
    }

    public DataAgentAnalysisResponse Start(string callerId, string goalOrQuestion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(goalOrQuestion);

        DateTimeOffset now = clock();
        DataAgentAnalysisSession session = store.Create(callerId, goalOrQuestion, now);
        return ExecuteQueryTurn(session, goalOrQuestion, goalOrQuestion, DataAgentAnalysisTurnIntent.NewQuestion, now);
    }

    public DataAgentAnalysisResponse Continue(string sessionId, string question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        DateTimeOffset now = clock();
        DataAgentAnalysisSession? session = store.Get(sessionId);
        if (session is null)
            return Reject(sessionId, DataAgentAnalysisTurnIntent.NewQuestion, "analysis_session_not_found");

        if (session.Status == DataAgentAnalysisSessionStatus.Ended)
            return Reject(sessionId, DataAgentAnalysisTurnIntent.Continue, "analysis_session_ended");

        DataAgentAnalysisTurnIntent intent = followUpInterpreter.Interpret(question, session);
        if (intent == DataAgentAnalysisTurnIntent.Summarize)
            return Summarize(sessionId, question);

        if (intent == DataAgentAnalysisTurnIntent.End)
            return End(sessionId, question);

        string composedQuestion = ComposeQuestion(session, question, intent);
        return ExecuteQueryTurn(session, question, composedQuestion, intent, now);
    }

    public DataAgentAnalysisResponse Summarize(string sessionId)
    {
        return Summarize(sessionId, "summarize");
    }

    DataAgentAnalysisResponse Summarize(string sessionId, string turnQuestion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        DateTimeOffset now = clock();
        DataAgentAnalysisSession? session = store.Get(sessionId);
        if (session is null)
            return Reject(sessionId, DataAgentAnalysisTurnIntent.Summarize, "analysis_session_not_found");

        if (session.Status == DataAgentAnalysisSessionStatus.Ended)
            return Reject(sessionId, DataAgentAnalysisTurnIntent.Summarize, "analysis_session_ended");

        DataAgentAnalysisSession? updated = store.Update(
            sessionId,
            current =>
            {
                if (current.Status == DataAgentAnalysisSessionStatus.Ended)
                    return current;

                string summary = DataAgentAnalysisSummarizer.Summarize(current);
                DataAgentAnalysisSession withTerminalTurn = AppendTerminalTurn(
                    current,
                    turnQuestion,
                    DataAgentAnalysisTurnIntent.Summarize,
                    now,
                    summary);

                return withTerminalTurn with
                {
                    Status = DataAgentAnalysisSessionStatus.Summarized,
                    UpdatedAt = now,
                    LastSummary = summary
                };
            });

        if (updated is null)
            return Reject(sessionId, DataAgentAnalysisTurnIntent.Summarize, "analysis_session_not_found");

        if (updated.Status == DataAgentAnalysisSessionStatus.Ended)
            return Reject(sessionId, DataAgentAnalysisTurnIntent.Summarize, "analysis_session_ended");

        string summaryText = updated.LastSummary ?? string.Empty;
        string context = DataAgentAnalysisContextProvider.Build(updated);
        return new DataAgentAnalysisResponse(
            updated.SessionId,
            updated.Status,
            DataAgentAnalysisTurnIntent.Summarize,
            null,
            summaryText,
            context,
            true,
            string.Empty);
    }

    public DataAgentAnalysisResponse End(string sessionId)
    {
        return End(sessionId, "end");
    }

    DataAgentAnalysisResponse End(string sessionId, string turnQuestion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        DateTimeOffset now = clock();
        DataAgentAnalysisSession? updated = store.Update(
            sessionId,
            current =>
            {
                if (current.Status == DataAgentAnalysisSessionStatus.Ended)
                    return current;

                string summary = DataAgentAnalysisSummarizer.Summarize(current);
                DataAgentAnalysisSession withTerminalTurn = AppendTerminalTurn(
                    current,
                    turnQuestion,
                    DataAgentAnalysisTurnIntent.End,
                    now,
                    summary);

                return withTerminalTurn with
                {
                    Status = DataAgentAnalysisSessionStatus.Ended,
                    UpdatedAt = now,
                    LastSummary = summary
                };
            });

        if (updated is null)
            return Reject(sessionId, DataAgentAnalysisTurnIntent.End, "analysis_session_not_found");

        string summaryText = updated.LastSummary ?? string.Empty;
        return new DataAgentAnalysisResponse(
            updated.SessionId,
            updated.Status,
            DataAgentAnalysisTurnIntent.End,
            null,
            summaryText,
            DataAgentAnalysisContextProvider.Build(updated),
            true,
            string.Empty);
    }

    DataAgentAnalysisResponse ExecuteQueryTurn(
        DataAgentAnalysisSession session,
        string originalQuestion,
        string questionForDataAgent,
        DataAgentAnalysisTurnIntent intent,
        DateTimeOffset now)
    {
        DataAgentAnswer dataAgentAnswer = answer(questionForDataAgent);
        DataAgentAnalysisTurn? appendedTurn = null;
        DataAgentAnalysisSession? updated = store.Update(
            session.SessionId,
            current =>
            {
                if (current.Status == DataAgentAnalysisSessionStatus.Ended)
                {
                    appendedTurn = null;
                    return current;
                }

                DataAgentAnalysisTurn turn = new(
                    Guid.NewGuid().ToString("N"),
                    current.Turns.Count + 1,
                    DataAgentContextFieldSanitizer.Sanitize(originalQuestion, MaxStoredQuestionLength),
                    intent,
                    now,
                    dataAgentAnswer.Dataset,
                    dataAgentAnswer.Sql,
                    dataAgentAnswer.RowCount,
                    dataAgentAnswer.Summary,
                    dataAgentAnswer.Validated,
                    dataAgentAnswer.RejectedReason);

                IReadOnlyList<DataAgentAnalysisTurn> turns = current.Turns.Concat([turn]).ToArray();
                DataAgentAnalysisSessionStatus status = ResolveStatus(dataAgentAnswer, turns);
                appendedTurn = turn;
                return current with
                {
                    Status = status,
                    UpdatedAt = now,
                    LastDataset = string.IsNullOrWhiteSpace(dataAgentAnswer.Dataset) ? current.LastDataset : dataAgentAnswer.Dataset,
                    LastSummary = dataAgentAnswer.Summary,
                    PendingClarificationQuestion = ResolvePendingClarificationQuestion(dataAgentAnswer),
                    Turns = turns
                };
            });

        if (updated is null)
            return Reject(session.SessionId, intent, "analysis_session_not_found");

        if (appendedTurn is null || updated.Status == DataAgentAnalysisSessionStatus.Ended)
            return Reject(session.SessionId, intent, "analysis_session_ended");

        string context = string.Join(
            Environment.NewLine,
            DataAgentAnalysisContextProvider.Build(updated, appendedTurn),
            dataAgentAnswer.Context);

        return new DataAgentAnalysisResponse(
            updated.SessionId,
            updated.Status,
            intent,
            dataAgentAnswer,
            dataAgentAnswer.Summary,
            context,
            true,
            string.Empty);
    }

    static DataAgentAnalysisSessionStatus ResolveStatus(
        DataAgentAnswer answer,
        IReadOnlyList<DataAgentAnalysisTurn> turns)
    {
        if (answer.RejectedReason == "needs_clarification")
            return DataAgentAnalysisSessionStatus.AwaitingClarification;

        int validatedTurns = turns.Count(turn => turn.Intent.ProducesQuery() && turn.Validated);
        if (validatedTurns >= SummaryWindowValidatedTurns)
            return DataAgentAnalysisSessionStatus.ReadyToSummarize;

        return DataAgentAnalysisSessionStatus.Active;
    }

    static DataAgentAnalysisSession AppendTerminalTurn(
        DataAgentAnalysisSession session,
        string question,
        DataAgentAnalysisTurnIntent intent,
        DateTimeOffset now,
        string summary)
    {
        DataAgentAnalysisTurn turn = new(
            Guid.NewGuid().ToString("N"),
            session.Turns.Count + 1,
            DataAgentContextFieldSanitizer.Sanitize(question, MaxStoredQuestionLength),
            intent,
            now,
            string.Empty,
            string.Empty,
            0,
            summary,
            false,
            NonQueryTerminalTurnRejectedReason);

        return session with { Turns = session.Turns.Concat([turn]).ToArray() };
    }

    static string? ResolvePendingClarificationQuestion(DataAgentAnswer answer)
    {
        if (answer.RejectedReason != "needs_clarification")
            return null;

        string? question = ExtractContextField(answer.Context, "clarification_question");
        if (string.IsNullOrWhiteSpace(question))
            question = answer.Summary;

        string sanitized = DataAgentContextFieldSanitizer.Sanitize(
            question,
            MaxPendingClarificationQuestionLength);

        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }

    static string? ExtractContextField(string context, string field)
    {
        if (string.IsNullOrWhiteSpace(context))
            return null;

        string prefix = field + "=";
        bool insideDataAgentContext = false;
        bool needsClarification = false;
        foreach (string rawLine in context.Split('\n'))
        {
            string line = rawLine.Trim().TrimEnd('\r');
            if (string.Equals(line, "[data_agent_context]", StringComparison.Ordinal))
            {
                insideDataAgentContext = true;
                needsClarification = false;
                continue;
            }

            if (string.Equals(line, "[/data_agent_context]", StringComparison.Ordinal))
            {
                insideDataAgentContext = false;
                needsClarification = false;
                continue;
            }

            if (insideDataAgentContext == false)
                continue;

            if (string.Equals(line, "sql_status=needs_clarification", StringComparison.Ordinal))
            {
                needsClarification = true;
                continue;
            }

            if (needsClarification && line.StartsWith(prefix, StringComparison.Ordinal))
                return line[prefix.Length..];
        }

        return null;
    }

    static string ComposeQuestion(
        DataAgentAnalysisSession session,
        string question,
        DataAgentAnalysisTurnIntent intent)
    {
        if (intent == DataAgentAnalysisTurnIntent.NewQuestion && session.Turns.Count == 0)
            return question;

        StringBuilder builder = new();
        builder.AppendLine($"Analysis goal: {SanitizeForPrompt(session.Goal, 240)}");
        builder.AppendLine($"Previous dataset: {SanitizeForPrompt(session.LastDataset ?? string.Empty, 120)}");
        builder.AppendLine($"Previous summary: {SanitizeForPrompt(session.LastSummary ?? string.Empty, 480)}");
        if (string.IsNullOrWhiteSpace(session.PendingClarificationQuestion) == false)
            builder.AppendLine($"Pending clarification: {SanitizeForPrompt(session.PendingClarificationQuestion, 240)}");

        builder.AppendLine($"Follow-up intent: {intent}");
        builder.Append($"Follow-up question: {SanitizeForPrompt(question, 240)}");
        return builder.ToString();
    }

    static string SanitizeForPrompt(string value, int maxLength)
    {
        return DataAgentContextFieldSanitizer.Sanitize(value, maxLength);
    }

    static DataAgentAnalysisResponse Reject(
        string sessionId,
        DataAgentAnalysisTurnIntent intent,
        string reason)
    {
        DataAgentAnalysisSessionStatus status = reason == "analysis_session_not_found"
            ? DataAgentAnalysisSessionStatus.Rejected
            : DataAgentAnalysisSessionStatus.Ended;

        return new DataAgentAnalysisResponse(
            sessionId,
            status,
            intent,
            null,
            string.Empty,
            string.Empty,
            false,
            reason);
    }
}
