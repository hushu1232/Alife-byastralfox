# DataAgent V3.9 Replay Runbook Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an offline DataAgent replay runbook CLI that reads a JSON fixture, executes the real route/policy/analysis/diagnostics chain, and emits stable Markdown or JSON reports with expected-marker validation.

**Architecture:** Add a `tools/replay-dataagent-chain.ps1` wrapper around a small .NET console harness in `tools/dataagent-replay`. The harness uses real DataAgent/FunctionCaller/QChat chain boundary classes with deterministic fake store/planner/clock collaborators, while tests lock CLI behavior, output shape, marker validation, and readiness integration.

**Tech Stack:** .NET 9, C#, NUnit, System.Text.Json, PowerShell, existing DataAgent/FunctionCaller/QChat modules.

---

## Execution Base

Implement this plan only after V3.8 is merged into the working branch, or in a worktree based on `dataagent-v3-8-chain-contract`.

V3.9 assumes these V3.8 pieces exist:

- `Tests/Alife.Test.DataAgent/DataAgentEndToEndChainContractTests.cs`
- `Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj` references `Alife.Function.QChat`
- `DataAgentEndToEndChainContractPresent`
- Static readiness count is `92`

If execution starts from a branch where those do not exist, first merge or rebase onto the V3.8 implementation branch. Do not reimplement V3.8 inside this plan.

At the start of execution, record the V3.9 base SHA for final diff verification:

```powershell
$v39Base = git rev-parse HEAD
$v39Base
```

Keep `$v39Base` in the same shell session until Task 6.

If the shell session is lost after Task 2 has been committed, recover the same value with:

```powershell
$firstV39Commit = git log --format=%H --reverse --grep="Add DataAgent V3.9 replay harness" | Select-Object -First 1
$v39Base = git rev-parse "$firstV39Commit^"
$v39Base
```

---

## File Structure

- Create: `tools/replay-dataagent-chain.ps1`
  - User-facing CLI wrapper.
  - Resolves default fixture.
  - Invokes the local .NET 9 SDK when present.
  - Passes `--fixture` and `--format` to the harness.

- Create: `tools/dataagent-replay/Alife.Tools.DataAgentReplay.csproj`
  - Console project for the replay harness.
  - References DataAgent, FunctionCaller, and QChat projects.

- Create: `tools/dataagent-replay/DataAgentReplayModels.cs`
  - Fixture, report, marker, diagnostics, and boundary models.

- Create: `tools/dataagent-replay/DataAgentReplayRunner.cs`
  - Executes the real offline replay chain.
  - Contains deterministic fake store/planner collaborators.

- Create: `tools/dataagent-replay/DataAgentReplayReportFormatter.cs`
  - Formats Markdown and JSON output from one result model.

- Create: `tools/dataagent-replay/Program.cs`
  - Parses CLI arguments.
  - Reads fixture.
  - Runs replay.
  - Writes report.
  - Returns non-zero exit codes for invalid input, missing fixture, missing markers, or replay errors.

- Create: `Tests/Alife.Test.DataAgent/Fixtures/DataAgentReplay/v3.9-owner-readiness-analysis.json`
  - Default fixture for owner/private/trusted readiness analysis replay.

- Create: `Tests/Alife.Test.DataAgent/DataAgentReplayRunbookTests.cs`
  - NUnit tests for fixture, harness, CLI wrapper, Markdown/JSON output, missing-marker behavior, and offline boundary.

- Modify: `Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj`
  - Add project reference to `tools/dataagent-replay`.
  - Include replay JSON fixture as copied content if needed.

- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
  - Add dynamic `DataAgentReplayRunbookPresent`.
  - Increase core readiness count from `77` to `78` after V3.8.

- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
  - Assert new dynamic marker and details.
  - Update static summary from `92` to `93`.
  - Add static script marker test for V3.9.

- Modify: `tools/check-dataagent-readiness.ps1`
  - Add static `DataAgentReplayRunbookPresent`.
  - Increase `$expectedRequired` from `92` to `93`.

- Modify: version guard readiness tests that assert `$expectedRequired = 92`
  - Update all stale assertions to `$expectedRequired = 93`.

Do not modify:

- `sources/Alife.Function/Alife.Function.QChat/**`
- `tools/dataagent-graph-sidecar/**`
- `tools/run-dataagent-graph-sidecar-smoke.ps1`
- Python runtime files
- upload scripts
- `D:\FOXD` or any FOXD integration path

---

### Task 1: Add Fixture, Test Project Wiring, And CLI Contract Tests

**Files:**
- Create: `Tests/Alife.Test.DataAgent/Fixtures/DataAgentReplay/v3.9-owner-readiness-analysis.json`
- Create: `Tests/Alife.Test.DataAgent/DataAgentReplayRunbookTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj`

- [ ] **Step 1: Add the default replay fixture**

Create `Tests/Alife.Test.DataAgent/Fixtures/DataAgentReplay/v3.9-owner-readiness-analysis.json`:

```json
{
  "version": "v3.9",
  "name": "owner-readiness-analysis",
  "callerId": "owner",
  "utterance": "DataAgent analyze project readiness",
  "routeState": {
    "isOwner": true,
    "isPrivate": true,
    "trustedRuntime": true,
    "activeDataAgentSessionId": "",
    "activeDataAgentSessionStatus": ""
  },
  "planner": {
    "dataset": "document_index",
    "intent": "find_dataagent_documents",
    "select": ["path", "title"],
    "filters": [
      {
        "field": "tags",
        "operator": "contains",
        "value": "dataagent"
      }
    ],
    "limit": 20
  },
  "expectedMarkers": [
    "route_allowed",
    "dataagent_analysis_start",
    "RouteGate:Succeeded",
    "Execute:Succeeded",
    "Checkpoint:Succeeded",
    "sql_status=validated",
    "DataAgent evidence diagnostics",
    "DataAgent trace diagnostics",
    "DataAgent progress diagnostics",
    "graph_sidecar",
    "sidecar_authority=false",
    "default_tests_live_runtime=false"
  ]
}
```

- [ ] **Step 2: Update the test project reference and fixture content**

Modify `Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj` so the project references include the replay harness project. Preserve the existing DataAgent and QChat references from V3.8.

```xml
    <ItemGroup>
        <ProjectReference Include="..\..\Sources\Alife.Function\Alife.Function.DataAgent\Alife.Function.DataAgent.csproj" />
        <ProjectReference Include="..\..\Sources\Alife.Function\Alife.Function.QChat\Alife.Function.QChat.csproj" />
        <ProjectReference Include="..\..\tools\dataagent-replay\Alife.Tools.DataAgentReplay.csproj" />
    </ItemGroup>

    <ItemGroup>
        <None Include="Fixtures\DataAgentReplay\*.json" CopyToOutputDirectory="PreserveNewest" />
    </ItemGroup>
```

If the QChat reference is already present, do not duplicate it.

- [ ] **Step 3: Write failing replay runbook tests**

Create `Tests/Alife.Test.DataAgent/DataAgentReplayRunbookTests.cs`:

```csharp
using System.Diagnostics;
using System.Text.Json;
using Alife.Tools.DataAgentReplay;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentReplayRunbookTests
{
    [Test]
    public void DefaultFixtureContainsV39OwnerReadinessReplayShape()
    {
        string fixturePath = DefaultFixturePath();
        string fixtureText = File.ReadAllText(fixturePath);

        using JsonDocument document = JsonDocument.Parse(fixtureText);
        JsonElement root = document.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("version").GetString(), Is.EqualTo("v3.9"));
            Assert.That(root.GetProperty("name").GetString(), Is.EqualTo("owner-readiness-analysis"));
            Assert.That(root.GetProperty("callerId").GetString(), Is.EqualTo("owner"));
            Assert.That(root.GetProperty("utterance").GetString(), Is.EqualTo("DataAgent analyze project readiness"));
            Assert.That(root.GetProperty("routeState").GetProperty("isOwner").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("routeState").GetProperty("isPrivate").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("routeState").GetProperty("trustedRuntime").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("planner").GetProperty("dataset").GetString(), Is.EqualTo("document_index"));
            Assert.That(root.GetProperty("planner").GetProperty("intent").GetString(), Is.EqualTo("find_dataagent_documents"));
            Assert.That(root.GetProperty("expectedMarkers").EnumerateArray().Select(item => item.GetString()), Does.Contain("sidecar_authority=false"));
            Assert.That(root.GetProperty("expectedMarkers").EnumerateArray().Select(item => item.GetString()), Does.Contain("default_tests_live_runtime=false"));
        });
    }

    [Test]
    public void RunnerExecutesDefaultFixtureThroughRealOfflineChain()
    {
        DataAgentReplayResult result = DataAgentReplayRunner.Run(DataAgentReplayFixture.Load(DefaultFixturePath()));

        Assert.Multiple(() =>
        {
            Assert.That(result.Passed, Is.True);
            Assert.That(result.Route.ReasonCode, Is.EqualTo("route_allowed"));
            Assert.That(result.Route.AllowedTools, Does.Contain("dataagent_analysis_start"));
            Assert.That(result.XmlPolicy.Allowed, Is.True);
            Assert.That(result.RouteContext.Present, Is.True);
            Assert.That(result.RouteContext.ToolName, Is.EqualTo("dataagent_analysis_start"));
            Assert.That(result.RouteContext.AllowsQuery, Is.True);
            Assert.That(result.Orchestration.Trace, Does.Contain("RouteGate:Succeeded"));
            Assert.That(result.Orchestration.Trace, Does.Contain("Execute:Succeeded"));
            Assert.That(result.Session.SessionId, Is.Not.Empty);
            Assert.That(result.Diagnostics.Evidence, Does.Contain("DataAgent evidence diagnostics"));
            Assert.That(result.Diagnostics.Trace, Does.Contain("DataAgent trace diagnostics"));
            Assert.That(result.Diagnostics.Progress, Does.Contain("DataAgent progress diagnostics"));
            Assert.That(result.Diagnostics.Graph, Does.Contain("graph_sidecar"));
            Assert.That(result.OfflineBoundary.SidecarAuthority, Is.False);
            Assert.That(result.OfflineBoundary.DefaultTestsLiveRuntime, Is.False);
            Assert.That(result.ExpectedMarkers.All(marker => marker.Passed), Is.True);
        });
    }

    [Test]
    public void MarkdownFormatterEmitsStableRunbookSections()
    {
        DataAgentReplayResult result = DataAgentReplayRunner.Run(DataAgentReplayFixture.Load(DefaultFixturePath()));

        string markdown = DataAgentReplayReportFormatter.FormatMarkdown(result);

        Assert.Multiple(() =>
        {
            Assert.That(markdown, Does.Contain("# DataAgent Replay: owner-readiness-analysis"));
            Assert.That(markdown, Does.Contain("## Fixture"));
            Assert.That(markdown, Does.Contain("## Route Decision"));
            Assert.That(markdown, Does.Contain("## XML Policy"));
            Assert.That(markdown, Does.Contain("## Route Context"));
            Assert.That(markdown, Does.Contain("## Orchestration"));
            Assert.That(markdown, Does.Contain("## Session"));
            Assert.That(markdown, Does.Contain("## Diagnostics"));
            Assert.That(markdown, Does.Contain("## Expected Markers"));
            Assert.That(markdown, Does.Contain("## Offline Boundary"));
            Assert.That(markdown, Does.Contain("PASS"));
            Assert.That(markdown, Does.Contain("sidecar_authority=false"));
            Assert.That(markdown, Does.Contain("default_tests_live_runtime=false"));
            Assert.That(markdown, Does.Not.Contain("RequestsVisibleText=True"));
        });
    }

    [Test]
    public void JsonFormatterEmitsParseableStableTopLevelShape()
    {
        DataAgentReplayResult result = DataAgentReplayRunner.Run(DataAgentReplayFixture.Load(DefaultFixturePath()));

        string json = DataAgentReplayReportFormatter.FormatJson(result);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("passed").GetBoolean(), Is.True);
            Assert.That(root.TryGetProperty("fixture", out _), Is.True);
            Assert.That(root.TryGetProperty("route", out _), Is.True);
            Assert.That(root.TryGetProperty("xmlPolicy", out _), Is.True);
            Assert.That(root.TryGetProperty("routeContext", out _), Is.True);
            Assert.That(root.TryGetProperty("orchestration", out _), Is.True);
            Assert.That(root.TryGetProperty("session", out _), Is.True);
            Assert.That(root.TryGetProperty("diagnostics", out _), Is.True);
            Assert.That(root.TryGetProperty("expectedMarkers", out _), Is.True);
            Assert.That(root.TryGetProperty("offlineBoundary", out _), Is.True);
        });
    }

    [Test]
    public void RunnerReportsMissingExpectedMarkerWithoutThrowing()
    {
        DataAgentReplayFixture fixture = DataAgentReplayFixture.Load(DefaultFixturePath()) with
        {
            ExpectedMarkers = ["marker_that_is_not_in_the_replay_report"]
        };

        DataAgentReplayResult result = DataAgentReplayRunner.Run(fixture);

        Assert.Multiple(() =>
        {
            Assert.That(result.Passed, Is.False);
            Assert.That(result.ExpectedMarkers, Has.Count.EqualTo(1));
            Assert.That(result.ExpectedMarkers[0].Marker, Is.EqualTo("marker_that_is_not_in_the_replay_report"));
            Assert.That(result.ExpectedMarkers[0].Passed, Is.False);
        });
    }

    [Test]
    public void ReplayScriptDefaultRunEmitsMarkdown()
    {
        ProcessResult result = RunScript();

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("# DataAgent Replay: owner-readiness-analysis"));
            Assert.That(result.StandardOutput, Does.Contain("## Expected Markers"));
            Assert.That(result.StandardOutput, Does.Contain("sidecar_authority=false"));
        });
    }

    [Test]
    public void ReplayScriptJsonRunEmitsParseableJson()
    {
        ProcessResult result = RunScript("-Format", "json");

        Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
        using JsonDocument document = JsonDocument.Parse(result.StandardOutput);
        Assert.That(document.RootElement.GetProperty("passed").GetBoolean(), Is.True);
    }

    [Test]
    public void ReplayScriptMissingFixtureFailsWithClearError()
    {
        string missingPath = Path.Combine(FindRepoRoot(TestContext.CurrentContext.TestDirectory), "missing-dataagent-replay-fixture.json");

        ProcessResult result = RunScript("-Fixture", missingPath);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Not.EqualTo(0));
            Assert.That(result.StandardError + result.StandardOutput, Does.Contain("Fixture not found"));
            Assert.That(result.StandardError + result.StandardOutput, Does.Contain(missingPath));
        });
    }

    [Test]
    public void ReplayScriptUnsupportedFormatFailsWithClearError()
    {
        ProcessResult result = RunScript("-Format", "xml");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Not.EqualTo(0));
            Assert.That(result.StandardError + result.StandardOutput, Does.Contain("Unsupported format"));
            Assert.That(result.StandardError + result.StandardOutput, Does.Contain("markdown"));
            Assert.That(result.StandardError + result.StandardOutput, Does.Contain("json"));
        });
    }

    [Test]
    public void ReplayImplementationUsesRealRoutePolicyAccessorAndDisabledRuntime()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string runner = File.ReadAllText(Path.Combine(repoRoot, "tools", "dataagent-replay", "DataAgentReplayRunner.cs"));
        string script = File.ReadAllText(Path.Combine(repoRoot, "tools", "replay-dataagent-chain.ps1"));

        Assert.Multiple(() =>
        {
            Assert.That(runner, Does.Contain("ToolCapabilityRouter.CreateDefault"));
            Assert.That(runner, Does.Contain("XmlFunctionExecutionPolicy"));
            Assert.That(runner, Does.Contain("XmlPolicyDataAgentToolRouteContextAccessor"));
            Assert.That(runner, Does.Contain("DataAgentAnalysisToolHandler"));
            Assert.That(runner, Does.Contain("QChatDiagnosticsService"));
            Assert.That(runner, Does.Contain("DataAgentGraphHandshakeOptions.Disabled"));
            Assert.That(runner, Does.Contain("DisabledDataAgentGraphSidecarClient.Instance"));
            Assert.That(runner, Does.Not.Contain("FixedRouteContextAccessor"));
            Assert.That(script, Does.Contain("v3.9-owner-readiness-analysis.json"));
            Assert.That(script, Does.Contain("C:\\Users\\hu shu\\.dotnet\\dotnet.exe"));
        });
    }

    static string DefaultFixturePath()
    {
        return Path.Combine(
            FindRepoRoot(TestContext.CurrentContext.TestDirectory),
            "Tests",
            "Alife.Test.DataAgent",
            "Fixtures",
            "DataAgentReplay",
            "v3.9-owner-readiness-analysis.json");
    }

    static ProcessResult RunScript(params string[] arguments)
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        List<string> command = [
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            Path.Combine(repoRoot, "tools", "replay-dataagent-chain.ps1")
        ];
        command.AddRange(arguments);

        using Process process = new();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "powershell",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (string argument in command)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit(60000);
        return new ProcessResult(process.ExitCode, standardOutput, standardError);
    }

    static string FindRepoRoot(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
```

