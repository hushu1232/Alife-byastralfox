# DataAgent V3.2 Sidecar Progress Bridge Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the DataAgent V3.2 sidecar progress bridge so bounded, untrusted dev sidecar progress can be validated by C#, mapped to `DataAgentProgressEvent`, and published only through the existing `IDataAgentProgressSink` pipeline.

**Architecture:** Add a sidecar-specific progress DTO and bridge service beside the existing graph handshake types. The bridge validates request/session/node/status/reason/message/facts, maps safe events into existing progress events with `ExecutedSql=false`, and lets `DataAgentGraphHandshakeCoordinator` publish safe response `NodeProgress` only after the handshake response itself is accepted.

**Tech Stack:** .NET 9, C# records/classes, NUnit, PowerShell readiness scripts, existing DataAgent graph handshake and progress diagnostics components.

---

## CodeGraph Context Used

`codegraph context -p D:\Alife -n 40 -c 8 "DataAgent graph handshake coordinator progress diagnostics recorder sink sidecar bridge integration"` identified the current structural entry points:

- `DataAgentGraphHandshakeCoordinator` owns `TryHandshake`, response validation, and fallback outcomes.
- `IDataAgentGraphSidecarClient` is the existing sidecar transport boundary.
- `DataAgentProgressRecorder`, `DataAgentProgressDiagnosticsPublisher`, and `DataAgentProgressDiagnosticsFormatter` are the existing C# recorder/publisher/formatter authority path.
- `DataAgentReadiness` and `tools/check-dataagent-readiness.ps1` carry the dynamic/static DataAgent readiness markers.

The local CodeGraph index belongs to `D:\Alife`, not the V3.2 worktree, so V3.1/V3.2 worktree-only file contents must be read from `D:\Alife\.worktrees\dataagent-v3.2-sidecar-progress-bridge` while implementing.

## File Map

- Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphSidecarProgressModels.cs`
  - Defines `DataAgentGraphSidecarProgressStatus`, `DataAgentGraphSidecarProgressEvent`, and `DataAgentGraphSidecarProgressBridgeResult`.
- Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphSidecarProgressBridge.cs`
  - Validates untrusted sidecar progress, maps safe events to `DataAgentProgressEvent`, publishes through optional `IDataAgentProgressSink`, and adapts existing `DataAgentGraphHandshakeProgress` values from handshake responses.
- Create `Tests/Alife.Test.DataAgent/DataAgentGraphSidecarProgressBridgeTests.cs`
  - Direct bridge tests for accepted mapping, rejected unknown node, rejected undefined status, rejected unsafe reason/message/facts, over-budget fail closed behavior, and response-only `NodeProgress` adaptation.
- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeCoordinator.cs`
  - Add optional `DataAgentGraphSidecarProgressBridge` dependency and publish response `NodeProgress` after accepted validation.
- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`
  - Wire the production-created coordinator with `new DataAgentGraphSidecarProgressBridge(progressSink)` while keeping graph handshake disabled by default.
- Modify `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeCoordinatorTests.cs`
  - Add coordinator tests proving disabled/rejected paths do not publish and accepted safe sidecar progress publishes through the bridge.
- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
  - Add dynamic V3.2 readiness check `GraphHandshakeDevSidecarProgressBridgePresent`.
- Modify `tools/check-dataagent-readiness.ps1`
  - Add static V3.2 marker check and update required count from 87 to 88.
