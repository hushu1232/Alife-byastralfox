# DataAgent v1.4 Analysis Session Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an in-memory multi-turn Analysis Session layer for DataAgent while preserving the v1.3 single-turn NL2SQL safety chain.

**Architecture:** Add a session service above `DataAgentService.Answer(...)`. The session layer owns multi-turn state, caller isolation, follow-up intent, summary window, and explicit session context; `DataAgentService` keeps ownership of planning, validation, SQL compilation, SQL safety, execution, audit, and single-turn context.

**Tech Stack:** .NET 9, C#, NUnit, existing DataAgent service/planner/safety/audit chain, PowerShell readiness script.

---

## File Map

- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisModels.cs`
  - Owns session/turn/response records and session/turn status enums.
- Create: `sources/Alife.Function/Alife.Function.DataAgent/IDataAgentAnalysisSessionStore.cs`
  - Defines storage boundary for v1.4 memory store and V2 PostgreSQL store.
- Create: `sources/Alife.Function/Alife.Function.DataAgent/InMemoryDataAgentAnalysisSessionStore.cs`
  - Thread-safe in-memory store backed by `ConcurrentDictionary`.
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentFollowUpInterpreter.cs`
  - Deterministic classifier for `继续`, `只看失败的`, `总结一下`, `结束`, and clarification answers.
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisContextProvider.cs`
  - Emits explicit `[data_agent_analysis_session_context]` blocks.
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisSummarizer.cs`
  - Deterministic summary from stored turn snapshots.
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisService.cs`
  - High-level session entry point and only owner of session state transitions.
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
  - Adds v1.4 runtime readiness checks.
- Modify: `tools/check-dataagent-readiness.ps1`
  - Adds v1.4 static readiness checks and no-SQLite-session-binding check.
- Create tests:
  - `Tests/Alife.Test.DataAgent/DataAgentAnalysisSessionStoreTests.cs`
  - `Tests/Alife.Test.DataAgent/DataAgentFollowUpInterpreterTests.cs`
  - `Tests/Alife.Test.DataAgent/DataAgentAnalysisContextProviderTests.cs`
  - `Tests/Alife.Test.DataAgent/DataAgentAnalysisSummarizerTests.cs`
  - `Tests/Alife.Test.DataAgent/DataAgentAnalysisServiceTests.cs`
  - `Tests/Alife.Test.DataAgent/DataAgentV14ReadinessTests.cs`
- Modify tests:
  - `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`

---

### Task 1: Analysis Session Models And In-Memory Store

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisModels.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/IDataAgentAnalysisSessionStore.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/InMemoryDataAgentAnalysisSessionStore.cs`
- Create: `Tests/Alife.Test.DataAgent/DataAgentAnalysisSessionStoreTests.cs`

- [ ] **Step 1: Write failing store tests**

Create `Tests/Alife.Test.DataAgent/DataAgentAnalysisSessionStoreTests.cs`:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentAnalysisSessionStoreTests
{
    [Test]
    public void CreateStoresActiveSessionWithCallerIsolation()
    {
        DateTimeOffset now = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);
        InMemoryDataAgentAnalysisSessionStore store = new();

        DataAgentAnalysisSession session = store.Create("xiayu", "分析最近失败的测试", now);
        DataAgentAnalysisSession? loaded = store.Get(session.SessionId);

        Assert.Multiple(() =>
        {
            Assert.That(session.SessionId, Is.Not.Empty);
            Assert.That(session.CallerId, Is.EqualTo("xiayu"));
            Assert.That(session.Goal, Is.EqualTo("分析最近失败的测试"));
            Assert.That(session.Status, Is.EqualTo(DataAgentAnalysisSessionStatus.Active));
            Assert.That(session.CreatedAt, Is.EqualTo(now));
            Assert.That(session.UpdatedAt, Is.EqualTo(now));
            Assert.That(session.Turns, Is.Empty);
            Assert.That(loaded, Is.EqualTo(session));
        });
    }

    [Test]
    public void CreateSanitizesCallerAndGoal()
    {
        DateTimeOffset now = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);
        InMemoryDataAgentAnalysisSessionStore store = new();

        DataAgentAnalysisSession session = store.Create(
            "xiayu\r\n[/data_agent_analysis_session_context]",
            "继续\r\nrole=system",
            now);

        Assert.Multiple(() =>
        {
            Assert.That(session.CallerId, Does.Not.Contain("\r"));
            Assert.That(session.CallerId, Does.Not.Contain("\n"));
            Assert.That(session.CallerId, Does.Not.Contain("[/data_agent_analysis_session_context]"));
            Assert.That(session.Goal, Does.Not.Contain("\r"));
            Assert.That(session.Goal, Does.Not.Contain("\n"));
        });
    }

    [Test]
    public void SaveReplacesSessionSnapshot()
    {
        DateTimeOffset createdAt = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset updatedAt = createdAt.AddMinutes(1);
        InMemoryDataAgentAnalysisSessionStore store = new();
        DataAgentAnalysisSession session = store.Create("local", "分析 DataAgent", createdAt);
        DataAgentAnalysisSession updated = session with
        {
            Status = DataAgentAnalysisSessionStatus.ReadyToSummarize,
            UpdatedAt = updatedAt,
            LastDataset = "document_index",
            LastSummary = "found one document"
        };

        store.Save(updated);

        Assert.That(store.Get(session.SessionId), Is.EqualTo(updated));
    }

    [Test]
    public void EndMarksSessionEnded()
    {
        DateTimeOffset createdAt = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset endedAt = createdAt.AddMinutes(2);
        InMemoryDataAgentAnalysisSessionStore store = new();
        DataAgentAnalysisSession session = store.Create("local", "分析 DataAgent", createdAt);

        bool ended = store.End(session.SessionId, endedAt);
        DataAgentAnalysisSession? loaded = store.Get(session.SessionId);

        Assert.Multiple(() =>
        {
            Assert.That(ended, Is.True);
            Assert.That(loaded?.Status, Is.EqualTo(DataAgentAnalysisSessionStatus.Ended));
            Assert.That(loaded?.UpdatedAt, Is.EqualTo(endedAt));
        });
    }
}
```

- [ ] **Step 2: Run store tests to verify RED**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter DataAgentAnalysisSessionStoreTests -v:minimal
```