- [ ] **Step 4: Run tests and verify they fail**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReplayRunbookTests" -v:minimal
```

Expected: fail to compile because `Alife.Tools.DataAgentReplay` and the replay harness project do not exist yet.

- [ ] **Step 5: Keep the failing contract uncommitted**

Do not commit this red state. Task 2 adds the replay harness and commits the fixture, tests, project reference, harness project, models, runner, and formatter together once the focused runner/formatter tests pass.

---

### Task 2: Add Replay Harness Project, Models, Runner, And Formatters

**Files:**
- Create: `tools/dataagent-replay/Alife.Tools.DataAgentReplay.csproj`
- Create: `tools/dataagent-replay/DataAgentReplayModels.cs`
- Create: `tools/dataagent-replay/DataAgentReplayRunner.cs`
- Create: `tools/dataagent-replay/DataAgentReplayReportFormatter.cs`

- [ ] **Step 1: Create the replay harness project**

Create `tools/dataagent-replay/Alife.Tools.DataAgentReplay.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\sources\Alife.Function\Alife.Function.DataAgent\Alife.Function.DataAgent.csproj" />
    <ProjectReference Include="..\..\sources\Alife.Function\Alife.Function.FunctionCaller\Alife.Function.FunctionCaller.csproj" />
    <ProjectReference Include="..\..\sources\Alife.Function\Alife.Function.QChat\Alife.Function.QChat.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create replay models**

Create `tools/dataagent-replay/DataAgentReplayModels.cs`:

```csharp
using System.Text.Json;

namespace Alife.Tools.DataAgentReplay;

public sealed record DataAgentReplayFixture(
    string Version,
    string Name,
    string CallerId,
    string Utterance,
    DataAgentReplayRouteStateFixture RouteState,
    DataAgentReplayPlannerFixture Planner,
    IReadOnlyList<string> ExpectedMarkers)
{
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static DataAgentReplayFixture Load(string path)
    {
        if (File.Exists(path) == false)
            throw new FileNotFoundException($"Fixture not found: {path}", path);

        string json = File.ReadAllText(path);
        DataAgentReplayFixture? fixture = JsonSerializer.Deserialize<DataAgentReplayFixture>(json, JsonOptions);
        if (fixture is null)
            throw new InvalidOperationException($"Fixture could not be parsed: {path}");

        return fixture.Normalize();
    }

    public DataAgentReplayFixture Normalize()
    {
        return this with
        {
            Version = string.IsNullOrWhiteSpace(Version) ? "v3.9" : Version.Trim(),
            Name = string.IsNullOrWhiteSpace(Name) ? "unnamed" : Name.Trim(),
            CallerId = string.IsNullOrWhiteSpace(CallerId) ? "owner" : CallerId.Trim(),
            Utterance = string.IsNullOrWhiteSpace(Utterance) ? "DataAgent analyze project readiness" : Utterance.Trim(),
            ExpectedMarkers = ExpectedMarkers
                .Where(marker => string.IsNullOrWhiteSpace(marker) == false)
                .Select(marker => marker.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray()
        };
    }

    public static JsonSerializerOptions ReportJsonOptions => JsonOptions;
}

public sealed record DataAgentReplayRouteStateFixture(
    bool IsOwner,
    bool IsPrivate,
    bool TrustedRuntime,
    string ActiveDataAgentSessionId,
    string ActiveDataAgentSessionStatus);

public sealed record DataAgentReplayPlannerFixture(
    string Dataset,
    string Intent,
    IReadOnlyList<string> Select,
    IReadOnlyList<DataAgentReplayFilterFixture> Filters,
    int Limit);

public sealed record DataAgentReplayFilterFixture(string Field, string Operator, string Value);

public sealed record DataAgentReplayResult(
    DataAgentReplayFixtureSummary Fixture,
    DataAgentReplayRouteReport Route,
    DataAgentReplayXmlPolicyReport XmlPolicy,
    DataAgentReplayRouteContextReport RouteContext,
    DataAgentReplayOrchestrationReport Orchestration,
    DataAgentReplaySessionReport Session,
    DataAgentReplayDiagnosticsReport Diagnostics,
    IReadOnlyList<DataAgentReplayExpectedMarker> ExpectedMarkers,
    DataAgentReplayOfflineBoundary OfflineBoundary,
    bool Passed);

public sealed record DataAgentReplayFixtureSummary(string Version, string Name, string CallerId, string Utterance);

public sealed record DataAgentReplayRouteReport(
    string Domain,
    string Intent,
    string ReasonCode,
    string Reason,
    IReadOnlyList<string> AllowedTools,
    IReadOnlyList<string> DeniedTools);

public sealed record DataAgentReplayXmlPolicyReport(bool Allowed, string Reason);

public sealed record DataAgentReplayRouteContextReport(
    bool Present,
    string ToolName,
    bool AllowsTool,
    bool AllowsQuery,
    string RouteId,
    string Intent,
    string ReasonCode,
    string RouteSessionId);

public sealed record DataAgentReplayOrchestrationReport(string Trace, bool Accepted, string RejectedReason, int RowCount);

public sealed record DataAgentReplaySessionReport(string SessionId, string Status, bool HasActiveRouteSession);

public sealed record DataAgentReplayDiagnosticsReport(
    string Evidence,
    string Trace,
    string Progress,
    string Graph,
    string QChatEvidence,
    string QChatTrace,
    string QChatProgress,
    string QChatGraph);

public sealed record DataAgentReplayExpectedMarker(string Marker, bool Passed);

public sealed record DataAgentReplayOfflineBoundary(bool SidecarAuthority, bool DefaultTestsLiveRuntime);
```

- [ ] **Step 3: Create the replay runner**

Create `tools/dataagent-replay/DataAgentReplayRunner.cs`:

```csharp
using Alife.Function.DataAgent;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;
using Alife.Function.QChat;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alife.Tools.DataAgentReplay;

public static class DataAgentReplayRunner
{
    static readonly DateTimeOffset ReplayNow = new(2026, 7, 9, 9, 0, 0, TimeSpan.Zero);

    public static DataAgentReplayResult Run(DataAgentReplayFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        fixture = fixture.Normalize();

        ToolCapabilityRouter router = ToolCapabilityRouter.CreateDefault();
        ToolRouteState routeState = new(
            fixture.RouteState.ActiveDataAgentSessionId,
            fixture.RouteState.ActiveDataAgentSessionStatus,
            fixture.RouteState.IsOwner,
            fixture.RouteState.IsPrivate,
            fixture.RouteState.TrustedRuntime);
        ToolRouteDecision route = router.Route(fixture.Utterance, routeState);

        XmlFunctionExecutionPolicy policy = new();
        policy.SetGovernedToolNames(router.ToolNames);
        policy.CurrentRoute = route;
        XmlFunctionExecutionDecision xmlDecision = policy.TryConsume(
            Function("dataagent_analysis_start"),
            ContextWithSession(null));

        RecordingDataAgentStore store = new();
        FixedPlanner planner = new(ToPlan(fixture.Planner));
        DataAgentProgressRecorder progressRecorder = new();
        List<string> progressDiagnostics = [];
        DataAgentProgressDiagnosticsPublisher progressSink = new(
            progressRecorder,
            progressDiagnostics.Add,
            () => ReplayNow);
        InMemoryDataAgentAnalysisSessionStore sessionStore = new();
        DataAgentService dataAgentService = new(store, planner);
        DataAgentAnalysisService analysisService = new(dataAgentService, sessionStore, progressSink, () => ReplayNow);
        DataAgentAnalysisOrchestrator orchestrator = new(
            analysisService,
            sessionStore,
            progressSink: progressSink,
            progressClock: () => ReplayNow);

        List<string> publishedContexts = [];
        List<string> evidenceDiagnostics = [];
        List<string> traceDiagnostics = [];
        List<string> graphDiagnostics = [];
        DataAgentAnalysisToolHandler handler = new(
            orchestrator,
            publishedContexts.Add,
            new XmlPolicyDataAgentToolRouteContextAccessor(policy),
            evidenceDiagnostics.Add,
            traceDiagnostics.Add,
            new DataAgentTraceRecorder(),
            () => ReplayNow,
            graphDiagnostics.Add,
            new DataAgentGraphHandshakeCoordinator(
                DataAgentGraphHandshakeOptions.Disabled,
                DisabledDataAgentGraphSidecarClient.Instance));

        string context = xmlDecision.IsAllowed
            ? handler.Start(fixture.CallerId, fixture.Utterance)
            : string.Empty;

        XmlFunctionCaller functionCaller = new(NullLogger<XmlFunctionCaller>.Instance);
        functionCaller.UpdateDataAgentAnalysisRouteSessionFromContext(context);
        ToolRouteState activeRouteState = functionCaller.CreateToolRouteState(
            fixture.RouteState.IsOwner,
            fixture.RouteState.IsPrivate,
            fixture.RouteState.TrustedRuntime);

        string evidenceText = evidenceDiagnostics.LastOrDefault() ?? string.Empty;
        string traceText = traceDiagnostics.LastOrDefault() ?? string.Empty;
        string progressText = progressDiagnostics.LastOrDefault() ?? string.Empty;
        string graphText = graphDiagnostics.LastOrDefault() ?? string.Empty;
        QChatDiagnosticsRuntimeState diagnosticsState = new(
            RecentDataAgentEvidence: evidenceText,
            RecentDataAgentTrace: traceText,
            RecentDataAgentProgress: progressText,
            RecentDataAgentGraph: graphText);
        QChatAgentRoute qchatRoute = OwnerPrivateRoute();
        QChatAgentProfile qchatProfile = OwnerProfile();
        QChatDiagnosticsResult qchatEvidence = QChatDiagnosticsService.TryHandle("/dataagent diag evidence", qchatRoute, qchatProfile, diagnosticsState);
        QChatDiagnosticsResult qchatTrace = QChatDiagnosticsService.TryHandle("/dataagent diag trace", qchatRoute, qchatProfile, diagnosticsState);
        QChatDiagnosticsResult qchatProgress = QChatDiagnosticsService.TryHandle("/dataagent diag progress", qchatRoute, qchatProfile, diagnosticsState);
        QChatDiagnosticsResult qchatGraph = QChatDiagnosticsService.TryHandle("/dataagent diag graph", qchatRoute, qchatProfile, diagnosticsState);

        string combined = string.Join(
            Environment.NewLine,
            context,
            evidenceText,
            traceText,
            progressText,
            graphText,
            qchatEvidence.Text,
            qchatTrace.Text,
            qchatProgress.Text,
            qchatGraph.Text,
            "sidecar_authority=false",
            "default_tests_live_runtime=false");

        DataAgentReplayExpectedMarker[] markerResults = fixture.ExpectedMarkers
            .Select(marker => new DataAgentReplayExpectedMarker(marker, combined.Contains(marker, StringComparison.Ordinal)))
            .ToArray();
        bool allMarkersPassed = markerResults.All(marker => marker.Passed);

        DataAgentToolRouteContext routeContext = new XmlPolicyDataAgentToolRouteContextAccessor(policy)
            .Get("dataagent_analysis_start", null);

        return new DataAgentReplayResult(
            new DataAgentReplayFixtureSummary(fixture.Version, fixture.Name, fixture.CallerId, fixture.Utterance),
            new DataAgentReplayRouteReport(
                route.Domain.ToString(),
                route.Intent,
                route.ReasonCode,
                route.Reason,
                route.AllowedTools.ToArray(),
                route.DeniedTools.Select(tool => $"{tool.Name}:{tool.Reason}").ToArray()),
            new DataAgentReplayXmlPolicyReport(xmlDecision.IsAllowed, xmlDecision.Reason ?? "allowed"),
            new DataAgentReplayRouteContextReport(
                routeContext.Present,
                routeContext.ToolName,
                routeContext.AllowsTool,
                routeContext.AllowsQuery,
                routeContext.RouteId,
                routeContext.Intent,
                routeContext.ReasonCode,
                routeContext.RouteSessionId),
            new DataAgentReplayOrchestrationReport(
                ReadContextValue(context, "orchestration_trace="),
                context.Contains("accepted=true", StringComparison.Ordinal) || store.AcceptedAudit.Count > 0,
                ReadContextValue(context, "rejected_reason="),
                store.QueryCount),
            new DataAgentReplaySessionReport(
                ReadContextValue(context, "session_id="),
                ReadContextValue(context, "status="),
                activeRouteState.HasActiveDataAgentSession),
            new DataAgentReplayDiagnosticsReport(
                evidenceText,
                traceText,
                progressText,
                graphText,
                qchatEvidence.Text,
                qchatTrace.Text,
                qchatProgress.Text,
                qchatGraph.Text),
            markerResults,
            new DataAgentReplayOfflineBoundary(
                SidecarAuthority: false,
                DefaultTestsLiveRuntime: false),
            allMarkersPassed && xmlDecision.IsAllowed && routeContext.AllowsQuery);
    }

    static DataAgentQueryPlan ToPlan(DataAgentReplayPlannerFixture planner)
    {
        return new DataAgentQueryPlan(
            planner.Dataset,
            planner.Intent,
            planner.Select.ToArray(),
            planner.Filters
                .Select(filter => new DataAgentFilter(filter.Field, filter.Operator, filter.Value))
                .ToArray(),
            [],
            planner.Limit);
    }

    static XmlFunction Function(string name) => new()
    {
        Name = name,
        Mode = FunctionMode.OneShot,
        Invoker = (_, _) => Task.CompletedTask
    };

    static XmlContext ContextWithSession(string? sessionId)
    {
        Dictionary<string, string> parameters = new(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(sessionId) == false)
            parameters["sessionid"] = sessionId;

        return new XmlContext
        {
            CallMode = CallMode.OneShot,
            Parameters = parameters
        };
    }

    static string ReadContextValue(string context, string prefix)
    {
        foreach (string line in context.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith(prefix, StringComparison.Ordinal))
                return trimmed[prefix.Length..].Trim();
        }

        return string.Empty;
    }

    static QChatAgentRoute OwnerPrivateRoute() => new(
        "xiayu",
        10001,
        QChatConversationKind.Private,
        20002,
        20002,
        true,
        "qq:xiayu:10001:private:20002");

    static QChatAgentProfile OwnerProfile() => new(
        "xiayu",
        "XiaYu",
        "persona.md",
        "owner",
        "test-model",
        "owner",
        [],
        new QChatAgentCapabilities(
            AllowComputerFileTools: true,
            AllowProjectModification: true,
            AllowRecall: true,
            AllowPoke: true));

    sealed class FixedPlanner(DataAgentQueryPlan plan) : IDataAgentQueryPlanner
    {
        public DataAgentQueryPlanEnvelope Plan(DataAgentQueryRequest request)
        {
            return new DataAgentQueryPlanEnvelope(
                plan,
                new DataAgentPlannerExplanation(
                    nameof(FixedPlanner),
                    plan.Intent,
                    plan.Dataset,
                    "high",
                    ["v3_9_replay_runbook"],
                    "V3.9 replay runbook fixed planner"));
        }
    }

    sealed class RecordingDataAgentStore : IDataAgentStore
    {
        readonly List<DataAgentAuditRecord> queryAudit = [];
        readonly List<DataAgentToolBrokerAuditRecord> toolBrokerAudit = [];

        public string ProviderName => "recording";
        public int QueryCount { get; private set; }
        public List<DataAgentAcceptedAuditInput> AcceptedAudit { get; } = [];
        public List<DataAgentRejectedAuditInput> RejectedAudit { get; } = [];

        public void Initialize() {}

        public void ImportFixtures() {}

        public DataAgentQueryResult Query(DataAgentCompiledSql compiledSql)
        {
            QueryCount++;
            return new DataAgentQueryResult([
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["path"] = "docs/dataagent/dataagent-v3.9.md",
                    ["title"] = "DataAgent V3.9 replay runbook"
                }
            ]);
        }

        public void RecordAccepted(DataAgentAcceptedAuditInput input)
        {
            AcceptedAudit.Add(input);
            queryAudit.Add(new DataAgentAuditRecord(
                input.Question,
                input.Dataset,
                input.QueryPlanJson,
                input.GeneratedSql,
                true,
                string.Empty,
                input.RowCount,
                input.Elapsed,
                DateTimeOffset.UtcNow));
        }

        public void RecordRejected(DataAgentRejectedAuditInput input)
        {
            RejectedAudit.Add(input);
            queryAudit.Add(new DataAgentAuditRecord(
                input.Question,
                input.Dataset,
                input.QueryPlanJson,
                input.GeneratedSql,
                false,
                input.RejectedReason,
                0,
                input.Elapsed,
                DateTimeOffset.UtcNow));
        }

        public IReadOnlyList<DataAgentAuditRecord> ReadQueryAudit() => queryAudit;

        public void RecordToolBrokerAudit(DataAgentToolBrokerAuditRecord record)
        {
            toolBrokerAudit.Add(record);
        }

        public IReadOnlyList<DataAgentToolBrokerAuditRecord> ReadToolBrokerAudit() => toolBrokerAudit;
    }
}
```