- Modify `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
  - Update dynamic count from 73 to 74, script summary from 87 to 88, and add a static V3.2 readiness declaration test.
- Inspect `tools/check-qchat-engineering-map.ps1`
  - Keep QChat required count at 63. The existing `"DataAgentGraphSidecar"` and `"DataAgentGraphHandshake"` omit patterns already cover the new bridge names, so no edit is expected.
- Inspect `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`
  - Leave the test file unchanged; the existing direct-import boundary test already rejects the new `DataAgentGraphSidecarProgress*` names through the broader `DataAgentGraphSidecar` marker.
- Modify `tools/dataagent-graph-sidecar/app.py`
  - Add optional static progress-shape fields to the dev stub response model and align node names with the C# manifest.
- Modify `tools/dataagent-graph-sidecar/README.md`
  - Document V3.2 progress shape, manual-only execution, and C# progress authority.
- Create `docs/dataagent/dataagent-v3.2-sidecar-progress-bridge.md`
  - Developer note for the V3.2 boundary, testing model, and next handoff.
- Modify `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeDevSidecarStubTests.cs`
  - Add static documentation/stub assertions for V3.2 without starting Python.

## Task 1: Add Failing Sidecar Progress Bridge Tests

**Files:**
- Create: `Tests/Alife.Test.DataAgent/DataAgentGraphSidecarProgressBridgeTests.cs`

- [ ] **Step 1: Create the bridge test file**

Add this complete file:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentGraphSidecarProgressBridgeTests
{
    [Test]
    public void PublishMapsAcceptedSidecarProgressThroughSink()
    {
        RecordingProgressSink sink = new();
        DataAgentGraphSidecarProgressBridge bridge = new(sink, Now);
        DataAgentGraphHandshakeRequest request = NewRequest();
        DataAgentOrchestrationResult result = NewResult();

        DataAgentGraphSidecarProgressBridgeResult summary = bridge.Publish(
            request,
            result,
            [
                new DataAgentGraphSidecarProgressEvent(
                    request.RequestId,
                    request.SessionId,
                    DataAgentWorkflowNodeNames.QueryPlanner,
                    DataAgentGraphSidecarProgressStatus.Completed,
                    "planner_suggested",
                    "planner ready",
                    Now().AddMinutes(-5),
                    new Dictionary<string, string>
                    {
                        ["stage"] = "planner"
                    })
            ]);

        DataAgentProgressEvent progress = sink.Events.Single();
        Assert.Multiple(() =>
        {
            Assert.That(summary.AcceptedCount, Is.EqualTo(1));
            Assert.That(summary.RejectedCount, Is.EqualTo(0));
            Assert.That(progress.SessionId, Is.EqualTo("session-1"));
            Assert.That(progress.Kind, Is.EqualTo(DataAgentProgressEventKind.Planner));
            Assert.That(progress.Phase, Is.EqualTo(DataAgentProgressEventPhase.Completed));
            Assert.That(progress.Status, Is.EqualTo(DataAgentProgressEventStatus.Succeeded));
            Assert.That(progress.ReasonCode, Is.EqualTo("planner_suggested"));
            Assert.That(progress.TurnCount, Is.EqualTo(1));
            Assert.That(progress.CreatedAt, Is.EqualTo(Now()));
            Assert.That(progress.ExecutedSql, Is.False);
            Assert.That(progress.QueryAllowed, Is.True);
            Assert.That(progress.Terminal, Is.False);
            Assert.That(progress.Facts["source"], Is.EqualTo("graph_sidecar"));
            Assert.That(progress.Facts["node"], Is.EqualTo(DataAgentWorkflowNodeNames.QueryPlanner));
            Assert.That(progress.Facts["request_id"], Is.EqualTo(request.RequestId));
            Assert.That(progress.Facts["message"], Is.EqualTo("planner ready"));
            Assert.That(progress.Facts["stage"], Is.EqualTo("planner"));
        });
    }

    [Test]
    public void PublishRejectsUnknownNodeWithoutPublishing()
    {
        RecordingProgressSink sink = new();
        DataAgentGraphSidecarProgressBridge bridge = new(sink, Now);
        DataAgentGraphHandshakeRequest request = NewRequest();

        DataAgentGraphSidecarProgressBridgeResult summary = bridge.Publish(
            request,
            NewResult(),
            [
                SafeEvent(request) with
                {
                    NodeName = "unknown_node"
                }
            ]);

        Assert.Multiple(() =>
        {
            Assert.That(summary.AcceptedCount, Is.EqualTo(0));
            Assert.That(summary.RejectedCount, Is.EqualTo(1));
            Assert.That(sink.Events, Is.Empty);
        });
    }

    [Test]
    public void PublishRejectsUndefinedStatusWithoutPublishing()
    {
        RecordingProgressSink sink = new();
        DataAgentGraphSidecarProgressBridge bridge = new(sink, Now);
        DataAgentGraphHandshakeRequest request = NewRequest();

        DataAgentGraphSidecarProgressBridgeResult summary = bridge.Publish(
            request,
            NewResult(),
            [
                SafeEvent(request) with
                {
                    Status = (DataAgentGraphSidecarProgressStatus)999
                }
            ]);

        Assert.Multiple(() =>
        {
            Assert.That(summary.AcceptedCount, Is.EqualTo(0));
            Assert.That(summary.RejectedCount, Is.EqualTo(1));
            Assert.That(sink.Events, Is.Empty);
        });
    }

    [Test]
    public void PublishRejectsUnsafeReasonCodeWithoutPublishing()
    {
        RecordingProgressSink sink = new();
        DataAgentGraphSidecarProgressBridge bridge = new(sink, Now);
        DataAgentGraphHandshakeRequest request = NewRequest();

        DataAgentGraphSidecarProgressBridgeResult summary = bridge.Publish(
            request,
            NewResult(),
            [
                SafeEvent(request) with
                {
                    ReasonCode = "planner suggested"
                }
            ]);

        Assert.Multiple(() =>
        {
            Assert.That(summary.AcceptedCount, Is.EqualTo(0));
            Assert.That(summary.RejectedCount, Is.EqualTo(1));
            Assert.That(sink.Events, Is.Empty);
        });
    }

    [Test]
    public void PublishRejectsUnsafeMessageAndFactsBeforeFormatting()
    {
        RecordingProgressSink sink = new();
        DataAgentGraphSidecarProgressBridge bridge = new(sink, Now);
        DataAgentGraphHandshakeRequest request = NewRequest();

        DataAgentGraphSidecarProgressBridgeResult unsafeMessage = bridge.Publish(
            request,
            NewResult(),
            [
                SafeEvent(request) with
                {
                    Message = "SELECT * FROM engineering_gate"
                }
            ]);
        DataAgentGraphSidecarProgressBridgeResult unsafeFactKey = bridge.Publish(
            request,
            NewResult(),
            [
                SafeEvent(request) with
                {
                    Facts = new Dictionary<string, string>
                    {
                        ["hidden_context"] = "[hidden_context]secret[/hidden_context]"
                    }
                }
            ]);
        DataAgentGraphSidecarProgressBridgeResult unsafeFactValue = bridge.Publish(
            request,
            NewResult(),
            [
                SafeEvent(request) with
                {
                    Facts = new Dictionary<string, string>
                    {
                        ["stage"] = "Bearer sk-test123456"
                    }
                }
            ]);

        Assert.Multiple(() =>
        {
            Assert.That(unsafeMessage.AcceptedCount, Is.EqualTo(0));
            Assert.That(unsafeMessage.RejectedCount, Is.EqualTo(1));
            Assert.That(unsafeFactKey.AcceptedCount, Is.EqualTo(0));
            Assert.That(unsafeFactKey.RejectedCount, Is.EqualTo(1));
            Assert.That(unsafeFactValue.AcceptedCount, Is.EqualTo(0));
            Assert.That(unsafeFactValue.RejectedCount, Is.EqualTo(1));
            Assert.That(sink.Events, Is.Empty);
        });
    }

    [Test]
    public void PublishFailsClosedForOverBudgetInput()
    {
        RecordingProgressSink sink = new();
        DataAgentGraphSidecarProgressBridge bridge = new(sink, Now);
        DataAgentGraphHandshakeRequest request = NewRequest();
        Dictionary<string, string> tooManyFacts = Enumerable.Range(0, 9)
            .ToDictionary(index => $"fact_{index}", index => $"value_{index}");
        DataAgentGraphSidecarProgressEvent[] tooManyEvents = Enumerable.Range(0, DataAgentGraphHandshakeLimits.MaxProgressEvents + 1)
            .Select(_ => SafeEvent(request))
            .ToArray();

        DataAgentGraphSidecarProgressBridgeResult factSummary = bridge.Publish(
            request,
            NewResult(),
            [
                SafeEvent(request) with
                {
                    Facts = tooManyFacts
                }
            ]);
        DataAgentGraphSidecarProgressBridgeResult eventSummary = bridge.Publish(
            request,
            NewResult(),
            tooManyEvents);

        Assert.Multiple(() =>
        {
            Assert.That(factSummary.AcceptedCount, Is.EqualTo(0));
            Assert.That(factSummary.RejectedCount, Is.EqualTo(1));
            Assert.That(eventSummary.AcceptedCount, Is.EqualTo(0));
            Assert.That(eventSummary.RejectedCount, Is.EqualTo(DataAgentGraphHandshakeLimits.MaxProgressEvents + 1));
            Assert.That(sink.Events, Is.Empty);
        });
    }

    [Test]
    public void PublishHandshakeProgressMapsResponseNodeProgress()
    {
        RecordingProgressSink sink = new();
        DataAgentGraphSidecarProgressBridge bridge = new(sink, Now);
        DataAgentGraphHandshakeRequest request = NewRequest();

        DataAgentGraphSidecarProgressBridgeResult summary = bridge.PublishHandshakeProgress(
            request,
            NewResult(),
            [
                new DataAgentGraphHandshakeProgress(
                    DataAgentWorkflowNodeNames.ScenarioKnowledge,
                    DataAgentGraphHandshakeProgressStatus.Started,
                    "scenario_started")
            ]);

        DataAgentProgressEvent progress = sink.Events.Single();
        Assert.Multiple(() =>
        {
            Assert.That(summary.AcceptedCount, Is.EqualTo(1));
            Assert.That(summary.RejectedCount, Is.EqualTo(0));
            Assert.That(progress.Kind, Is.EqualTo(DataAgentProgressEventKind.SchemaContext));
            Assert.That(progress.Phase, Is.EqualTo(DataAgentProgressEventPhase.Started));
            Assert.That(progress.Status, Is.EqualTo(DataAgentProgressEventStatus.Running));
            Assert.That(progress.ExecutedSql, Is.False);
            Assert.That(progress.Facts["source"], Is.EqualTo("graph_sidecar"));
            Assert.That(progress.Facts["node"], Is.EqualTo(DataAgentWorkflowNodeNames.ScenarioKnowledge));
        });
    }

    static DataAgentGraphSidecarProgressEvent SafeEvent(DataAgentGraphHandshakeRequest request)
    {
        return new DataAgentGraphSidecarProgressEvent(
            request.RequestId,
            request.SessionId,
            DataAgentWorkflowNodeNames.QueryPlanner,
            DataAgentGraphSidecarProgressStatus.Completed,
            "planner_suggested",
            "planner ready",
            Now().AddMinutes(-5),
            new Dictionary<string, string>
            {
                ["stage"] = "planner"
            });
    }

    static DataAgentGraphHandshakeRequest NewRequest()
    {
        return new DataAgentGraphHandshakeRequest(
            "graph-handshake-session-1-turn-1",
            "session-1",
            "turn-1",
            "owner",
            "Which gates failed?",
            "scenario_context=deterministic_csharp",
            "route_present=true;route_allows_query=true;route_reason_code=route_allowed",
            "status=Active;executed_sql=false;terminal=false",
            DataAgentGraphHandshakeManifestFactory.CreateDefault(),
            NoSqlAuthority: true,
            ReadOnly: true,
            FallbackAvailable: true,
            TraceBudgetChars: DataAgentGraphHandshakeLimits.MaxTraceSummaryChars,
            ProgressBudget: DataAgentGraphHandshakeLimits.MaxProgressEvents);
    }

    static DataAgentOrchestrationResult NewResult()
    {
        DataAgentAnswer answer = new(
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
        DataAgentAnalysisResponse response = new(
            "session-1",
            DataAgentAnalysisSessionStatus.Active,
            DataAgentAnalysisTurnIntent.NewQuestion,
            answer,
            answer.Summary,
            answer.Context,
            Accepted: true,
            RejectedReason: string.Empty);
        DataAgentOrchestrationCheckpoint checkpoint = new(
            "session-1",
            DataAgentAnalysisSessionStatus.Active,
            "document_index",
            TurnCount: 1,
            CanContinue: true,
            CanSummarize: true,
            Terminal: false);
        DataAgentToolRouteContext routeContext = new(
            true,
            "dataagent_analysis_start",
            true,
            true,
            "route-test",
            "analysis_start",
            "route_allowed",
            string.Empty);

        return new DataAgentOrchestrationResult(
            "session-1",
            DataAgentAnalysisSessionStatus.Active,
            [
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.RouteGate, DataAgentOrchestrationStepStatus.Succeeded, "route_allowed", ExecutedSql: false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Plan, DataAgentOrchestrationStepStatus.Succeeded, "plan_ready", ExecutedSql: false)
            ],
            checkpoint,
            response,
            routeContext);
    }

    static DateTimeOffset Now()
    {
        return new DateTimeOffset(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);
    }

    sealed class RecordingProgressSink : IDataAgentProgressSink
    {
        public List<DataAgentProgressEvent> Events { get; } = [];

        public void Publish(DataAgentProgressEvent? progressEvent)
        {
            if (progressEvent is not null)
                Events.Add(progressEvent);
        }
    }
}
```