Expected: compile fails because `DataAgentAnalysisSession`, `DataAgentAnalysisSessionStatus`, and `InMemoryDataAgentAnalysisSessionStore` do not exist.

- [ ] **Step 3: Add session model records**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisModels.cs`:

```csharp
namespace Alife.Function.DataAgent;

public enum DataAgentAnalysisSessionStatus
{
    Active,
    AwaitingClarification,
    ReadyToSummarize,
    Summarized,
    Ended
}

public enum DataAgentAnalysisTurnIntent
{
    NewQuestion,
    Continue,
    RefinePrevious,
    AnswerClarification,
    Summarize,
    End
}

public sealed record DataAgentAnalysisTurn(
    string TurnId,
    int Index,
    string Question,
    DataAgentAnalysisTurnIntent Intent,
    DateTimeOffset CreatedAt,
    string Dataset,
    string Sql,
    int RowCount,
    string Summary,
    bool Validated,
    string RejectedReason);

public sealed record DataAgentAnalysisSession(
    string SessionId,
    string CallerId,
    string Goal,
    DataAgentAnalysisSessionStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? LastDataset,
    string? LastSummary,
    string? PendingClarificationQuestion,
    IReadOnlyList<DataAgentAnalysisTurn> Turns);

public sealed record DataAgentAnalysisResponse(
    string SessionId,
    DataAgentAnalysisSessionStatus Status,
    DataAgentAnalysisTurnIntent Intent,
    DataAgentAnswer? Answer,
    string Summary,
    string Context,
    bool Accepted,
    string RejectedReason);
```

- [ ] **Step 4: Add store interface**

Create `sources/Alife.Function/Alife.Function.DataAgent/IDataAgentAnalysisSessionStore.cs`:

```csharp
namespace Alife.Function.DataAgent;

public interface IDataAgentAnalysisSessionStore
{
    DataAgentAnalysisSession Create(string callerId, string goal, DateTimeOffset now);

    DataAgentAnalysisSession? Get(string sessionId);

    DataAgentAnalysisSession Save(DataAgentAnalysisSession session);

    bool End(string sessionId, DateTimeOffset now);
}
```

- [ ] **Step 5: Add in-memory store**

Create `sources/Alife.Function/Alife.Function.DataAgent/InMemoryDataAgentAnalysisSessionStore.cs`:

```csharp
using System.Collections.Concurrent;

namespace Alife.Function.DataAgent;

public sealed class InMemoryDataAgentAnalysisSessionStore : IDataAgentAnalysisSessionStore
{
    readonly ConcurrentDictionary<string, DataAgentAnalysisSession> sessions = new(StringComparer.Ordinal);

    public DataAgentAnalysisSession Create(string callerId, string goal, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(goal);

        string safeCallerId = string.IsNullOrWhiteSpace(callerId)
            ? "local"
            : DataAgentContextFieldSanitizer.Sanitize(callerId, 80);
        string safeGoal = DataAgentContextFieldSanitizer.Sanitize(goal, 240);

        DataAgentAnalysisSession session = new(
            Guid.NewGuid().ToString("N"),
            safeCallerId,
            safeGoal,
            DataAgentAnalysisSessionStatus.Active,
            now,
            now,
            null,
            null,
            null,
            []);

        sessions[session.SessionId] = session;
        return session;
    }

    public DataAgentAnalysisSession? Get(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return null;

        return sessions.TryGetValue(sessionId, out DataAgentAnalysisSession? session)
            ? session
            : null;
    }

    public DataAgentAnalysisSession Save(DataAgentAnalysisSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.SessionId);

        sessions[session.SessionId] = session;
        return session;
    }

    public bool End(string sessionId, DateTimeOffset now)
    {
        if (Get(sessionId) is not DataAgentAnalysisSession session)
            return false;

        Save(session with
        {
            Status = DataAgentAnalysisSessionStatus.Ended,
            UpdatedAt = now
        });
        return true;
    }
}
```

- [ ] **Step 6: Run store tests to verify GREEN**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter DataAgentAnalysisSessionStoreTests -v:minimal
```

Expected: all `DataAgentAnalysisSessionStoreTests` pass.

- [ ] **Step 7: Commit task 1**

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisModels.cs sources/Alife.Function/Alife.Function.DataAgent/IDataAgentAnalysisSessionStore.cs sources/Alife.Function/Alife.Function.DataAgent/InMemoryDataAgentAnalysisSessionStore.cs Tests/Alife.Test.DataAgent/DataAgentAnalysisSessionStoreTests.cs
git commit -m "Add DataAgent analysis session store"
```

---

### Task 2: Follow-Up Interpreter, Session Context, And Summarizer

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentFollowUpInterpreter.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisContextProvider.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisSummarizer.cs`
- Create: `Tests/Alife.Test.DataAgent/DataAgentFollowUpInterpreterTests.cs`
- Create: `Tests/Alife.Test.DataAgent/DataAgentAnalysisContextProviderTests.cs`
- Create: `Tests/Alife.Test.DataAgent/DataAgentAnalysisSummarizerTests.cs`

