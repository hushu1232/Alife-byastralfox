# DataAgent V2.8 Trace Timeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an in-memory, owner-only DataAgent trace timeline that lets QChat diagnostics replay the latest DataAgent analysis chain without adding new authority, SQL execution, model calls, or persistent storage.

**Architecture:** DataAgent owns trace models, recorder, builder, and formatter. `DataAgentAnalysisToolHandler` publishes a safe trace diagnostics string after existing orchestrator results are produced; QChat receives only safe text and stores it in `QChatRecentDiagnosticsCache`. Owner commands read the recent trace through the same diagnostics path as V2.7, with cache-first behavior and fail-closed redaction.

**Tech Stack:** C#/.NET 9, NUnit, existing DataAgent orchestrator models, existing QChat owner diagnostics, existing PowerShell readiness gates.

---

## Scope And Constraints

Implement only V2.8:

- In-memory trace timelines.
- Owner-only diagnostics command text.
- QChat recent diagnostics summary line.
- Readiness and engineering-map gates.
- No database persistence.
- No frontend.
- No streaming UI.
- No LangGraph.
- No new SQL path.
- No model call from diagnostics.
- No XML/tool call from diagnostics.
- No DataAgent reference to QChat.

Use this verified design as the source of truth:

```text
docs/superpowers/specs/2026-07-02-dataagent-v2.8-trace-timeline-design.md
```

Use .NET 9 explicitly:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe"
```

Use a feature worktree at execution time, for example:

```powershell
git worktree add "D:\Alife\.worktrees\dataagent-v2.8-trace-timeline" -b dataagent-v2.8-trace-timeline
```

## File Structure

Create these DataAgent files:

- `sources/Alife.Function/Alife.Function.DataAgent/DataAgentTraceModels.cs`
  - Trace enums and immutable trace records.
- `sources/Alife.Function/Alife.Function.DataAgent/IDataAgentTraceRecorder.cs`
  - Recorder interface used by handler/module wiring.
- `sources/Alife.Function/Alife.Function.DataAgent/DataAgentTraceRecorder.cs`
  - In-memory TTL/capacity-limited, non-mutating recent trace store.
- `sources/Alife.Function/Alife.Function.DataAgent/DataAgentTraceTimelineBuilder.cs`
  - Converts existing `DataAgentOrchestrationResult` plus `DataAgentEvidencePack` into a safe timeline.
- `sources/Alife.Function/Alife.Function.DataAgent/DataAgentTraceDiagnosticsFormatter.cs`
  - Formats owner-readable, redacted trace diagnostics.

Create these tests:

- `Tests/Alife.Test.DataAgent/DataAgentTraceRecorderTests.cs`
- `Tests/Alife.Test.DataAgent/DataAgentTraceDiagnosticsFormatterTests.cs`

Modify these DataAgent files:

- `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs`
  - Add trace diagnostics publisher and publish one trace per orchestrator result.
- `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisCapabilityProvider.cs`
  - Pass trace diagnostics publisher to the handler.
- `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`
  - Instantiate `DataAgentTraceRecorder`; pass `functionService.RecordRecentDataAgentTraceDiagnostics`.
- `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
  - Add runtime proof for trace timeline capability.
- `tools/check-dataagent-readiness.ps1`
  - Add required `DataAgentTraceTimelinePresent` check and update expected count from 74 to 75.

Modify these QChat/FunctionCaller files:

- `sources/Alife.Function/Alife.Function.FunctionCaller/XmlFunctionCaller.cs`
  - Store recent DataAgent trace diagnostics string.
- `sources/Alife.Function/Alife.Function.QChat/QChatRecentDiagnosticsCache.cs`
  - Add `DataAgentTrace` kind.
- `sources/Alife.Function/Alife.Function.QChat/QChatRecentDiagnosticsFormatter.cs`
  - Add summary line and redacted title for trace.
- `sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs`
  - Add runtime state field and `/dataagent diag trace` commands.
- `sources/Alife.Function/Alife.Function.QChat/QChatOwnerCommandService.cs`
  - Treat trace diagnostics as owner-only diagnostics.
- `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
  - Pass recent trace callback and record trace into QChat recent diagnostics.
- `tools/check-qchat-engineering-map.ps1`
  - Add required Harness check for DataAgent trace diagnostics and update expected count from 50 to 51.

Modify these tests:

- `Tests/Alife.Test.DataAgent/DataAgentAnalysisToolHandlerTests.cs`
- `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
- `Tests/Alife.Test.QChat/QChatRecentDiagnosticsCacheTests.cs`
- `Tests/Alife.Test.QChat/QChatDiagnosticsServiceTests.cs`
- `Tests/Alife.Test.QChat/QChatOwnerCommandServiceTests.cs`
- `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`
- `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`

---