- [ ] **Step 2: Run the new tests and verify compile failure**

Run from `D:\Alife\.worktrees\dataagent-v3.2-sidecar-progress-bridge`:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphSidecarProgressBridgeTests" -v:minimal
```

Expected: FAIL with missing type errors for `DataAgentGraphSidecarProgressBridge`, `DataAgentGraphSidecarProgressBridgeResult`, `DataAgentGraphSidecarProgressEvent`, and `DataAgentGraphSidecarProgressStatus`.

- [ ] **Step 3: Commit the failing tests**

```powershell
git add Tests\Alife.Test.DataAgent\DataAgentGraphSidecarProgressBridgeTests.cs
git commit -m "Add DataAgent sidecar progress bridge tests"
```

## Task 2: Implement Sidecar Progress Models And Bridge

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphSidecarProgressModels.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphSidecarProgressBridge.cs`

- [ ] **Step 1: Add the sidecar progress model file**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphSidecarProgressModels.cs`:

```csharp
namespace Alife.Function.DataAgent;

public enum DataAgentGraphSidecarProgressStatus
{
    Started,
    Completed,
    Skipped,
    Rejected,
    Failed
}

public sealed record DataAgentGraphSidecarProgressEvent(
    string RequestId,
    string SessionId,
    string NodeName,
    DataAgentGraphSidecarProgressStatus Status,
    string ReasonCode,
    string Message,
    DateTimeOffset CreatedAt,
    IReadOnlyDictionary<string, string> Facts);

public sealed record DataAgentGraphSidecarProgressBridgeResult(
    int AcceptedCount,
    int RejectedCount);