- [ ] **Step 1: Write failing follow-up interpreter tests**

Create `Tests/Alife.Test.DataAgent/DataAgentFollowUpInterpreterTests.cs`:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentFollowUpInterpreterTests
{
    [TestCase("继续", DataAgentAnalysisTurnIntent.Continue)]
    [TestCase("接着看", DataAgentAnalysisTurnIntent.Continue)]
    [TestCase("只看失败的", DataAgentAnalysisTurnIntent.RefinePrevious)]
    [TestCase("换成 DataAgent 相关", DataAgentAnalysisTurnIntent.RefinePrevious)]
    [TestCase("总结一下", DataAgentAnalysisTurnIntent.Summarize)]
    [TestCase("这次分析结论是什么", DataAgentAnalysisTurnIntent.Summarize)]
    [TestCase("结束", DataAgentAnalysisTurnIntent.End)]
    [TestCase("停止这次分析", DataAgentAnalysisTurnIntent.End)]
    [TestCase("最近的 readiness 状态是什么", DataAgentAnalysisTurnIntent.NewQuestion)]
    public void InterpretsCommonChineseFollowUpPhrases(string question, DataAgentAnalysisTurnIntent expected)
    {
        DataAgentFollowUpInterpreter interpreter = new();

        Assert.That(interpreter.Interpret(question), Is.EqualTo(expected));
    }

    [Test]
    public void AwaitingClarificationTreatsPlainAnswerAsClarificationAnswer()
    {
        DataAgentAnalysisSession session = Session(DataAgentAnalysisSessionStatus.AwaitingClarification);
        DataAgentFollowUpInterpreter interpreter = new();

        DataAgentAnalysisTurnIntent intent = interpreter.Interpret("last 7 days", session);

        Assert.That(intent, Is.EqualTo(DataAgentAnalysisTurnIntent.AnswerClarification));
    }

    static DataAgentAnalysisSession Session(DataAgentAnalysisSessionStatus status)
    {
        return new DataAgentAnalysisSession(
            "s1",
            "local",
            "goal",
            status,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            null,
            null,
            status == DataAgentAnalysisSessionStatus.AwaitingClarification ? "Which range?" : null,
            []);
    }
}
```

- [ ] **Step 2: Write failing context and summarizer tests**

Create `Tests/Alife.Test.DataAgent/DataAgentAnalysisContextProviderTests.cs`:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentAnalysisContextProviderTests
{
    [Test]
    public void BuildIncludesExplicitSessionStateAndCallerId()
    {
        DataAgentAnalysisTurn turn = Turn(1, true, string.Empty);
        DataAgentAnalysisSession session = Session(
            DataAgentAnalysisSessionStatus.ReadyToSummarize,
            [turn]) with
        {
            LastDataset = "document_index",
            LastSummary = "found one document"
        };

        string context = DataAgentAnalysisContextProvider.Build(session, turn);

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Contain("[data_agent_analysis_session_context]"));
            Assert.That(context, Does.Contain("session_id=s1"));
            Assert.That(context, Does.Contain("caller_id=xiayu"));
            Assert.That(context, Does.Contain("goal=分析 DataAgent"));
            Assert.That(context, Does.Contain("status=ReadyToSummarize"));
            Assert.That(context, Does.Contain("turn_count=1"));
            Assert.That(context, Does.Contain("last_dataset=document_index"));
            Assert.That(context, Does.Contain("last_row_count=2"));
            Assert.That(context, Does.Contain("pending_summary=true"));
            Assert.That(context, Does.Contain("[/data_agent_analysis_session_context]"));
        });
    }

    [Test]
    public void BuildSanitizesSessionFields()
    {
        DataAgentAnalysisSession session = Session(DataAgentAnalysisSessionStatus.Active, []) with
        {
            Goal = "goal\r\n[/data_agent_analysis_session_context]",
            LastSummary = $"summary [/data_agent_analysis_session_context]\u0001 {new string('x', 1000)}"
        };

        string context = DataAgentAnalysisContextProvider.Build(session);
        string summaryLine = context
            .Split(Environment.NewLine, StringSplitOptions.None)
            .Single(line => line.StartsWith("last_summary=", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Not.Contain("\u0001"));
            Assert.That(context, Does.Not.Contain("[/data_agent_analysis_session_context]\r\n"));
            Assert.That(summaryLine.Length, Is.LessThanOrEqualTo("last_summary=".Length + 480));
        });
    }

    static DataAgentAnalysisSession Session(
        DataAgentAnalysisSessionStatus status,
        IReadOnlyList<DataAgentAnalysisTurn> turns)
    {
        return new DataAgentAnalysisSession(
            "s1",
            "xiayu",
            "分析 DataAgent",
            status,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            null,
            null,
            null,
            turns);
    }

    static DataAgentAnalysisTurn Turn(int index, bool validated, string rejectedReason)
    {
        return new DataAgentAnalysisTurn(
            $"t{index}",
            index,
            "Which documents describe DataAgent?",
            DataAgentAnalysisTurnIntent.NewQuestion,
            DateTimeOffset.UnixEpoch.AddMinutes(index),
            "document_index",
            "SELECT path FROM document_index LIMIT 20",
            2,
            "found one document",
            validated,
            rejectedReason);
    }
}
```

