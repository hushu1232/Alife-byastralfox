using System.Text;

namespace Alife.Function.DataAgent;

public sealed class DataAgentAnalysisService
{
    const int SummaryWindowValidatedTurns = 3;

    readonly Func<string, DataAgentAnswer> answer;
    readonly IDataAgentAnalysisSessionStore store;
    readonly DataAgentFollowUpInterpreter followUpInterpreter;
    readonly Func<DateTimeOffset> clock;

    public DataAgentAnalysisService(
        DataAgentService dataAgentService,
        IDataAgentAnalysisSessionStore store)
        : this(dataAgentService.Answer, store, new DataAgentFollowUpInterpreter(), () => DateTimeOffset.UtcNow)
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
            return Summarize(sessionId);

        if (intent == DataAgentAnalysisTurnIntent.End)
            return End(sessionId);

        string composedQuestion = ComposeQuestion(session, question, intent);
        return ExecuteQueryTurn(session, question, composedQuestion, intent, now);
    }

    public DataAgentAnalysisResponse Summarize(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        DateTimeOffset now = clock();
        DataAgentAnalysisSession? session = store.Get(sessionId);
        if (session is null)
            return Reject(sessionId, DataAgentAnalysisTurnIntent.Summarize, "analysis_session_not_found");

        if (session.Status == DataAgentAnalysisSessionStatus.Ended)
            return Reject(sessionId, DataAgentAnalysisTurnIntent.Summarize, "analysis_session_ended");

        DataAgentAnalysisSession updated = store.Save(session with
        {
            Status = DataAgentAnalysisSessionStatus.Summarized,
            UpdatedAt = now,
            LastSummary = DataAgentAnalysisSummarizer.Summarize(session)
        });

        string context = DataAgentAnalysisContextProvider.Build(updated);
        return new DataAgentAnalysisResponse(
            updated.SessionId,
            updated.Status,
            DataAgentAnalysisTurnIntent.Summarize,
            null,
            updated.LastSummary ?? string.Empty,
            context,
            true,
            string.Empty);
    }

    public DataAgentAnalysisResponse End(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        DateTimeOffset now = clock();
        DataAgentAnalysisSession? session = store.Get(sessionId);
        if (session is null)
            return Reject(sessionId, DataAgentAnalysisTurnIntent.End, "analysis_session_not_found");

        string summary = DataAgentAnalysisSummarizer.Summarize(session);
        DataAgentAnalysisSession updated = store.Save(session with
        {
            Status = DataAgentAnalysisSessionStatus.Ended,
            UpdatedAt = now,
            LastSummary = summary
        });

        return new DataAgentAnalysisResponse(
            updated.SessionId,
            updated.Status,
            DataAgentAnalysisTurnIntent.End,
            null,
            summary,
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
        DataAgentAnalysisTurn turn = new(
            Guid.NewGuid().ToString("N"),
            session.Turns.Count + 1,
            DataAgentContextFieldSanitizer.Sanitize(originalQuestion, 240),
            intent,
            now,
            dataAgentAnswer.Dataset,
            dataAgentAnswer.Sql,
            dataAgentAnswer.RowCount,
            dataAgentAnswer.Summary,
            dataAgentAnswer.Validated,
            dataAgentAnswer.RejectedReason);

        IReadOnlyList<DataAgentAnalysisTurn> turns = session.Turns.Concat([turn]).ToArray();
        DataAgentAnalysisSessionStatus status = ResolveStatus(dataAgentAnswer, turns);
        DataAgentAnalysisSession updated = store.Save(session with
        {
            Status = status,
            UpdatedAt = now,
            LastDataset = string.IsNullOrWhiteSpace(dataAgentAnswer.Dataset) ? session.LastDataset : dataAgentAnswer.Dataset,
            LastSummary = dataAgentAnswer.Summary,
            PendingClarificationQuestion = dataAgentAnswer.RejectedReason == "needs_clarification" ? dataAgentAnswer.Summary : null,
            Turns = turns
        });

        string context = string.Join(
            Environment.NewLine,
            DataAgentAnalysisContextProvider.Build(updated, turn),
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

        int validatedTurns = turns.Count(turn => turn.Validated);
        if (validatedTurns >= SummaryWindowValidatedTurns)
            return DataAgentAnalysisSessionStatus.ReadyToSummarize;

        return DataAgentAnalysisSessionStatus.Active;
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
        return new DataAgentAnalysisResponse(
            sessionId,
            DataAgentAnalysisSessionStatus.Ended,
            intent,
            null,
            string.Empty,
            string.Empty,
            false,
            reason);
    }
}