```

- [ ] **Step 2: Add the bridge implementation file**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphSidecarProgressBridge.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace Alife.Function.DataAgent;

public sealed class DataAgentGraphSidecarProgressBridge
{
    const int MaxNodeNameLength = 128;
    const int MaxMessageLength = 240;
    const int MaxFactCount = 8;
    const int MaxFactKeyLength = 64;
    const int MaxFactValueLength = 160;

    static readonly Regex MachineTokenPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9_.-]*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    static readonly string[] UnsafeFactKeyFragments =
    [
        "hidden",
        "tool_route",
        "evidence_pack",
        "connection",
        "authorization",
        "token",
        "password",
        "pwd",
        "secret",
        "credential",
        "api",
        "key",
        "sql",
        "query",
        "dataset",
        "table"
    ];

    readonly IDataAgentProgressSink? progressSink;
    readonly Func<DateTimeOffset> clock;

    public DataAgentGraphSidecarProgressBridge(
        IDataAgentProgressSink? progressSink = null,
        Func<DateTimeOffset>? clock = null)
    {
        this.progressSink = progressSink;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public DataAgentGraphSidecarProgressBridgeResult PublishHandshakeProgress(
        DataAgentGraphHandshakeRequest request,
        DataAgentOrchestrationResult result,
        IReadOnlyList<DataAgentGraphHandshakeProgress>? progress)
    {
        if (progress is null)
            return Publish(request, result, []);

        List<DataAgentGraphSidecarProgressEvent> events = new(progress.Count);
        foreach (DataAgentGraphHandshakeProgress item in progress)
        {
            if (item is null)
            {
                events.Add(null!);
                continue;
            }

            events.Add(new DataAgentGraphSidecarProgressEvent(
                request.RequestId,
                request.SessionId,
                item.NodeName,
                MapStatus(item.Status),
                item.ReasonCode,
                string.Empty,
                clock(),
                new Dictionary<string, string>()));
        }

        return Publish(request, result, events);
    }

    public DataAgentGraphSidecarProgressBridgeResult Publish(
        DataAgentGraphHandshakeRequest request,
        DataAgentOrchestrationResult result,
        IReadOnlyList<DataAgentGraphSidecarProgressEvent>? events)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);

        if (events is null || events.Count == 0)
            return new DataAgentGraphSidecarProgressBridgeResult(0, 0);

        if (events.Count > request.ProgressBudget ||
            events.Count > DataAgentGraphHandshakeLimits.MaxProgressEvents)
        {
            return new DataAgentGraphSidecarProgressBridgeResult(0, events.Count);
        }

        HashSet<string> manifestNodeNames = request.NodeManifests
            .Select(manifest => manifest.NodeName)
            .ToHashSet(StringComparer.Ordinal);
        int accepted = 0;
        int rejected = 0;

        foreach (DataAgentGraphSidecarProgressEvent progressEvent in events)
        {
            if (TryMap(request, result, manifestNodeNames, progressEvent, clock(), out DataAgentProgressEvent? mapped))
            {
                accepted++;
                progressSink?.Publish(mapped);
            }
            else
            {
                rejected++;
            }
        }

        return new DataAgentGraphSidecarProgressBridgeResult(accepted, rejected);
    }

    static bool TryMap(
        DataAgentGraphHandshakeRequest request,
        DataAgentOrchestrationResult result,
        HashSet<string> manifestNodeNames,
        DataAgentGraphSidecarProgressEvent? progressEvent,
        DateTimeOffset now,
        out DataAgentProgressEvent? mapped)
    {
        mapped = null;

        if (progressEvent is null ||
            IsIdentityMatch(request.RequestId, progressEvent.RequestId, DataAgentGraphHandshakeLimits.MaxRequestIdLength) == false ||
            IsIdentityMatch(request.SessionId, progressEvent.SessionId, DataAgentGraphHandshakeLimits.MaxSessionIdLength) == false ||
            HasBoundedText(progressEvent.NodeName, MaxNodeNameLength) == false ||
            manifestNodeNames.Contains(progressEvent.NodeName) == false ||
            Enum.IsDefined(typeof(DataAgentGraphSidecarProgressStatus), progressEvent.Status) == false ||
            IsMachineToken(progressEvent.ReasonCode, DataAgentGraphHandshakeLimits.MaxReasonCodeLength) == false ||
            IsSafeOptionalText(progressEvent.Message, MaxMessageLength) == false ||
            TryBuildFacts(progressEvent, out IReadOnlyDictionary<string, string>? facts) == false)
        {
            return false;
        }

        mapped = new DataAgentProgressEvent(
            progressEvent.SessionId.Trim(),
            MapKind(progressEvent.NodeName),
            MapPhase(progressEvent.Status),
            MapProgressStatus(progressEvent.Status),
            progressEvent.ReasonCode.Trim(),
            result.Checkpoint.TurnCount,
            now,
            ExecutedSql: false,
            QueryAllowed: result.RouteContext?.AllowsQuery == true,
            Terminal: result.Checkpoint.Terminal || string.Equals(progressEvent.NodeName, DataAgentWorkflowNodeNames.Terminal, StringComparison.Ordinal),
            facts!);

        return true;
    }

    static bool TryBuildFacts(
        DataAgentGraphSidecarProgressEvent progressEvent,
        out IReadOnlyDictionary<string, string>? facts)
    {
        facts = null;
        if (progressEvent.Facts is null ||
            progressEvent.Facts.Count > MaxFactCount)
        {
            return false;
        }

        Dictionary<string, string> safeFacts = new(StringComparer.Ordinal)
        {
            ["source"] = "graph_sidecar",
            ["node"] = progressEvent.NodeName.Trim(),
            ["request_id"] = progressEvent.RequestId.Trim()
        };

        if (string.IsNullOrWhiteSpace(progressEvent.Message) == false)
            safeFacts["message"] = DataAgentContextFieldSanitizer.Sanitize(progressEvent.Message.Trim(), MaxMessageLength);

        foreach (KeyValuePair<string, string> fact in progressEvent.Facts)
        {
            if (TryNormalizeFact(fact, out string? key, out string? value) == false ||
                safeFacts.ContainsKey(key!))
            {
                return false;
            }

            safeFacts[key!] = value!;
        }

        facts = new ReadOnlyDictionary<string, string>(safeFacts);
        return true;
    }

    static bool TryNormalizeFact(
        KeyValuePair<string, string> fact,
        out string? key,
        out string? value)
    {
        key = null;
        value = null;

        if (IsMachineToken(fact.Key, MaxFactKeyLength) == false ||
            UnsafeFactKeyFragments.Any(fragment => fact.Key.Contains(fragment, StringComparison.OrdinalIgnoreCase)) ||
            IsSafeOptionalText(fact.Value, MaxFactValueLength) == false ||
            string.IsNullOrWhiteSpace(fact.Value))
        {
            return false;
        }

        key = fact.Key.Trim();
        value = DataAgentContextFieldSanitizer.Sanitize(fact.Value.Trim(), MaxFactValueLength);
        return true;
    }

    static bool IsIdentityMatch(string expected, string actual, int maxLength)
    {
        return HasBoundedText(actual, maxLength) &&
               string.Equals(expected, actual.Trim(), StringComparison.Ordinal);
    }

    static bool HasBoundedText(string? value, int maxLength)
    {
        return string.IsNullOrWhiteSpace(value) == false &&
               value.Length <= maxLength;
    }

    static bool IsSafeOptionalText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        return value.Length <= maxLength &&
               DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText(value) == false;
    }

    static bool IsMachineToken(string? value, int maxLength)
    {
        return HasBoundedText(value, maxLength) &&
               MachineTokenPattern.IsMatch(value!);
    }

    static DataAgentProgressEventKind MapKind(string nodeName)
    {
        return nodeName switch
        {
            DataAgentWorkflowNodeNames.RouteGate => DataAgentProgressEventKind.RouteGate,
            DataAgentWorkflowNodeNames.ScenarioKnowledge => DataAgentProgressEventKind.SchemaContext,
            DataAgentWorkflowNodeNames.QueryPlanner => DataAgentProgressEventKind.Planner,
            DataAgentWorkflowNodeNames.QueryPlanValidator => DataAgentProgressEventKind.Validate,
            DataAgentWorkflowNodeNames.SqlSafety => DataAgentProgressEventKind.SqlSafety,
            DataAgentWorkflowNodeNames.ReadOnlyExecute => DataAgentProgressEventKind.Execute,
            DataAgentWorkflowNodeNames.ResultExplainer => DataAgentProgressEventKind.Explain,
            DataAgentWorkflowNodeNames.DiagnosticsRouter => DataAgentProgressEventKind.Explain,
            DataAgentWorkflowNodeNames.CheckpointProgress => DataAgentProgressEventKind.Checkpoint,
            DataAgentWorkflowNodeNames.Terminal => DataAgentProgressEventKind.End,
            DataAgentWorkflowNodeNames.Reject => DataAgentProgressEventKind.Reject,
            _ => DataAgentProgressEventKind.Explain
        };
    }

    static DataAgentProgressEventPhase MapPhase(DataAgentGraphSidecarProgressStatus status)
    {
        return status == DataAgentGraphSidecarProgressStatus.Started
            ? DataAgentProgressEventPhase.Started
            : DataAgentProgressEventPhase.Completed;
    }

    static DataAgentProgressEventStatus MapProgressStatus(DataAgentGraphSidecarProgressStatus status)
    {
        return status switch
        {
            DataAgentGraphSidecarProgressStatus.Started => DataAgentProgressEventStatus.Running,
            DataAgentGraphSidecarProgressStatus.Completed => DataAgentProgressEventStatus.Succeeded,
            DataAgentGraphSidecarProgressStatus.Skipped => DataAgentProgressEventStatus.Skipped,
            DataAgentGraphSidecarProgressStatus.Rejected => DataAgentProgressEventStatus.Rejected,
            DataAgentGraphSidecarProgressStatus.Failed => DataAgentProgressEventStatus.Failed,
            _ => DataAgentProgressEventStatus.Failed
        };
    }

    static DataAgentGraphSidecarProgressStatus MapStatus(DataAgentGraphHandshakeProgressStatus status)
    {
        return status switch
        {
            DataAgentGraphHandshakeProgressStatus.Started => DataAgentGraphSidecarProgressStatus.Started,
            DataAgentGraphHandshakeProgressStatus.Completed => DataAgentGraphSidecarProgressStatus.Completed,
            DataAgentGraphHandshakeProgressStatus.Skipped => DataAgentGraphSidecarProgressStatus.Skipped,
            DataAgentGraphHandshakeProgressStatus.Rejected => DataAgentGraphSidecarProgressStatus.Rejected,
            DataAgentGraphHandshakeProgressStatus.Failed => DataAgentGraphSidecarProgressStatus.Failed,
            _ => (DataAgentGraphSidecarProgressStatus)999
        };
    }
}
```

- [ ] **Step 3: Run bridge tests and verify pass**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphSidecarProgressBridgeTests" -v:minimal
```

Expected: PASS for `DataAgentGraphSidecarProgressBridgeTests`.

- [ ] **Step 4: Commit bridge implementation**

```powershell
git add sources\Alife.Function\Alife.Function.DataAgent\DataAgentGraphSidecarProgressModels.cs sources\Alife.Function\Alife.Function.DataAgent\DataAgentGraphSidecarProgressBridge.cs
git commit -m "Add DataAgent sidecar progress bridge"
```

## Task 3: Wire The Bridge Into Graph Handshake Coordinator

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeCoordinator.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeCoordinatorTests.cs`