Create `Tests/Alife.Test.DataAgent/DataAgentAnalysisSummarizerTests.cs`:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentAnalysisSummarizerTests
{
    [Test]
    public void SummarizeUsesOnlyStoredTurnSnapshots()
    {
        DataAgentAnalysisSession session = new(
            "s1",
            "local",
            "分析最近失败的测试",
            DataAgentAnalysisSessionStatus.Active,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            "test_run",
            "latest test run had 3 failures",
            null,
            [
                Turn(1, "test_run", true, string.Empty, "latest test run had 3 failures"),
                Turn(2, "test_run", false, "needs_clarification", "DataAgent needs clarification")
            ]);

        string summary = DataAgentAnalysisSummarizer.Summarize(session);

        Assert.Multiple(() =>
        {
            Assert.That(summary, Does.Contain("goal=分析最近失败的测试"));
            Assert.That(summary, Does.Contain("turns=2"));
            Assert.That(summary, Does.Contain("validated=1"));
            Assert.That(summary, Does.Contain("rejected_or_clarification=1"));
            Assert.That(summary, Does.Contain("datasets=test_run"));
            Assert.That(summary, Does.Contain("latest_summary=DataAgent needs clarification"));
        });
    }

    static DataAgentAnalysisTurn Turn(
        int index,
        string dataset,
        bool validated,
        string rejectedReason,
        string summary)
    {
        return new DataAgentAnalysisTurn(
            $"t{index}",
            index,
            $"question {index}",
            DataAgentAnalysisTurnIntent.NewQuestion,
            DateTimeOffset.UnixEpoch.AddMinutes(index),
            dataset,
            "SELECT 1 LIMIT 1",
            validated ? 1 : 0,
            summary,
            validated,
            rejectedReason);
    }
}
```

- [ ] **Step 3: Run task 2 tests to verify RED**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "DataAgentFollowUpInterpreterTests|DataAgentAnalysisContextProviderTests|DataAgentAnalysisSummarizerTests" -v:minimal
```

Expected: compile fails because interpreter, analysis context provider, and summarizer do not exist.

- [ ] **Step 4: Add follow-up interpreter**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentFollowUpInterpreter.cs`:

```csharp
namespace Alife.Function.DataAgent;

public sealed class DataAgentFollowUpInterpreter
{
    static readonly string[] ContinuePhrases = ["继续", "接着", "再看", "继续看"];
    static readonly string[] RefinePhrases = ["只看", "筛选", "过滤", "换成", "相关"];
    static readonly string[] SummarizePhrases = ["总结", "结论", "归纳"];
    static readonly string[] EndPhrases = ["结束", "停止", "关闭"];

    public DataAgentAnalysisTurnIntent Interpret(
        string question,
        DataAgentAnalysisSession? session = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        string normalized = question.Trim();

        if (ContainsAny(normalized, SummarizePhrases))
            return DataAgentAnalysisTurnIntent.Summarize;

        if (ContainsAny(normalized, EndPhrases))
            return DataAgentAnalysisTurnIntent.End;

        if (session?.Status == DataAgentAnalysisSessionStatus.AwaitingClarification)
            return DataAgentAnalysisTurnIntent.AnswerClarification;

        if (ContainsAny(normalized, ContinuePhrases))
            return DataAgentAnalysisTurnIntent.Continue;

        if (ContainsAny(normalized, RefinePhrases))
            return DataAgentAnalysisTurnIntent.RefinePrevious;

        return DataAgentAnalysisTurnIntent.NewQuestion;
    }

