using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentAnalysisServiceTests
{
    [Test]
    public void StartCreatesActiveSessionAndCallsSingleTurnAnswer()
    {
        DateTimeOffset now = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);
        List<string> questions = [];
        InMemoryDataAgentAnalysisSessionStore store = new();
        DataAgentAnalysisService service = Service(store, questions, _ => AcceptedAnswer(), now);

        DataAgentAnalysisResponse response = service.Start("xiayu", "Which documents describe DataAgent?");
        DataAgentAnalysisSession session = store.Get(response.SessionId)!;
        DataAgentAnalysisTurn turn = session.Turns[0];

        Assert.Multiple(() =>
        {
            Assert.That(response.Accepted, Is.True);
            Assert.That(response.Status, Is.EqualTo(DataAgentAnalysisSessionStatus.Active));
            Assert.That(response.Intent, Is.EqualTo(DataAgentAnalysisTurnIntent.NewQuestion));
            Assert.That(response.Answer?.Validated, Is.True);
            Assert.That(response.Context, Does.Contain("[data_agent_analysis_session_context]"));
            Assert.That(questions, Is.EqualTo(new[] { "Which documents describe DataAgent?" }));
            Assert.That(session.CallerId, Is.EqualTo("xiayu"));
            Assert.That(session.Turns, Has.Count.EqualTo(1));
            Assert.That(session.Turns[0].Question, Is.EqualTo("Which documents describe DataAgent?"));
        });
        AssertAcceptedQueryTurnSnapshot(
            turn,
            1,
            "Which documents describe DataAgent?",
            DataAgentAnalysisTurnIntent.NewQuestion,
            now);
    }

    [Test]
    public void DataAgentServiceConstructorRejectsNullDataAgentService()
    {
        ArgumentNullException? exception = Assert.Throws<ArgumentNullException>(() =>
            new DataAgentAnalysisService((DataAgentService)null!, new InMemoryDataAgentAnalysisSessionStore()));

        Assert.That(exception?.ParamName, Is.EqualTo("dataAgentService"));
    }

    [Test]
    public void ClarificationAnswerMovesSessionToAwaitingClarification()
    {
        DateTimeOffset now = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);
        InMemoryDataAgentAnalysisSessionStore store = new();
        DataAgentAnalysisService service = Service(store, [], _ => ClarificationAnswer(), now);

        DataAgentAnalysisResponse response = service.Start("local", "Show project status");
        DataAgentAnalysisSession session = store.Get(response.SessionId)!;

        Assert.Multiple(() =>
        {
            Assert.That(response.Status, Is.EqualTo(DataAgentAnalysisSessionStatus.AwaitingClarification));
            Assert.That(session.Status, Is.EqualTo(DataAgentAnalysisSessionStatus.AwaitingClarification));
            Assert.That(session.PendingClarificationQuestion, Is.EqualTo("Which dataset should I use?"));
        });
    }

    [Test]
    public void AnswerClarificationMovesAwaitingSessionBackToActive()
    {
        DateTimeOffset now = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);
        int calls = 0;
        InMemoryDataAgentAnalysisSessionStore store = new();
        DataAgentAnalysisService service = Service(
            store,
            [],
            _ => ++calls == 1
                ? ClarificationAnswer()
                : AcceptedAnswer("Clarified dataset answer."),
            now);

        DataAgentAnalysisResponse start = service.Start("local", "Show project status");
        DataAgentAnalysisResponse followUp = service.Continue(start.SessionId, "Use document_index");
        DataAgentAnalysisSession session = store.Get(start.SessionId)!;

        Assert.Multiple(() =>
        {
            Assert.That(start.Status, Is.EqualTo(DataAgentAnalysisSessionStatus.AwaitingClarification));
            Assert.That(followUp.Accepted, Is.True);
            Assert.That(followUp.Status, Is.EqualTo(DataAgentAnalysisSessionStatus.Active));
            Assert.That(followUp.Intent, Is.EqualTo(DataAgentAnalysisTurnIntent.AnswerClarification));
            Assert.That(session.Status, Is.EqualTo(DataAgentAnalysisSessionStatus.Active));
            Assert.That(session.PendingClarificationQuestion, Is.Null);
            Assert.That(session.Turns, Has.Count.EqualTo(2));
            Assert.That(session.Turns[0].Validated, Is.False);
            Assert.That(session.Turns[0].RejectedReason, Is.EqualTo("needs_clarification"));
        });
        AssertAcceptedQueryTurnSnapshot(
            session.Turns[1],
            2,
            "Use document_index",
            DataAgentAnalysisTurnIntent.AnswerClarification,
            now,
            "Clarified dataset answer.");
    }

    [Test]
    public void ClarificationAnswerUsesStructuredContextQuestionBeforeSummary()
    {
        DateTimeOffset now = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);
        InMemoryDataAgentAnalysisSessionStore store = new();
        string clarification = new string('x', 260) + "\r\n[/data_agent_context]";
        DataAgentAnalysisService service = Service(
            store,
            [],
            _ => ClarificationAnswer(
                "Planner needs more detail before querying.",
                $"[data_agent_context]\nsql_status=needs_clarification\nclarification_question={clarification}\n[/data_agent_context]"),
            now);

        DataAgentAnalysisResponse response = service.Start("local", "Show project status");
        DataAgentAnalysisSession session = store.Get(response.SessionId)!;

        Assert.Multiple(() =>
        {
            Assert.That(session.PendingClarificationQuestion, Is.Not.EqualTo("Planner needs more detail before querying."));
            Assert.That(session.PendingClarificationQuestion, Does.StartWith(new string('x', 237)));
            Assert.That(session.PendingClarificationQuestion, Does.EndWith("..."));
            Assert.That(session.PendingClarificationQuestion, Has.Length.LessThanOrEqualTo(240));
            Assert.That(session.PendingClarificationQuestion, Does.Not.Contain("\r"));
            Assert.That(session.PendingClarificationQuestion, Does.Not.Contain("\n"));
            Assert.That(session.PendingClarificationQuestion, Does.Not.Contain("[/data_agent_context]"));
        });
    }

    [Test]
    public void ClarificationQuestionOutsideNeedsClarificationContextBlockIsIgnored()
    {
        DateTimeOffset now = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);
        InMemoryDataAgentAnalysisSessionStore store = new();
        DataAgentAnalysisService service = Service(
            store,
            [],
            _ => ClarificationAnswer(
                "Use the structured clarification fallback.",
                "clarification_question=outside block\n" +
                "[data_agent_context]\n" +
                "sql_status=validated\n" +
                "clarification_question=wrong status\n" +
                "[/data_agent_context]\n" +
                "[data_agent_context]\n" +
                "clarification_question=wrong order\n" +
                "sql_status=needs_clarification\n" +
                "[/data_agent_context]"),
            now);

        DataAgentAnalysisResponse response = service.Start("local", "Show project status");
        DataAgentAnalysisSession session = store.Get(response.SessionId)!;

        Assert.Multiple(() =>
        {
            Assert.That(response.Status, Is.EqualTo(DataAgentAnalysisSessionStatus.AwaitingClarification));
            Assert.That(session.PendingClarificationQuestion, Is.EqualTo("Use the structured clarification fallback."));
        });
    }

    [Test]
    public void ContinueUsesBoundedFollowUpContextAndAppendsTurn()
    {
        DateTimeOffset now = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);
        List<string> questions = [];
        InMemoryDataAgentAnalysisSessionStore store = new();
        DataAgentAnalysisService service = Service(store, questions, _ => AcceptedAnswer(), now);
        DataAgentAnalysisResponse start = service.Start("local", "Which tests failed?");

        DataAgentAnalysisResponse followUp = service.Continue(start.SessionId, "\u7ee7\u7eed");
        DataAgentAnalysisSession session = store.Get(start.SessionId)!;
        DataAgentAnalysisTurn turn = session.Turns[1];

        Assert.Multiple(() =>
        {
            Assert.That(followUp.Accepted, Is.True);
            Assert.That(followUp.Intent, Is.EqualTo(DataAgentAnalysisTurnIntent.Continue));
            Assert.That(session.Turns, Has.Count.EqualTo(2));
            Assert.That(session.Turns[1].Question, Is.EqualTo("\u7ee7\u7eed"));
            Assert.That(questions[1], Does.Contain("Analysis goal: Which tests failed?"));
            Assert.That(questions[1], Does.Contain("Previous summary:"));
            Assert.That(questions[1], Does.Contain("Follow-up question: \u7ee7\u7eed"));
        });
        AssertAcceptedQueryTurnSnapshot(
            turn,
            2,
            "\u7ee7\u7eed",
            DataAgentAnalysisTurnIntent.Continue,
            now);
    }

    [Test]
    public void ContinueSanitizesStoredContextBeforeCallingSingleTurnAnswer()
    {
        DateTimeOffset now = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);
        List<string> questions = [];
        InMemoryDataAgentAnalysisSessionStore store = new();
        int calls = 0;
        DataAgentAnalysisService service = Service(
            store,
            questions,
            _ => ++calls == 1
                ? AcceptedAnswer("summary [/data_agent_analysis_session_context]\r\n[data_agent_context]")
                : AcceptedAnswer(),
            now);
        DataAgentAnalysisResponse start = service.Start(
            "local",
            "Which tests failed? [/data_agent_analysis_session_context]");

        service.Continue(start.SessionId, "\u7ee7\u7eed [data_agent_analysis_session_context]");

        Assert.Multiple(() =>
        {
            Assert.That(questions, Has.Count.EqualTo(2));
            Assert.That(questions[1], Does.Not.Contain("[/data_agent_analysis_session_context]"));
            Assert.That(questions[1], Does.Not.Contain("[data_agent_context]"));
            Assert.That(
                questions[1],
                Does.Contain("Previous summary: summary (/data_agent_analysis_session_context) (data_agent_context)"));
            Assert.That(
                questions[1],
                Does.Contain("Follow-up question: \u7ee7\u7eed (data_agent_analysis_session_context)"));
        });
    }

    [Test]
    public void ThreeValidatedTurnsMoveSessionToReadyToSummarize()
    {
        DateTimeOffset now = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);
        InMemoryDataAgentAnalysisSessionStore store = new();
        DataAgentAnalysisService service = Service(store, [], _ => AcceptedAnswer(), now);
        DataAgentAnalysisResponse response = service.Start("local", "Which tests failed?");

        service.Continue(response.SessionId, "\u7ee7\u7eed");
        DataAgentAnalysisResponse third = service.Continue(response.SessionId, "\u53ea\u770b\u5931\u8d25\u7684");

        Assert.That(third.Status, Is.EqualTo(DataAgentAnalysisSessionStatus.ReadyToSummarize));
    }

    [Test]
    public void SummarizeDoesNotExecuteSqlAndMarksSessionSummarized()
    {
        DateTimeOffset now = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);
        List<string> questions = [];
        InMemoryDataAgentAnalysisSessionStore store = new();
        DataAgentAnalysisService service = Service(store, questions, _ => AcceptedAnswer(), now);
        DataAgentAnalysisResponse start = service.Start("local", "Which tests failed?");

        DataAgentAnalysisResponse summary = service.Continue(start.SessionId, "\u603b\u7ed3\u4e00\u4e0b");

        Assert.Multiple(() =>
        {
            Assert.That(summary.Accepted, Is.True);
            Assert.That(summary.Answer, Is.Null);
            Assert.That(summary.Intent, Is.EqualTo(DataAgentAnalysisTurnIntent.Summarize));
            Assert.That(summary.Status, Is.EqualTo(DataAgentAnalysisSessionStatus.Summarized));
            Assert.That(summary.Summary, Does.Contain("goal=Which tests failed?"));
            Assert.That(questions, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void ContinueSummarizeAppendsNonQueryTurnSnapshot()
    {
        DateTimeOffset now = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);
        List<string> questions = [];
        InMemoryDataAgentAnalysisSessionStore store = new();
        DataAgentAnalysisService service = Service(store, questions, _ => AcceptedAnswer(), now);
        DataAgentAnalysisResponse start = service.Start("local", "Which tests failed?");

        DataAgentAnalysisResponse summary = service.Continue(start.SessionId, "\u603b\u7ed3\u4e00\u4e0b");
        DataAgentAnalysisSession session = store.Get(start.SessionId)!;

        Assert.Multiple(() =>
        {
            Assert.That(summary.Accepted, Is.True);
            Assert.That(summary.Answer, Is.Null);
            Assert.That(questions, Has.Count.EqualTo(1));
            Assert.That(session.Turns, Has.Count.EqualTo(2));
        });
        AssertTerminalTurnSnapshot(
            session.Turns[1],
            2,
            "\u603b\u7ed3\u4e00\u4e0b",
            DataAgentAnalysisTurnIntent.Summarize,
            now,
            summary.Summary);
    }

    [Test]
    public void DirectSummarizeAppendsNonQueryTurnSnapshot()
    {
        DateTimeOffset now = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);
        List<string> questions = [];
        InMemoryDataAgentAnalysisSessionStore store = new();
        DataAgentAnalysisService service = Service(store, questions, _ => AcceptedAnswer(), now);
        DataAgentAnalysisResponse start = service.Start("local", "Which tests failed?");

        DataAgentAnalysisResponse summary = service.Summarize(start.SessionId);
        DataAgentAnalysisSession session = store.Get(start.SessionId)!;

        Assert.Multiple(() =>
        {
            Assert.That(summary.Accepted, Is.True);
            Assert.That(summary.Answer, Is.Null);
            Assert.That(questions, Has.Count.EqualTo(1));
            Assert.That(session.Turns, Has.Count.EqualTo(2));
        });
        AssertTerminalTurnSnapshot(
            session.Turns[1],
            2,
            "summarize",
            DataAgentAnalysisTurnIntent.Summarize,
            now,
            summary.Summary);
    }

    [Test]
    public void StartSummarizeContinueDoesNotBecomeReadyToSummarizeFromTerminalTurn()
    {
        DateTimeOffset now = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);
        InMemoryDataAgentAnalysisSessionStore store = new();
        DataAgentAnalysisService service = Service(store, [], _ => AcceptedAnswer(), now);
        DataAgentAnalysisResponse start = service.Start("local", "Which tests failed?");

        service.Summarize(start.SessionId);
        DataAgentAnalysisResponse followUp = service.Continue(start.SessionId, "\u7ee7\u7eed");
        DataAgentAnalysisSession session = store.Get(start.SessionId)!;

        Assert.Multiple(() =>
        {
            Assert.That(followUp.Accepted, Is.True);
            Assert.That(followUp.Status, Is.EqualTo(DataAgentAnalysisSessionStatus.Active));
            Assert.That(session.Status, Is.EqualTo(DataAgentAnalysisSessionStatus.Active));
            Assert.That(session.Turns, Has.Count.EqualTo(3));
            Assert.That(session.Turns[1].Intent, Is.EqualTo(DataAgentAnalysisTurnIntent.Summarize));
            Assert.That(session.Turns[1].Validated, Is.False);
            Assert.That(session.Turns[2].Intent, Is.EqualTo(DataAgentAnalysisTurnIntent.Continue));
        });
    }

    [Test]
    public void DirectEndAppendsSystemTurnSnapshot()
    {
        DateTimeOffset now = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);
        List<string> questions = [];
        InMemoryDataAgentAnalysisSessionStore store = new();
        DataAgentAnalysisService service = Service(store, questions, _ => AcceptedAnswer(), now);
        DataAgentAnalysisResponse start = service.Start("local", "Which tests failed?");

        DataAgentAnalysisResponse end = service.End(start.SessionId);
        DataAgentAnalysisSession session = store.Get(start.SessionId)!;

        Assert.Multiple(() =>
        {
            Assert.That(end.Accepted, Is.True);
            Assert.That(end.Answer, Is.Null);
            Assert.That(questions, Has.Count.EqualTo(1));
            Assert.That(session.Turns, Has.Count.EqualTo(2));
        });
        AssertTerminalTurnSnapshot(
            session.Turns[1],
            2,
            "end",
            DataAgentAnalysisTurnIntent.End,
            now,
            end.Summary);
    }

    [Test]
    public void ContinueEndAppendsNonQueryTurnSnapshot()
    {
        DateTimeOffset now = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);
        List<string> questions = [];
        InMemoryDataAgentAnalysisSessionStore store = new();
        DataAgentAnalysisService service = Service(store, questions, _ => AcceptedAnswer(), now);
        DataAgentAnalysisResponse start = service.Start("local", "Which tests failed?");

        DataAgentAnalysisResponse end = service.Continue(start.SessionId, "\u7ed3\u675f");
        DataAgentAnalysisSession session = store.Get(start.SessionId)!;

        Assert.Multiple(() =>
        {
            Assert.That(end.Accepted, Is.True);
            Assert.That(end.Answer, Is.Null);
            Assert.That(questions, Has.Count.EqualTo(1));
            Assert.That(session.Turns, Has.Count.EqualTo(2));
        });
        AssertTerminalTurnSnapshot(
            session.Turns[1],
            2,
            "\u7ed3\u675f",
            DataAgentAnalysisTurnIntent.End,
            now,
            end.Summary);
    }

    [Test]
    public void EndPreservesTurnAddedBeforeTerminalUpdate()
    {
        DateTimeOffset now = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);
        InMemoryDataAgentAnalysisSessionStore inner = new();
        DataAgentAnalysisSession session = inner.Create("local", "Which tests failed?", now);
        DataAgentAnalysisTurn firstTurn = CreateTurn(1, "Which tests failed?", DataAgentAnalysisTurnIntent.NewQuestion, now);
        inner.Save(session with
        {
            UpdatedAt = now,
            LastDataset = "document_index",
            LastSummary = "first answer",
            Turns = [firstTurn]
        });
        InterleavingTerminalSaveStore store = new(
            inner,
            current =>
            {
                DataAgentAnalysisTurn concurrentTurn = CreateTurn(
                    current.Turns.Count + 1,
                    "concurrent follow-up",
                    DataAgentAnalysisTurnIntent.Continue,
                    now.AddSeconds(1));
                return current with
                {
                    UpdatedAt = now.AddSeconds(1),
                    LastSummary = "concurrent answer",
                    Turns = current.Turns.Concat([concurrentTurn]).ToArray()
                };
            });
        DataAgentAnalysisService service = Service(store, [], _ => throw new InvalidOperationException("End must not call answer."), now.AddSeconds(2));

        DataAgentAnalysisResponse end = service.End(session.SessionId);
        DataAgentAnalysisSession updated = store.Get(session.SessionId)!;

        Assert.Multiple(() =>
        {
            Assert.That(end.Accepted, Is.True);
            Assert.That(updated.Status, Is.EqualTo(DataAgentAnalysisSessionStatus.Ended));
            Assert.That(updated.Turns, Has.Count.EqualTo(3));
            Assert.That(updated.Turns[0].Question, Is.EqualTo("Which tests failed?"));
            Assert.That(updated.Turns[1].Question, Is.EqualTo("concurrent follow-up"));
            Assert.That(updated.Turns[1].Intent, Is.EqualTo(DataAgentAnalysisTurnIntent.Continue));
            Assert.That(updated.Turns[2].Intent, Is.EqualTo(DataAgentAnalysisTurnIntent.End));
            Assert.That(updated.Turns[2].Summary, Is.EqualTo(end.Summary));
        });
    }

    [Test]
    public void ContinuePreservesTurnAddedBeforeQueryUpdate()
    {
        DateTimeOffset now = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);
        InMemoryDataAgentAnalysisSessionStore inner = new();
        DataAgentAnalysisSession session = inner.Create("local", "Which tests failed?", now);
        DataAgentAnalysisTurn firstTurn = CreateTurn(1, "Which tests failed?", DataAgentAnalysisTurnIntent.NewQuestion, now);
        inner.Save(session with
        {
            UpdatedAt = now,
            LastDataset = "document_index",
            LastSummary = "first answer",
            Turns = [firstTurn]
        });
        InterleavingTerminalSaveStore store = new(
            inner,
            current =>
            {
                DataAgentAnalysisTurn concurrentTurn = CreateTurn(
                    current.Turns.Count + 1,
                    "concurrent follow-up",
                    DataAgentAnalysisTurnIntent.Continue,
                    now.AddSeconds(1));
                return current with
                {
                    UpdatedAt = now.AddSeconds(1),
                    LastSummary = "concurrent answer",
                    Turns = current.Turns.Concat([concurrentTurn]).ToArray()
                };
            });
        DataAgentAnalysisService service = Service(store, [], _ => AcceptedAnswer("service follow-up"), now.AddSeconds(2));

        DataAgentAnalysisResponse followUp = service.Continue(session.SessionId, "\u7ee7\u7eed");
        DataAgentAnalysisSession updated = store.Get(session.SessionId)!;

        Assert.Multiple(() =>
        {
            Assert.That(followUp.Accepted, Is.True);
            Assert.That(updated.Turns, Has.Count.EqualTo(3));
            Assert.That(updated.Turns[0].Question, Is.EqualTo("Which tests failed?"));
            Assert.That(updated.Turns[0].Index, Is.EqualTo(1));
            Assert.That(updated.Turns[1].Question, Is.EqualTo("concurrent follow-up"));
            Assert.That(updated.Turns[1].Index, Is.EqualTo(2));
            Assert.That(updated.Turns[2].Question, Is.EqualTo("\u7ee7\u7eed"));
            Assert.That(updated.Turns[2].Index, Is.EqualTo(3));
            Assert.That(updated.LastSummary, Is.EqualTo("service follow-up"));
        });
    }

    [Test]
    public void ContinueRejectsWhenSessionEndsBeforeQueryUpdate()
    {
        DateTimeOffset now = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);
        InMemoryDataAgentAnalysisSessionStore inner = new();
        DataAgentAnalysisSession session = inner.Create("local", "Which tests failed?", now);
        DataAgentAnalysisTurn firstTurn = CreateTurn(1, "Which tests failed?", DataAgentAnalysisTurnIntent.NewQuestion, now);
        inner.Save(session with
        {
            UpdatedAt = now,
            LastDataset = "document_index",
            LastSummary = "first answer",
            Turns = [firstTurn]
        });
        InterleavingTerminalSaveStore store = new(
            inner,
            current => current with
            {
                Status = DataAgentAnalysisSessionStatus.Ended,
                UpdatedAt = now.AddSeconds(1)
            });
        DataAgentAnalysisService service = Service(store, [], _ => AcceptedAnswer("late answer"), now.AddSeconds(2));

        DataAgentAnalysisResponse followUp = service.Continue(session.SessionId, "\u7ee7\u7eed");
        DataAgentAnalysisSession updated = store.Get(session.SessionId)!;

        Assert.Multiple(() =>
        {
            Assert.That(followUp.Accepted, Is.False);
            Assert.That(followUp.Status, Is.EqualTo(DataAgentAnalysisSessionStatus.Ended));
            Assert.That(followUp.RejectedReason, Is.EqualTo("analysis_session_ended"));
            Assert.That(updated.Status, Is.EqualTo(DataAgentAnalysisSessionStatus.Ended));
            Assert.That(updated.Turns, Has.Count.EqualTo(1));
            Assert.That(updated.Turns[0].Question, Is.EqualTo("Which tests failed?"));
        });
    }

    [Test]
    public void EndDoesNotExecuteSqlAndRejectsLaterContinue()
    {
        DateTimeOffset now = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);
        List<string> questions = [];
        InMemoryDataAgentAnalysisSessionStore store = new();
        DataAgentAnalysisService service = Service(store, questions, _ => AcceptedAnswer(), now);
        DataAgentAnalysisResponse start = service.Start("local", "Which tests failed?");

        DataAgentAnalysisResponse end = service.Continue(start.SessionId, "\u7ed3\u675f");
        DataAgentAnalysisResponse rejected = service.Continue(start.SessionId, "\u7ee7\u7eed");

        Assert.Multiple(() =>
        {
            Assert.That(end.Accepted, Is.True);
            Assert.That(end.Status, Is.EqualTo(DataAgentAnalysisSessionStatus.Ended));
            Assert.That(rejected.Accepted, Is.False);
            Assert.That(rejected.RejectedReason, Is.EqualTo("analysis_session_ended"));
            Assert.That(questions, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void MissingSessionReturnsStableRejection()
    {
        DataAgentAnalysisService service = Service(new InMemoryDataAgentAnalysisSessionStore(), [], _ => AcceptedAnswer(), DateTimeOffset.UnixEpoch);

        DataAgentAnalysisResponse response = service.Continue("missing", "\u7ee7\u7eed");

        Assert.Multiple(() =>
        {
            Assert.That(response.Accepted, Is.False);
            Assert.That(response.RejectedReason, Is.EqualTo("analysis_session_not_found"));
            Assert.That(response.Status.ToString(), Is.EqualTo("Rejected"));
            Assert.That(response.Intent, Is.EqualTo(DataAgentAnalysisTurnIntent.Continue));
            Assert.That(response.SessionId, Is.EqualTo("missing"));
        });
    }

    static DataAgentAnalysisService Service(
        IDataAgentAnalysisSessionStore store,
        List<string> questions,
        Func<string, DataAgentAnswer> answerFactory,
        DateTimeOffset now)
    {
        return new DataAgentAnalysisService(
            question =>
            {
                questions.Add(question);
                return answerFactory(question);
            },
            store,
            new DataAgentFollowUpInterpreter(),
            () => now);
    }

    static void AssertAcceptedQueryTurnSnapshot(
        DataAgentAnalysisTurn turn,
        int index,
        string question,
        DataAgentAnalysisTurnIntent intent,
        DateTimeOffset createdAt,
        string summary = "Found DataAgent documentation.")
    {
        Assert.Multiple(() =>
        {
            Assert.That(turn.Index, Is.EqualTo(index));
            Assert.That(turn.Question, Is.EqualTo(question));
            Assert.That(turn.Intent, Is.EqualTo(intent));
            Assert.That(turn.CreatedAt, Is.EqualTo(createdAt));
            Assert.That(turn.Dataset, Is.EqualTo("document_index"));
            Assert.That(turn.Sql, Is.EqualTo("SELECT path FROM document_index LIMIT 20"));
            Assert.That(turn.RowCount, Is.EqualTo(2));
            Assert.That(turn.Summary, Is.EqualTo(summary));
            Assert.That(turn.Validated, Is.True);
            Assert.That(turn.RejectedReason, Is.Empty);
        });
    }

    static void AssertTerminalTurnSnapshot(
        DataAgentAnalysisTurn turn,
        int index,
        string question,
        DataAgentAnalysisTurnIntent intent,
        DateTimeOffset createdAt,
        string summary)
    {
        Assert.Multiple(() =>
        {
            Assert.That(turn.Index, Is.EqualTo(index));
            Assert.That(turn.Question, Is.EqualTo(question));
            Assert.That(turn.Intent, Is.EqualTo(intent));
            Assert.That(turn.CreatedAt, Is.EqualTo(createdAt));
            Assert.That(turn.Dataset, Is.Empty);
            Assert.That(turn.Sql, Is.Empty);
            Assert.That(turn.RowCount, Is.Zero);
            Assert.That(turn.Summary, Is.EqualTo(summary));
            Assert.That(turn.Validated, Is.False);
            Assert.That(turn.RejectedReason, Is.EqualTo("non_query_terminal_turn"));
        });
    }

    static DataAgentAnswer AcceptedAnswer(string summary = "Found DataAgent documentation.")
    {
        return new DataAgentAnswer(
            "document_index",
            "SELECT path FROM document_index LIMIT 20",
            2,
            summary,
            "[data_agent_context]\nsql_status=validated\n[/data_agent_context]",
            true,
            string.Empty,
            new DataAgentPlannerExplanation(
                "TestPlanner",
                "find_documents",
                "document_index",
                "high",
                ["test"],
                "test accepted answer"));
    }

    static DataAgentAnswer ClarificationAnswer(
        string summary = "Which dataset should I use?",
        string context = "[data_agent_context]\nsql_status=needs_clarification\nclarification_question=Which dataset should I use?\n[/data_agent_context]")
    {
        return new DataAgentAnswer(
            string.Empty,
            string.Empty,
            0,
            summary,
            context,
            false,
            "needs_clarification",
            new DataAgentPlannerExplanation(
                "TestPlanner",
                "clarify",
                string.Empty,
                "low",
                ["ambiguous_dataset"],
                "ambiguous dataset"));
    }

    static DataAgentAnalysisTurn CreateTurn(
        int index,
        string question,
        DataAgentAnalysisTurnIntent intent,
        DateTimeOffset createdAt)
    {
        return new DataAgentAnalysisTurn(
            $"turn-{index}",
            index,
            question,
            intent,
            createdAt,
            "document_index",
            "SELECT 1",
            1,
            $"summary {index}",
            true,
            string.Empty);
    }

    sealed class InterleavingTerminalSaveStore : IDataAgentAnalysisSessionStore
    {
        readonly InMemoryDataAgentAnalysisSessionStore inner;
        readonly Func<DataAgentAnalysisSession, DataAgentAnalysisSession> interleave;
        bool interleaved;

        public InterleavingTerminalSaveStore(
            InMemoryDataAgentAnalysisSessionStore inner,
            Func<DataAgentAnalysisSession, DataAgentAnalysisSession> interleave)
        {
            this.inner = inner;
            this.interleave = interleave;
        }

        public DataAgentAnalysisSession Create(string callerId, string goal, DateTimeOffset now)
        {
            return inner.Create(callerId, goal, now);
        }

        public DataAgentAnalysisSession? Get(string sessionId)
        {
            return inner.Get(sessionId);
        }

        public DataAgentAnalysisSession Save(DataAgentAnalysisSession session)
        {
            Interleave(session.SessionId);
            return inner.Save(session);
        }

        public bool End(string sessionId, DateTimeOffset now)
        {
            return inner.End(sessionId, now);
        }

        public DataAgentAnalysisSession? Update(
            string sessionId,
            Func<DataAgentAnalysisSession, DataAgentAnalysisSession> update)
        {
            Interleave(sessionId);
            DataAgentAnalysisSession? current = inner.Get(sessionId);
            return current is null ? null : inner.Save(update(current));
        }

        void Interleave(string sessionId)
        {
            if (interleaved)
                return;

            DataAgentAnalysisSession? current = inner.Get(sessionId);
            if (current is null)
                return;

            interleaved = true;
            inner.Save(interleave(current));
        }
    }
}