- [ ] **Step 4: Create report formatter**

Create `tools/dataagent-replay/DataAgentReplayReportFormatter.cs`:

```csharp
using System.Text;
using System.Text.Json;

namespace Alife.Tools.DataAgentReplay;

public static class DataAgentReplayReportFormatter
{
    public static string FormatMarkdown(DataAgentReplayResult result)
    {
        StringBuilder builder = new();
        builder.AppendLine($"# DataAgent Replay: {result.Fixture.Name}");
        builder.AppendLine();
        builder.AppendLine("## Fixture");
        builder.AppendLine($"- version: {result.Fixture.Version}");
        builder.AppendLine($"- caller: {result.Fixture.CallerId}");
        builder.AppendLine($"- utterance: {result.Fixture.Utterance}");
        builder.AppendLine();
        builder.AppendLine("## Route Decision");
        builder.AppendLine($"- domain: {result.Route.Domain}");
        builder.AppendLine($"- intent: {result.Route.Intent}");
        builder.AppendLine($"- reason_code: {result.Route.ReasonCode}");
        builder.AppendLine($"- allowed_tools: {string.Join(", ", result.Route.AllowedTools)}");
        builder.AppendLine();
        builder.AppendLine("## XML Policy");
        builder.AppendLine($"- allowed: {LowerBool(result.XmlPolicy.Allowed)}");
        builder.AppendLine($"- reason: {result.XmlPolicy.Reason}");
        builder.AppendLine();
        builder.AppendLine("## Route Context");
        builder.AppendLine($"- present: {LowerBool(result.RouteContext.Present)}");
        builder.AppendLine($"- tool: {result.RouteContext.ToolName}");
        builder.AppendLine($"- allows_query: {LowerBool(result.RouteContext.AllowsQuery)}");
        builder.AppendLine($"- reason_code: {result.RouteContext.ReasonCode}");
        builder.AppendLine($"- route_session_id: {result.RouteContext.RouteSessionId}");
        builder.AppendLine();
        builder.AppendLine("## Orchestration");
        builder.AppendLine($"- trace: {result.Orchestration.Trace}");
        builder.AppendLine($"- accepted: {LowerBool(result.Orchestration.Accepted)}");
        builder.AppendLine($"- row_count: {result.Orchestration.RowCount}");
        builder.AppendLine();
        builder.AppendLine("## Session");
        builder.AppendLine($"- session_id: {result.Session.SessionId}");
        builder.AppendLine($"- status: {result.Session.Status}");
        builder.AppendLine($"- active_route_session: {LowerBool(result.Session.HasActiveRouteSession)}");
        builder.AppendLine();
        builder.AppendLine("## Diagnostics");
        builder.AppendLine($"- evidence: {FirstLine(result.Diagnostics.Evidence)}");
        builder.AppendLine($"- trace: {FirstLine(result.Diagnostics.Trace)}");
        builder.AppendLine($"- progress: {FirstLine(result.Diagnostics.Progress)}");
        builder.AppendLine($"- graph: {FirstLine(result.Diagnostics.Graph)}");
        builder.AppendLine($"- qchat_evidence: {FirstLine(result.Diagnostics.QChatEvidence)}");
        builder.AppendLine($"- qchat_trace: {FirstLine(result.Diagnostics.QChatTrace)}");
        builder.AppendLine($"- qchat_progress: {FirstLine(result.Diagnostics.QChatProgress)}");
        builder.AppendLine($"- qchat_graph: {FirstLine(result.Diagnostics.QChatGraph)}");
        builder.AppendLine();
        builder.AppendLine("## Expected Markers");
        foreach (DataAgentReplayExpectedMarker marker in result.ExpectedMarkers)
            builder.AppendLine($"- {(marker.Passed ? "PASS" : "MISSING")} {marker.Marker}");
        builder.AppendLine();
        builder.AppendLine("## Offline Boundary");
        builder.AppendLine($"- sidecar_authority={LowerBool(result.OfflineBoundary.SidecarAuthority)}");
        builder.AppendLine($"- default_tests_live_runtime={LowerBool(result.OfflineBoundary.DefaultTestsLiveRuntime)}");
        builder.AppendLine($"- passed={LowerBool(result.Passed)}");
        return builder.ToString();
    }

    public static string FormatJson(DataAgentReplayResult result)
    {
        return JsonSerializer.Serialize(result, DataAgentReplayFixture.ReportJsonOptions);
    }

    static string FirstLine(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unavailable";

        return value.ReplaceLineEndings("\n").Split('\n')[0].Trim();
    }

    static string LowerBool(bool value) => value ? "true" : "false";
}
```