- [ ] **Step 1: Add failing coordinator progress tests**

Append these tests inside `DataAgentGraphHandshakeCoordinatorTests` before `ConstructorRejectsNullOptions`:

```csharp
[Test]
public void EnabledCoordinatorPublishesAcceptedResponseNodeProgressThroughBridge()
{
    RecordingSidecarClient sidecar = new(NewAcceptedResponse);
    RecordingProgressSink progressSink = new();
    DataAgentGraphHandshakeCoordinator coordinator = new(
        new DataAgentGraphHandshakeOptions(true),
        sidecar,
        new DataAgentGraphSidecarProgressBridge(progressSink, Now));

    DataAgentGraphHandshakeOutcome outcome = coordinator.TryHandshake(
        "owner",
        "Which gates failed?",
        AcceptedResult());

    DataAgentProgressEvent progress = progressSink.Events.Single();
    Assert.Multiple(() =>
    {
        Assert.That(outcome.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Accepted));
        Assert.That(progress.Kind, Is.EqualTo(DataAgentProgressEventKind.Planner));
        Assert.That(progress.Status, Is.EqualTo(DataAgentProgressEventStatus.Succeeded));
        Assert.That(progress.ReasonCode, Is.EqualTo("planner_suggested"));
        Assert.That(progress.CreatedAt, Is.EqualTo(Now()));
        Assert.That(progress.ExecutedSql, Is.False);
        Assert.That(progress.Facts["source"], Is.EqualTo("graph_sidecar"));
        Assert.That(progress.Facts["node"], Is.EqualTo(DataAgentWorkflowNodeNames.QueryPlanner));
    });
}

[Test]
public void RejectedCoordinatorOutcomeDoesNotPublishSidecarProgress()
{
    RecordingSidecarClient sidecar = new(request => NewAcceptedResponse(request) with
    {
        NodeProgress:
        [
            new DataAgentGraphHandshakeProgress("unknown_node", DataAgentGraphHandshakeProgressStatus.Completed, "unknown_done")
        ]
    });
    RecordingProgressSink progressSink = new();
    DataAgentGraphHandshakeCoordinator coordinator = new(
        new DataAgentGraphHandshakeOptions(true),
        sidecar,
        new DataAgentGraphSidecarProgressBridge(progressSink, Now));

    DataAgentGraphHandshakeOutcome outcome = coordinator.TryHandshake(
        "owner",
        "Which gates failed?",
        AcceptedResult());

    Assert.Multiple(() =>
    {
        Assert.That(outcome.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Rejected));
        Assert.That(outcome.ReasonCode, Is.EqualTo("progress_invalid"));
        Assert.That(outcome.Response, Is.Null);
        Assert.That(progressSink.Events, Is.Empty);
    });
}

[Test]
public void DisabledCoordinatorDoesNotPublishSidecarProgress()
{
    RecordingSidecarClient sidecar = new(NewAcceptedResponse);
    RecordingProgressSink progressSink = new();
    DataAgentGraphHandshakeCoordinator coordinator = new(
        DataAgentGraphHandshakeOptions.Disabled,
        sidecar,
        new DataAgentGraphSidecarProgressBridge(progressSink, Now));

    DataAgentGraphHandshakeOutcome outcome = coordinator.TryHandshake(
        "owner",
        "Which gates failed?",
        AcceptedResult());

    Assert.Multiple(() =>
    {
        Assert.That(outcome.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Disabled));
        Assert.That(sidecar.Requests, Is.Empty);
        Assert.That(progressSink.Events, Is.Empty);
    });
}
```

Add these helpers near the existing nested helper classes:

```csharp
static DateTimeOffset Now()
{
    return new DateTimeOffset(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);
}

sealed class RecordingProgressSink : IDataAgentProgressSink
{
    public List<DataAgentProgressEvent> Events { get; } = [];

    public void Publish(DataAgentProgressEvent? progressEvent)
    {
        if (progressEvent is not null)
            Events.Add(progressEvent);
    }
}
```

