# DataAgent V2.9 Progress Streaming Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an owner-only, in-memory DataAgent progress streaming boundary so QChat diagnostics can observe the current analysis chain while it is running, without adding persistence, frontend UI, LangGraph, model calls, tool calls, or a new SQL execution path.

**Architecture:** Add a DataAgent-owned progress event sink, recorder, and diagnostics formatter, then instrument the existing DataAgent route, planner, validation, SQL safety, execute, evidence, checkpoint, summarize, end, and reject boundaries. Bridge formatted progress diagnostics through `XmlFunctionCaller` into QChat recent diagnostics, keeping DataAgent independent from QChat and keeping all owner command output redacted.

**Tech Stack:** C#/.NET 9, existing DataAgent orchestration and store boundary, existing QChat diagnostics/cache commands, NUnit, existing PowerShell readiness gates.

---

## Scope Lock

V2.9 must do:

- Emit in-process progress events as DataAgent analysis advances.
- Keep events in memory only, with TTL and capacity limits.
- Make progress available through owner-only QChat diagnostics commands.
- Preserve V2.8 trace timeline diagnostics and reuse its redaction posture.
- Add required readiness and engineering-map gates.

V2.9 must not do:

- No PostgreSQL audit persistence for progress events.
- No frontend viewer.
- No websocket or browser streaming UI.
- No LangGraph.
- No broad natural-language command replacement yet.
- No extra SQL execution path.
- No diagnostics-triggered model call, XML tool call, or SQL query.
- No DataAgent project reference to QChat.
- No raw SQL, hidden context, evidence-pack tags, tool-route context, bearer/API tokens, or connection strings in diagnostics.

## Recommended Approach

Use **Approach B: backend progress sink plus QChat owner diagnostics bridge**.

Approach A, "format progress only after the result returns", is faster but is not truly streaming; it would just be another trace formatter.

Approach B emits events from the existing runtime boundaries and publishes safe snapshots into the existing QChat diagnostics cache. It gives us real backend streaming semantics without UI complexity.

Approach C, "full push stream to frontend or QQ live messages", is powerful but belongs after V2.9. It adds delivery ordering, throttling, multi-client state, and user-facing UX risk before the backend contract is mature.

---

### Task 1: Progress Event Models and Recorder

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentProgressModels.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/IDataAgentProgressSink.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentProgressRecorder.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentProgressRecorderTests.cs`

- [ ] **Step 1: Write failing recorder tests**

Create `Tests/Alife.Test.DataAgent/DataAgentProgressRecorderTests.cs` with tests for:

```csharp
[Test]
public void PublishStoresDefensiveSnapshotsPerSession()
{
    Dictionary<string, string> facts = new()
    {
        ["rows"] = "3",
        ["sql"] = "redacted"
    };
    DataAgentProgressRecorder recorder = new(maxEventsPerSession: 4, ttl: TimeSpan.FromMinutes(10), maxEventsTotal: 8);
    DateTimeOffset now = new(2026, 7, 3, 10, 0, 0, TimeSpan.Zero);

    recorder.Publish(new DataAgentProgressEvent(
        "session-a",
        DataAgentProgressEventKind.Execute,
        DataAgentProgressEventPhase.Completed,
        DataAgentProgressEventStatus.Succeeded,
        "read_only_query_executed",
        TurnCount: 1,
        now,
        ExecutedSql: true,
        QueryAllowed: true,
        Terminal: false,
        facts));
    facts["rows"] = "999";

    IReadOnlyList<DataAgentProgressEvent> recent = recorder.GetRecent("session-a", now);

    Assert.That(recent, Has.Count.EqualTo(1));
    Assert.That(recent[0].Facts["rows"], Is.EqualTo("3"));
    Assert.Throws<NotSupportedException>(() =>
        ((IDictionary<string, string>)recent[0].Facts)["rows"] = "mutated");
}