- [ ] **Step 5: Run replay tests and verify model/runner/formatter tests pass except script tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReplayRunbookTests.RunnerExecutesDefaultFixtureThroughRealOfflineChain|FullyQualifiedName~DataAgentReplayRunbookTests.MarkdownFormatterEmitsStableRunbookSections|FullyQualifiedName~DataAgentReplayRunbookTests.JsonFormatterEmitsParseableStableTopLevelShape|FullyQualifiedName~DataAgentReplayRunbookTests.RunnerReportsMissingExpectedMarkerWithoutThrowing|FullyQualifiedName~DataAgentReplayRunbookTests.ReplayImplementationUsesRealRoutePolicyAccessorAndDisabledRuntime" -v:minimal
```

Expected: pass. If constructor signatures differ from the snippets, adjust only the argument order to match existing types and keep the same behavior.

- [ ] **Step 6: Commit contract tests, harness project, models, runner, and formatters**

Run:

```powershell
git add Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj Tests\Alife.Test.DataAgent\Fixtures\DataAgentReplay\v3.9-owner-readiness-analysis.json Tests\Alife.Test.DataAgent\DataAgentReplayRunbookTests.cs tools\dataagent-replay\Alife.Tools.DataAgentReplay.csproj tools\dataagent-replay\DataAgentReplayModels.cs tools\dataagent-replay\DataAgentReplayRunner.cs tools\dataagent-replay\DataAgentReplayReportFormatter.cs
git commit -m "Add DataAgent V3.9 replay harness"
```

---

### Task 3: Add CLI Program And PowerShell Wrapper

**Files:**
- Create: `tools/dataagent-replay/Program.cs`
- Create: `tools/replay-dataagent-chain.ps1`

- [ ] **Step 1: Create the console entry point**

Create `tools/dataagent-replay/Program.cs`:

```csharp
using Alife.Tools.DataAgentReplay;

return DataAgentReplayProgram.Run(args);

public static class DataAgentReplayProgram
{
    public static int Run(string[] args)
    {
        try
        {
            DataAgentReplayOptions options = DataAgentReplayOptions.Parse(args);
            DataAgentReplayFixture fixture = DataAgentReplayFixture.Load(options.FixturePath);
            DataAgentReplayResult result = DataAgentReplayRunner.Run(fixture);
            string output = options.Format switch
            {
                "markdown" => DataAgentReplayReportFormatter.FormatMarkdown(result),
                "json" => DataAgentReplayReportFormatter.FormatJson(result),
                _ => throw new ArgumentOutOfRangeException(nameof(options.Format), options.Format, "Unsupported format.")
            };

            Console.Out.Write(output);
            return result.Passed ? 0 : 2;
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine($"Fixture not found: {ex.FileName}");
            return 1;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }
}

public sealed record DataAgentReplayOptions(string FixturePath, string Format)
{
    public static DataAgentReplayOptions Parse(string[] args)
    {
        string? fixture = null;
        string format = "markdown";

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (string.Equals(arg, "--fixture", StringComparison.OrdinalIgnoreCase))
            {
                fixture = ReadValue(args, ref i, "--fixture");
                continue;
            }

            if (string.Equals(arg, "--format", StringComparison.OrdinalIgnoreCase))
            {
                format = ReadValue(args, ref i, "--format").Trim().ToLowerInvariant();
                continue;
            }

            throw new ArgumentException($"Unsupported argument: {arg}");
        }

        if (string.IsNullOrWhiteSpace(fixture))
            throw new ArgumentException("Missing required --fixture argument.");

        if (format is not ("markdown" or "json"))
            throw new ArgumentException($"Unsupported format '{format}'. Accepted formats: markdown, json.");

        return new DataAgentReplayOptions(fixture, format);
    }

    static string ReadValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException($"Missing value for {option}.");

        index++;
        return args[index];
    }
}
```

- [ ] **Step 2: Create the PowerShell wrapper**

Create `tools/replay-dataagent-chain.ps1`:

```powershell
Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

param(
    [string]$Fixture = "",
    [ValidateSet("markdown", "json")]
    [string]$Format = "markdown"
)

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot

if ([string]::IsNullOrWhiteSpace($Fixture)) {
    $Fixture = Join-Path $repoRoot "Tests\Alife.Test.DataAgent\Fixtures\DataAgentReplay\v3.9-owner-readiness-analysis.json"
}
elseif ([System.IO.Path]::IsPathRooted($Fixture) -eq $false) {
    $Fixture = Join-Path $repoRoot $Fixture
}

if ((Test-Path -LiteralPath $Fixture) -eq $false) {
    Write-Error "Fixture not found: $Fixture"
    exit 1
}

$localDotnet = "C:\Users\hu shu\.dotnet\dotnet.exe"
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { "dotnet" }
$project = Join-Path $repoRoot "tools\dataagent-replay\Alife.Tools.DataAgentReplay.csproj"

& $dotnet run --project $project -- --fixture $Fixture --format $Format
exit $LASTEXITCODE
```

- [ ] **Step 3: Run CLI script tests and verify they pass**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReplayRunbookTests.ReplayScriptDefaultRunEmitsMarkdown|FullyQualifiedName~DataAgentReplayRunbookTests.ReplayScriptJsonRunEmitsParseableJson|FullyQualifiedName~DataAgentReplayRunbookTests.ReplayScriptMissingFixtureFailsWithClearError|FullyQualifiedName~DataAgentReplayRunbookTests.ReplayScriptUnsupportedFormatFailsWithClearError" -v:minimal
```

Expected: pass.

- [ ] **Step 4: Run the script manually in both formats**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\replay-dataagent-chain.ps1
```

Expected: exit code `0`, output includes:

```text
# DataAgent Replay: owner-readiness-analysis
## Expected Markers
PASS route_allowed
sidecar_authority=false
default_tests_live_runtime=false
```

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\replay-dataagent-chain.ps1 -Format json
```

Expected: exit code `0`, output is parseable JSON and includes:

```json
"passed": true
```

- [ ] **Step 5: Commit CLI program and wrapper**

Run:

```powershell
git add tools\dataagent-replay\Program.cs tools\replay-dataagent-chain.ps1
git commit -m "Add DataAgent V3.9 replay CLI"
```

---

### Task 4: Add Dynamic Readiness Marker

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`

- [ ] **Step 1: Update failing dynamic readiness tests**

In `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`, change the core readiness count from `77` to `78`.

Add assertions after the V3.8 `DataAgentEndToEndChainContractPresent` assertions:

```csharp
Assert.That(checks.Select(check => check.Name), Does.Contain("DataAgentReplayRunbookPresent"));
DataAgentReadinessCheck replayRunbookCheck = checks.Single(check => check.Name == "DataAgentReplayRunbookPresent");
Assert.That(replayRunbookCheck.Detail, Does.Contain("cli=true"));
Assert.That(replayRunbookCheck.Detail, Does.Contain("fixture=true"));
Assert.That(replayRunbookCheck.Detail, Does.Contain("real_chain=true"));
Assert.That(replayRunbookCheck.Detail, Does.Contain("markdown=true"));
Assert.That(replayRunbookCheck.Detail, Does.Contain("json=true"));
Assert.That(replayRunbookCheck.Detail, Does.Contain("expected_markers=true"));
Assert.That(replayRunbookCheck.Detail, Does.Contain("sidecar_authority=false"));
Assert.That(replayRunbookCheck.Detail, Does.Contain("default_tests_live_runtime=false"));
```

- [ ] **Step 2: Run dynamic readiness and verify it fails**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests.CoreReadinessChecksAllPass" -v:minimal
```

Expected: fail because `DataAgentReplayRunbookPresent` is not yet returned by `DataAgentReadiness.CheckCore`.

- [ ] **Step 3: Add dynamic readiness check**

In `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`, insert this block immediately after `DataAgentEndToEndChainContractPresent`:

```csharp
string dataAgentReplayScriptPath = Path.Combine(
    FindRepositoryRoot(AppContext.BaseDirectory),
    "tools",
    "replay-dataagent-chain.ps1");
string dataAgentReplayProjectPath = Path.Combine(
    FindRepositoryRoot(AppContext.BaseDirectory),
    "tools",
    "dataagent-replay",
    "Alife.Tools.DataAgentReplay.csproj");
string dataAgentReplayRunnerPath = Path.Combine(
    FindRepositoryRoot(AppContext.BaseDirectory),
    "tools",
    "dataagent-replay",
    "DataAgentReplayRunner.cs");
string dataAgentReplayFormatterPath = Path.Combine(
    FindRepositoryRoot(AppContext.BaseDirectory),
    "tools",
    "dataagent-replay",
    "DataAgentReplayReportFormatter.cs");
string dataAgentReplayFixturePath = Path.Combine(
    FindRepositoryRoot(AppContext.BaseDirectory),
    "Tests",
    "Alife.Test.DataAgent",
    "Fixtures",
    "DataAgentReplay",
    "v3.9-owner-readiness-analysis.json");
string dataAgentReplayTestsPath = Path.Combine(
    FindRepositoryRoot(AppContext.BaseDirectory),
    "Tests",
    "Alife.Test.DataAgent",
    "DataAgentReplayRunbookTests.cs");

string dataAgentReplayScriptSource = File.Exists(dataAgentReplayScriptPath)
    ? File.ReadAllText(dataAgentReplayScriptPath)
    : string.Empty;
string dataAgentReplayRunnerSource = File.Exists(dataAgentReplayRunnerPath)
    ? File.ReadAllText(dataAgentReplayRunnerPath)
    : string.Empty;
string dataAgentReplayFormatterSource = File.Exists(dataAgentReplayFormatterPath)
    ? File.ReadAllText(dataAgentReplayFormatterPath)
    : string.Empty;
bool dataAgentReplayCliReady =
    File.Exists(dataAgentReplayScriptPath) &&
    File.Exists(dataAgentReplayProjectPath) &&
    dataAgentReplayScriptSource.Contains("v3.9-owner-readiness-analysis.json", StringComparison.Ordinal) &&
    dataAgentReplayScriptSource.Contains("C:\\Users\\hu shu\\.dotnet\\dotnet.exe", StringComparison.Ordinal);
bool dataAgentReplayFixtureReady = File.Exists(dataAgentReplayFixturePath);
bool dataAgentReplayRealChainReady =
    dataAgentReplayRunnerSource.Contains("ToolCapabilityRouter.CreateDefault", StringComparison.Ordinal) &&
    dataAgentReplayRunnerSource.Contains("XmlFunctionExecutionPolicy", StringComparison.Ordinal) &&
    dataAgentReplayRunnerSource.Contains("XmlPolicyDataAgentToolRouteContextAccessor", StringComparison.Ordinal) &&
    dataAgentReplayRunnerSource.Contains("DataAgentAnalysisToolHandler", StringComparison.Ordinal) &&
    dataAgentReplayRunnerSource.Contains("QChatDiagnosticsService", StringComparison.Ordinal) &&
    dataAgentReplayRunnerSource.Contains("FixedRouteContextAccessor", StringComparison.Ordinal) == false;
bool dataAgentReplayMarkdownReady =
    dataAgentReplayFormatterSource.Contains("FormatMarkdown", StringComparison.Ordinal) &&
    dataAgentReplayFormatterSource.Contains("# DataAgent Replay:", StringComparison.Ordinal) &&
    dataAgentReplayFormatterSource.Contains("## Expected Markers", StringComparison.Ordinal);
bool dataAgentReplayJsonReady =
    dataAgentReplayFormatterSource.Contains("FormatJson", StringComparison.Ordinal) &&
    dataAgentReplayFormatterSource.Contains("JsonSerializer.Serialize", StringComparison.Ordinal);
bool dataAgentReplayExpectedMarkersReady =
    dataAgentReplayRunnerSource.Contains("ExpectedMarkers", StringComparison.Ordinal) &&
    dataAgentReplayRunnerSource.Contains(".Select(marker => new DataAgentReplayExpectedMarker", StringComparison.Ordinal) &&
    dataAgentReplayRunnerSource.Contains("combined.Contains(marker, StringComparison.Ordinal)", StringComparison.Ordinal) &&
    dataAgentReplayRunnerSource.Contains("All(marker => marker.Passed)", StringComparison.Ordinal);
bool dataAgentReplaySidecarBoundaryReady =
    dataAgentReplayRunnerSource.Contains("DataAgentGraphHandshakeOptions.Disabled", StringComparison.Ordinal) &&
    dataAgentReplayRunnerSource.Contains("DisabledDataAgentGraphSidecarClient.Instance", StringComparison.Ordinal);
bool dataAgentReplayDefaultLiveRuntimeReady =
    dataAgentReplayScriptSource.Contains("uvicorn", StringComparison.OrdinalIgnoreCase) == false &&
    dataAgentReplayScriptSource.Contains("Start-Process", StringComparison.Ordinal) == false &&
    dataAgentReplayRunnerSource.Contains("DataAgentGraphHandshakeHttpClient", StringComparison.Ordinal) == false;
bool dataAgentReplayTestsReady =
    File.Exists(dataAgentReplayTestsPath);
bool dataAgentReplayRunbookReady =
    dataAgentReplayCliReady &&
    dataAgentReplayFixtureReady &&
    dataAgentReplayRealChainReady &&
    dataAgentReplayMarkdownReady &&
    dataAgentReplayJsonReady &&
    dataAgentReplayExpectedMarkersReady &&
    dataAgentReplaySidecarBoundaryReady &&
    dataAgentReplayDefaultLiveRuntimeReady &&
    dataAgentReplayTestsReady;
string dataAgentReplayRunbookDetail =
    $"cli={LowerBool(dataAgentReplayCliReady)};fixture={LowerBool(dataAgentReplayFixtureReady)};real_chain={LowerBool(dataAgentReplayRealChainReady)};markdown={LowerBool(dataAgentReplayMarkdownReady)};json={LowerBool(dataAgentReplayJsonReady)};expected_markers={LowerBool(dataAgentReplayExpectedMarkersReady)};sidecar_authority={LowerBool(dataAgentReplaySidecarBoundaryReady == false)};default_tests_live_runtime={LowerBool(dataAgentReplayDefaultLiveRuntimeReady == false)}";
checks.Add(dataAgentReplayRunbookReady
    ? Pass("DataAgentReplayRunbookPresent", "cli=true;fixture=true;real_chain=true;markdown=true;json=true;expected_markers=true;sidecar_authority=false;default_tests_live_runtime=false")
    : Fail("DataAgentReplayRunbookPresent", dataAgentReplayRunbookDetail));
```

The `MISSING` text belongs in the formatter, while the runner readiness check verifies marker evaluation from the fixture through `DataAgentReplayExpectedMarker.Passed`.