### Task 1: Add DataAgent Trace Models And Recorder

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentTraceModels.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/IDataAgentTraceRecorder.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentTraceRecorder.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentTraceRecorderTests.cs`

- [ ] **Step 1: Write the failing recorder tests**

Create `Tests/Alife.Test.DataAgent/DataAgentTraceRecorderTests.cs`:

```csharp
using Alife.Function.DataAgent;
using NUnit.Framework;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentTraceRecorderTests
{
    [Test]
    public void GetLatestReturnsNewestTimelineForSession()
    {
        DataAgentTraceRecorder recorder = new(maxTimelinesPerSession: 4, ttl: TimeSpan.FromMinutes(30));
        DateTimeOffset start = DateTimeOffset.Parse("2026-07-02T00:00:00Z");

        recorder.Record(Timeline("session-a", 1, start, "old"));
        recorder.Record(Timeline("session-a", 2, start.AddSeconds(5), "new"));

        DataAgentTraceTimeline? latest = recorder.GetLatest("session-a", start.AddSeconds(6));

        Assert.Multiple(() =>
        {
            Assert.That(latest, Is.Not.Null);
            Assert.That(latest!.TurnCount, Is.EqualTo(2));
            Assert.That(latest.Events.Single().ReasonCode, Is.EqualTo("new"));
        });
    }

    [Test]
    public void GetLatestIsolatesSessions()
    {
        DataAgentTraceRecorder recorder = new(maxTimelinesPerSession: 4, ttl: TimeSpan.FromMinutes(30));
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:00:00Z");

        recorder.Record(Timeline("session-a", 1, now, "a"));
        recorder.Record(Timeline("session-b", 1, now, "b"));

        Assert.Multiple(() =>
        {
            Assert.That(recorder.GetLatest("session-a", now)!.SessionId, Is.EqualTo("session-a"));
            Assert.That(recorder.GetLatest("session-b", now)!.SessionId, Is.EqualTo("session-b"));
            Assert.That(recorder.GetLatest("session-c", now), Is.Null);
        });
    }

    [Test]
    public void RecordEvictsOldestTimelineWithinSessionCapacity()
    {
        DataAgentTraceRecorder recorder = new(maxTimelinesPerSession: 2, ttl: TimeSpan.FromMinutes(30));
        DateTimeOffset start = DateTimeOffset.Parse("2026-07-02T00:00:00Z");

        recorder.Record(Timeline("session-a", 1, start, "first"));
        recorder.Record(Timeline("session-a", 2, start.AddSeconds(1), "second"));
        recorder.Record(Timeline("session-a", 3, start.AddSeconds(2), "third"));

        IReadOnlyList<DataAgentTraceTimeline> recent = recorder.GetRecent("session-a", start.AddSeconds(3));

        Assert.Multiple(() =>
        {
            Assert.That(recent.Select(timeline => timeline.TurnCount), Is.EqualTo(new[] { 2, 3 }));
            Assert.That(recent.Select(timeline => timeline.Events.Single().ReasonCode), Is.EqualTo(new[] { "second", "third" }));
        });
    }

    [Test]
    public void ReadsFilterExpiredTimelinesWithoutRemovingThem()
    {
        DataAgentTraceRecorder recorder = new(maxTimelinesPerSession: 4, ttl: TimeSpan.FromSeconds(30));
        DateTimeOffset start = DateTimeOffset.Parse("2026-07-02T00:00:00Z");

        recorder.Record(Timeline("session-a", 1, start, "before-expiry"));

        Assert.Multiple(() =>
        {
            Assert.That(recorder.GetRecent("session-a", start.AddSeconds(45)), Is.Empty);
            Assert.That(recorder.GetLatest("session-a", start.AddSeconds(45)), Is.Null);
            Assert.That(recorder.GetLatest("session-a", start.AddSeconds(20)), Is.Not.Null);
            Assert.That(recorder.GetRecent("session-a", start.AddSeconds(20)).Single().TurnCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void RecordIgnoresEmptySessionAndTimelineWithoutEvents()
    {
        DataAgentTraceRecorder recorder = new(maxTimelinesPerSession: 4, ttl: TimeSpan.FromMinutes(30));
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:00:00Z");

        recorder.Record(new DataAgentTraceTimeline(
            "",
            DataAgentAnalysisSessionStatus.Active,
            1,
            now,
            now,
            Terminal: false,
            []));
        recorder.Record(new DataAgentTraceTimeline(
            "session-a",
            DataAgentAnalysisSessionStatus.Active,
            1,
            now,
            now,
            Terminal: false,
            []));

        Assert.That(recorder.GetRecent("session-a", now), Is.Empty);
    }

    static DataAgentTraceTimeline Timeline(string sessionId, int turn, DateTimeOffset startedAt, string reason)
    {
        return new DataAgentTraceTimeline(
            sessionId,
            DataAgentAnalysisSessionStatus.Active,
            turn,
            startedAt,
            startedAt.AddMilliseconds(10),
            Terminal: false,
            [
                new DataAgentTraceEvent(
                    DataAgentTraceEventKind.RouteGate,
                    DataAgentTraceEventStatus.Succeeded,
                    reason,
                    ExecutedSql: false,
                    QueryAllowed: true,
                    Terminal: false,
                    new Dictionary<string, string> { ["route_allowed"] = "true" })
            ]);
    }
}
```

- [ ] **Step 2: Run recorder tests to verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentTraceRecorderTests" -v:minimal
```

Expected: build fails because `DataAgentTraceRecorder`, `DataAgentTraceTimeline`, `DataAgentTraceEvent`, `DataAgentTraceEventKind`, and `DataAgentTraceEventStatus` do not exist.

- [ ] **Step 3: Add trace model records**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentTraceModels.cs`:

```csharp
namespace Alife.Function.DataAgent;

public enum DataAgentTraceEventKind
{
    RouteGate,
    SchemaContext,
    Planner,
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

public enum DataAgentTraceEventStatus
{
    Succeeded,
    Skipped,
    Rejected,
    Failed
}

public sealed record DataAgentTraceEvent(
    DataAgentTraceEventKind Kind,
    DataAgentTraceEventStatus Status,
    string ReasonCode,
    bool ExecutedSql,
    bool QueryAllowed,
    bool Terminal,
    IReadOnlyDictionary<string, string> Facts);

public sealed record DataAgentTraceTimeline(
    string SessionId,
    DataAgentAnalysisSessionStatus SessionStatus,
    int TurnCount,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    bool Terminal,
    IReadOnlyList<DataAgentTraceEvent> Events);
```

- [ ] **Step 4: Add recorder interface**

Create `sources/Alife.Function/Alife.Function.DataAgent/IDataAgentTraceRecorder.cs`:

```csharp
namespace Alife.Function.DataAgent;

public interface IDataAgentTraceRecorder
{
    void Record(DataAgentTraceTimeline? timeline);

    DataAgentTraceTimeline? GetLatest(string sessionId, DateTimeOffset now);

    IReadOnlyList<DataAgentTraceTimeline> GetRecent(string sessionId, DateTimeOffset now);
}
```

- [ ] **Step 5: Add in-memory recorder implementation**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentTraceRecorder.cs`:

```csharp
namespace Alife.Function.DataAgent;

public sealed class DataAgentTraceRecorder : IDataAgentTraceRecorder
{
    readonly object gate = new();
    readonly int maxTimelinesPerSession;
    readonly TimeSpan ttl;
    readonly List<DataAgentTraceTimelineRecord> timelines = [];
    long nextSequence;

    public DataAgentTraceRecorder(int maxTimelinesPerSession = 4, TimeSpan? ttl = null)
    {
        this.maxTimelinesPerSession = Math.Max(1, maxTimelinesPerSession);
        this.ttl = ttl ?? TimeSpan.FromMinutes(30);
    }

    public void Record(DataAgentTraceTimeline? timeline)
    {
        if (timeline is null ||
            string.IsNullOrWhiteSpace(timeline.SessionId) ||
            timeline.Events.Count == 0)
        {
            return;
        }

        DataAgentTraceTimeline normalized = timeline with
        {
            SessionId = NormalizeSessionId(timeline.SessionId)
        };

        lock (gate)
        {
            PruneExpiredLocked(normalized.EndedAt);
            timelines.Add(new DataAgentTraceTimelineRecord(normalized, nextSequence++));
            PruneCapacityLocked(normalized.SessionId);
        }
    }

    public DataAgentTraceTimeline? GetLatest(string sessionId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return null;

        string normalizedSessionId = NormalizeSessionId(sessionId);
        lock (gate)
        {
            return timelines
                .Where(record => IsExpired(record, now) == false &&
                                 string.Equals(record.Timeline.SessionId, normalizedSessionId, StringComparison.Ordinal))
                .OrderByDescending(record => record.Timeline.EndedAt)
                .ThenByDescending(record => record.Sequence)
                .Select(record => record.Timeline)
                .FirstOrDefault();
        }
    }

    public IReadOnlyList<DataAgentTraceTimeline> GetRecent(string sessionId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return [];

        string normalizedSessionId = NormalizeSessionId(sessionId);
        lock (gate)
        {
            return timelines
                .Where(record => IsExpired(record, now) == false &&
                                 string.Equals(record.Timeline.SessionId, normalizedSessionId, StringComparison.Ordinal))
                .OrderBy(record => record.Timeline.EndedAt)
                .ThenBy(record => record.Sequence)
                .Select(record => record.Timeline)
                .ToArray();
        }
    }

    void PruneExpiredLocked(DateTimeOffset now)
    {
        timelines.RemoveAll(record => IsExpired(record, now));
    }

    bool IsExpired(DataAgentTraceTimelineRecord record, DateTimeOffset now)
    {
        return now - record.Timeline.EndedAt > ttl;
    }

    void PruneCapacityLocked(string sessionId)
    {
        List<DataAgentTraceTimelineRecord> sessionTimelines = timelines
            .Where(record => string.Equals(record.Timeline.SessionId, sessionId, StringComparison.Ordinal))
            .OrderBy(record => record.Timeline.EndedAt)
            .ThenBy(record => record.Sequence)
            .ToList();

        int excess = sessionTimelines.Count - maxTimelinesPerSession;
        if (excess <= 0)
            return;

        foreach (DataAgentTraceTimelineRecord record in sessionTimelines.Take(excess))
            timelines.Remove(record);
    }

    static string NormalizeSessionId(string value)
    {
        return value.ReplaceLineEndings(" ").Replace(';', ',').Trim();
    }

    sealed class DataAgentTraceTimelineRecord(DataAgentTraceTimeline timeline, long sequence)
    {
        public DataAgentTraceTimeline Timeline { get; } = timeline;

        public long Sequence { get; } = sequence;
    }
}
```

- [ ] **Step 6: Run recorder tests to verify GREEN**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentTraceRecorderTests" -v:minimal
```

Expected: `DataAgentTraceRecorderTests` pass with 0 failures.

- [ ] **Step 7: Commit Task 1**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentTraceModels.cs sources/Alife.Function/Alife.Function.DataAgent/IDataAgentTraceRecorder.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentTraceRecorder.cs Tests/Alife.Test.DataAgent/DataAgentTraceRecorderTests.cs
git commit -m "Add DataAgent trace recorder"
```

---

### Task 2: Add Trace Diagnostics Formatter With Fail-Closed Redaction

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentTraceDiagnosticsFormatter.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentTraceDiagnosticsFormatterTests.cs`

- [ ] **Step 1: Write the failing formatter tests**

Create `Tests/Alife.Test.DataAgent/DataAgentTraceDiagnosticsFormatterTests.cs`:

```csharp
using Alife.Function.DataAgent;
using NUnit.Framework;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentTraceDiagnosticsFormatterTests
{
    [Test]
    public void FormatEmitsStableTimelineDiagnostics()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:00:05Z");
        DataAgentTraceTimeline timeline = new(
            "session-1",
            DataAgentAnalysisSessionStatus.Active,
            2,
            DateTimeOffset.Parse("2026-07-02T00:00:00Z"),
            now,
            Terminal: false,
            [
                Event(DataAgentTraceEventKind.RouteGate, DataAgentTraceEventStatus.Succeeded, "route_allowed", false, true, false, new Dictionary<string, string>
                {
                    ["route_allowed"] = "true",
                    ["route_allows_query"] = "true"
                }),
                Event(DataAgentTraceEventKind.Execute, DataAgentTraceEventStatus.Succeeded, "read_only_query_executed", true, true, false, new Dictionary<string, string>
                {
                    ["rows"] = "3",
                    ["sql"] = "SELECT * FROM document_index"
                }),
                Event(DataAgentTraceEventKind.Checkpoint, DataAgentTraceEventStatus.Succeeded, "checkpoint_created", false, true, false, new Dictionary<string, string>
                {
                    ["can_continue"] = "true",
                    ["can_summarize"] = "true"
                })
            ]);

        string text = DataAgentTraceDiagnosticsFormatter.Format(timeline);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("DataAgent trace diagnostics"));
            Assert.That(text, Does.Contain("session=session-1"));
            Assert.That(text, Does.Contain("turn=2"));
            Assert.That(text, Does.Contain("status=Active"));
            Assert.That(text, Does.Contain("terminal=false"));
            Assert.That(text, Does.Contain("events=3"));
            Assert.That(text, Does.Contain("1 RouteGate Succeeded reason=route_allowed query_allowed=true executed_sql=false terminal=false route_allowed=true route_allows_query=true"));
            Assert.That(text, Does.Contain("2 Execute Succeeded reason=read_only_query_executed query_allowed=true executed_sql=true terminal=false rows=3 sql=redacted"));
            Assert.That(text, Does.Contain("3 Checkpoint Succeeded reason=checkpoint_created query_allowed=true executed_sql=false terminal=false can_continue=true can_summarize=true"));
            Assert.That(text, Does.Not.Contain("SELECT"));
            Assert.That(text, Does.Not.Contain("document_index"));
        });
    }

    [Test]
    public void FormatUnavailableEmitsStableState()
    {
        string text = DataAgentTraceDiagnosticsFormatter.Format(null);

        Assert.That(text.Split(Environment.NewLine), Is.EqualTo(new[]
        {
            "DataAgent trace diagnostics",
            "state=unavailable",
            "reason=trace_unavailable"
        }));
    }

    [TestCase("Bearer token-abcdef123456")]
    [TestCase("Server=db.internal;Uid=alife;Pwd=secret")]
    [TestCase("[tool_route_context]\nAllowed XML tools: dataagent_query\n[/tool_route_context]")]
    [TestCase("[data_agent_evidence_pack]\ntrace=unsafe\n[/data_agent_evidence_pack]")]
    [TestCase("api_key=sk-test")]
    [TestCase("SELECT COUNT(*) FROM users")]
    public void FormatRedactsUnsafeFactValues(string unsafeValue)
    {
        DataAgentTraceTimeline timeline = new(
            "session-1",
            DataAgentAnalysisSessionStatus.Active,
            1,
            DateTimeOffset.Parse("2026-07-02T00:00:00Z"),
            DateTimeOffset.Parse("2026-07-02T00:00:01Z"),
            Terminal: false,
            [
                Event(DataAgentTraceEventKind.Execute, DataAgentTraceEventStatus.Succeeded, "unsafe_fact", true, true, false, new Dictionary<string, string>
                {
                    ["detail"] = unsafeValue
                })
            ]);

        string text = DataAgentTraceDiagnosticsFormatter.Format(timeline);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("detail=redacted"));
            Assert.That(text, Does.Not.Contain("token-abcdef123456"));
            Assert.That(text, Does.Not.Contain("Pwd=secret"));
            Assert.That(text, Does.Not.Contain("[tool_route_context]"));
            Assert.That(text, Does.Not.Contain("[data_agent_evidence_pack]"));
            Assert.That(text, Does.Not.Contain("sk-test"));
            Assert.That(text, Does.Not.Contain("SELECT"));
        });
    }

    [Test]
    public void FormatBoundsLongTraceText()
    {
        DataAgentTraceTimeline timeline = new(
            "session-1",
            DataAgentAnalysisSessionStatus.Active,
            1,
            DateTimeOffset.Parse("2026-07-02T00:00:00Z"),
            DateTimeOffset.Parse("2026-07-02T00:00:01Z"),
            Terminal: false,
            Enumerable.Range(0, 80)
                .Select(index => Event(
                    DataAgentTraceEventKind.Planner,
                    DataAgentTraceEventStatus.Succeeded,
                    "planner_step_" + index,
                    false,
                    true,
                    false,
                    new Dictionary<string, string> { ["signal"] = new string('a', 80) }))
                .ToArray());

        string text = DataAgentTraceDiagnosticsFormatter.Format(timeline, maxChars: 1800);

        Assert.Multiple(() =>
        {
            Assert.That(text, Has.Length.LessThanOrEqualTo(1800));
            Assert.That(text, Does.EndWith("..."));
        });
    }

    static DataAgentTraceEvent Event(
        DataAgentTraceEventKind kind,
        DataAgentTraceEventStatus status,
        string reason,
        bool executedSql,
        bool queryAllowed,
        bool terminal,
        IReadOnlyDictionary<string, string> facts)
    {
        return new DataAgentTraceEvent(kind, status, reason, executedSql, queryAllowed, terminal, facts);
    }
}
```

- [ ] **Step 2: Run formatter tests to verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentTraceDiagnosticsFormatterTests" -v:minimal
```