    static bool ContainsAny(string value, IReadOnlyList<string> phrases)
    {
        foreach (string phrase in phrases)
        {
            if (value.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
```

- [ ] **Step 5: Add analysis context provider**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisContextProvider.cs`:

```csharp
using System.Text;

namespace Alife.Function.DataAgent;

public static class DataAgentAnalysisContextProvider
{
    const int MaxSummaryLength = 480;

    public static string Build(
        DataAgentAnalysisSession session,
        DataAgentAnalysisTurn? latestTurn = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        StringBuilder builder = new();
        builder.AppendLine("[data_agent_analysis_session_context]");
        builder.AppendLine($"session_id={Sanitize(session.SessionId)}");
        builder.AppendLine($"caller_id={Sanitize(session.CallerId)}");
        builder.AppendLine($"goal={Sanitize(session.Goal)}");
        builder.AppendLine($"status={session.Status}");
        builder.AppendLine($"turn_count={session.Turns.Count}");
        builder.AppendLine($"last_dataset={Sanitize(session.LastDataset ?? string.Empty)}");
        builder.AppendLine($"last_row_count={latestTurn?.RowCount ?? 0}");
        builder.AppendLine($"last_summary={Sanitize(session.LastSummary ?? string.Empty, MaxSummaryLength)}");
        builder.AppendLine($"pending_clarification={ToLowerBool(string.IsNullOrWhiteSpace(session.PendingClarificationQuestion) == false)}");
        builder.AppendLine($"pending_summary={ToLowerBool(session.Status == DataAgentAnalysisSessionStatus.ReadyToSummarize)}");
        builder.AppendLine("[/data_agent_analysis_session_context]");
        return builder.ToString().Trim();
    }

    static string Sanitize(string value)
    {
        return DataAgentContextFieldSanitizer.Sanitize(value);
    }

    static string Sanitize(string value, int maxLength)
    {
        return DataAgentContextFieldSanitizer.Sanitize(value, maxLength);
    }

    static string ToLowerBool(bool value)
    {
        return value ? "true" : "false";
    }
}
```

- [ ] **Step 6: Add deterministic summarizer**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisSummarizer.cs`:

```csharp
using System.Text;

namespace Alife.Function.DataAgent;

public static class DataAgentAnalysisSummarizer
{
    public static string Summarize(DataAgentAnalysisSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        int validated = session.Turns.Count(turn => turn.Validated);
        int rejected = session.Turns.Count(turn => turn.Validated == false);
        string datasets = string.Join(
            ", ",
            session.Turns
                .Select(turn => turn.Dataset)
                .Where(dataset => string.IsNullOrWhiteSpace(dataset) == false)
                .Distinct(StringComparer.OrdinalIgnoreCase));
        string latestSummary = session.Turns.LastOrDefault()?.Summary ?? string.Empty;

        StringBuilder builder = new();
        builder.Append($"goal={session.Goal}; ");
        builder.Append($"turns={session.Turns.Count}; ");
        builder.Append($"validated={validated}; ");
        builder.Append($"rejected_or_clarification={rejected}; ");
        builder.Append($"datasets={datasets}; ");
        builder.Append($"latest_summary={latestSummary}");

        if (string.IsNullOrWhiteSpace(session.PendingClarificationQuestion) == false)
            builder.Append($"; pending_clarification={session.PendingClarificationQuestion}");

        return DataAgentContextFieldSanitizer.Sanitize(builder.ToString(), 720);
    }
}
```

- [ ] **Step 7: Run task 2 tests to verify GREEN**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "DataAgentFollowUpInterpreterTests|DataAgentAnalysisContextProviderTests|DataAgentAnalysisSummarizerTests" -v:minimal
```

Expected: all task 2 tests pass.

- [ ] **Step 8: Commit task 2**

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentFollowUpInterpreter.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisContextProvider.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisSummarizer.cs Tests/Alife.Test.DataAgent/DataAgentFollowUpInterpreterTests.cs Tests/Alife.Test.DataAgent/DataAgentAnalysisContextProviderTests.cs Tests/Alife.Test.DataAgent/DataAgentAnalysisSummarizerTests.cs
git commit -m "Add DataAgent analysis context helpers"
```

---

### Task 3: Analysis Service State Machine

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisService.cs`
- Create: `Tests/Alife.Test.DataAgent/DataAgentAnalysisServiceTests.cs`

- [ ] **Step 1: Write failing service tests**

Create `Tests/Alife.Test.DataAgent/DataAgentAnalysisServiceTests.cs`:

```csharp
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
    public void ContinueUsesBoundedFollowUpContextAndAppendsTurn()
    {
        DateTimeOffset now = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);
        List<string> questions = [];
        InMemoryDataAgentAnalysisSessionStore store = new();
        DataAgentAnalysisService service = Service(store, questions, _ => AcceptedAnswer(), now);
        DataAgentAnalysisResponse start = service.Start("local", "Which tests failed?");

        DataAgentAnalysisResponse followUp = service.Continue(start.SessionId, "继续");
        DataAgentAnalysisSession session = store.Get(start.SessionId)!;

        Assert.Multiple(() =>
        {
            Assert.That(followUp.Accepted, Is.True);
            Assert.That(followUp.Intent, Is.EqualTo(DataAgentAnalysisTurnIntent.Continue));
            Assert.That(session.Turns, Has.Count.EqualTo(2));
            Assert.That(session.Turns[1].Question, Is.EqualTo("继续"));
            Assert.That(questions[1], Does.Contain("Analysis goal: Which tests failed?"));
            Assert.That(questions[1], Does.Contain("Previous summary:"));
            Assert.That(questions[1], Does.Contain("Follow-up question: 继续"));
        });
    }

    [Test]
    public void ThreeValidatedTurnsMoveSessionToReadyToSummarize()
    {
        DateTimeOffset now = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);
        InMemoryDataAgentAnalysisSessionStore store = new();
        DataAgentAnalysisService service = Service(store, [], _ => AcceptedAnswer(), now);
        DataAgentAnalysisResponse response = service.Start("local", "Which tests failed?");

        service.Continue(response.SessionId, "继续");
        DataAgentAnalysisResponse third = service.Continue(response.SessionId, "只看失败的");

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

        DataAgentAnalysisResponse summary = service.Continue(start.SessionId, "总结一下");

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
    public void EndDoesNotExecuteSqlAndRejectsLaterContinue()
    {
        DateTimeOffset now = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);
        List<string> questions = [];
        InMemoryDataAgentAnalysisSessionStore store = new();
        DataAgentAnalysisService service = Service(store, questions, _ => AcceptedAnswer(), now);
        DataAgentAnalysisResponse start = service.Start("local", "Which tests failed?");

        DataAgentAnalysisResponse end = service.Continue(start.SessionId, "结束");
        DataAgentAnalysisResponse rejected = service.Continue(start.SessionId, "继续");

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

        DataAgentAnalysisResponse response = service.Continue("missing", "继续");

        Assert.Multiple(() =>
        {
            Assert.That(response.Accepted, Is.False);
            Assert.That(response.RejectedReason, Is.EqualTo("analysis_session_not_found"));
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

    static DataAgentAnswer AcceptedAnswer()
    {
        return new DataAgentAnswer(
            "document_index",
            "SELECT path FROM document_index LIMIT 20",
            2,
            "Found DataAgent documentation.",
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

    static DataAgentAnswer ClarificationAnswer()
    {
        return new DataAgentAnswer(
            string.Empty,
            string.Empty,
            0,
            "Which dataset should I use?",
            "[data_agent_context]\nsql_status=needs_clarification\nclarification_question=Which dataset should I use?\n[/data_agent_context]",
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
}
```

- [ ] **Step 2: Run service tests to verify RED**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter DataAgentAnalysisServiceTests -v:minimal
```

Expected: compile fails because `DataAgentAnalysisService` does not exist.

- [ ] **Step 3: Add analysis service**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisService.cs`:

```csharp
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
        builder.AppendLine($"Analysis goal: {session.Goal}");
        builder.AppendLine($"Previous dataset: {session.LastDataset ?? string.Empty}");
        builder.AppendLine($"Previous summary: {session.LastSummary ?? string.Empty}");
        if (string.IsNullOrWhiteSpace(session.PendingClarificationQuestion) == false)
            builder.AppendLine($"Pending clarification: {session.PendingClarificationQuestion}");

        builder.AppendLine($"Follow-up intent: {intent}");
        builder.Append($"Follow-up question: {question}");
        return builder.ToString();
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
```

- [ ] **Step 4: Run service tests to verify GREEN**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter DataAgentAnalysisServiceTests -v:minimal
```

Expected: all `DataAgentAnalysisServiceTests` pass.

- [ ] **Step 5: Run all new v1.4 unit tests**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "DataAgentAnalysisSessionStoreTests|DataAgentFollowUpInterpreterTests|DataAgentAnalysisContextProviderTests|DataAgentAnalysisSummarizerTests|DataAgentAnalysisServiceTests" -v:minimal
```

Expected: all new v1.4 tests pass.

- [ ] **Step 6: Commit task 3**

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisService.cs Tests/Alife.Test.DataAgent/DataAgentAnalysisServiceTests.cs
git commit -m "Add DataAgent analysis session service"
```

---

### Task 4: v1.4 Readiness Checks

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
- Modify: `tools/check-dataagent-readiness.ps1`
- Create: `Tests/Alife.Test.DataAgent/DataAgentV14ReadinessTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`

- [ ] **Step 1: Write failing v1.4 readiness tests**

Create `Tests/Alife.Test.DataAgent/DataAgentV14ReadinessTests.cs`:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV14ReadinessTests
{
    static readonly string[] RequiredChecks =
    [
        "AnalysisSessionServicePresent",
        "AnalysisSessionStorePresent",
        "AnalysisSessionStateMachineTransitions",
        "AnalysisFollowUpInterpreterPresent",
        "AnalysisSessionContextProviderPresent",
        "AnalysisSummaryWindowPresent",
        "AnalysisSessionHasNoSqliteBinding"
    ];

    [Test]
    public void CoreReadinessIncludesAllV14AnalysisSessionChecks()
    {
        string databasePath = NewDatabasePath();

        IReadOnlyDictionary<string, DataAgentReadinessCheck> checks = DataAgentReadiness
            .CheckCore(databasePath)
            .ToDictionary(check => check.Name, StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            foreach (string checkName in RequiredChecks)
            {
                Assert.That(checks, Does.ContainKey(checkName), checkName);
                Assert.That(checks[checkName].Passed, Is.True, $"{checkName}:{checks[checkName].Detail}");
            }
        });
    }

    [Test]
    public void StaticReadinessScriptContainsAllV14AnalysisSessionMarkers()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string script = File.ReadAllText(Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1"));

        Assert.Multiple(() =>
        {
            foreach (string checkName in RequiredChecks)
                Assert.That(script, Does.Contain(checkName), checkName);

            Assert.That(script, Does.Contain("DataAgentAnalysisService.cs"));
            Assert.That(script, Does.Contain("InMemoryDataAgentAnalysisSessionStore.cs"));
            Assert.That(script, Does.Contain("DataAgentFollowUpInterpreter.cs"));
            Assert.That(script, Does.Contain("DataAgentAnalysisContextProvider.cs"));
            Assert.That(script, Does.Contain("DataAgentAnalysisSummarizer.cs"));
            Assert.That(script, Does.Contain("Test-FileOmitsMarker"));
            Assert.That(script, Does.Contain("SqliteConnection"));
        });
    }

    static string NewDatabasePath()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "dataagent-v14-readiness-tests");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Guid.NewGuid():N}.sqlite");
    }

    static string FindRepoRoot(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tools")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test directory.");
    }
}
```

Modify `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`:

```csharp
Assert.That(checks, Has.Count.EqualTo(29));
```

and:

```csharp
Assert.That(result.StandardOutput, Does.Contain("Summary: 29 required passed, 0 required missing"));
```

- [ ] **Step 2: Run readiness tests to verify RED**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "DataAgentV14ReadinessTests|DataAgentReadinessTests" -v:minimal
```

Expected: readiness tests fail because v1.4 checks are not yet added to runtime readiness or the PowerShell script.

- [ ] **Step 3: Add runtime readiness checks**

Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs` inside `CheckCore`, after the existing tool handler check:

```csharp
            InMemoryDataAgentAnalysisSessionStore analysisStore = new();
            DataAgentAnalysisService analysisService = new(
                new DataAgentService(databasePath),
                analysisStore);
            DataAgentAnalysisResponse analysisStart = analysisService.Start(
                "local",
                "Which documents describe DataAgent NL2SQL?");
            DataAgentAnalysisSession? analysisSession = analysisStore.Get(analysisStart.SessionId);

            checks.Add(typeof(DataAgentAnalysisService).IsClass &&
                       analysisStart.Accepted &&
                       analysisSession?.Turns.Count == 1
                ? Pass("AnalysisSessionServicePresent", analysisStart.Status.ToString())
                : Fail("AnalysisSessionServicePresent", analysisStart.RejectedReason));

            checks.Add(typeof(IDataAgentAnalysisSessionStore).IsInterface &&
                       typeof(IDataAgentAnalysisSessionStore).IsAssignableFrom(typeof(InMemoryDataAgentAnalysisSessionStore))
                ? Pass("AnalysisSessionStorePresent", nameof(InMemoryDataAgentAnalysisSessionStore))
                : Fail("AnalysisSessionStorePresent", "analysis session store boundary missing"));

            DataAgentAnalysisResponse analysisEnd = analysisService.End(analysisStart.SessionId);
            DataAgentAnalysisResponse endedContinue = analysisService.Continue(analysisStart.SessionId, "继续");
            checks.Add(analysisStart.Status is DataAgentAnalysisSessionStatus.Active or DataAgentAnalysisSessionStatus.ReadyToSummarize &&
                       analysisEnd.Status == DataAgentAnalysisSessionStatus.Ended &&
                       endedContinue.Accepted == false &&
                       endedContinue.RejectedReason == "analysis_session_ended"
                ? Pass("AnalysisSessionStateMachineTransitions", $"{analysisStart.Status}->{analysisEnd.Status}")
                : Fail("AnalysisSessionStateMachineTransitions", endedContinue.RejectedReason));

            DataAgentFollowUpInterpreter interpreter = new();
            checks.Add(interpreter.Interpret("继续") == DataAgentAnalysisTurnIntent.Continue &&
                       interpreter.Interpret("只看失败的") == DataAgentAnalysisTurnIntent.RefinePrevious &&
                       interpreter.Interpret("总结一下") == DataAgentAnalysisTurnIntent.Summarize
                ? Pass("AnalysisFollowUpInterpreterPresent", "common Chinese follow-up intents recognized")
                : Fail("AnalysisFollowUpInterpreterPresent", "follow-up intent mismatch"));

            string analysisContext = analysisSession is null
                ? string.Empty
                : DataAgentAnalysisContextProvider.Build(analysisSession);
            checks.Add(analysisContext.Contains("[data_agent_analysis_session_context]", StringComparison.Ordinal) &&
                       analysisContext.Contains("caller_id=local", StringComparison.Ordinal) &&
                       analysisContext.Contains("pending_summary=", StringComparison.Ordinal)
                ? Pass("AnalysisSessionContextProviderPresent", "analysis session context emitted")
                : Fail("AnalysisSessionContextProviderPresent", analysisContext));

            DataAgentAnalysisResponse summaryWindowStart = analysisService.Start("local", "Which documents describe DataAgent NL2SQL?");
            analysisService.Continue(summaryWindowStart.SessionId, "继续");
            DataAgentAnalysisResponse thirdTurn = analysisService.Continue(summaryWindowStart.SessionId, "只看 DataAgent 相关");
            checks.Add(thirdTurn.Status == DataAgentAnalysisSessionStatus.ReadyToSummarize
                ? Pass("AnalysisSummaryWindowPresent", thirdTurn.Status.ToString())
                : Fail("AnalysisSummaryWindowPresent", thirdTurn.Status.ToString()));

            bool storeInterfaceHasSqliteBinding = typeof(IDataAgentAnalysisSessionStore)
                .GetMethods()
                .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType))
                .Any(type => type.FullName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true);
            checks.Add(storeInterfaceHasSqliteBinding == false
                ? Pass("AnalysisSessionHasNoSqliteBinding", "store interface is provider-neutral")
                : Fail("AnalysisSessionHasNoSqliteBinding", "store interface exposes sqlite types"));
```

- [ ] **Step 4: Add static readiness checks**

Modify `tools/check-dataagent-readiness.ps1` by adding this function after `Test-FileMarker`:

```powershell
function Test-FileOmitsMarker {
    param(
        [string]$RelativePath,
        [string[]]$Markers
    )

    $fullPath = Join-Path $repoRoot $RelativePath
    if (-not (Test-Path -LiteralPath $fullPath)) {
        return $false
    }

    $content = Get-Content -LiteralPath $fullPath -Raw
    foreach ($marker in $Markers) {
        if ($content.IndexOf($marker, [System.StringComparison]::Ordinal) -ge 0) {
            return $false
        }
    }

    return $true
}
```

Add these checks to `$checks` after `ToolHandlerReturnsDataAgentContext`:

```powershell
    New-Check -Group "Analysis" -Name "AnalysisSessionServicePresent" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisService.cs" @("DataAgentAnalysisService", "DataAgentService", "ExecuteQueryTurn", "analysis_session_ended")) -Detail "analysis session service markers"
    New-Check -Group "Analysis" -Name "AnalysisSessionStorePresent" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/InMemoryDataAgentAnalysisSessionStore.cs" @("InMemoryDataAgentAnalysisSessionStore", "ConcurrentDictionary", "IDataAgentAnalysisSessionStore")) -Detail "in-memory analysis session store markers"
    New-Check -Group "Analysis" -Name "AnalysisSessionStateMachineTransitions" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisService.cs" @("AwaitingClarification", "ReadyToSummarize", "Summarized", "Ended")) -Detail "analysis session state transition markers"
    New-Check -Group "Analysis" -Name "AnalysisFollowUpInterpreterPresent" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentFollowUpInterpreter.cs" @("DataAgentFollowUpInterpreter", "继续", "只看", "总结", "结束")) -Detail "follow-up interpreter markers"
    New-Check -Group "Analysis" -Name "AnalysisSessionContextProviderPresent" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisContextProvider.cs" @("[data_agent_analysis_session_context]", "caller_id", "pending_summary")) -Detail "analysis context provider markers"
    New-Check -Group "Analysis" -Name "AnalysisSummaryWindowPresent" -Passed (Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisService.cs" @("SummaryWindowValidatedTurns", "ReadyToSummarize")) -Detail "summary window markers"
    New-Check -Group "Analysis" -Name "AnalysisSessionHasNoSqliteBinding" -Passed ((Test-FileMarker "Sources/Alife.Function/Alife.Function.DataAgent/IDataAgentAnalysisSessionStore.cs" @("IDataAgentAnalysisSessionStore", "DataAgentAnalysisSession")) -and (Test-FileOmitsMarker "Sources/Alife.Function/Alife.Function.DataAgent/IDataAgentAnalysisSessionStore.cs" @("SqliteConnection", "Microsoft.Data.Sqlite"))) -Detail "analysis session store has no sqlite binding"
```

Update group rendering:

```powershell
foreach ($group in @("Core", "Schema", "Safety", "Query", "Context", "Planner", "Tool", "Analysis")) {
```

- [ ] **Step 5: Run readiness tests to verify GREEN**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "DataAgentV14ReadinessTests|DataAgentReadinessTests" -v:minimal
```

Expected: all readiness tests pass with 29 required checks.

- [ ] **Step 6: Run readiness script**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected:

```text
Summary: 29 required passed, 0 required missing
```

- [ ] **Step 7: Commit task 4**

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs tools/check-dataagent-readiness.ps1 Tests/Alife.Test.DataAgent/DataAgentV14ReadinessTests.cs Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs
git commit -m "Add DataAgent v1.4 readiness checks"
```

---

### Task 5: Full Verification And Integration Commit

**Files:**
- Verify all files created or modified in tasks 1-4.

- [ ] **Step 1: Run focused DataAgent test suite**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
```

Expected: all DataAgent tests pass.

- [ ] **Step 2: Run full solution build**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' build Alife.slnx --no-restore -v:minimal
```

Expected: build succeeds. Existing QChat fake-runtime CS0067 warnings may appear and are acceptable if unchanged from baseline.

- [ ] **Step 3: Run full solution tests**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Alife.slnx --no-restore --no-build -v:minimal
```

Expected: all projects pass with the same known skipped tests as baseline.

- [ ] **Step 4: Run DataAgent readiness script**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected:

```text
Summary: 29 required passed, 0 required missing
```

- [ ] **Step 5: Run QChat engineering map readiness script**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected:

```text
0 required missing
```

- [ ] **Step 6: Check formatting and staged diff**

Run:

```powershell
git diff --check
git status --short
```

Expected: `git diff --check` produces no output. `git status --short` only shows files intentionally changed by v1.4 implementation tasks.

- [ ] **Step 7: Commit any uncommitted verification fixes**

If tasks 1-4 already produced clean commits and no files remain changed, skip this step. If small verification fixes were needed, commit them:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent Tests/Alife.Test.DataAgent tools/check-dataagent-readiness.ps1
git commit -m "Stabilize DataAgent v1.4 analysis session"
```

- [ ] **Step 8: Report implementation status**

Summarize:

```text
DataAgent v1.4 implemented:
- in-memory Analysis Session store
- explicit session state machine
- follow-up interpreter
- deterministic analysis context and summary
- readiness upgraded from 22 to 29 required checks
- no SQLite session binding; V2 PostgreSQL boundary preserved
```

Do not upload to GitHub until the implementation is verified and the user confirms the upload step.

---

## Self-Review Checklist

- Spec coverage: tasks cover in-memory session store, explicit state machine, follow-up intent, summary window, context injection, QChat separation, caller isolation, no SQLite session binding, V2 PostgreSQL boundary, and readiness checks.
- Placeholder scan: this plan contains no unresolved placeholder markers, no incomplete sections, and no implementation steps without concrete files or code snippets.
- Type consistency: `DataAgentAnalysisSessionStatus`, `DataAgentAnalysisTurnIntent`, `DataAgentAnalysisSession`, `DataAgentAnalysisTurn`, `DataAgentAnalysisResponse`, `IDataAgentAnalysisSessionStore`, `InMemoryDataAgentAnalysisSessionStore`, `DataAgentFollowUpInterpreter`, `DataAgentAnalysisContextProvider`, `DataAgentAnalysisSummarizer`, and `DataAgentAnalysisService` are used consistently across tasks.
- Safety boundary: query-producing turns use `DataAgentAnalysisService.ExecuteQueryTurn`, which calls the injected single-turn answer boundary; production construction uses `DataAgentService.Answer`.
- Persistence boundary: v1.4 store uses `ConcurrentDictionary`; readiness checks guard the store interface against SQLite-specific types.