- [ ] **Step 2: Run coordinator tests and verify constructor failure**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeCoordinatorTests" -v:minimal
```

Expected: FAIL with a constructor overload error for `DataAgentGraphHandshakeCoordinator`.

- [ ] **Step 3: Add optional bridge dependency to the coordinator**

In `DataAgentGraphHandshakeCoordinator.cs`, replace the primary constructor and fields with:

```csharp
public sealed class DataAgentGraphHandshakeCoordinator(
    DataAgentGraphHandshakeOptions options,
    IDataAgentGraphSidecarClient? sidecarClient = null,
    DataAgentGraphSidecarProgressBridge? progressBridge = null)
{
    readonly DataAgentGraphHandshakeOptions options = options ?? throw new ArgumentNullException(nameof(options));
    readonly IDataAgentGraphSidecarClient sidecarClient = sidecarClient ?? DisabledDataAgentGraphSidecarClient.Instance;
    readonly DataAgentGraphSidecarProgressBridge? progressBridge = progressBridge;
```

After accepted response validation and before returning `Accepted`, insert:

```csharp
progressBridge?.PublishHandshakeProgress(request!, result, response.NodeProgress);
```

The accepted branch should become:

```csharp
progressBridge?.PublishHandshakeProgress(request!, result, response.NodeProgress);

return Outcome(
    DataAgentGraphHandshakeStatus.Accepted,
    validation.ReasonCode,
    response.FallbackRequired,
    request,
    response,
    validation);
```

- [ ] **Step 4: Wire module service to the existing progress sink**

In `DataAgentModuleService.cs`, replace the coordinator construction with:

```csharp
DataAgentGraphHandshakeCoordinator graphHandshakeCoordinator = new(
    graphHandshakeOptions,
    CreateGraphHandshakeSidecarClient(graphHandshakeOptions, graphHandshakeHttpOptions),
    new DataAgentGraphSidecarProgressBridge(progressSink));
```

This keeps graph handshake disabled by default, keeps Python manual-only, and makes configured dev sidecar progress flow through the same C# owner diagnostics sink as deterministic progress.

- [ ] **Step 5: Run coordinator tests and verify pass**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeCoordinatorTests" -v:minimal
```

Expected: PASS for `DataAgentGraphHandshakeCoordinatorTests`.

- [ ] **Step 6: Commit coordinator integration**

```powershell
git add sources\Alife.Function\Alife.Function.DataAgent\DataAgentGraphHandshakeCoordinator.cs sources\Alife.Function\Alife.Function.DataAgent\DataAgentModuleService.cs Tests\Alife.Test.DataAgent\DataAgentGraphHandshakeCoordinatorTests.cs
git commit -m "Wire DataAgent graph sidecar progress bridge"
```

## Task 4: Add V3.2 Readiness And QChat Boundary Protection

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
- Modify: `tools/check-dataagent-readiness.ps1`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
- Inspect: `tools/check-qchat-engineering-map.ps1`
- Inspect: `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`

- [ ] **Step 1: Add failing readiness assertions**

In `DataAgentReadinessTests.CoreReadinessChecksAllPass`, change:

```csharp
Assert.That(checks, Has.Count.EqualTo(73));
```

to:

```csharp
Assert.That(checks, Has.Count.EqualTo(74));
```

After the existing `GraphHandshakeDevSidecarAdapterPresent` assertions, add:

```csharp
Assert.That(checks.Select(check => check.Name), Does.Contain("GraphHandshakeDevSidecarProgressBridgePresent"));
DataAgentReadinessCheck graphHandshakeProgressBridgeCheck = checks.Single(check => check.Name == "GraphHandshakeDevSidecarProgressBridgePresent");
Assert.That(graphHandshakeProgressBridgeCheck.Detail, Does.Contain("default_enabled=false"));
Assert.That(graphHandshakeProgressBridgeCheck.Detail, Does.Contain("progress_bridge=true"));
Assert.That(graphHandshakeProgressBridgeCheck.Detail, Does.Contain("csharp_recorder_authority=true"));
Assert.That(graphHandshakeProgressBridgeCheck.Detail, Does.Contain("unsafe_progress_rejected=true"));
Assert.That(graphHandshakeProgressBridgeCheck.Detail, Does.Contain("unsafe_progress_redacted=true"));
Assert.That(graphHandshakeProgressBridgeCheck.Detail, Does.Contain("qchat_boundary=true"));
Assert.That(graphHandshakeProgressBridgeCheck.Detail, Does.Contain("no_sql_authority=true"));
Assert.That(graphHandshakeProgressBridgeCheck.Detail, Does.Contain("runtime_required=false"));
```

In `ReadinessScriptDefaultModeExitsZeroAndPrintsSummary`, change the summary expectation to:

```csharp
"  Summary: 88 required passed, 0 required missing"
```

Add:

```csharp
Assert.That(result.StandardOutput, Does.Contain("GraphHandshakeDevSidecarProgressBridgePresent"));
```

Add this new test beside `StaticReadinessScriptContainsV31DevSidecarAdapterMarkers`:

```csharp
[Test]
public void StaticReadinessScriptContainsV32SidecarProgressBridgeMarkers()
{
    string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
    string script = File.ReadAllText(Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1"));
    string declaration = FindNewCheckDeclaration(script, "GraphHandshakeDevSidecarProgressBridgePresent");

    Assert.Multiple(() =>
    {
        Assert.That(declaration, Does.Contain("DataAgentGraphSidecarProgressModels.cs"));
        Assert.That(declaration, Does.Contain("DataAgentGraphSidecarProgressBridge.cs"));
        Assert.That(declaration, Does.Contain("DataAgentGraphSidecarProgressEvent"));
        Assert.That(declaration, Does.Contain("DataAgentGraphSidecarProgressBridge"));
        Assert.That(declaration, Does.Contain("IDataAgentProgressSink"));
        Assert.That(declaration, Does.Contain("DataAgentProgressEvent"));
        Assert.That(declaration, Does.Contain("unsafe_progress_rejected=true"));
        Assert.That(declaration, Does.Contain("unsafe_progress_redacted=true"));
        Assert.That(declaration, Does.Contain("qchat_boundary=true"));
        Assert.That(declaration, Does.Contain("runtime_required=false"));
    });
}
```

- [ ] **Step 2: Run readiness tests and verify failure**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests" -v:minimal
```

Expected: FAIL because `GraphHandshakeDevSidecarProgressBridgePresent` is not emitted yet and the script still expects 87 required checks.

- [ ] **Step 3: Add dynamic readiness in `DataAgentReadiness.cs`**

Insert this code after the existing graph handshake dev sidecar adapter readiness block:

```csharp
DateTimeOffset graphSidecarProgressNow = new(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);
DataAgentProgressRecorder graphSidecarProgressRecorder = new();
DataAgentGraphSidecarProgressBridge graphSidecarProgressBridge = new(
    graphSidecarProgressRecorder,
    () => graphSidecarProgressNow);
DataAgentOrchestrationResult graphSidecarProgressResult = CreateReadinessDataQueryGraphAcceptedResult();
DataAgentGraphSidecarProgressBridgeResult graphSidecarProgressAccepted = graphSidecarProgressBridge.Publish(
    graphHandshakeRequest,
    graphSidecarProgressResult,
    [
        new DataAgentGraphSidecarProgressEvent(
            graphHandshakeRequest.RequestId,
            graphHandshakeRequest.SessionId,
            DataAgentWorkflowNodeNames.QueryPlanner,
            DataAgentGraphSidecarProgressStatus.Completed,
            "planner_suggested",
            "planner ready",
            graphSidecarProgressNow.AddSeconds(-5),
            new Dictionary<string, string>
            {
                ["stage"] = "planner"
            })
    ]);
DataAgentGraphSidecarProgressBridgeResult graphSidecarProgressUnsafe = graphSidecarProgressBridge.Publish(
    graphHandshakeRequest,
    graphSidecarProgressResult,
    [
        new DataAgentGraphSidecarProgressEvent(
            graphHandshakeRequest.RequestId,
            graphHandshakeRequest.SessionId,
            DataAgentWorkflowNodeNames.QueryPlanner,
            DataAgentGraphSidecarProgressStatus.Completed,
            "planner_suggested",
            "SELECT * FROM engineering_gate",
            graphSidecarProgressNow,
            new Dictionary<string, string>())
    ]);
IReadOnlyList<DataAgentProgressEvent> graphSidecarProgressEvents = graphSidecarProgressRecorder.GetRecent(
    graphHandshakeRequest.SessionId,
    graphSidecarProgressNow);
string graphSidecarProgressDiagnostics = DataAgentProgressDiagnosticsFormatter.Format(
    graphSidecarProgressEvents,
    graphHandshakeRequest.SessionId,
    graphSidecarProgressNow);
string graphSidecarProgressRedactionProbe = DataAgentProgressDiagnosticsFormatter.Format(
    [
        new DataAgentProgressEvent(
            graphHandshakeRequest.SessionId,
            DataAgentProgressEventKind.Planner,
            DataAgentProgressEventPhase.Completed,
            DataAgentProgressEventStatus.Succeeded,
            "planner_suggested",
            TurnCount: 1,
            graphSidecarProgressNow,
            ExecutedSql: false,
            QueryAllowed: true,
            Terminal: false,
            new Dictionary<string, string>
            {
                ["sql"] = "SELECT * FROM engineering_gate",
                ["hidden_context"] = "[hidden_context]secret[/hidden_context]"
            })
    ],
    graphHandshakeRequest.SessionId,
    graphSidecarProgressNow);
bool graphSidecarProgressBridgeReady =
    graphSidecarProgressAccepted.AcceptedCount == 1 &&
    graphSidecarProgressAccepted.RejectedCount == 0 &&
    graphSidecarProgressEvents.Count == 1 &&
    graphSidecarProgressEvents.Single().ExecutedSql == false &&
    graphSidecarProgressEvents.Single().Facts.ContainsKey("source") &&
    string.Equals(graphSidecarProgressEvents.Single().Facts["source"], "graph_sidecar", StringComparison.Ordinal);
bool graphSidecarProgressUnsafeRejected =
    graphSidecarProgressUnsafe.AcceptedCount == 0 &&
    graphSidecarProgressUnsafe.RejectedCount == 1 &&
    graphSidecarProgressRecorder.GetRecent(graphHandshakeRequest.SessionId, graphSidecarProgressNow).Count == 1 &&
    graphSidecarProgressDiagnostics.Contains("SELECT", StringComparison.OrdinalIgnoreCase) == false;
bool graphSidecarProgressUnsafeRedacted =
    graphSidecarProgressRedactionProbe.Contains("sql=redacted", StringComparison.Ordinal) &&
    graphSidecarProgressRedactionProbe.Contains("SELECT", StringComparison.OrdinalIgnoreCase) == false &&
    graphSidecarProgressRedactionProbe.Contains("hidden_context", StringComparison.OrdinalIgnoreCase) == false;
bool graphSidecarProgressQChatBoundary =
    string.Equals(typeof(DataAgentGraphSidecarProgressBridge).Namespace, "Alife.Function.DataAgent", StringComparison.Ordinal) &&
    typeof(DataAgentGraphSidecarProgressBridge).Assembly.GetName().Name?.Contains("QChat", StringComparison.OrdinalIgnoreCase) == false;
bool graphSidecarProgressReady =
    graphHandshakeDefaultOptions.Enabled == false &&
    graphSidecarProgressBridgeReady &&
    graphSidecarProgressUnsafeRejected &&
    graphSidecarProgressUnsafeRedacted &&
    graphSidecarProgressQChatBoundary;

checks.Add(graphSidecarProgressReady
    ? Pass("GraphHandshakeDevSidecarProgressBridgePresent", "default_enabled=false;progress_bridge=true;csharp_recorder_authority=true;unsafe_progress_rejected=true;unsafe_progress_redacted=true;qchat_boundary=true;no_sql_authority=true;runtime_required=false")
    : Fail("GraphHandshakeDevSidecarProgressBridgePresent", $"default_enabled={LowerBool(graphHandshakeDefaultOptions.Enabled)};progress_bridge={LowerBool(graphSidecarProgressBridgeReady)};csharp_recorder_authority={LowerBool(graphSidecarProgressEvents.Count == 1)};unsafe_progress_rejected={LowerBool(graphSidecarProgressUnsafeRejected)};unsafe_progress_redacted={LowerBool(graphSidecarProgressUnsafeRedacted)};qchat_boundary={LowerBool(graphSidecarProgressQChatBoundary)};no_sql_authority={LowerBool(graphSidecarProgressEvents.All(item => item.ExecutedSql == false))};runtime_required=false"));
```

- [ ] **Step 4: Add static readiness script check**

In `tools/check-dataagent-readiness.ps1`, add this check immediately after `GraphHandshakeDevSidecarAdapterPresent`:

```powershell
New-Check -Group "Store" -Name "GraphHandshakeDevSidecarProgressBridgePresent" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphSidecarProgressModels.cs" @("DataAgentGraphSidecarProgressEvent", "DataAgentGraphSidecarProgressStatus", "DataAgentGraphSidecarProgressBridgeResult")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphSidecarProgressBridge.cs" @("DataAgentGraphSidecarProgressBridge", "IDataAgentProgressSink", "DataAgentProgressEvent", "PublishHandshakeProgress", "unsafe", "ExecutedSql: false", "graph_sidecar")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeCoordinator.cs" @("DataAgentGraphSidecarProgressBridge", "PublishHandshakeProgress")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs" @("new DataAgentGraphSidecarProgressBridge(progressSink)")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentGraphSidecarProgressBridgeTests.cs" @("PublishMapsAcceptedSidecarProgressThroughSink", "PublishRejectsUnknownNodeWithoutPublishing", "PublishRejectsUndefinedStatusWithoutPublishing", "PublishRejectsUnsafeMessageAndFactsBeforeFormatting", "PublishFailsClosedForOverBudgetInput")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("GraphHandshakeDevSidecarProgressBridgePresent", "default_enabled=false", "progress_bridge=true", "csharp_recorder_authority=true", "unsafe_progress_rejected=true", "unsafe_progress_redacted=true", "qchat_boundary=true", "no_sql_authority=true", "runtime_required=false"))) -Detail "V3.2 graph handshake dev sidecar progress bridge markers default_enabled=false progress_bridge=true csharp_recorder_authority=true unsafe_progress_rejected=true unsafe_progress_redacted=true qchat_boundary=true no_sql_authority=true runtime_required=false"
```

Change:

```powershell
$expectedRequired = 87
```

to:

```powershell
$expectedRequired = 88
```

- [ ] **Step 5: Confirm QChat boundary script needs no required-count change**

Inspect `tools/check-qchat-engineering-map.ps1`. Keep:

```powershell
$expectedRequired = 63
```

The existing `DataAgent diagnostics command contract` check omits `"DataAgentGraphSidecar"` and `"DataAgentGraphHandshake"` from QChat production source. Because the new bridge types begin with `DataAgentGraphSidecar`, the current omit pattern already covers `DataAgentGraphSidecarProgressBridge`, `DataAgentGraphSidecarProgressEvent`, and `DataAgentGraphSidecarProgressStatus`.

- [ ] **Step 6: Run readiness and QChat boundary checks**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests" -v:minimal
```

Expected: PASS for `DataAgentReadinessTests`.

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected output includes:

```text
PASS     GraphHandshakeDevSidecarProgressBridgePresent
Summary: 88 required passed, 0 required missing
```

```powershell
rg -n "DataAgentGraphSidecarProgress|DataAgentGraphHandshake" sources\Alife.Function\Alife.Function.QChat
```

Expected: no matches and exit code 1.

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected output includes:

```text
Summary: 63 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 7: Commit readiness and boundary work**

```powershell
git add sources\Alife.Function\Alife.Function.DataAgent\DataAgentReadiness.cs tools\check-dataagent-readiness.ps1 Tests\Alife.Test.DataAgent\DataAgentReadinessTests.cs
git commit -m "Add DataAgent V3.2 progress bridge readiness"
```

No QChat engineering-map commit is expected for V3.2 because the existing broader omit markers cover the new progress bridge type names.

## Task 5: Update Dev Stub Shape And Documentation

**Files:**
- Modify: `tools/dataagent-graph-sidecar/app.py`
- Modify: `tools/dataagent-graph-sidecar/README.md`
- Create: `docs/dataagent/dataagent-v3.2-sidecar-progress-bridge.md`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeDevSidecarStubTests.cs`

- [ ] **Step 1: Add failing static stub/doc tests**

Add this test to `DataAgentGraphHandshakeDevSidecarStubTests`:

```csharp
[Test]
public void PythonDevStubDocumentsV32ProgressShapeWithoutRuntimeDependency()
{
    string root = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
    string app = File.ReadAllText(Path.Combine(root, "tools", "dataagent-graph-sidecar", "app.py"));
    string readme = File.ReadAllText(Path.Combine(root, "tools", "dataagent-graph-sidecar", "README.md"));
    string doc = File.ReadAllText(Path.Combine(root, "docs", "dataagent", "dataagent-v3.2-sidecar-progress-bridge.md"));

    Assert.Multiple(() =>
    {
        Assert.That(app, Does.Contain("Message: str"));
        Assert.That(app, Does.Contain("Facts: dict[str, str]"));
        Assert.That(app, Does.Contain("scenario_knowledge"));
        Assert.That(app, Does.Contain("graph_sidecar"));
        Assert.That(app, Does.Not.Contain("subprocess"));
        Assert.That(app, Does.Not.Contain("sqlite"));
        Assert.That(app, Does.Not.Contain("postgres"));
        Assert.That(readme, Does.Contain("V3.2"));
        Assert.That(readme, Does.Contain("progress shape"));
        Assert.That(readme, Does.Contain("C# remains the only progress recorder"));
        Assert.That(readme, Does.Contain("default tests do not require Python"));
        Assert.That(doc, Does.Contain("DataAgent V3.2"));
        Assert.That(doc, Does.Contain("sidecar progress is untrusted input"));
        Assert.That(doc, Does.Contain("IDataAgentProgressSink"));
        Assert.That(doc, Does.Contain("default tests do not require Python"));
        Assert.That(doc, Does.Contain("SSE"));
        Assert.That(doc, Does.Contain("NDJSON"));
    });
}
```

- [ ] **Step 2: Run stub tests and verify doc failure**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeDevSidecarStubTests" -v:minimal
```

Expected: FAIL because `docs/dataagent/dataagent-v3.2-sidecar-progress-bridge.md` does not exist and `app.py` lacks `Message`/`Facts` fields.

- [ ] **Step 3: Update the Python dev stub shape**

In `tools/dataagent-graph-sidecar/app.py`, change `GraphHandshakeProgress` to:

```python
class GraphHandshakeProgress(BaseModel):
    NodeName: str
    Status: str
    ReasonCode: str
    Message: str = ""
    Facts: dict[str, str] = Field(default_factory=dict)
```

Change the selected node list in `handshake` to:

```python
selected_nodes = ["scenario_knowledge", "query_planner", "diagnostics_router"]
```

Change the first progress item to:

```python
GraphHandshakeProgress(
    NodeName="scenario_knowledge",
    Status="Completed",
    ReasonCode="scenario_context_ready",
    Message="scenario context ready",
    Facts={"source": "graph_sidecar", "stage": "scenario"},
),
```

Change the second and third progress items to include safe messages and facts:

```python
GraphHandshakeProgress(
    NodeName="query_planner",
    Status="Completed",
    ReasonCode="planner_suggested",
    Message="planner ready",
    Facts={"source": "graph_sidecar", "stage": "planner"},
),
GraphHandshakeProgress(
    NodeName="diagnostics_router",
    Status="Completed",
    ReasonCode="diagnostics_ready",
    Message="diagnostics ready",
    Facts={"source": "graph_sidecar", "stage": "diagnostics"},
),
```

- [ ] **Step 4: Update sidecar README**

Append this section to `tools/dataagent-graph-sidecar/README.md`:

````markdown
## V3.2 Progress Shape

V3.2 lets the stub return bounded progress-shaped fields on each
`NodeProgress` item:

```json
{
  "NodeName": "query_planner",
  "Status": "Completed",
  "ReasonCode": "planner_suggested",
  "Message": "planner ready",
  "Facts": {
    "source": "graph_sidecar",
    "stage": "planner"
  }
}
```

C# remains the only progress recorder and diagnostics publisher. The stub does
not write to `DataAgentProgressRecorder`, does not publish owner diagnostics,
and does not send visible chat text. Default tests do not require Python,
FastAPI, uvicorn, a live port, network access, QChat, QQ, PostgreSQL, browser
automation, model calls, or a live sidecar.
````

- [ ] **Step 5: Add the V3.2 developer note**

Create `docs/dataagent/dataagent-v3.2-sidecar-progress-bridge.md`:

````markdown
# DataAgent V3.2 Sidecar Progress Bridge

DataAgent V3.2 adds a sidecar progress bridge, not a production graph runtime.
The optional dev sidecar can return bounded progress-shaped data, but sidecar
progress is untrusted input until C# validates and maps it.

## Authority Boundary

The bridge accepts sidecar-specific progress DTOs and maps safe events to
`DataAgentProgressEvent`. It publishes only through `IDataAgentProgressSink`,
so the existing `DataAgentProgressRecorder`,
`DataAgentProgressDiagnosticsPublisher`, and
`DataAgentProgressDiagnosticsFormatter` remain the recorder and owner
diagnostics authority.

Sidecar progress cannot prove SQL execution, cannot set `ExecutedSql=true`,
cannot mutate checkpoints, cannot write evidence, cannot decide Tool Broker
route state, cannot send QChat text, and cannot own QQ ingress.

## Validation

The bridge rejects progress with mismatched request or session ids, unknown
manifest nodes, undefined statuses, unsafe reason codes, unsafe messages,
unsafe fact keys, unsafe fact values, or over-budget event/fact payloads.
Accepted facts are bounded and stamped with safe C# facts such as:

```text
source=graph_sidecar
node=<manifest-node>
request_id=<handshake-request-id>
```

## Testing

Default tests use fake sidecar progress and fake handshake responses. They do
not start Python, FastAPI, uvicorn, or a live sidecar, and they do not require
network access, QChat, QQ, PostgreSQL, browser automation, model calls, or a
live LangGraph runtime.

## Future Transport

SSE or NDJSON streaming can attach to the same bridge by parsing untrusted
sidecar progress DTOs and passing them to C#. That transport is outside V3.2.
The V3.2 outcome is the stable C# validation, mapping, and publication
boundary.
````

- [ ] **Step 6: Run stub/doc tests and verify pass**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeDevSidecarStubTests" -v:minimal
```

Expected: PASS for `DataAgentGraphHandshakeDevSidecarStubTests`.

- [ ] **Step 7: Commit stub and docs**

```powershell
git add tools\dataagent-graph-sidecar\app.py tools\dataagent-graph-sidecar\README.md docs\dataagent\dataagent-v3.2-sidecar-progress-bridge.md Tests\Alife.Test.DataAgent\DataAgentGraphHandshakeDevSidecarStubTests.cs
git commit -m "Document DataAgent V3.2 sidecar progress bridge"
```

## Task 6: Final Verification

**Files:**
- Verify all changed files from Tasks 1-5.

- [ ] **Step 1: Run focused V3.2 tests**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphSidecarProgressBridgeTests|FullyQualifiedName~DataAgentGraphHandshakeCoordinatorTests|FullyQualifiedName~DataAgentReadinessTests|FullyQualifiedName~DataAgentGraphHandshakeDevSidecarStubTests" -v:minimal
```

Expected: PASS with 0 failed tests in the focused V3.2 set.

- [ ] **Step 2: Run DataAgent readiness script**

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected output includes:

```text
PASS     GraphHandshakeDevSidecarProgressBridgePresent
Summary: 88 required passed, 0 required missing
```

- [ ] **Step 3: Run QChat source boundary scan**

```powershell
rg -n "DataAgentGraphSidecarProgress|DataAgentGraphHandshake" sources\Alife.Function\Alife.Function.QChat
```

Expected: no matches and exit code 1.

- [ ] **Step 4: Run QChat engineering map**

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected output includes:

```text
Summary: 63 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 5: Restore, build, and run full solution tests**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" restore Alife.slnx
```

Expected: restore exits 0.

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" build Alife.slnx --no-restore -v:minimal
```

Expected: build exits 0 with 0 errors. Existing CS0067 warnings may remain.

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore --no-build -v:minimal
```

Expected: full solution tests exit 0 with 0 failed tests.

- [ ] **Step 6: Review git diff and commit verification marker only if files changed**

```powershell
git status --short
git diff --check
```

Expected: no whitespace errors. If verification required no file changes, do not create a verification-only commit.

## Self-Review

- Spec coverage: The plan covers sidecar progress contract, validation, mapper, bridge publication through `IDataAgentProgressSink`, coordinator integration, readiness, QChat boundary, optional Python stub shape, docs, and full verification.
- Non-goals preserved: No production LangGraph runtime, no automatic Python startup, no default live sidecar tests, no QChat import of DataAgent graph/sidecar/progress bridge types, no SQL/checkpoint/Tool Broker/evidence authority moved to the sidecar.
- Type consistency: The plan defines `DataAgentGraphSidecarProgressEvent`, `DataAgentGraphSidecarProgressStatus`, `DataAgentGraphSidecarProgressBridgeResult`, and `DataAgentGraphSidecarProgressBridge` before later tasks reference them. `Publish` and `PublishHandshakeProgress` signatures are consistent across tests, coordinator, readiness, and docs.
- Placeholder scan: The plan contains exact paths, test methods, command lines, expected outputs, and concrete code snippets for every implementation step.
- Boundary check: Sidecar progress always maps with `ExecutedSql=false`, records `source=graph_sidecar`, and publishes only through C# `IDataAgentProgressSink`.