- [ ] **Step 4: Run dynamic readiness and verify it passes**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests.CoreReadinessChecksAllPass" -v:minimal
```

Expected: pass with `78` checks and `DataAgentReplayRunbookPresent`.

- [ ] **Step 5: Commit dynamic readiness marker**

Run:

```powershell
git add sources\Alife.Function\Alife.Function.DataAgent\DataAgentReadiness.cs Tests\Alife.Test.DataAgent\DataAgentReadinessTests.cs
git commit -m "Add DataAgent V3.9 replay dynamic readiness"
```

---

### Task 5: Add Static Readiness Marker And Version Guard Alignment

**Files:**
- Modify: `tools/check-dataagent-readiness.ps1`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
- Modify: version guard readiness tests with `$expectedRequired = 92`

- [ ] **Step 1: Update failing static readiness tests**

In `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`, update the default script summary assertion from:

```csharp
"  Summary: 92 required passed, 0 required missing"
```

to:

```csharp
"  Summary: 93 required passed, 0 required missing"
```

Add:

```csharp
Assert.That(result.StandardOutput, Does.Contain("DataAgentReplayRunbookPresent"));
```

Update script count protection from:

```csharp
Assert.That(script, Does.Contain("$expectedRequired = 92"));
```

to:

```csharp
Assert.That(script, Does.Contain("$expectedRequired = 93"));
```

Add this test after the V3.8 static marker test:

```csharp
[Test]
public void StaticReadinessScriptContainsV39ReplayRunbookMarkers()
{
    string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
    string scriptPath = Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1");
    string script = File.ReadAllText(scriptPath);
    string declaration = FindNewCheckDeclaration(script, "DataAgentReplayRunbookPresent");

    Assert.Multiple(() =>
    {
        Assert.That(declaration, Does.Contain("tools/replay-dataagent-chain.ps1"));
        Assert.That(declaration, Does.Contain("tools/dataagent-replay/Alife.Tools.DataAgentReplay.csproj"));
        Assert.That(declaration, Does.Contain("DataAgentReplayRunner.cs"));
        Assert.That(declaration, Does.Contain("DataAgentReplayReportFormatter.cs"));
        Assert.That(declaration, Does.Contain("v3.9-owner-readiness-analysis.json"));
        Assert.That(declaration, Does.Contain("DataAgentReplayRunbookTests.cs"));
        Assert.That(declaration, Does.Contain("ToolCapabilityRouter.CreateDefault"));
        Assert.That(declaration, Does.Contain("XmlPolicyDataAgentToolRouteContextAccessor"));
        Assert.That(declaration, Does.Contain("QChatDiagnosticsService"));
        Assert.That(declaration, Does.Contain("DataAgentGraphHandshakeOptions.Disabled"));
        Assert.That(declaration, Does.Contain("FormatMarkdown"));
        Assert.That(declaration, Does.Contain("FormatJson"));
        Assert.That(declaration, Does.Contain("sidecar_authority=false"));
        Assert.That(declaration, Does.Contain("default_tests_live_runtime=false"));
    });
}
```

Find every stale version guard:

```powershell
rg -n "\$expectedRequired = 92|Summary: 92 required passed" Tests\Alife.Test.DataAgent
```

Change every static readiness version guard that protects the script global count to `93`.

- [ ] **Step 2: Run static readiness tests and verify they fail**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests.ReadinessScriptDefaultModeExitsZeroAndPrintsSummary|FullyQualifiedName~DataAgentReadinessTests.StaticReadinessScriptContainsV39ReplayRunbookMarkers|FullyQualifiedName~StaticReadinessScriptContainsV" -v:minimal
```

Expected: fail because `tools/check-dataagent-readiness.ps1` still has `92` and does not contain `DataAgentReplayRunbookPresent`.

- [ ] **Step 3: Add the static readiness script marker**

In `tools/check-dataagent-readiness.ps1`, insert this check in the `Governance` group immediately after `DataAgentEndToEndChainContractPresent`:

```powershell
    New-Check -Group "Governance" -Name "DataAgentReplayRunbookPresent" -Passed ((Test-FileMarker "tools/replay-dataagent-chain.ps1" @("v3.9-owner-readiness-analysis.json", "C:\Users\hu shu\.dotnet\dotnet.exe", "Alife.Tools.DataAgentReplay.csproj", "-Format")) -and (Test-FileMarker "tools/dataagent-replay/Alife.Tools.DataAgentReplay.csproj" @("OutputType", "Alife.Function.DataAgent.csproj", "Alife.Function.FunctionCaller.csproj", "Alife.Function.QChat.csproj")) -and (Test-FileMarker "tools/dataagent-replay/DataAgentReplayRunner.cs" @("ToolCapabilityRouter.CreateDefault", "XmlFunctionExecutionPolicy", "XmlPolicyDataAgentToolRouteContextAccessor", "DataAgentAnalysisToolHandler", "QChatDiagnosticsService", "DataAgentGraphHandshakeOptions.Disabled", "DisabledDataAgentGraphSidecarClient.Instance")) -and (Test-FileMarker "tools/dataagent-replay/DataAgentReplayReportFormatter.cs" @("FormatMarkdown", "FormatJson", "# DataAgent Replay:", "## Expected Markers")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/Fixtures/DataAgentReplay/v3.9-owner-readiness-analysis.json" @("""version"": ""v3.9""", """name"": ""owner-readiness-analysis""", """expectedMarkers""", "sidecar_authority=false", "default_tests_live_runtime=false")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentReplayRunbookTests.cs" @("ReplayScriptDefaultRunEmitsMarkdown", "ReplayScriptJsonRunEmitsParseableJson", "RunnerExecutesDefaultFixtureThroughRealOfflineChain", "ReplayImplementationUsesRealRoutePolicyAccessorAndDisabledRuntime"))) -Detail "V3.9 DataAgent replay runbook cli=true fixture=true real_chain=true markdown=true json=true expected_markers=true sidecar_authority=false default_tests_live_runtime=false"
```

Change:

```powershell
$expectedRequired = 92
```

to:

```powershell
$expectedRequired = 93
```

- [ ] **Step 4: Run static readiness tests and script**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests.ReadinessScriptDefaultModeExitsZeroAndPrintsSummary|FullyQualifiedName~DataAgentReadinessTests.StaticReadinessScriptContainsV39ReplayRunbookMarkers|FullyQualifiedName~StaticReadinessScriptContainsV" -v:minimal
```

Expected: pass.

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected:

```text
PASS     DataAgentReplayRunbookPresent
Summary: 93 required passed, 0 required missing
```

- [ ] **Step 5: Commit static readiness marker**

Run:

```powershell
git add tools\check-dataagent-readiness.ps1 Tests\Alife.Test.DataAgent\DataAgentReadinessTests.cs Tests\Alife.Test.DataAgent\*ReadinessTests.cs
git commit -m "Add DataAgent V3.9 replay static readiness"
```

---

### Task 6: Final Verification

**Files:**
- Verify only unless a correction is required.

- [ ] **Step 1: Run focused replay and readiness tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReplayRunbookTests|FullyQualifiedName~DataAgentReadinessTests" -v:minimal
```

Expected: pass.

- [ ] **Step 2: Run both replay CLI formats**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\replay-dataagent-chain.ps1
```

Expected: exit code `0`, Markdown output includes:

```text
# DataAgent Replay: owner-readiness-analysis
## Expected Markers
PASS route_allowed
sidecar_authority=false
default_tests_live_runtime=false
```

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\replay-dataagent-chain.ps1 -Format json
```

Expected: exit code `0`, JSON output includes `"passed": true` and parses with `System.Text.Json`.

- [ ] **Step 3: Run static readiness**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected:

```text
PASS     DataAgentReplayRunbookPresent
Summary: 93 required passed, 0 required missing
```

- [ ] **Step 4: Run full DataAgent tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
```

Expected: pass, with live Postgres tests skipped by default.

- [ ] **Step 5: Verify changed files**

Run:

```powershell
git diff --name-only $v39Base..HEAD
```

Expected changed files are limited to:

```text
Tests/Alife.Test.DataAgent/Alife.Test.DataAgent.csproj
Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs
Tests/Alife.Test.DataAgent/DataAgentReplayRunbookTests.cs
Tests/Alife.Test.DataAgent/Fixtures/DataAgentReplay/v3.9-owner-readiness-analysis.json
Tests/Alife.Test.DataAgent/*ReadinessTests.cs
sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs
tools/check-dataagent-readiness.ps1
tools/replay-dataagent-chain.ps1
tools/dataagent-replay/Alife.Tools.DataAgentReplay.csproj
tools/dataagent-replay/DataAgentReplayModels.cs
tools/dataagent-replay/DataAgentReplayRunner.cs
tools/dataagent-replay/DataAgentReplayReportFormatter.cs
tools/dataagent-replay/Program.cs
```

- [ ] **Step 6: Run diff whitespace check**

Run:

```powershell
git diff --check $v39Base..HEAD
```

Expected: no output and exit code `0`.

- [ ] **Step 7: Commit any final correction**

If a correction is needed, run:

```powershell
git add Tests\Alife.Test.DataAgent tools\replay-dataagent-chain.ps1 tools\dataagent-replay sources\Alife.Function\Alife.Function.DataAgent\DataAgentReadiness.cs tools\check-dataagent-readiness.ps1
git commit -m "Verify DataAgent V3.9 replay runbook"
```

If no correction is needed, do not create an empty commit.