[Test]
public void GetRecentFiltersBySessionTtlAndCapacity()
{
    DataAgentProgressRecorder recorder = new(maxEventsPerSession: 2, ttl: TimeSpan.FromMinutes(5), maxEventsTotal: 10);
    DateTimeOffset now = new(2026, 7, 3, 10, 0, 0, TimeSpan.Zero);

    recorder.Publish(Event("session-a", DataAgentProgressEventKind.RouteGate, now.AddMinutes(-6)));
    recorder.Publish(Event("session-a", DataAgentProgressEventKind.Planner, now.AddMinutes(-2)));
    recorder.Publish(Event("session-a", DataAgentProgressEventKind.Execute, now.AddMinutes(-1)));
    recorder.Publish(Event("session-b", DataAgentProgressEventKind.RouteGate, now.AddMinutes(-1)));

    IReadOnlyList<DataAgentProgressEvent> recent = recorder.GetRecent("session-a", now);

    Assert.That(recent.Select(item => item.Kind), Is.EqualTo(new[]
    {
        DataAgentProgressEventKind.Planner,
        DataAgentProgressEventKind.Execute
    }));
}
```

Add a local helper:

```csharp
static DataAgentProgressEvent Event(string sessionId, DataAgentProgressEventKind kind, DateTimeOffset at)
{
    return new DataAgentProgressEvent(
        sessionId,
        kind,
        DataAgentProgressEventPhase.Completed,
        DataAgentProgressEventStatus.Succeeded,
        "ok",
        TurnCount: 1,
        at,
        ExecutedSql: kind == DataAgentProgressEventKind.Execute,
        QueryAllowed: true,
        Terminal: false,
        new Dictionary<string, string>());
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentProgressRecorderTests" -v:minimal
```

Expected: compile failure because progress models and recorder do not exist.

- [ ] **Step 3: Add progress models**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentProgressModels.cs`:

```csharp
namespace Alife.Function.DataAgent;

public enum DataAgentProgressEventKind
{
    RouteGate,
    SchemaContext,
    Planner,
    Validate,
    SqlSafety,
    Execute,
    EvidencePack,
    Checkpoint,
    Summarize,
    End,
    Answer,
    Reject,
    Explain,
    Clarification
}

public enum DataAgentProgressEventPhase
{
    Started,
    Completed
}

public enum DataAgentProgressEventStatus
{
    Running,
    Succeeded,
    Skipped,
    Rejected,
    Failed
}

public sealed record DataAgentProgressEvent(
    string SessionId,
    DataAgentProgressEventKind Kind,
    DataAgentProgressEventPhase Phase,
    DataAgentProgressEventStatus Status,
    string ReasonCode,
    int TurnCount,
    DateTimeOffset CreatedAt,
    bool ExecutedSql,
    bool QueryAllowed,
    bool Terminal,
    IReadOnlyDictionary<string, string> Facts);
```

- [ ] **Step 4: Add progress sink interface**

Create `sources/Alife.Function/Alife.Function.DataAgent/IDataAgentProgressSink.cs`:

```csharp
namespace Alife.Function.DataAgent;

public interface IDataAgentProgressSink
{
    void Publish(DataAgentProgressEvent? progressEvent);
}
```

- [ ] **Step 5: Add in-memory recorder**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentProgressRecorder.cs` using the same defensive snapshot pattern as `DataAgentTraceRecorder`:

```csharp
using System.Collections.ObjectModel;

namespace Alife.Function.DataAgent;

public sealed class DataAgentProgressRecorder : IDataAgentProgressSink
{
    readonly object gate = new();
    readonly int maxEventsPerSession;
    readonly int maxEventsTotal;
    readonly TimeSpan ttl;
    readonly List<DataAgentProgressRecord> events = [];
    long nextSequence;

    public DataAgentProgressRecorder(int maxEventsPerSession = 32, TimeSpan? ttl = null, int maxEventsTotal = 512)
    {
        this.maxEventsPerSession = Math.Max(1, maxEventsPerSession);
        this.maxEventsTotal = Math.Max(1, maxEventsTotal);
        this.ttl = ttl ?? TimeSpan.FromMinutes(30);
    }

    public void Publish(DataAgentProgressEvent? progressEvent)
    {
        if (progressEvent is null || string.IsNullOrWhiteSpace(progressEvent.SessionId))
            return;

        DataAgentProgressEvent normalized = Snapshot(progressEvent) with
        {
            SessionId = NormalizeToken(progressEvent.SessionId),
            ReasonCode = NormalizeToken(progressEvent.ReasonCode)
        };

        lock (gate)
        {
            PruneExpiredLocked(normalized.CreatedAt);
            events.Add(new DataAgentProgressRecord(normalized, nextSequence++));
            PruneCapacityLocked(normalized.SessionId);
            PruneGlobalCapacityLocked();
        }
    }

    public IReadOnlyList<DataAgentProgressEvent> GetRecent(string sessionId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return [];

        string normalizedSessionId = NormalizeToken(sessionId);
        lock (gate)
        {
            return events
                .Where(record => IsExpired(record, now) == false &&
                                 string.Equals(record.Event.SessionId, normalizedSessionId, StringComparison.Ordinal))
                .OrderBy(record => record.Event.CreatedAt)
                .ThenBy(record => record.Sequence)
                .Select(record => Snapshot(record.Event))
                .ToArray();
        }
    }

    public DataAgentProgressEvent? GetLatest(string sessionId, DateTimeOffset now)
    {
        return GetRecent(sessionId, now).LastOrDefault();
    }

    void PruneExpiredLocked(DateTimeOffset now)
    {
        events.RemoveAll(record => IsExpired(record, now));
    }

    bool IsExpired(DataAgentProgressRecord record, DateTimeOffset now)
    {
        return now - record.Event.CreatedAt > ttl;
    }

    void PruneCapacityLocked(string sessionId)
    {
        List<DataAgentProgressRecord> sessionEvents = events
            .Where(record => string.Equals(record.Event.SessionId, sessionId, StringComparison.Ordinal))
            .OrderBy(record => record.Event.CreatedAt)
            .ThenBy(record => record.Sequence)
            .ToList();

        int excess = sessionEvents.Count - maxEventsPerSession;
        if (excess <= 0)
            return;

        foreach (DataAgentProgressRecord record in sessionEvents.Take(excess))
            events.Remove(record);
    }

    void PruneGlobalCapacityLocked()
    {
        int excess = events.Count - maxEventsTotal;
        if (excess <= 0)
            return;

        foreach (DataAgentProgressRecord record in events
                     .OrderBy(item => item.Event.CreatedAt)
                     .ThenBy(item => item.Sequence)
                     .Take(excess)
                     .ToArray())
        {
            events.Remove(record);
        }
    }

    static DataAgentProgressEvent Snapshot(DataAgentProgressEvent progressEvent)
    {
        return progressEvent with
        {
            Facts = new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(progressEvent.Facts))
        };
    }

    static string NormalizeToken(string value)
    {
        return value.ReplaceLineEndings(" ").Replace(';', ',').Trim();
    }

    sealed class DataAgentProgressRecord(DataAgentProgressEvent progressEvent, long sequence)
    {
        public DataAgentProgressEvent Event { get; } = progressEvent;

        public long Sequence { get; } = sequence;
    }
}
```

- [ ] **Step 6: Verify recorder tests pass**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentProgressRecorderTests" -v:minimal
```

Expected: all `DataAgentProgressRecorderTests` pass.

- [ ] **Step 7: Commit**

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentProgressModels.cs sources/Alife.Function/Alife.Function.DataAgent/IDataAgentProgressSink.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentProgressRecorder.cs Tests/Alife.Test.DataAgent/DataAgentProgressRecorderTests.cs
git commit -m "Add DataAgent progress recorder"
```

---

### Task 2: Progress Diagnostics Formatter and Redaction

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentProgressDiagnosticsFormatter.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentProgressDiagnosticsFormatterTests.cs`

- [ ] **Step 1: Write failing formatter tests**

Create tests proving:

- Empty progress returns stable unavailable text.
- Safe progress lists recent events in order.
- Raw SQL and SQL fragments are never exposed.
- `data_agent_evidence_pack`, `tool_route_context`, `Allowed XML tools`, hidden context, bearer/API tokens, connection strings, and `sk-...` tokens are redacted.
- Output length is bounded.