Expected: build fails because `DataAgentTraceDiagnosticsFormatter` does not exist.

- [ ] **Step 3: Implement formatter**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentTraceDiagnosticsFormatter.cs`:

```csharp
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Alife.Function.DataAgent;

public static class DataAgentTraceDiagnosticsFormatter
{
    const string TruncationEllipsis = "...";

    static readonly Regex ApiKeyPattern = new(@"\bapi[-_]?key\s*[:=]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Regex AuthorizationBearerPattern = new(@"\b(?:authorization\s*:\s*)?bearer\s+\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Regex ConnectionSecretPattern = new(@"\b(connection[_\s-]?string|host|server|data\s+source|username|user\s*id|userid|uid|user|password|pwd)\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Regex SqlPattern = new(@"\b(select|insert\s+into|update\s+\w+|delete\s+from|drop\s+(table|database|schema)|create\s+(table|database|schema|index|view)|alter\s+(table|database|schema|index|view)|truncate\s+(table|database)|merge\s+into|exec(ute)?\s+\w+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    public static string Format(DataAgentTraceTimeline? timeline, int maxChars = 1800)
    {
        if (timeline is null)
            return string.Join(Environment.NewLine,
                "DataAgent trace diagnostics",
                "state=unavailable",
                "reason=trace_unavailable");

        StringBuilder builder = new();
        builder.AppendLine("DataAgent trace diagnostics");
        builder.AppendLine("session=" + SafeToken(timeline.SessionId));
        builder.AppendLine("turn=" + Math.Max(0, timeline.TurnCount).ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("status=" + timeline.SessionStatus);
        builder.AppendLine("terminal=" + Bool(timeline.Terminal));
        builder.AppendLine("events=" + timeline.Events.Count.ToString(CultureInfo.InvariantCulture));

        for (int i = 0; i < timeline.Events.Count; i++)
        {
            DataAgentTraceEvent traceEvent = timeline.Events[i];
            builder.Append((i + 1).ToString(CultureInfo.InvariantCulture));
            builder.Append(' ');
            builder.Append(traceEvent.Kind);
            builder.Append(' ');
            builder.Append(traceEvent.Status);
            builder.Append(" reason=");
            builder.Append(SafeValue(traceEvent.ReasonCode));
            builder.Append(" query_allowed=");
            builder.Append(Bool(traceEvent.QueryAllowed));
            builder.Append(" executed_sql=");
            builder.Append(Bool(traceEvent.ExecutedSql));
            builder.Append(" terminal=");
            builder.Append(Bool(traceEvent.Terminal));

            foreach (KeyValuePair<string, string> fact in traceEvent.Facts.OrderBy(fact => fact.Key, StringComparer.Ordinal))
            {
                builder.Append(' ');
                builder.Append(SafeKey(fact.Key));
                builder.Append('=');
                builder.Append(SafeValue(fact.Value));
            }

            if (i + 1 < timeline.Events.Count)
                builder.AppendLine();
        }

        return Bound(builder.ToString(), maxChars);
    }

    static string SafeKey(string value)
    {
        string sanitized = DataAgentContextFieldSanitizer.Sanitize(value, 48)
            .Replace(' ', '_');
        return string.IsNullOrWhiteSpace(sanitized) ? "fact" : sanitized;
    }

    static string SafeValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "none";

        if (ContainsUnsafeText(value))
            return "redacted";

        return DataAgentContextFieldSanitizer.Sanitize(value, 120)
            .ReplaceLineEndings(" ")
            .Replace('=', ':')
            .Trim();
    }

    static string SafeToken(string value)
    {
        return DataAgentContextFieldSanitizer.Sanitize(value, 160)
            .ReplaceLineEndings(" ")
            .Replace(';', ',')
            .Trim();
    }

    static bool ContainsUnsafeText(string value)
    {
        return value.Contains("[tool_route_context]", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("[/tool_route_context]", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("[data_agent_context]", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("[/data_agent_context]", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("[data_agent_evidence_pack]", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("[/data_agent_evidence_pack]", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Allowed XML tools", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("connection_string", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("sk-", StringComparison.OrdinalIgnoreCase) ||
               ApiKeyPattern.IsMatch(value) ||
               AuthorizationBearerPattern.IsMatch(value) ||
               ConnectionSecretPattern.IsMatch(value) ||
               SqlPattern.IsMatch(value);
    }

    static string Bound(string text, int maxChars)
    {
        int safeMax = Math.Max(TruncationEllipsis.Length, maxChars);
        string normalized = text.ReplaceLineEndings(Environment.NewLine).Trim();
        if (normalized.Length <= safeMax)
            return normalized;

        return normalized[..(safeMax - TruncationEllipsis.Length)].TrimEnd() + TruncationEllipsis;
    }

    static string Bool(bool value)
    {
        return value ? "true" : "false";
    }
}
```

- [ ] **Step 4: Run formatter tests to verify GREEN**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentTraceDiagnosticsFormatterTests" -v:minimal
```

Expected: `DataAgentTraceDiagnosticsFormatterTests` pass with 0 failures.

- [ ] **Step 5: Commit Task 2**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentTraceDiagnosticsFormatter.cs Tests/Alife.Test.DataAgent/DataAgentTraceDiagnosticsFormatterTests.cs
git commit -m "Add DataAgent trace diagnostics formatter"
```

---

### Task 3: Build Trace Timelines From Orchestrator Results And Publish Them

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentTraceTimelineBuilder.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisCapabilityProvider.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentAnalysisToolHandlerTests.cs`

- [ ] **Step 1: Add failing tool handler trace publish tests**

Append these tests to `Tests/Alife.Test.DataAgent/DataAgentAnalysisToolHandlerTests.cs`:

```csharp
[Test]
public void StartPublishesTraceDiagnosticsForAcceptedQuery()
{
    List<string> traceDiagnostics = [];
    RecordingOrchestrator orchestrator = new(new Dictionary<string, DataAgentOrchestrationResult>
    {
        ["start"] = OrchestratedResult(
            "session-1",
            DataAgentAnalysisSessionStatus.Active,
            DataAgentAnalysisTurnIntent.NewQuestion,
            [
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Succeeded, "route_allowed", false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.SchemaContext, DataAgentOrchestrationStepStatus.Succeeded, "schema_context_ready", false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Plan, DataAgentOrchestrationStepStatus.Succeeded, "plan_created", false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Validate, DataAgentOrchestrationStepStatus.Succeeded, "read_only_sql_validated", false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Execute, DataAgentOrchestrationStepStatus.Succeeded, "read_only_query_executed", true),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
            ],
            1,
            "[data_agent_analysis_session_context]\nsession_id=session-1\n[/data_agent_analysis_session_context]")
    });
    RecordingRouteContextAccessor routeAccessor = new(new DataAgentToolRouteContext(
        true,
        "dataagent_analysis_start",
        true,
        true,
        "route-1",
        "analysis_start",
        "route_allowed",
        string.Empty));
    DataAgentAnalysisToolHandler handler = new(
        orchestrator,
        routeContextAccessor: routeAccessor,
        traceDiagnosticsPublisher: traceDiagnostics.Add);

    handler.Start("xiayu", "Which documents describe DataAgent?");

    Assert.Multiple(() =>
    {
        Assert.That(traceDiagnostics, Has.Count.EqualTo(1));
        Assert.That(traceDiagnostics.Single(), Does.Contain("DataAgent trace diagnostics"));
        Assert.That(traceDiagnostics.Single(), Does.Contain("session=session-1"));
        Assert.That(traceDiagnostics.Single(), Does.Contain("RouteGate Succeeded reason=route_allowed"));
        Assert.That(traceDiagnostics.Single(), Does.Contain("SchemaContext Succeeded reason=schema_context_ready"));
        Assert.That(traceDiagnostics.Single(), Does.Contain("Planner Succeeded reason=plan_created"));
        Assert.That(traceDiagnostics.Single(), Does.Contain("SqlSafety Succeeded reason=read_only_sql_validated"));
        Assert.That(traceDiagnostics.Single(), Does.Contain("Execute Succeeded reason=read_only_query_executed"));
        Assert.That(traceDiagnostics.Single(), Does.Contain("EvidencePack Succeeded"));
        Assert.That(traceDiagnostics.Single(), Does.Contain("Checkpoint Succeeded reason=checkpoint_created"));
        Assert.That(traceDiagnostics.Single(), Does.Contain("executed_sql=true"));
        Assert.That(traceDiagnostics.Single(), Does.Not.Contain("[data_agent_evidence_pack]"));
    });
}

[Test]
public void StartRouteDeniedTraceDoesNotContainExecuteEvent()
{
    List<string> traceDiagnostics = [];
    RecordingOrchestrator orchestrator = new(new Dictionary<string, DataAgentOrchestrationResult>
    {
        ["start"] = OrchestratedResult(
            "session-denied",
            DataAgentAnalysisSessionStatus.Active,
            DataAgentAnalysisTurnIntent.NewQuestion,
            [
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Rejected, "tool_route_required", false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Reject, DataAgentOrchestrationStepStatus.Rejected, "route_denied_no_query", false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_unchanged", false)
            ],
            0,
            "[data_agent_analysis_session_context]\nsession_id=session-denied\n[/data_agent_analysis_session_context]")
    });
    RecordingRouteContextAccessor routeAccessor = new(new DataAgentToolRouteContext(
        false,
        "dataagent_analysis_start",
        false,
        false,
        "route-denied",
        "analysis_start",
        "tool_route_required",
        string.Empty));
    DataAgentAnalysisToolHandler handler = new(
        orchestrator,
        routeContextAccessor: routeAccessor,
        traceDiagnosticsPublisher: traceDiagnostics.Add);

    handler.Start("xiayu", "Which documents describe DataAgent?");

    Assert.Multiple(() =>
    {
        Assert.That(traceDiagnostics.Single(), Does.Contain("RouteGate Rejected reason=tool_route_required"));
        Assert.That(traceDiagnostics.Single(), Does.Contain("Reject Rejected reason=route_denied_no_query"));
        Assert.That(traceDiagnostics.Single(), Does.Contain("Checkpoint Succeeded reason=checkpoint_unchanged"));
        Assert.That(traceDiagnostics.Single(), Does.Not.Contain("Execute Succeeded"));
        Assert.That(traceDiagnostics.Single(), Does.Contain("query_allowed=false"));
    });
}

[Test]
public void SummarizePublishesTerminalTraceWithoutSqlExecution()
{
    List<string> traceDiagnostics = [];
    RecordingOrchestrator orchestrator = new(new Dictionary<string, DataAgentOrchestrationResult>
    {
        ["summarize"] = OrchestratedResult(
            "session-1",
            DataAgentAnalysisSessionStatus.Summarized,
            DataAgentAnalysisTurnIntent.Summarize,
            [
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Summarize, DataAgentOrchestrationStepStatus.Succeeded, "terminal_summary", false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_created", false)
            ],
            2,
            "[data_agent_analysis_session_context]\nsession_id=session-1\n[/data_agent_analysis_session_context]")
    });
    RecordingRouteContextAccessor routeAccessor = new(new DataAgentToolRouteContext(
        true,
        "dataagent_analysis_summarize",
        true,
        false,
        "route-summary",
        "analysis_summarize",
        "route_allowed",
        "session-1"));
    DataAgentAnalysisToolHandler handler = new(
        orchestrator,
        routeContextAccessor: routeAccessor,
        traceDiagnosticsPublisher: traceDiagnostics.Add);

    handler.Summarize("session-1");

    Assert.Multiple(() =>
    {
        Assert.That(traceDiagnostics.Single(), Does.Contain("Summarize Succeeded reason=terminal_summary"));
        Assert.That(traceDiagnostics.Single(), Does.Contain("terminal=true"));
        Assert.That(traceDiagnostics.Single(), Does.Contain("executed_sql=false"));
        Assert.That(traceDiagnostics.Single(), Does.Not.Contain("Execute Succeeded"));
    });
}
```

- [ ] **Step 2: Run handler tests to verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "Name=StartPublishesTraceDiagnosticsForAcceptedQuery|Name=StartRouteDeniedTraceDoesNotContainExecuteEvent|Name=SummarizePublishesTerminalTraceWithoutSqlExecution" -v:minimal
```

Expected: build fails because `DataAgentAnalysisToolHandler` has no `traceDiagnosticsPublisher` named argument.

- [ ] **Step 3: Implement timeline builder**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentTraceTimelineBuilder.cs`:

```csharp
namespace Alife.Function.DataAgent;

public sealed class DataAgentTraceTimelineBuilder
{
    const int MaxEvents = 32;

    public DataAgentTraceTimeline Build(
        DataAgentOrchestrationResult result,
        DataAgentEvidencePack evidencePack,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(evidencePack);

        List<DataAgentTraceEvent> events = [];
        bool evidenceInserted = false;

        foreach (DataAgentOrchestrationStep step in result.Steps.Take(MaxEvents))
        {
            if (evidenceInserted == false && step.Node == DataAgentOrchestrationNodeKind.Checkpoint)
            {
                events.Add(BuildEvidenceEvent(evidencePack));
                evidenceInserted = true;
            }

            events.Add(BuildStepEvent(step, result, evidencePack));
        }

        if (evidenceInserted == false && events.Count < MaxEvents)
            events.Add(BuildEvidenceEvent(evidencePack));

        if (events.Count > MaxEvents)
            events = events.Take(MaxEvents).ToList();

        DateTimeOffset startedAt = now;
        DateTimeOffset endedAt = now;
        return new DataAgentTraceTimeline(
            result.SessionId,
            result.SessionStatus,
            result.Checkpoint.TurnCount,
            startedAt,
            endedAt,
            result.Checkpoint.Terminal,
            events);
    }

    static DataAgentTraceEvent BuildStepEvent(
        DataAgentOrchestrationStep step,
        DataAgentOrchestrationResult result,
        DataAgentEvidencePack evidencePack)
    {
        Dictionary<string, string> facts = [];
        if (step.Node == DataAgentOrchestrationNodeKind.RouteGate)
        {
            facts["route_present"] = Bool(result.RouteContext?.Present == true);
            facts["route_allowed"] = Bool(result.RouteContext?.AllowsTool == true);
            facts["route_allows_query"] = Bool(result.RouteContext?.AllowsQuery == true);
            facts["route_reason"] = result.RouteContext?.ReasonCode ?? string.Empty;
        }
        else if (step.Node == DataAgentOrchestrationNodeKind.Checkpoint)
        {
            facts["can_continue"] = Bool(result.Checkpoint.CanContinue);
            facts["can_summarize"] = Bool(result.Checkpoint.CanSummarize);
            facts["checkpoint_terminal"] = Bool(result.Checkpoint.Terminal);
        }
        else if (step.Node == DataAgentOrchestrationNodeKind.Execute)
        {
            facts["rows"] = evidencePack.AuditRowCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            facts["sql"] = "redacted";
        }

        return new DataAgentTraceEvent(
            MapKind(step.Node),
            MapStatus(step.Status),
            step.Reason,
            step.ExecutedSql,
            result.RouteContext?.AllowsQuery == true,
            result.Checkpoint.Terminal || step.Node is DataAgentOrchestrationNodeKind.Summarize or DataAgentOrchestrationNodeKind.End,
            facts);
    }

    static DataAgentTraceEvent BuildEvidenceEvent(DataAgentEvidencePack pack)
    {
        Dictionary<string, string> facts = new()
        {
            ["risk"] = pack.RiskLevel.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            ["analysis_confidence"] = pack.AnalysisConfidence.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            ["tool_broker_audit_allowed"] = Bool(pack.ToolBrokerAuditAllowed)
        };

        return new DataAgentTraceEvent(
            DataAgentTraceEventKind.EvidencePack,
            DataAgentTraceEventStatus.Succeeded,
            pack.StateEstimateReasonCode,
            pack.ExecutedSql,
            pack.RouteAllowsQuery,
            pack.Terminal,
            facts);
    }

    static DataAgentTraceEventKind MapKind(DataAgentOrchestrationNodeKind node)
    {
        return node switch
        {
            DataAgentOrchestrationNodeKind.RouteGate => DataAgentTraceEventKind.RouteGate,
            DataAgentOrchestrationNodeKind.SchemaContext => DataAgentTraceEventKind.SchemaContext,
            DataAgentOrchestrationNodeKind.Plan => DataAgentTraceEventKind.Planner,
            DataAgentOrchestrationNodeKind.Validate => DataAgentTraceEventKind.SqlSafety,
            DataAgentOrchestrationNodeKind.Execute => DataAgentTraceEventKind.Execute,
            DataAgentOrchestrationNodeKind.Explain => DataAgentTraceEventKind.Explain,
            DataAgentOrchestrationNodeKind.Clarification => DataAgentTraceEventKind.Clarification,
            DataAgentOrchestrationNodeKind.Summarize => DataAgentTraceEventKind.Summarize,
            DataAgentOrchestrationNodeKind.End => DataAgentTraceEventKind.End,
            DataAgentOrchestrationNodeKind.Reject => DataAgentTraceEventKind.Reject,
            DataAgentOrchestrationNodeKind.Checkpoint => DataAgentTraceEventKind.Checkpoint,
            _ => DataAgentTraceEventKind.Answer
        };
    }

    static DataAgentTraceEventStatus MapStatus(DataAgentOrchestrationStepStatus status)
    {
        return status switch
        {
            DataAgentOrchestrationStepStatus.Succeeded => DataAgentTraceEventStatus.Succeeded,
            DataAgentOrchestrationStepStatus.Skipped => DataAgentTraceEventStatus.Skipped,
            DataAgentOrchestrationStepStatus.Rejected => DataAgentTraceEventStatus.Rejected,
            DataAgentOrchestrationStepStatus.Failed => DataAgentTraceEventStatus.Failed,
            _ => DataAgentTraceEventStatus.Failed
        };
    }

    static string Bool(bool value)
    {
        return value ? "true" : "false";
    }
}
```

- [ ] **Step 4: Update `DataAgentAnalysisToolHandler` to publish trace diagnostics**

Modify constructor signature:

```csharp
public sealed class DataAgentAnalysisToolHandler(
    IDataAgentAnalysisOrchestrator orchestrator,
    Action<string>? resultPublisher = null,
    IDataAgentToolRouteContextAccessor? routeContextAccessor = null,
    Action<string>? evidenceDiagnosticsPublisher = null,
    Action<string>? traceDiagnosticsPublisher = null,
    IDataAgentTraceRecorder? traceRecorder = null,
    Func<DateTimeOffset>? traceClock = null)
```

Add fields after `routeContextAccessor`:

```csharp
readonly IDataAgentTraceRecorder traceRecorder = traceRecorder ?? new DataAgentTraceRecorder();
readonly Func<DateTimeOffset> traceClock = traceClock ?? (() => DateTimeOffset.UtcNow);
```

Replace `PublishResult` with:

```csharp
void PublishResult(DataAgentOrchestrationResult result, string context)
{
    resultPublisher?.Invoke(context);

    DataAgentEvidencePack pack = new DataAgentEvidencePackBuilder().Build(result);
    evidenceDiagnosticsPublisher?.Invoke(DataAgentEvidenceDiagnosticsFormatter.Format(pack));

    if (traceDiagnosticsPublisher is null)
        return;

    DataAgentTraceTimeline timeline = new DataAgentTraceTimelineBuilder().Build(result, pack, traceClock());
    traceRecorder.Record(timeline);
    DataAgentTraceTimeline? latest = traceRecorder.GetLatest(timeline.SessionId, traceClock());
    traceDiagnosticsPublisher(DataAgentTraceDiagnosticsFormatter.Format(latest));
}
```

- [ ] **Step 5: Update `DataAgentAnalysisCapabilityProvider`**

Modify constructor signature:

```csharp
public sealed class DataAgentAnalysisCapabilityProvider(
    IDataAgentAnalysisOrchestrator orchestrator,
    Action<string>? resultPublisher = null,
    IDataAgentToolRouteContextAccessor? routeContextAccessor = null,
    Action<string>? evidenceDiagnosticsPublisher = null,
    Action<string>? traceDiagnosticsPublisher = null,
    IDataAgentTraceRecorder? traceRecorder = null) : IDataAgentCapabilityProvider
```

Update handler construction:

```csharp
registrar.RegisterXmlHandlerWithoutStaticDocument(new XmlHandler(new DataAgentAnalysisToolHandler(
    orchestrator,
    resultPublisher,
    routeContextAccessor,
    evidenceDiagnosticsPublisher,
    traceDiagnosticsPublisher,
    traceRecorder)));
```

- [ ] **Step 6: Run handler tests to verify GREEN**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "Name=StartPublishesTraceDiagnosticsForAcceptedQuery|Name=StartRouteDeniedTraceDoesNotContainExecuteEvent|Name=SummarizePublishesTerminalTraceWithoutSqlExecution" -v:minimal
```

Expected: new handler trace tests pass with 0 failures.

- [ ] **Step 7: Run DataAgent focused tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentTraceRecorderTests|FullyQualifiedName~DataAgentTraceDiagnosticsFormatterTests|FullyQualifiedName~DataAgentAnalysisToolHandlerTests" -v:minimal
```

Expected: focused DataAgent trace and handler tests pass with 0 failures.

- [ ] **Step 8: Commit Task 3**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentTraceTimelineBuilder.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisCapabilityProvider.cs Tests/Alife.Test.DataAgent/DataAgentAnalysisToolHandlerTests.cs
git commit -m "Publish DataAgent trace diagnostics"
```

---

### Task 4: Wire Runtime Storage Through FunctionCaller And DataAgent Module

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.FunctionCaller/XmlFunctionCaller.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`

- [ ] **Step 1: Add failing readiness reflection test**

Append this test to `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`:

```csharp
[Test]
public void FunctionCallerStoresRecentDataAgentTraceDiagnostics()
{
    Type functionCallerType = typeof(Alife.Function.FunctionCaller.XmlFunctionCaller);

    Assert.Multiple(() =>
    {
        Assert.That(functionCallerType.GetProperty("RecentDataAgentTraceDiagnostics"), Is.Not.Null);
        Assert.That(functionCallerType.GetMethod("RecordRecentDataAgentTraceDiagnostics"), Is.Not.Null);
        Assert.That(
            File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "sources", "Alife.Function", "Alife.Function.DataAgent", "DataAgentModuleService.cs")),
            Does.Contain("functionService.RecordRecentDataAgentTraceDiagnostics"));
    });
}
```

- [ ] **Step 2: Run reflection test to verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "Name=FunctionCallerStoresRecentDataAgentTraceDiagnostics" -v:minimal
```

Expected: test fails because the property and method do not exist.

- [ ] **Step 3: Add FunctionCaller recent trace storage**

Modify `sources/Alife.Function/Alife.Function.FunctionCaller/XmlFunctionCaller.cs`.

Add property after `RecentDataAgentEvidenceDiagnostics`:

```csharp
public string RecentDataAgentTraceDiagnostics
{
    get
    {
        lock (dataAgentTraceDiagnosticsGate)
        {
            return recentDataAgentTraceDiagnostics;
        }
    }
}
```

Add method after `RecordRecentDataAgentEvidenceDiagnostics`:

```csharp
public void RecordRecentDataAgentTraceDiagnostics(string? diagnostics)
{
    string normalized = string.IsNullOrWhiteSpace(diagnostics)
        ? string.Empty
        : diagnostics.ReplaceLineEndings("\n").Trim();

    lock (dataAgentTraceDiagnosticsGate)
    {
        recentDataAgentTraceDiagnostics = normalized;
    }
}
```

Add fields near the existing diagnostics gate fields:

```csharp
readonly object dataAgentTraceDiagnosticsGate = new();
string recentDataAgentTraceDiagnostics = string.Empty;
```

- [ ] **Step 4: Wire DataAgent module trace recorder and callback**

Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`.

Add recorder after `analysisOrchestrator` creation:

```csharp
IDataAgentTraceRecorder traceRecorder = new DataAgentTraceRecorder();
```

Update `DataAgentAnalysisCapabilityProvider` construction:

```csharp
capabilityRegistry.Add(new DataAgentAnalysisCapabilityProvider(
    analysisOrchestrator,
    PublishAnalysisContext,
    routeContextAccessor,
    functionService.RecordRecentDataAgentEvidenceDiagnostics,
    functionService.RecordRecentDataAgentTraceDiagnostics,
    traceRecorder));
```

- [ ] **Step 5: Run reflection test to verify GREEN**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "Name=FunctionCallerStoresRecentDataAgentTraceDiagnostics" -v:minimal
```

Expected: test passes with 0 failures.

- [ ] **Step 6: Commit Task 4**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.FunctionCaller/XmlFunctionCaller.cs sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs
git commit -m "Wire DataAgent trace diagnostics runtime storage"
```

---

### Task 5: Add QChat Owner Trace Diagnostics Commands And Recent Cache Kind

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatRecentDiagnosticsCache.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatRecentDiagnosticsFormatter.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatOwnerCommandService.cs`
- Test: `Tests/Alife.Test.QChat/QChatRecentDiagnosticsCacheTests.cs`
- Test: `Tests/Alife.Test.QChat/QChatDiagnosticsServiceTests.cs`
- Test: `Tests/Alife.Test.QChat/QChatOwnerCommandServiceTests.cs`

- [ ] **Step 1: Add failing QChat recent trace cache tests**

Append to `Tests/Alife.Test.QChat/QChatRecentDiagnosticsCacheTests.cs`:

```csharp
[Test]
public void FormatSummaryIncludesDataAgentTraceRecentLine()
{
    QChatRecentDiagnosticsCache cache = new(maxEntriesPerSession: 8, ttl: TimeSpan.FromMinutes(30));
    DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:01:00Z");
    cache.Record(QChatRecentDiagnosticKind.DataAgentTrace, "session-a", "dataagent_trace", "DataAgent trace diagnostics", now.AddSeconds(-4));

    string text = QChatRecentDiagnosticsFormatter.FormatSummary(
        cache.GetRecent("session-a", now),
        "session-a",
        now);

    Assert.Multiple(() =>
    {
        Assert.That(text, Does.Contain("dataagent_trace_recent=available age_seconds=4 source=dataagent_trace redacted=false"));
        Assert.That(text, Does.Contain("session=session-a"));
    });
}
```

- [ ] **Step 2: Add failing QChat diagnostics service tests**

Append to `Tests/Alife.Test.QChat/QChatDiagnosticsServiceTests.cs`:

```csharp
[TestCase("/dataagent diag trace")]
[TestCase("/dataagent diagnostics trace")]
[TestCase("/qchat diag dataagent trace")]
[TestCase("/qchat diagnostics dataagent trace")]
public void TryHandleDataAgentTraceDiagnosticsShowsRecentTraceForOwner(string command)
{
    QChatDiagnosticsRuntimeState state = new(RecentDataAgentTrace: string.Join(Environment.NewLine,
        "DataAgent trace diagnostics",
        "session=session-1",
        "1 RouteGate Succeeded reason=route_allowed"));

    QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
        command,
        CreateRoute(),
        CreateProfile(),
        state);

    Assert.Multiple(() =>
    {
        Assert.That(result.Handled, Is.True);
        Assert.That(result.Text, Does.Contain("DataAgent trace diagnostics"));
        Assert.That(result.Text, Does.Contain("RouteGate Succeeded"));
    });
}

[Test]
public void TryHandleDataAgentTraceDiagnosticsReturnsUnavailableWhenNoTraceExists()
{
    QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
        "/dataagent diag trace",
        CreateRoute(),
        CreateProfile(),
        new QChatDiagnosticsRuntimeState());

    Assert.Multiple(() =>
    {
        Assert.That(result.Handled, Is.True);
        Assert.That(result.Text, Does.Contain("DataAgent trace diagnostics"));
        Assert.That(result.Text, Does.Contain("state=unavailable"));
        Assert.That(result.Text, Does.Contain("reason=trace_unavailable"));
    });
}

[Test]
public void TryHandleDataAgentTraceDiagnosticsPrefersSessionCacheOverLegacyTrace()
{
    DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:01:00Z");
    QChatRecentDiagnosticsCache cache = new();
    cache.Record(
        QChatRecentDiagnosticKind.DataAgentTrace,
        "qq:xiayu:2905391496:private:3045846738",
        "dataagent_trace",
        string.Join(Environment.NewLine,
            "DataAgent trace diagnostics",
            "session=from-cache",
            "1 RouteGate Succeeded reason=from_cache"),
        now);
    QChatDiagnosticsRuntimeState state = new(
        RecentDataAgentTrace: "legacy trace text",
        RecentDiagnosticsCache: cache,
        SessionKey: "qq:xiayu:2905391496:private:3045846738",
        DiagnosticsNow: now);

    QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
        "/dataagent diag trace",
        CreateRoute(),
        CreateProfile(),
        state);

    Assert.Multiple(() =>
    {
        Assert.That(result.Handled, Is.True);
        Assert.That(result.Text, Does.Contain("reason=from_cache"));
        Assert.That(result.Text, Does.Not.Contain("legacy trace text"));
    });
}

[Test]
public void TryHandleDataAgentTraceDiagnosticsRedactsUnsafeLegacyFallbackText()
{
    QChatDiagnosticsRuntimeState state = new(
        RecentDataAgentTrace: "DataAgent trace diagnostics\n1 Execute Succeeded sql=SELECT COUNT(*) FROM users; Bearer token-abcdef123456");

    QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
        "/dataagent diag trace",
        CreateRoute(),
        CreateProfile(),
        state);

    Assert.Multiple(() =>
    {
        Assert.That(result.Handled, Is.True);
        Assert.That(result.Text, Does.Contain("DataAgent trace diagnostics"));
        Assert.That(result.Text, Does.Contain("state=redacted"));
        Assert.That(result.Text, Does.Not.Contain("SELECT"));
        Assert.That(result.Text, Does.Not.Contain("token-abcdef123456"));
    });
}
```

In `QChatOwnerCommandServiceTests.cs`, add a test equivalent to the existing evidence diagnostics owner-only test:

```csharp
[Test]
public async Task TryHandleDiagnosticsCommandAsyncPassesRecentTraceToOwnerDiagnostics()
{
    List<string> sent = [];
    OneBotMessageEvent message = CreatePrivateMessage("/dataagent diag trace", userId: 3045846738);

    bool handled = await QChatOwnerCommandService.TryHandleDiagnosticsCommandAsync(
        message,
        QChatSenderRole.Owner,
        new QChatConfig(),
        (_, _, text) =>
        {
            sent.Add(text);
            return Task.CompletedTask;
        },
        (_, _, _, _) => { },
        recentDataAgentTrace: () => "DataAgent trace diagnostics\n1 RouteGate Succeeded reason=route_allowed");

    Assert.Multiple(() =>
    {
        Assert.That(handled, Is.True);
        Assert.That(sent.Single(), Does.Contain("DataAgent trace diagnostics"));
        Assert.That(sent.Single(), Does.Contain("RouteGate Succeeded"));
    });
}
```

Use the local helper names already present in `QChatOwnerCommandServiceTests.cs` for message creation. If the helper is named differently, use the existing private-message helper in that file without adding a second helper.

- [ ] **Step 3: Run QChat tests to verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "Name=FormatSummaryIncludesDataAgentTraceRecentLine|Name=TryHandleDataAgentTraceDiagnosticsShowsRecentTraceForOwner|Name=TryHandleDataAgentTraceDiagnosticsReturnsUnavailableWhenNoTraceExists|Name=TryHandleDataAgentTraceDiagnosticsPrefersSessionCacheOverLegacyTrace|Name=TryHandleDataAgentTraceDiagnosticsRedactsUnsafeLegacyFallbackText|Name=TryHandleDiagnosticsCommandAsyncPassesRecentTraceToOwnerDiagnostics" -v:minimal
```

Expected: build fails because `DataAgentTrace` kind and `RecentDataAgentTrace` runtime state do not exist.

- [ ] **Step 4: Add `DataAgentTrace` to recent diagnostics cache and formatter**

Modify `QChatRecentDiagnosticKind` in `QChatRecentDiagnosticsCache.cs`:

```csharp
public enum QChatRecentDiagnosticKind
{
    SemanticState,
    DataAgentEvidence,
    DataAgentTrace,
    ToolRoute
}
```

Modify `QChatRecentDiagnosticsFormatter.FormatSummary` to include the trace line between evidence and tool route:

```csharp
return string.Join(Environment.NewLine,
    "QChat recent diagnostics",
    FormatKindLine("semantic_state_recent", entries, QChatRecentDiagnosticKind.SemanticState, now),
    FormatKindLine("dataagent_evidence_recent", entries, QChatRecentDiagnosticKind.DataAgentEvidence, now),
    FormatKindLine("dataagent_trace_recent", entries, QChatRecentDiagnosticKind.DataAgentTrace, now),
    FormatKindLine("tool_route_recent", entries, QChatRecentDiagnosticKind.ToolRoute, now),
    "session=" + NormalizeToken(sessionKey));
```

Modify `Title`:

```csharp
QChatRecentDiagnosticKind.DataAgentTrace => "DataAgent trace diagnostics",
```

- [ ] **Step 5: Add runtime state and diagnostics command handling**

Modify `QChatDiagnosticsRuntimeState` in `QChatDiagnosticsService.cs`:

```csharp
string? RecentDataAgentTrace = null,
```

Place it after `RecentDataAgentEvidence`.

Update DataAgent command switch:

```csharp
if (dataAgentCommand)
    return command.ToLowerInvariant() switch
    {
        "diag evidence" or "diagnostics evidence" => Handled(BuildDataAgentEvidenceDiagnosticsText(runtimeState, route)),
        "diag trace" or "diagnostics trace" => Handled(BuildDataAgentTraceDiagnosticsText(runtimeState, route)),
        _ => new QChatDiagnosticsResult(false, string.Empty)
    };
```

Update QChat command switch:

```csharp
"diag dataagent trace" or "diagnostics dataagent trace" => Handled(BuildDataAgentTraceDiagnosticsText(runtimeState, route)),
```

Add method after `BuildDataAgentEvidenceDiagnosticsText`:

```csharp
static string BuildDataAgentTraceDiagnosticsText(QChatDiagnosticsRuntimeState runtimeState, QChatAgentRoute route)
{
    string? cached = GetRecentCachedText(runtimeState, route, QChatRecentDiagnosticKind.DataAgentTrace);
    if (string.IsNullOrWhiteSpace(cached) == false)
        return cached;

    string sanitized = SanitizeDiagnosticText(
        runtimeState.RecentDataAgentTrace,
        "DataAgent trace diagnostics",
        maxChars: 1800);
    return string.IsNullOrWhiteSpace(sanitized)
        ? string.Join(Environment.NewLine,
            "DataAgent trace diagnostics",
            "state=unavailable",
            "reason=trace_unavailable")
        : sanitized;
}
```

Change sanitizer helper signature:

```csharp
static string SanitizeDiagnosticText(string? text, string title, int maxChars = 900)
{
    return QChatDiagnosticTextSanitizer.SanitizeDiagnosticText(text, title, maxChars);
}
```

- [ ] **Step 6: Update owner command service**

Modify `IsDiagnosticsCommand` to allow trace commands:

```csharp
return command.Equals("diag evidence", StringComparison.OrdinalIgnoreCase)
       || command.Equals("diagnostics evidence", StringComparison.OrdinalIgnoreCase)
       || command.Equals("diag trace", StringComparison.OrdinalIgnoreCase)
       || command.Equals("diagnostics trace", StringComparison.OrdinalIgnoreCase);
```

Modify `TryHandleDiagnosticsCommandAsync` signature:

```csharp
Func<string>? recentDataAgentTrace = null,
```

Place after `recentDataAgentEvidence`.

Pass into runtime state:

```csharp
RecentDataAgentTrace: recentDataAgentTrace?.Invoke(),
```

- [ ] **Step 7: Run QChat diagnostics tests to verify GREEN**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "Name=FormatSummaryIncludesDataAgentTraceRecentLine|Name=TryHandleDataAgentTraceDiagnosticsShowsRecentTraceForOwner|Name=TryHandleDataAgentTraceDiagnosticsReturnsUnavailableWhenNoTraceExists|Name=TryHandleDataAgentTraceDiagnosticsPrefersSessionCacheOverLegacyTrace|Name=TryHandleDataAgentTraceDiagnosticsRedactsUnsafeLegacyFallbackText|Name=TryHandleDiagnosticsCommandAsyncPassesRecentTraceToOwnerDiagnostics" -v:minimal
```

Expected: new QChat trace diagnostics tests pass with 0 failures.

- [ ] **Step 8: Commit Task 5**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.QChat/QChatRecentDiagnosticsCache.cs sources/Alife.Function/Alife.Function.QChat/QChatRecentDiagnosticsFormatter.cs sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs sources/Alife.Function/Alife.Function.QChat/QChatOwnerCommandService.cs Tests/Alife.Test.QChat/QChatRecentDiagnosticsCacheTests.cs Tests/Alife.Test.QChat/QChatDiagnosticsServiceTests.cs Tests/Alife.Test.QChat/QChatOwnerCommandServiceTests.cs
git commit -m "Add QChat DataAgent trace diagnostics command"
```

---

### Task 6: Record Trace Diagnostics In QChat Runtime Session Cache

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
- Test: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`

- [ ] **Step 1: Add failing service adapter tests**

Add tests near existing recent evidence diagnostics tests in `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`:

```csharp
[Test]
public async Task OwnerDataAgentTraceDiagnosticsReadsRecentSessionCache()
{
    using StartedQChatService started = await CreateStartedService();
    QChatService service = started.Service;

    service.RecordRecentDataAgentTraceDiagnostics(string.Join(Environment.NewLine,
        "DataAgent trace diagnostics",
        "session=qq:xiayu:2905391496:private:3045846738",
        "1 RouteGate Succeeded reason=route_allowed"));

    await started.EmitPrivateMessageAsync(
        userId: 3045846738,
        rawMessage: "/dataagent diag trace");

    string reply = started.Runtime.SentMessages.Last().Message;

    Assert.Multiple(() =>
    {
        Assert.That(reply, Does.Contain("DataAgent trace diagnostics"));
        Assert.That(reply, Does.Contain("RouteGate Succeeded"));
    });
}

[Test]
public async Task RecentDiagnosticsSummaryIncludesDataAgentTraceAfterTraceRecorded()
{
    using StartedQChatService started = await CreateStartedService();
    QChatService service = started.Service;

    service.RecordRecentDataAgentTraceDiagnostics(string.Join(Environment.NewLine,
        "DataAgent trace diagnostics",
        "1 RouteGate Succeeded reason=route_allowed"));

    await started.EmitPrivateMessageAsync(
        userId: 3045846738,
        rawMessage: "/qchat diag recent");

    string reply = started.Runtime.SentMessages.Last().Message;

    Assert.Multiple(() =>
    {
        Assert.That(reply, Does.Contain("QChat recent diagnostics"));
        Assert.That(reply, Does.Contain("dataagent_trace_recent=available"));
    });
}
```

Use the existing service adapter helpers in that file. If the local helper names are `CreateStartedService`, `EmitPrivateMessageAsync`, or `SentMessages` with slightly different wrappers, follow the nearby V2.7 evidence diagnostics tests and only change the diagnostics command and assertions.

- [ ] **Step 2: Run adapter tests to verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "Name=OwnerDataAgentTraceDiagnosticsReadsRecentSessionCache|Name=RecentDiagnosticsSummaryIncludesDataAgentTraceAfterTraceRecorded" -v:minimal
```

Expected: build fails because `QChatService.RecordRecentDataAgentTraceDiagnostics` does not exist.

- [ ] **Step 3: Add QChatService trace diagnostics storage**

Modify `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`.

Add fields near `recentDataAgentEvidenceDiagnostics`:

```csharp
readonly object dataAgentTraceDiagnosticsGate = new();
string recentDataAgentTraceDiagnostics = string.Empty;
```

Update `TryHandleQChatDiagnosticsCommandAsync` call:

```csharp
GetRecentDataAgentEvidenceDiagnostics,
GetRecentDataAgentTraceDiagnostics,
recentDiagnosticsCache);
```

Add methods near `RecordRecentDataAgentEvidenceDiagnostics`:

```csharp
public void RecordRecentDataAgentTraceDiagnostics(string? diagnostics)
{
    string normalized = NormalizeCachedDiagnosticText(diagnostics);
    lock (dataAgentTraceDiagnosticsGate)
    {
        recentDataAgentTraceDiagnostics = normalized;
    }

    functionService.RecordRecentDataAgentTraceDiagnostics(normalized);
    QChatReplySession? replySession = GetCurrentReplySessionForGuard();
    recentDiagnosticsCache.Record(
        QChatRecentDiagnosticKind.DataAgentTrace,
        replySession != null
            ? BuildRecentDiagnosticsSessionKey(replySession)
            : BuildOwnerPrivateRecentDiagnosticsSessionKey(),
        "dataagent_trace",
        normalized,
        DateTimeOffset.UtcNow);
}

string GetRecentDataAgentTraceDiagnostics()
{
    lock (dataAgentTraceDiagnosticsGate)
    {
        if (string.IsNullOrWhiteSpace(recentDataAgentTraceDiagnostics) == false)
            return recentDataAgentTraceDiagnostics;
    }

    return functionService.RecentDataAgentTraceDiagnostics;
}
```

- [ ] **Step 4: Update DataAgentModuleService callback target if needed**

If `DataAgentModuleService` is expected to publish into QChat service rather than directly into `XmlFunctionCaller`, keep Task 4's callback to `functionService.RecordRecentDataAgentTraceDiagnostics`. QChat service reads the FunctionCaller fallback through `GetRecentDataAgentTraceDiagnostics`, matching the existing evidence behavior.

- [ ] **Step 5: Run adapter tests to verify GREEN**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "Name=OwnerDataAgentTraceDiagnosticsReadsRecentSessionCache|Name=RecentDiagnosticsSummaryIncludesDataAgentTraceAfterTraceRecorded" -v:minimal
```

Expected: new QChat service adapter trace tests pass with 0 failures.

- [ ] **Step 6: Commit Task 6**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.QChat/QChatService.cs Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs
git commit -m "Record DataAgent trace diagnostics in QChat"
```

---

### Task 7: Add Readiness And Engineering Map Required Gates

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
- Modify: `tools/check-dataagent-readiness.ps1`
- Modify: `tools/check-qchat-engineering-map.ps1`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
- Modify: `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`

- [ ] **Step 1: Update tests for required count changes**

In `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`, update the required readiness count assertion from 74 to 75. Add assertion that output includes:

```text
DataAgentTraceTimelinePresent
```

In `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`, update the required engineering map count from 50 to 51. Add assertion that output includes:

```text
DataAgent trace diagnostics
```

- [ ] **Step 2: Run readiness tests to verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests" -v:minimal
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatEngineeringMapRequiredV2Tests" -v:minimal
```

Expected: readiness and engineering map tests fail because scripts and runtime readiness do not include trace gates.

- [ ] **Step 3: Add runtime readiness proof**

In `DataAgentReadiness.CheckCore`, after `DataAgentEvidenceDiagnosticsPresent`, add a runtime check:

```csharp
DataAgentEvidencePack traceEvidencePack = new DataAgentEvidencePackBuilder().Build(acceptedOrchestration);
DataAgentTraceTimeline traceTimeline = new DataAgentTraceTimelineBuilder().Build(
    acceptedOrchestration,
    traceEvidencePack,
    DateTimeOffset.UtcNow);
string traceDiagnostics = DataAgentTraceDiagnosticsFormatter.Format(traceTimeline);
bool traceTimelineReady =
    traceTimeline.Events.Any(traceEvent => traceEvent.Kind == DataAgentTraceEventKind.RouteGate) &&
    traceTimeline.Events.Any(traceEvent => traceEvent.Kind == DataAgentTraceEventKind.Execute) &&
    traceTimeline.Events.Any(traceEvent => traceEvent.Kind == DataAgentTraceEventKind.EvidencePack) &&
    traceTimeline.Events.Any(traceEvent => traceEvent.Kind == DataAgentTraceEventKind.Checkpoint) &&
    traceDiagnostics.Contains("DataAgent trace diagnostics", StringComparison.Ordinal) &&
    traceDiagnostics.Contains("sql=redacted", StringComparison.Ordinal) &&
    traceDiagnostics.Contains("[data_agent_evidence_pack]", StringComparison.OrdinalIgnoreCase) == false;
checks.Add(traceTimelineReady
    ? Pass("DataAgentTraceTimelinePresent", "trace_timeline=true;owner_diag=true;sql_redacted=true")
    : Fail("DataAgentTraceTimelinePresent", traceDiagnostics));
```

Use the existing accepted orchestration variable names in `DataAgentReadiness.cs`. If the accepted result variable is named differently, use the local accepted orchestration result already used for evidence pack readiness.

- [ ] **Step 4: Add readiness script gate**

In `tools/check-dataagent-readiness.ps1`, add this required check after `DataAgentEvidenceRecentDiagnosticsBridgePresent`:

```powershell
New-Check -Group "Analysis" -Name "DataAgentTraceTimelinePresent" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentTraceModels.cs" @("DataAgentTraceEvent", "DataAgentTraceTimeline", "DataAgentTraceEventKind")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentTraceRecorder.cs" @("DataAgentTraceRecorder", "GetLatest", "GetRecent", "PruneExpiredLocked")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentTraceDiagnosticsFormatter.cs" @("DataAgent trace diagnostics", "trace_unavailable", "sql=redacted")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs" @("traceDiagnosticsPublisher", "DataAgentTraceTimelineBuilder", "DataAgentTraceDiagnosticsFormatter.Format")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentTraceRecorderTests.cs" @("GetLatestReturnsNewestTimelineForSession", "ReadsFilterExpiredTimelinesWithoutRemovingThem")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentTraceDiagnosticsFormatterTests.cs" @("FormatEmitsStableTimelineDiagnostics", "FormatRedactsUnsafeFactValues"))) -Detail "DataAgent trace timeline diagnostics markers"
```

Update expected required count:

```powershell
$expectedRequired = 75
```

- [ ] **Step 5: Add QChat engineering map gate**

In `tools/check-qchat-engineering-map.ps1`, add this Harness check after `QChat diagnostics cache redaction`:

```powershell
Add-Check -Group "Harness" -Name "DataAgent trace diagnostics" -Path "sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs" -Patterns @("RecentDataAgentTrace", "diag trace", "BuildDataAgentTraceDiagnosticsText", "DataAgent trace diagnostics")
```

Update expected required count at the bottom from 50 to 51.

- [ ] **Step 6: Run readiness and map tests to verify GREEN**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests" -v:minimal
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatEngineeringMapRequiredV2Tests" -v:minimal
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected:

```text
DataAgentReadinessTests: 0 failed
QChatEngineeringMapRequiredV2Tests: 0 failed
check-dataagent-readiness.ps1: 75 required passed, 0 required missing
check-qchat-engineering-map.ps1: 51 required passed, 0 required missing
```

- [ ] **Step 7: Commit Task 7**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs tools/check-dataagent-readiness.ps1 tools/check-qchat-engineering-map.ps1 Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs
git commit -m "Require DataAgent trace diagnostics readiness"
```

---

### Task 8: Focused Verification And Code Review

**Files:**
- Verify all changed V2.8 files.

- [ ] **Step 1: Run focused DataAgent tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentTraceRecorderTests|FullyQualifiedName~DataAgentTraceDiagnosticsFormatterTests|FullyQualifiedName~DataAgentAnalysisToolHandlerTests|FullyQualifiedName~DataAgentReadinessTests" -v:minimal
```

Expected: all selected DataAgent tests pass with 0 failures.

- [ ] **Step 2: Run focused QChat tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatRecentDiagnosticsCacheTests|FullyQualifiedName~QChatDiagnosticsServiceTests|FullyQualifiedName~QChatOwnerCommandServiceTests|FullyQualifiedName~QChatServiceAdapterTests|FullyQualifiedName~QChatEngineeringMapRequiredV2Tests" -v:minimal
```

Expected: all selected QChat tests pass with 0 failures.

- [ ] **Step 3: Run readiness scripts**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected:

```text
Summary: 75 required passed, 0 required missing
Summary: 51 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 4: Run diff hygiene**

Run:

```powershell
git diff --check
git status --short --branch
```

Expected: `git diff --check` exits 0. Branch status shows only intended V2.8 trace timeline changes before final commits, then clean after commits.

- [ ] **Step 5: Request code review**

Request a subagent review with this scope:

```text
DESCRIPTION: DataAgent V2.8 in-memory trace timeline diagnostics.
REQUIREMENTS: owner-only, read-only, no new SQL/model/tool calls from diagnostics, DataAgent does not reference QChat, trace text redacts SQL/tokens/connection strings/hidden context, recent cache remains session-scoped and non-mutating.
BASE_SHA: commit before Task 1
HEAD_SHA: current branch HEAD
```

Reviewer must check:

- No DataAgent -> QChat reference.
- Diagnostics commands are owner-only.
- No raw SQL leaks in trace formatter or QChat fallback.
- Trace recording is observational and best-effort.
- Readiness gate counts are correct.

- [ ] **Step 6: Fix any Critical or Important review findings**

For each accepted finding:

1. Write or extend a failing test.
2. Run it and verify RED.
3. Patch the minimal production code.
4. Run it and verify GREEN.
5. Commit with a focused message.

Do not proceed to merge with open Critical or Important findings.

---

### Task 9: Full Verification, Merge, Upload, And Cleanup

**Files:**
- Full repository verification.
- Git branch integration.

- [ ] **Step 1: Run full solution tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore -v:minimal -m:1
```

Expected: full solution passes with 0 failures. Environment-gated live tests may skip.

- [ ] **Step 2: Verify final branch hygiene**

Run:

```powershell
git diff --check
git status --short --branch
git log --oneline -10
```

Expected: diff check exits 0; branch is clean and ahead of master only by V2.8 commits.

- [ ] **Step 3: Merge to master**

From `D:\Alife`:

```powershell
git status --short --branch
git merge dataagent-v2.8-trace-timeline
```

Expected: merge succeeds. Prefer fast-forward if no intervening master commits exist.

- [ ] **Step 4: Re-run post-merge focused verification on master**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentTraceRecorderTests|FullyQualifiedName~DataAgentTraceDiagnosticsFormatterTests|FullyQualifiedName~DataAgentAnalysisToolHandlerTests|FullyQualifiedName~DataAgentReadinessTests" -v:minimal
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatRecentDiagnosticsCacheTests|FullyQualifiedName~QChatDiagnosticsServiceTests|FullyQualifiedName~QChatOwnerCommandServiceTests|FullyQualifiedName~QChatServiceAdapterTests|FullyQualifiedName~QChatEngineeringMapRequiredV2Tests" -v:minimal
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore -v:minimal -m:1
git diff --check
```

Expected: all commands exit 0, with live/environment-gated skips only.

- [ ] **Step 5: Push only to Alife-byastralfox**

Run:

```powershell
git push alife-byastralfox master
git ls-remote alife-byastralfox refs/heads/master
```

Expected: remote `refs/heads/master` points to the merged V2.8 commit.

Do not use `D:\FOXD`. Do not upload to `ASRRAL-FOX`.

- [ ] **Step 6: Remove feature worktree and branch after successful push**

Run:

```powershell
git worktree remove "D:\Alife\.worktrees\dataagent-v2.8-trace-timeline"
git worktree prune
git branch -d dataagent-v2.8-trace-timeline
```

If Windows reports `Filename too long` after unregistering the worktree, confirm the path is exactly under `D:\Alife\.worktrees\dataagent-v2.8-trace-timeline`, then remove the leftover directory using a long-path literal:

```powershell
Remove-Item -LiteralPath "\\?\D:\Alife\.worktrees\dataagent-v2.8-trace-timeline" -Recurse -Force
git worktree prune
git branch -d dataagent-v2.8-trace-timeline
```

- [ ] **Step 7: Report completion**

Report:

- Final commit SHA.
- GitHub remote verification SHA.
- Verification commands and pass counts.
- Any skipped live/environment-gated tests.
- Worktree/branch cleanup status.
- What V2.8 adds to Loop Engineering and Harness Engineering.

## Self-Review Notes

- Spec coverage: this plan covers in-memory trace models, recorder, formatter, orchestrator-result timeline building, DataAgent runtime publishing, QChat owner diagnostics commands, QChat recent cache summary, readiness gates, engineering map gates, verification, review, merge, upload, and cleanup.
- Placeholder scan: the plan contains concrete paths, commands, expected failures, expected passes, and code snippets for each code-changing task.
- Type consistency: `DataAgentTraceTimeline`, `DataAgentTraceEvent`, `DataAgentTraceRecorder`, `DataAgentTraceDiagnosticsFormatter`, `RecentDataAgentTrace`, and `QChatRecentDiagnosticKind.DataAgentTrace` are named consistently across tasks.
- Boundary check: DataAgent emits safe strings and stores trace data internally; QChat owns chat diagnostics and recent cache; diagnostics remain owner-only and read-only; Tool Broker remains permission authority.