Use this representative unsafe input:

```csharp
DataAgentProgressEvent unsafeEvent = new(
    "session-a",
    DataAgentProgressEventKind.Execute,
    DataAgentProgressEventPhase.Completed,
    DataAgentProgressEventStatus.Succeeded,
    "read_only_query_executed",
    1,
    new DateTimeOffset(2026, 7, 3, 10, 0, 0, TimeSpan.Zero),
    ExecutedSql: true,
    QueryAllowed: true,
    Terminal: false,
    new Dictionary<string, string>
    {
        ["sql"] = "SELECT * FROM engineering_gate WHERE required = 1",
        ["hidden_context"] = "[hidden_context]secret[/hidden_context]",
        ["tool_route_context"] = "Allowed XML tools for this turn: dataagent_query",
        ["token"] = "Bearer sk-test1234567890",
        ["connection_string"] = "Host=localhost;Username=postgres;Password=secret"
    });
```

Assert:

```csharp
string text = DataAgentProgressDiagnosticsFormatter.Format([unsafeEvent], "session-a", unsafeEvent.CreatedAt);

Assert.That(text, Does.Contain("DataAgent progress diagnostics"));
Assert.That(text, Does.Contain("sql=redacted"));
Assert.That(text, Does.Not.Contain("SELECT"));
Assert.That(text, Does.Not.Contain("engineering_gate"));
Assert.That(text, Does.Not.Contain("hidden_context"));
Assert.That(text, Does.Not.Contain("Allowed XML tools"));
Assert.That(text, Does.Not.Contain("sk-test"));
Assert.That(text, Does.Not.Contain("Password=secret"));
```

- [ ] **Step 2: Run tests and verify failure**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentProgressDiagnosticsFormatterTests" -v:minimal
```

Expected: compile failure because formatter does not exist.

- [ ] **Step 3: Implement formatter**

Create `DataAgentProgressDiagnosticsFormatter`:

```csharp
using System.Text;
using System.Text.RegularExpressions;

namespace Alife.Function.DataAgent;

public static partial class DataAgentProgressDiagnosticsFormatter
{
    const int MaxEvents = 16;
    const int MaxChars = 1800;

    public static string Format(
        IReadOnlyList<DataAgentProgressEvent>? events,
        string sessionId,
        DateTimeOffset now)
    {
        if (events is null || events.Count == 0)
        {
            return string.Join(Environment.NewLine,
                "DataAgent progress diagnostics",
                "state=unavailable",
                "reason=progress_unavailable");
        }

        StringBuilder builder = new();
        builder.AppendLine("DataAgent progress diagnostics");
        builder.AppendLine($"session={SanitizeScalar(sessionId)}");
        builder.AppendLine($"events={events.Count}");
        builder.AppendLine($"now={now:O}");

        foreach (DataAgentProgressEvent progressEvent in events.TakeLast(MaxEvents))
        {
            builder.Append(progressEvent.CreatedAt.ToString("O"));
            builder.Append(' ');
            builder.Append(progressEvent.Kind);
            builder.Append(':');
            builder.Append(progressEvent.Phase);
            builder.Append(':');
            builder.Append(progressEvent.Status);
            builder.Append(" reason=");
            builder.Append(SanitizeReason(progressEvent.ReasonCode));
            builder.Append(" sql=");
            builder.Append(progressEvent.ExecutedSql ? "redacted" : "not_executed");
            builder.Append(" query_allowed=");
            builder.Append(progressEvent.QueryAllowed ? "true" : "false");
            builder.Append(" terminal=");
            builder.Append(progressEvent.Terminal ? "true" : "false");
            AppendFacts(builder, progressEvent.Facts);
            builder.AppendLine();
        }

        return Bound(builder.ToString().TrimEnd());
    }

    static void AppendFacts(StringBuilder builder, IReadOnlyDictionary<string, string> facts)
    {
        foreach ((string key, string value) in facts.OrderBy(item => item.Key, StringComparer.Ordinal).Take(8))
        {
            string safeKey = SanitizeFactKey(key);
            if (string.IsNullOrWhiteSpace(safeKey))
                continue;

            string safeValue = string.Equals(safeKey, "sql", StringComparison.OrdinalIgnoreCase)
                ? "redacted"
                : SanitizeScalar(value);
            builder.Append(' ');
            builder.Append(safeKey);
            builder.Append('=');
            builder.Append(safeValue);
        }
    }

    static string SanitizeFactKey(string key)
    {
        string normalized = SanitizeScalar(key).ToLowerInvariant();
        if (normalized.Contains("secret", StringComparison.Ordinal) ||
            normalized.Contains("token", StringComparison.Ordinal) ||
            normalized.Contains("password", StringComparison.Ordinal) ||
            normalized.Contains("connection", StringComparison.Ordinal) ||
            normalized.Contains("hidden", StringComparison.Ordinal) ||
            normalized.Contains("tool_route", StringComparison.Ordinal) ||
            normalized.Contains("evidence_pack", StringComparison.Ordinal) ||
            normalized.Contains("dataset", StringComparison.Ordinal) ||
            normalized.Contains("table", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return normalized;
    }

    static string SanitizeReason(string value)
    {
        string sanitized = SanitizeScalar(value);
        int separator = sanitized.IndexOf(':', StringComparison.Ordinal);
        return separator >= 0 ? sanitized[..separator] : sanitized;
    }

    static string SanitizeScalar(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "empty";

        string sanitized = value.ReplaceLineEndings(" ").Replace(';', ',').Trim();
        sanitized = HiddenContextPattern().Replace(sanitized, "redacted");
        sanitized = AllowedXmlToolPattern().Replace(sanitized, "redacted");
        sanitized = SqlFragmentPattern().Replace(sanitized, "redacted");
        sanitized = BearerPattern().Replace(sanitized, "Bearer redacted");
        sanitized = SkTokenPattern().Replace(sanitized, "sk-redacted");
        sanitized = ConnectionStringPattern().Replace(sanitized, "connection_string=redacted");
        sanitized = sanitized.Replace("data_agent_evidence_pack", "redacted", StringComparison.OrdinalIgnoreCase);
        sanitized = sanitized.Replace("tool_route_context", "redacted", StringComparison.OrdinalIgnoreCase);
        return string.IsNullOrWhiteSpace(sanitized) ? "redacted" : sanitized;
    }

    static string Bound(string text)
    {
        return text.Length <= MaxChars ? text : text[..MaxChars];
    }

    [GeneratedRegex(@"(?is)\[hidden_context\].*?\[/hidden_context\]|hidden\s+context\s*:\s*\S+")]
    private static partial Regex HiddenContextPattern();

    [GeneratedRegex(@"(?is)Allowed XML tools.*")]
    private static partial Regex AllowedXmlToolPattern();

    [GeneratedRegex(@"(?is)\b(select|from|join|where|having|order\s+by|group\s+by|limit)\b.+")]
    private static partial Regex SqlFragmentPattern();

    [GeneratedRegex(@"(?i)\bBearer\s+[A-Za-z0-9._\-]+")]
    private static partial Regex BearerPattern();

    [GeneratedRegex(@"(?i)\bsk-[A-Za-z0-9_\-]{8,}")]
    private static partial Regex SkTokenPattern();

    [GeneratedRegex(@"(?i)\b(Host|Server|Data Source|Username|User ID|Password|Pwd)\s*=\s*[^,;\s]+")]
    private static partial Regex ConnectionStringPattern();
}
```

During implementation, prefer sharing sanitizer helpers with `DataAgentTraceDiagnosticsFormatter` if the local code shape makes that smaller and safer. Keep the public formatter contract as above.

- [ ] **Step 4: Verify formatter tests pass**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentProgressDiagnosticsFormatterTests" -v:minimal
```

Expected: all formatter tests pass.

- [ ] **Step 5: Commit**

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentProgressDiagnosticsFormatter.cs Tests/Alife.Test.DataAgent/DataAgentProgressDiagnosticsFormatterTests.cs
git commit -m "Add DataAgent progress diagnostics formatter"
```

---

### Task 3: Instrument DataAgent Runtime Boundaries

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentService.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisService.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisOrchestrator.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentProgressStreamingTests.cs`

- [ ] **Step 1: Write failing runtime progress tests**

Create tests proving:

- Route rejection publishes `RouteGate:Completed:Rejected` and `Reject:Completed:Rejected`.
- Accepted query publishes `RouteGate`, `SchemaContext`, `Planner`, `Validate`, `SqlSafety`, `Execute`, `Explain`, and `Checkpoint`.
- Clarification publishes `Planner`, `Validate:Skipped`, and `Clarification`.
- Summarize and end publish terminal progress and do not publish `Execute`.
- Instrumentation does not call the answer delegate twice.

Core assertion shape:

```csharp
DataAgentProgressRecorder progress = new();
int answerCalls = 0;
InMemoryDataAgentAnalysisSessionStore store = new();
DataAgentAnalysisService analysisService = new(
    question =>
    {
        answerCalls++;
        return AcceptedAnswer();
    },
    store,
    progressSink: progress,
    clock: () => Now);
DataAgentAnalysisOrchestrator orchestrator = new(analysisService, store, progress);

DataAgentOrchestrationResult result = orchestrator.Start(new DataAgentOrchestrationRequest(
    "owner",
    "Which required gates are passing?",
    null,
    RouteAllowsQuery: true,
    RouteContext: RouteAllowed("dataagent_analysis_start", null)));

IReadOnlyList<DataAgentProgressEvent> events = progress.GetRecent(result.SessionId, Now);

Assert.That(answerCalls, Is.EqualTo(1));
Assert.That(events.Select(item => item.Kind), Does.Contain(DataAgentProgressEventKind.Execute));
Assert.That(events.Any(item => item.Kind == DataAgentProgressEventKind.Execute && item.ExecutedSql), Is.True);
Assert.That(events.Any(item => item.Kind == DataAgentProgressEventKind.Checkpoint), Is.True);
```

- [ ] **Step 2: Run tests and verify failure**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentProgressStreamingTests" -v:minimal
```

Expected: compile failure because constructors and instrumentation do not exist.

- [ ] **Step 3: Add optional progress support to `DataAgentAnalysisService`**

Preserve existing constructors. Add optional progress sink and clock parameters:

```csharp
readonly IDataAgentProgressSink? progressSink;

public DataAgentAnalysisService(
    Func<string, DataAgentAnswer> answer,
    IDataAgentAnalysisSessionStore store,
    DataAgentFollowUpInterpreter? followUpInterpreter = null,
    Func<DateTimeOffset>? clock = null,
    IDataAgentProgressSink? progressSink = null)
{
    ...
    this.progressSink = progressSink;
}
```

For the constructor that receives `DataAgentService`, route through a new `Answer` overload:

```csharp
public DataAgentAnalysisService(
    DataAgentService dataAgentService,
    IDataAgentAnalysisSessionStore store,
    IDataAgentProgressSink? progressSink = null)
    : this(question => dataAgentService.Answer(question), store, new DataAgentFollowUpInterpreter(), () => DateTimeOffset.UtcNow, progressSink)
{
}
```

Emit `Checkpoint`, `Summarize`, `End`, and `Reject` progress events inside session-level methods, because those boundaries are owned here.

- [ ] **Step 4: Add optional progress support to `DataAgentService`**

Keep existing behavior unchanged when no sink is present. Add an overload:

```csharp
public DataAgentAnswer Answer(string question, IDataAgentProgressSink? progressSink, string sessionId, int turnCount, Func<DateTimeOffset> clock)
```

Have `Answer(string question)` call the new overload with a null sink:

```csharp
public DataAgentAnswer Answer(string question)
{
    return Answer(question, null, string.Empty, 0, () => DateTimeOffset.UtcNow);
}
```

Emit events around the existing boundaries:

```csharp
Publish(progressSink, sessionId, DataAgentProgressEventKind.Planner, DataAgentProgressEventPhase.Started, DataAgentProgressEventStatus.Running, "planner_started", turnCount, clock(), false, true, false);
DataAgentQueryPlanEnvelope envelope = ValidateEnvelope(planner.Plan(...));
Publish(progressSink, sessionId, DataAgentProgressEventKind.Planner, DataAgentProgressEventPhase.Completed, DataAgentProgressEventStatus.Succeeded, "planner_response_received", turnCount, clock(), false, true, false);
```

For rejected validation:

```csharp
Publish(progressSink, sessionId, DataAgentProgressEventKind.Validate, DataAgentProgressEventPhase.Completed, DataAgentProgressEventStatus.Rejected, reason, turnCount, clock(), false, true, false);
Publish(progressSink, sessionId, DataAgentProgressEventKind.Reject, DataAgentProgressEventPhase.Completed, DataAgentProgressEventStatus.Rejected, reason, turnCount, clock(), false, true, false);
```

For safe execution:

```csharp
Publish(progressSink, sessionId, DataAgentProgressEventKind.SqlSafety, DataAgentProgressEventPhase.Completed, DataAgentProgressEventStatus.Succeeded, "read_only_sql_safe", turnCount, clock(), false, true, false);
Publish(progressSink, sessionId, DataAgentProgressEventKind.Execute, DataAgentProgressEventPhase.Started, DataAgentProgressEventStatus.Running, "execute_started", turnCount, clock(), false, true, false);
DataAgentQueryResult result = store.Query(compiled);
Publish(progressSink, sessionId, DataAgentProgressEventKind.Execute, DataAgentProgressEventPhase.Completed, DataAgentProgressEventStatus.Succeeded, "read_only_query_executed", turnCount, clock(), true, true, false, new Dictionary<string, string> { ["rows"] = result.Rows.Count.ToString(), ["sql"] = "redacted" });
```

Do not publish raw SQL, plan JSON, raw question text, dataset names, or connection strings in facts.

- [ ] **Step 5: Add route-level progress support to `DataAgentAnalysisOrchestrator`**

Add optional sink and clock:

```csharp
readonly IDataAgentProgressSink? progressSink;
readonly Func<DateTimeOffset> progressClock;

public DataAgentAnalysisOrchestrator(
    DataAgentAnalysisService analysisService,
    IDataAgentAnalysisSessionStore sessionStore,
    DataAgentFollowUpInterpreter? followUpInterpreter = null,
    IDataAgentProgressSink? progressSink = null,
    Func<DateTimeOffset>? progressClock = null)
{
    ...
    this.progressSink = progressSink;
    this.progressClock = progressClock ?? (() => DateTimeOffset.UtcNow);
}
```

Emit route-level events:

```csharp
PublishProgress(request.SessionId ?? "pending", DataAgentProgressEventKind.RouteGate, DataAgentProgressEventPhase.Started, DataAgentProgressEventStatus.Running, "route_check_started", 0, false, request.RouteAllowsQuery, false);
PublishProgress(request.SessionId ?? "pending", DataAgentProgressEventKind.RouteGate, DataAgentProgressEventPhase.Completed, DataAgentProgressEventStatus.Succeeded, "route_allowed", 0, false, true, false);
PublishProgress(request.SessionId ?? "pending", DataAgentProgressEventKind.SchemaContext, DataAgentProgressEventPhase.Completed, DataAgentProgressEventStatus.Succeeded, "dataagent_catalog_available", 0, false, true, false);
```

For route denied, publish `RouteGate` and `Reject` with `QueryAllowed=false`, `ExecutedSql=false`, and no unsafe facts.

After `BuildResult`, publish checkpoint using the real response session id. If implementation shows duplicate checkpoint events from `DataAgentAnalysisService`, keep exactly one completed checkpoint event per returned orchestration.

- [ ] **Step 6: Verify runtime tests pass**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentProgressStreamingTests" -v:minimal
```

Expected: all progress streaming tests pass, and answer delegate call count remains 1.

- [ ] **Step 7: Run existing V2.8 trace tests**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentTraceRecorderTests|FullyQualifiedName~DataAgentTraceDiagnosticsFormatterTests|FullyQualifiedName~DataAgentAnalysisToolHandlerTests" -v:minimal
```

Expected: all pass; V2.9 must not regress trace timeline publishing.

- [ ] **Step 8: Commit**

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentService.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisService.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisOrchestrator.cs Tests/Alife.Test.DataAgent/DataAgentProgressStreamingTests.cs
git commit -m "Stream DataAgent progress events"
```

---

### Task 4: Bridge Progress Diagnostics Through FunctionCaller

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentProgressDiagnosticsPublisher.cs`
- Modify: `sources/Alife.Function/Alife.Function.FunctionCaller/XmlFunctionCaller.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentProgressDiagnosticsPublisherTests.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentAnalysisToolHandlerTests.cs`

- [ ] **Step 1: Write failing bridge tests**

Add tests proving:

- Publishing sink records progress and emits formatted diagnostics after each event.
- `XmlFunctionCaller.RecentDataAgentProgressDiagnostics` returns the latest normalized text.
- `DataAgentModuleService` wires a single progress recorder/publisher into DataAgent runtime.
- DataAgent still does not reference `Alife.Function.QChat`.

Core test shape:

```csharp
List<string> published = [];
DataAgentProgressRecorder recorder = new();
DataAgentProgressDiagnosticsPublisher publisher = new(
    recorder,
    text => published.Add(text),
    () => Now);

publisher.Publish(Event("session-a", DataAgentProgressEventKind.RouteGate, Now));

Assert.That(published, Has.Count.EqualTo(1));
Assert.That(published[0], Does.Contain("DataAgent progress diagnostics"));
Assert.That(published[0], Does.Contain("RouteGate"));
```

- [ ] **Step 2: Run tests and verify failure**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentProgressDiagnosticsPublisherTests|FullyQualifiedName~DataAgentAnalysisToolHandlerTests" -v:minimal
```

Expected: compile failure because publisher and FunctionCaller bridge do not exist.

- [ ] **Step 3: Implement `DataAgentProgressDiagnosticsPublisher`**

Create a small sink wrapper:

```csharp
namespace Alife.Function.DataAgent;

public sealed class DataAgentProgressDiagnosticsPublisher(
    DataAgentProgressRecorder recorder,
    Action<string>? diagnosticsPublisher,
    Func<DateTimeOffset>? clock = null) : IDataAgentProgressSink
{
    readonly Func<DateTimeOffset> clock = clock ?? (() => DateTimeOffset.UtcNow);

    public void Publish(DataAgentProgressEvent? progressEvent)
    {
        if (progressEvent is null)
            return;

        recorder.Publish(progressEvent);
        if (diagnosticsPublisher is null)
            return;

        DateTimeOffset now = clock();
        IReadOnlyList<DataAgentProgressEvent> recent = recorder.GetRecent(progressEvent.SessionId, now);
        diagnosticsPublisher(DataAgentProgressDiagnosticsFormatter.Format(recent, progressEvent.SessionId, now));
    }
}
```

- [ ] **Step 4: Add FunctionCaller progress storage**

In `sources/Alife.Function/Alife.Function.FunctionCaller/XmlFunctionCaller.cs`, mirror the trace diagnostics bridge:

```csharp
readonly object dataAgentProgressDiagnosticsGate = new();
string recentDataAgentProgressDiagnostics = string.Empty;

public string RecentDataAgentProgressDiagnostics
{
    get
    {
        lock (dataAgentProgressDiagnosticsGate)
        {
            return recentDataAgentProgressDiagnostics;
        }
    }
}

public void RecordRecentDataAgentProgressDiagnostics(string? diagnostics)
{
    string normalized = string.IsNullOrWhiteSpace(diagnostics)
        ? string.Empty
        : diagnostics.ReplaceLineEndings("\n").Trim();

    lock (dataAgentProgressDiagnosticsGate)
    {
        recentDataAgentProgressDiagnostics = normalized;
    }
}
```

- [ ] **Step 5: Wire DataAgent module**

In `DataAgentModuleService.AwakeAsync`, create:

```csharp
DataAgentProgressRecorder progressRecorder = new();
IDataAgentProgressSink progressSink = new DataAgentProgressDiagnosticsPublisher(
    progressRecorder,
    functionService.RecordRecentDataAgentProgressDiagnostics);
```

Pass `progressSink` into `DataAgentAnalysisService` and `DataAgentAnalysisOrchestrator`. Keep existing trace recorder wiring unchanged.

- [ ] **Step 6: Verify bridge tests pass**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentProgressDiagnosticsPublisherTests|FullyQualifiedName~DataAgentAnalysisToolHandlerTests" -v:minimal
```

Expected: all pass.

- [ ] **Step 7: Commit**

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentProgressDiagnosticsPublisher.cs sources/Alife.Function/Alife.Function.FunctionCaller/XmlFunctionCaller.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs Tests/Alife.Test.DataAgent/DataAgentProgressDiagnosticsPublisherTests.cs Tests/Alife.Test.DataAgent/DataAgentAnalysisToolHandlerTests.cs
git commit -m "Publish DataAgent progress diagnostics"
```

---

### Task 5: QChat Owner Diagnostics Commands and Recent Cache

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatRecentDiagnosticsCache.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatRecentDiagnosticsFormatter.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatOwnerCommandService.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
- Test: `Tests/Alife.Test.QChat/QChatRecentDiagnosticsCacheTests.cs`
- Test: `Tests/Alife.Test.QChat/QChatDiagnosticsServiceTests.cs`
- Test: `Tests/Alife.Test.QChat/QChatOwnerCommandServiceTests.cs`
- Test: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`

- [ ] **Step 1: Write failing QChat tests**

Add tests proving:

- `QChatRecentDiagnosticKind.DataAgentProgress` is accepted and appears in recent summary as `dataagent_progress_recent`.
- `/dataagent diag progress` and `/dataagent diagnostics progress` return cached progress.
- `/qchat diag dataagent progress` and `/qchat diagnostics dataagent progress` return cached progress.
- If no progress exists, output is:

```text
DataAgent progress diagnostics
state=unavailable
reason=progress_unavailable
```

- Non-owner command handling exits before invoking diagnostics callbacks.
- Unsafe legacy fallback text is sanitized before display.
- Recent progress cache allows 1800 chars like trace diagnostics.

- [ ] **Step 2: Run tests and verify failure**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatRecentDiagnosticsCacheTests|FullyQualifiedName~QChatDiagnosticsServiceTests|FullyQualifiedName~QChatOwnerCommandServiceTests|FullyQualifiedName~QChatServiceAdapterTests" -v:minimal
```

Expected: compile or behavior failures because DataAgent progress diagnostics are not exposed.

- [ ] **Step 3: Extend recent diagnostics kind**

Add `DataAgentProgress` to `QChatRecentDiagnosticKind`:

```csharp
public enum QChatRecentDiagnosticKind
{
    SemanticState,
    DataAgentEvidence,
    DataAgentTrace,
    DataAgentProgress,
    ToolRoute
}
```

Use the same 1800-character cap as trace:

```csharp
static int GetMaxTextChars(QChatRecentDiagnosticKind kind)
{
    return kind is QChatRecentDiagnosticKind.DataAgentTrace or QChatRecentDiagnosticKind.DataAgentProgress
        ? DataAgentTraceMaxTextChars
        : MaxTextChars;
}
```

- [ ] **Step 4: Extend formatter titles and summary**

In `QChatRecentDiagnosticsFormatter`, add:

```csharp
QChatRecentDiagnosticKind.DataAgentProgress => "DataAgent progress diagnostics"
```

Add summary marker:

```text
dataagent_progress_recent=...
```

- [ ] **Step 5: Extend diagnostics runtime state**

Add:

```csharp
string? RecentDataAgentProgress = null
```

to `QChatDiagnosticsRuntimeState`.

Add command cases:

```csharp
"diag progress" or "diagnostics progress" => Handled(BuildDataAgentProgressDiagnosticsText(runtimeState, route))
"diag dataagent progress" or "diagnostics dataagent progress" => Handled(BuildDataAgentProgressDiagnosticsText(runtimeState, route))
```

Implement:

```csharp
static string BuildDataAgentProgressDiagnosticsText(QChatDiagnosticsRuntimeState runtimeState, QChatAgentRoute route)
{
    string? cached = GetRecentCachedText(runtimeState, route, QChatRecentDiagnosticKind.DataAgentProgress);
    if (string.IsNullOrWhiteSpace(cached) == false)
        return cached;

    string sanitized = SanitizeDiagnosticText(
        runtimeState.RecentDataAgentProgress,
        "DataAgent progress diagnostics",
        maxChars: 1800);
    return string.IsNullOrWhiteSpace(sanitized)
        ? string.Join(Environment.NewLine,
            "DataAgent progress diagnostics",
            "state=unavailable",
            "reason=progress_unavailable")
        : sanitized;
}
```

- [ ] **Step 6: Sync FunctionCaller fallback into cache**

In `QChatService`, wherever trace diagnostics are synced from `XmlFunctionCaller.RecentDataAgentTraceDiagnostics`, mirror it for `RecentDataAgentProgressDiagnostics`.

Record with:

```csharp
recentDiagnosticsCache.Record(
    QChatRecentDiagnosticKind.DataAgentProgress,
    sessionKey,
    "function_caller",
    functionCaller.RecentDataAgentProgressDiagnostics,
    now);
```

Make sure later FunctionCaller progress diagnostics replace earlier fallback-synced progress in `/qchat diag recent`.

- [ ] **Step 7: Verify QChat tests pass**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatRecentDiagnosticsCacheTests|FullyQualifiedName~QChatDiagnosticsServiceTests|FullyQualifiedName~QChatOwnerCommandServiceTests|FullyQualifiedName~QChatServiceAdapterTests" -v:minimal
```

Expected: all focused QChat diagnostics tests pass.

- [ ] **Step 8: Commit**

```powershell
git add sources/Alife.Function/Alife.Function.QChat/QChatRecentDiagnosticsCache.cs sources/Alife.Function/Alife.Function.QChat/QChatRecentDiagnosticsFormatter.cs sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs sources/Alife.Function/Alife.Function.QChat/QChatOwnerCommandService.cs sources/Alife.Function/Alife.Function.QChat/QChatService.cs Tests/Alife.Test.QChat/QChatRecentDiagnosticsCacheTests.cs Tests/Alife.Test.QChat/QChatDiagnosticsServiceTests.cs Tests/Alife.Test.QChat/QChatOwnerCommandServiceTests.cs Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs
git commit -m "Expose DataAgent progress diagnostics in QChat"
```

---

### Task 6: Readiness Gates and Engineering Map

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
- Modify: `tools/check-dataagent-readiness.ps1`
- Modify: `tools/check-qchat-engineering-map.ps1`
- Test: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
- Test: `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`

- [ ] **Step 1: Write failing readiness tests**

Add tests proving:

- `DataAgentReadiness.CheckCore(...)` includes `DataAgentProgressStreamingPresent`.
- The check validates structural event presence: `RouteGate`, `Planner`, `Execute`, and `Checkpoint`.
- The check validates owner diagnostics text contains `DataAgent progress diagnostics`.
- The check validates redaction: SQL redacted, hidden context absent, evidence-pack tags absent, tool-route context absent.
- `check-dataagent-readiness.ps1` expected required count increases from `75` to `76`.
- `check-qchat-engineering-map.ps1` expected required count increases from `51` to `52`.
- QChat engineering map has a required `DataAgent progress diagnostics` gate.

- [ ] **Step 2: Run tests and verify failure**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests" -v:minimal
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatEngineeringMapRequiredV2Tests" -v:minimal
```

Expected: failures because required counts and checks are not yet updated.

- [ ] **Step 3: Add DataAgent readiness check**

In `DataAgentReadiness.CheckCore`, create a runtime progress recorder and progress publisher, run an accepted analysis, format progress diagnostics, and add:

```csharp
checks.Add(progressStreamingReady
    ? Pass("DataAgentProgressStreamingPresent", "progress_stream=true;owner_diag=true;sql_redacted=true;hidden_context_redacted=true;evidence_pack_redacted=true;tool_route_redacted=true")
    : Fail("DataAgentProgressStreamingPresent", progressFailureDetail));
```

Do not include raw diagnostic text in failure detail. Use structural booleans only.

- [ ] **Step 4: Update readiness scripts**

In `tools/check-dataagent-readiness.ps1`, update:

```powershell
$expectedRequired = 76
```

In `tools/check-qchat-engineering-map.ps1`, update:

```powershell
$expectedRequired = 52
```

Add required engineering-map marker:

```text
DataAgent progress diagnostics
```

- [ ] **Step 5: Verify gates pass**

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected:

```text
DataAgent: 76 required passed, 0 required missing
QChat map: 52 required passed, 0 required missing
```

- [ ] **Step 6: Commit**

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs tools/check-dataagent-readiness.ps1 tools/check-qchat-engineering-map.ps1 Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs
git commit -m "Require DataAgent progress streaming readiness"
```

---

### Task 7: Final Verification and Merge

**Files:**
- Verify all touched files.

- [ ] **Step 1: Run focused DataAgent tests**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentProgressRecorderTests|FullyQualifiedName~DataAgentProgressDiagnosticsFormatterTests|FullyQualifiedName~DataAgentProgressStreamingTests|FullyQualifiedName~DataAgentProgressDiagnosticsPublisherTests|FullyQualifiedName~DataAgentAnalysisToolHandlerTests|FullyQualifiedName~DataAgentReadinessTests" -v:minimal
```

Expected: 0 failed.

- [ ] **Step 2: Run focused QChat tests**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatRecentDiagnosticsCacheTests|FullyQualifiedName~QChatDiagnosticsServiceTests|FullyQualifiedName~QChatOwnerCommandServiceTests|FullyQualifiedName~QChatServiceAdapterTests|FullyQualifiedName~QChatEngineeringMapRequiredV2Tests" -v:minimal
```

Expected: 0 failed.

- [ ] **Step 3: Run readiness gates**

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected:

```text
76 required passed, 0 required missing
52 required passed, 0 required missing
```

- [ ] **Step 4: Run full solution**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore -v:minimal -m:1
```

Expected: 0 failed. Existing live/env-gated skips are acceptable.

- [ ] **Step 5: Run whitespace check and inspect status**

```powershell
git diff --check
git status --short --branch
```

Expected: `git diff --check` has no output; branch contains only V2.9 commits.

- [ ] **Step 6: Request review**

Dispatch a review against the V2.9 branch with focus on:

- progress events are genuinely emitted during runtime boundaries,
- diagnostics never trigger model/tool/SQL work,
- DataAgent does not reference QChat,
- QChat owner gating remains fail-closed,
- redaction protects SQL fragments, hidden context, evidence-pack tags, tool-route context, tokens, and connection strings.

- [ ] **Step 7: Merge and push after review approval**

After verification and review approval:

```powershell
git checkout master
git merge dataagent-v2.9-progress-streaming
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore -v:minimal -m:1
git push alife-byastralfox master
git ls-remote alife-byastralfox refs/heads/master
```

Do not use `D:\FOXD`. Push only to:

```text
git@github.com:hushu1232/Alife-byastralfox.git
```

---

## Future Roadmap: Natural Semantic Operation Layer

The project should evolve toward natural semantic operation triggering, where the owner does not need to remember exact commands such as `/dataagent diag progress`, `/qchat diag recent`, or future DataAgent operation commands. This is a future layer on top of the V2.9 progress stream, not part of the V2.9 completion boundary.

Target versioning:

- **V2.9:** Progress stream contract and owner diagnostics remain explicit-command first.
- **V2.10:** Owner-only semantic diagnostics router for low-risk read-only commands.
- **V3:** Confirmable semantic operation router for state-changing or high-risk actions.
- **V3+ with LangGraph:** Multi-agent semantic operation planning after the command/router safety contract is stable.

The recommended design is a two-stage semantic router:

1. **Intent candidate stage:** classify natural language into a small set of known operation intents, such as `diagnostics_recent`, `dataagent_progress`, `dataagent_trace`, `dataagent_evidence`, `toolbroker_diagnostics`, or `semantic_state`.
2. **Execution gate stage:** require route permissions, owner/private scope, confidence thresholds, ambiguity checks, and risk policy before dispatching the existing command handler.

The router must call existing command handlers rather than bypass them. For example, "show me where DataAgent is now" may resolve to the same safe path as `/dataagent diag progress`; it must not directly read internal objects, execute SQL, call XML tools, or call the model.

False-positive reduction strategy:

- Use a closed intent set instead of open-ended tool selection.
- Keep command execution fail-closed when confidence is below threshold.
- Require owner-only scope for diagnostics and private owner confirmation for risky actions.
- Require two signals for automatic execution: semantic intent plus route/context eligibility.
- Treat ambiguous requests as clarification, not execution.
- Prefer read-only diagnostics in V2.10; delay mutating actions until V3.
- Log semantic decisions into recent diagnostics so false positives can be inspected.
- Add negative fixtures for common chatty phrases that must not trigger operations.

Required future gates:

- `QChatSemanticOperationRouterPresent`
- `NaturalDiagnosticsTriggerPresent`
- `SemanticOperationFalsePositiveFixturesPresent`
- `SemanticOperationOwnerGatePresent`
- `SemanticOperationNoBypassPresent`

Example future owner utterances:

```text
show me where DataAgent is now
replay the last DataAgent chain
what is the recent diagnostics state
check why the tool route was not allowed
summarize the current semantic window state
```

Example future non-trigger utterances:

```text
this progress sounds fine
what did you mean by chain earlier
can this be more automatic in the future
that tool route idea sounds useful
```

The key principle is: natural language may select an existing safe operation, but it must not create a new authority path.

---

## Acceptance Criteria

V2.9 is complete only when all of these are true:

- DataAgent emits in-memory progress events for live analysis runtime boundaries.
- QChat owner can read progress diagnostics via `/dataagent diag progress`.
- `/qchat diag recent` includes DataAgent progress availability.
- Diagnostics output is bounded and redacted.
- DataAgent has no QChat dependency.
- No diagnostics path triggers model, XML tool, or SQL work.
- DataAgent readiness has `DataAgentProgressStreamingPresent`.
- QChat engineering map has `DataAgent progress diagnostics`.
- Focused tests, readiness scripts, full solution tests, and `git diff --check` pass.

## Interview Framing

This module can be described as:

> I upgraded the DataAgent analysis chain from after-the-fact trace replay to a real-time backend progress stream. The stream emits structured events at route, planning, safety, execution, evidence, and checkpoint boundaries, but keeps diagnostics read-only and owner-only. The design separates DataAgent from QChat through a narrow FunctionCaller bridge, keeps all runtime data in memory for V2.9, and enforces redaction plus readiness gates so observability does not become a security bypass.

The strongest interview points:

- It demonstrates Loop Engineering because the analysis loop now has visible intermediate state.
- It demonstrates Harness Engineering because readiness scripts and tests verify the stream contract.
- It demonstrates security maturity because diagnostics cannot execute SQL or leak raw SQL/tool context.
- It prepares V3 persisted audit trace without prematurely adding persistence in V2.9.
- It prepares a future natural semantic operation layer where the user can speak intent naturally, while closed intent sets, owner gates, confidence thresholds, and existing command handlers reduce false positives.

## V2.10 Handoff

V2.9 provides progress streaming for DataAgent runtime boundaries. V2.10 builds on that foundation by defining Alife-wide capability governance, a DataAgent scenario knowledge pack, and node-level DataAgent tool scopes.

The progress stream remains the observation channel for later workflow nodes; diagnostics stay owner-only, bounded, redacted, and read-only. This lets future multi-agent orchestration report linked-run progress without turning diagnostics into a second execution path.
