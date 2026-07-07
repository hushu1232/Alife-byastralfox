# DataAgent V3.1 Dev Sidecar Adapter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an optional local development HTTP sidecar adapter for the existing DataAgent V3.0 graph handshake boundary without starting Python or weakening C# authority.

**Architecture:** Reuse `IDataAgentGraphSidecarClient`, `DataAgentGraphHandshakeCoordinator`, and `DataAgentGraphHandshakeValidator`. Add fail-closed HTTP endpoint/timeout options, a loopback-only C# HTTP client, manual Python/FastAPI dev stub files under `tools/`, readiness gates, and documentation. Default runtime and default tests must not require Python, FastAPI, network access, live QChat, PostgreSQL, browser automation, model calls, or QQ.

**Tech Stack:** .NET 9, C#, `HttpClient`, `System.Text.Json`, NUnit, PowerShell readiness scripts, optional Python/FastAPI dev stub.

---

## Scope Check

This plan implements one milestone: V3.1 dev-only request/response transport for DataAgent graph handshake suggestions.

This plan does not add production LangGraph runtime behavior, streaming sidecar progress, human-in-the-loop interrupts, checkpointer reconciliation, automatic Python process management, sidecar tool execution, SQL authority, Tool Broker authority, QChat ownership, or plugin agentization.

## File Structure

- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeHttpOptions.cs`
  - Parses `ALIFE_DATAAGENT_GRAPH_HANDSHAKE_ENDPOINT` and `ALIFE_DATAAGENT_GRAPH_HANDSHAKE_TIMEOUT_MS`, enforces loopback-only endpoints, and exposes deterministic defaults.

- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeHttpClient.cs`
  - Implements `IDataAgentGraphSidecarClient` with `HttpClient`, JSON serialization, timeout handling, non-success status handling, malformed JSON handling, and no direct validation authority.

- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeCoordinator.cs`
  - Adds an invalid-sidecar-response exception catch so malformed HTTP JSON can map to `Invalid / invalid_response_schema`, not generic unavailable.

- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`
  - Wires the HTTP sidecar client only when handshake is explicitly enabled and a valid loopback endpoint is configured.

- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
  - Adds a V3.1 readiness check proving HTTP adapter presence, default no-runtime, endpoint-required, loopback-only policy, fallback, validator, and no-SQL-authority behavior.

- Modify: `tools/check-dataagent-readiness.ps1`
  - Adds static V3.1 readiness markers and increments `$expectedRequired` from `86` to `87`.

- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
  - Updates dynamic count from `72` to `73`, script summary from `86` to `87`, and adds V3.1 dynamic/static assertions.

- Modify: `Tests/Alife.Test.DataAgent/DataAgentV210ReadinessTests.cs`
  - Updates shared DataAgent readiness expected count to `87`.

- Modify: `Tests/Alife.Test.DataAgent/DataAgentV216ReadinessTests.cs`
  - Updates shared DataAgent readiness expected count to `87`.

- Modify: `Tests/Alife.Test.DataAgent/DataAgentV30ReadinessTests.cs`
  - Updates shared static readiness count references to `87` if present.

- Create: `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeHttpOptionsTests.cs`
  - Tests fail-closed endpoint and timeout parsing.

- Create: `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeHttpClientTests.cs`
  - Tests HTTP serialization, accepted responses, unsafe response rejection through coordinator, timeout, non-success, malformed JSON, and no raw payload retention.

- Modify: `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeCoordinatorTests.cs`
  - Adds invalid response exception mapping if not covered by HTTP client tests.

- Modify: `Tests/Alife.Test.DataAgent/DataAgentModuleServiceTests.cs`
  - Verifies module source/factory wiring for the dev HTTP sidecar adapter without starting a runtime.

- Create: `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeDevSidecarStubTests.cs`
  - Static tests proving the Python stub is optional, local-only, exposes `/handshake` and `/health`, and contains no forbidden authority markers.

- Modify: `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`
  - Confirm existing forbidden marker coverage still blocks `DataAgentGraphHandshake*` and add `DataAgentGraphSidecar` if not already present.

- Modify: `tools/check-qchat-engineering-map.ps1`
  - Keep `$expectedRequired = 63`; ensure production QChat omit scan still covers graph sidecar/handshake markers without adding a new QChat command or channel.

- Create: `tools/dataagent-graph-sidecar/app.py`
  - Optional manually run FastAPI dev stub.

- Create: `tools/dataagent-graph-sidecar/requirements.txt`
  - Minimal optional Python dependencies.

- Create: `tools/dataagent-graph-sidecar/README.md`
  - Manual startup and boundary notes.

- Create: `docs/dataagent/dataagent-v3.1-dev-sidecar-adapter.md`
  - Developer note explaining scope, configuration, fallback, and V3.2 handoff.

---

### Task 1: Add HTTP Adapter Options

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeHttpOptions.cs`
- Create: `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeHttpOptionsTests.cs`

- [ ] **Step 1: Write failing option tests**

Create `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeHttpOptionsTests.cs`:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentGraphHandshakeHttpOptionsTests
{
    [Test]
    public void DefaultsAreUnconfiguredAndDoNotStartRuntime()
    {
        DataAgentGraphHandshakeHttpOptions options = DataAgentGraphHandshakeHttpOptions.FromValues(null, null);

        Assert.Multiple(() =>
        {
            Assert.That(DataAgentGraphHandshakeHttpOptions.EndpointEnvironmentVariable, Is.EqualTo("ALIFE_DATAAGENT_GRAPH_HANDSHAKE_ENDPOINT"));
            Assert.That(DataAgentGraphHandshakeHttpOptions.TimeoutEnvironmentVariable, Is.EqualTo("ALIFE_DATAAGENT_GRAPH_HANDSHAKE_TIMEOUT_MS"));
            Assert.That(options.Configured, Is.False);
            Assert.That(options.Endpoint, Is.Null);
            Assert.That(options.Timeout, Is.EqualTo(TimeSpan.FromMilliseconds(800)));
            Assert.That(options.RuntimeStarted, Is.False);
        });
    }

    [TestCase("http://127.0.0.1:8765/handshake")]
    [TestCase("http://localhost:8765/handshake")]
    [TestCase("https://127.0.0.1:8765/handshake")]
    public void LoopbackHttpEndpointsAreAccepted(string endpoint)
    {
        DataAgentGraphHandshakeHttpOptions options = DataAgentGraphHandshakeHttpOptions.FromValues(endpoint, "1200");

        Assert.Multiple(() =>
        {
            Assert.That(options.Configured, Is.True);
            Assert.That(options.Endpoint, Is.Not.Null);
            Assert.That(options.Endpoint!.ToString(), Is.EqualTo(endpoint));
            Assert.That(options.Timeout, Is.EqualTo(TimeSpan.FromMilliseconds(1200)));
            Assert.That(options.RuntimeStarted, Is.False);
        });
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("not-a-uri")]
    [TestCase("ftp://127.0.0.1:8765/handshake")]
    [TestCase("http://example.com/handshake")]
    [TestCase("http://192.168.1.10:8765/handshake")]
    [TestCase("http://0.0.0.0:8765/handshake")]
    public void MissingMalformedAndNonLoopbackEndpointsFailClosed(string? endpoint)
    {
        DataAgentGraphHandshakeHttpOptions options = DataAgentGraphHandshakeHttpOptions.FromValues(endpoint, "1200");

        Assert.Multiple(() =>
        {
            Assert.That(options.Configured, Is.False);
            Assert.That(options.Endpoint, Is.Null);
            Assert.That(options.RuntimeStarted, Is.False);
        });
    }

    [TestCase(null, 800)]
    [TestCase("", 800)]
    [TestCase("0", 800)]
    [TestCase("-5", 800)]
    [TestCase("abc", 800)]
    [TestCase("250", 250)]
    [TestCase("5000", 5000)]
    [TestCase("5001", 800)]
    public void TimeoutParsingFailsClosedToDefault(string? value, int expectedMs)
    {
        DataAgentGraphHandshakeHttpOptions options =
            DataAgentGraphHandshakeHttpOptions.FromValues("http://127.0.0.1:8765/handshake", value);

        Assert.That(options.Timeout, Is.EqualTo(TimeSpan.FromMilliseconds(expectedMs)));
    }
}
```

- [ ] **Step 2: Run tests and verify they fail before implementation**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeHttpOptionsTests" -v:minimal
```

Expected: FAIL at compile time because `DataAgentGraphHandshakeHttpOptions` does not exist.

- [ ] **Step 3: Add HTTP option model**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeHttpOptions.cs`:

```csharp
namespace Alife.Function.DataAgent;

public sealed record DataAgentGraphHandshakeHttpOptions(
    Uri? Endpoint,
    TimeSpan Timeout,
    bool Configured,
    bool RuntimeStarted)
{
    public const string EndpointEnvironmentVariable = "ALIFE_DATAAGENT_GRAPH_HANDSHAKE_ENDPOINT";
    public const string TimeoutEnvironmentVariable = "ALIFE_DATAAGENT_GRAPH_HANDSHAKE_TIMEOUT_MS";
    public const int DefaultTimeoutMs = 800;
    public const int MinTimeoutMs = 100;
    public const int MaxTimeoutMs = 5000;

    public static DataAgentGraphHandshakeHttpOptions Disabled { get; } = new(
        Endpoint: null,
        Timeout: TimeSpan.FromMilliseconds(DefaultTimeoutMs),
        Configured: false,
        RuntimeStarted: false);

    public static DataAgentGraphHandshakeHttpOptions FromEnvironment()
    {
        return FromValues(
            Environment.GetEnvironmentVariable(EndpointEnvironmentVariable),
            Environment.GetEnvironmentVariable(TimeoutEnvironmentVariable));
    }

    public static DataAgentGraphHandshakeHttpOptions FromValues(string? endpointValue, string? timeoutValue)
    {
        TimeSpan timeout = ParseTimeout(timeoutValue);
        if (TryParseLoopbackEndpoint(endpointValue, out Uri? endpoint) == false)
            return Disabled with { Timeout = timeout };

        return new DataAgentGraphHandshakeHttpOptions(
            endpoint,
            timeout,
            Configured: true,
            RuntimeStarted: false);
    }

    static TimeSpan ParseTimeout(string? value)
    {
        if (int.TryParse(value, out int milliseconds) &&
            milliseconds is >= MinTimeoutMs and <= MaxTimeoutMs)
        {
            return TimeSpan.FromMilliseconds(milliseconds);
        }

        return TimeSpan.FromMilliseconds(DefaultTimeoutMs);
    }

    static bool TryParseLoopbackEndpoint(string? value, out Uri? endpoint)
    {
        endpoint = null;
        if (string.IsNullOrWhiteSpace(value) ||
            Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? candidate) == false ||
            candidate.Scheme is not ("http" or "https"))
        {
            return false;
        }

        if (candidate.IsLoopback == false)
            return false;

        endpoint = candidate;
        return true;
    }
}
```

- [ ] **Step 4: Run option tests and verify they pass**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeHttpOptionsTests" -v:minimal
```

Expected: PASS with `Failed: 0`.

- [ ] **Step 5: Commit Task 1**

Run:

```powershell
git add sources\Alife.Function\Alife.Function.DataAgent\DataAgentGraphHandshakeHttpOptions.cs Tests\Alife.Test.DataAgent\DataAgentGraphHandshakeHttpOptionsTests.cs
git commit -m "Add DataAgent graph handshake HTTP options"
```

---

### Task 2: Add HTTP Sidecar Client

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeHttpClient.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeCoordinator.cs`
- Create: `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeHttpClientTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeCoordinatorTests.cs`

- [ ] **Step 1: Write failing HTTP client tests**

Create `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeHttpClientTests.cs`:

```csharp
using System.Net;
using System.Text;
using System.Text.Json;
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentGraphHandshakeHttpClientTests
{
    [Test]
    public void TryHandshakePostsBoundedJsonRequestAndReturnsSafeResponse()
    {
        DataAgentGraphHandshakeRequest? captured = null;
        using HttpClient httpClient = NewClient(request =>
        {
            captured = JsonSerializer.Deserialize<DataAgentGraphHandshakeRequest>(
                request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());

            DataAgentGraphHandshakeResponse response = NewResponse(captured!);
            return Json(response);
        });
        DataAgentGraphHandshakeHttpClient client = new(
            httpClient,
            DataAgentGraphHandshakeHttpOptions.FromValues("http://127.0.0.1:8765/handshake", "800"));

        DataAgentGraphHandshakeRequest request = NewRequest();
        DataAgentGraphHandshakeResponse response = client.TryHandshake(request);

        Assert.Multiple(() =>
        {
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.RequestId, Is.EqualTo(request.RequestId));
            Assert.That(captured.NoSqlAuthority, Is.True);
            Assert.That(captured.ReadOnly, Is.True);
            Assert.That(captured.FallbackAvailable, Is.True);
            Assert.That(captured.NodeManifests, Is.Not.Empty);
            Assert.That(response.RequestId, Is.EqualTo(request.RequestId));
            Assert.That(DataAgentGraphHandshakeValidator.Validate(request, response).Accepted, Is.True);
        });
    }

    [Test]
    public void CoordinatorRejectsUnsafeHttpResponseAndDoesNotRetainRawPayload()
    {
        using HttpClient httpClient = NewClient(request =>
        {
            DataAgentGraphHandshakeRequest handshakeRequest = JsonSerializer.Deserialize<DataAgentGraphHandshakeRequest>(
                request.Content!.ReadAsStringAsync().GetAwaiter().GetResult())!;
            DataAgentGraphHandshakeResponse unsafeResponse = NewResponse(handshakeRequest) with
            {
                TraceSummary = "from document_index where status = failed limit 50"
            };
            return Json(unsafeResponse);
        });
        DataAgentGraphHandshakeHttpClient client = new(
            httpClient,
            DataAgentGraphHandshakeHttpOptions.FromValues("http://127.0.0.1:8765/handshake", "800"));
        DataAgentGraphHandshakeCoordinator coordinator = new(new DataAgentGraphHandshakeOptions(true), client);

        DataAgentGraphHandshakeOutcome outcome = coordinator.TryHandshake(
            "owner",
            "Which gates failed?",
            DataAgentGraphHandshakeCoordinatorTestData.AcceptedResult());

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Rejected));
            Assert.That(outcome.ReasonCode, Is.EqualTo("unsafe_trace"));
            Assert.That(outcome.FallbackRequired, Is.True);
            Assert.That(outcome.Response, Is.Null);
        });
    }

    [Test]
    public void NonSuccessStatusThrowsUnavailableException()
    {
        using HttpClient httpClient = NewClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        DataAgentGraphHandshakeHttpClient client = new(
            httpClient,
            DataAgentGraphHandshakeHttpOptions.FromValues("http://127.0.0.1:8765/handshake", "800"));

        Assert.Throws<InvalidOperationException>(() => client.TryHandshake(NewRequest()));
    }

    [Test]
    public void MalformedJsonThrowsInvalidResponseException()
    {
        using HttpClient httpClient = NewClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{not-json", Encoding.UTF8, "application/json")
        });
        DataAgentGraphHandshakeHttpClient client = new(
            httpClient,
            DataAgentGraphHandshakeHttpOptions.FromValues("http://127.0.0.1:8765/handshake", "800"));

        Assert.Throws<DataAgentGraphSidecarInvalidResponseException>(() => client.TryHandshake(NewRequest()));
    }

    [Test]
    public void TimeoutThrowsTimeoutException()
    {
        using HttpClient httpClient = NewClient(_ => throw new TaskCanceledException("timeout"));
        DataAgentGraphHandshakeHttpClient client = new(
            httpClient,
            DataAgentGraphHandshakeHttpOptions.FromValues("http://127.0.0.1:8765/handshake", "800"));

        Assert.Throws<TimeoutException>(() => client.TryHandshake(NewRequest()));
    }

    static HttpClient NewClient(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        return new HttpClient(new DelegateHandler(handler));
    }

    static HttpResponseMessage Json<T>(T value)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
        };
    }

    static DataAgentGraphHandshakeRequest NewRequest()
    {
        return new DataAgentGraphHandshakeRequest(
            "request-1",
            "session-1",
            "turn-1",
            "owner",
            "Which gates failed?",
            "scenario_context=true",
            "route_allowed",
            "dataset=engineering_gate;limit<=50",
            DataAgentGraphHandshakeManifestFactory.CreateDefault(),
            NoSqlAuthority: true,
            ReadOnly: true,
            FallbackAvailable: true,
            TraceBudgetChars: DataAgentGraphHandshakeLimits.MaxTraceSummaryChars,
            ProgressBudget: DataAgentGraphHandshakeLimits.MaxProgressEvents);
    }

    static DataAgentGraphHandshakeResponse NewResponse(DataAgentGraphHandshakeRequest request)
    {
        return new DataAgentGraphHandshakeResponse(
            request.RequestId,
            Accepted: true,
            ReasonCode: "dev_sidecar_accepted",
            SelectedNodes: [DataAgentWorkflowNodeNames.ScenarioKnowledge, DataAgentWorkflowNodeNames.QueryPlanner],
            NodeProgress:
            [
                new DataAgentGraphHandshakeProgress(DataAgentWorkflowNodeNames.ScenarioKnowledge, DataAgentGraphHandshakeProgressStatus.Completed, "scenario_context_ready"),
                new DataAgentGraphHandshakeProgress(DataAgentWorkflowNodeNames.QueryPlanner, DataAgentGraphHandshakeProgressStatus.Completed, "planner_suggested")
            ],
            TraceSummary: "ScenarioKnowledge:Completed>QueryPlanner:Completed",
            ContextContribution: "graph_handshake_dev_sidecar=accepted",
            FallbackRequired: false,
            NoSqlAuthority: true,
            ReadOnly: true,
            RequestedToolNames: [DataAgentGraphHandshakeToolNames.ProposeQueryPlan],
            RequestsCheckpointMutation: false,
            RequestsVisibleText: false);
    }

    sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(handler(request));
        }
    }
}
```

Add a shared test helper to the existing `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeCoordinatorTests.cs` or create `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeCoordinatorTestData.cs`:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

internal static class DataAgentGraphHandshakeCoordinatorTestData
{
    public static DataAgentOrchestrationResult AcceptedResult()
    {
        DataAgentAnalysisCheckpoint checkpoint = new(
            CurrentNode: DataAgentWorkflowNodeNames.ReadOnlyExecute,
            TurnCount: 1,
            Terminal: false);
        DataAgentOrchestrationStep[] steps =
        [
            new DataAgentOrchestrationStep(
                DataAgentWorkflowNodeNames.QueryPlanner,
                DataAgentWorkflowNodeNames.ReadOnlyExecute,
                "planner_succeeded",
                ExecutedSql: false),
            new DataAgentOrchestrationStep(
                DataAgentWorkflowNodeNames.ReadOnlyExecute,
                DataAgentWorkflowNodeNames.ResultInterpreter,
                "query_executed",
                ExecutedSql: true)
        ];

        return new DataAgentOrchestrationResult(
            SessionId: "session-1",
            SessionStatus: DataAgentAnalysisSessionStatus.Active,
            Response: new DataAgentAnalysisResponse(true, "ok", "accepted", null),
            Checkpoint: checkpoint,
            Steps: steps,
            RouteContext: new DataAgentToolRouteContext(true, "route_allowed", "tool_scope"));
    }
}
```

If `DataAgentGraphHandshakeCoordinatorTests` already has private `AcceptedResult`, keep it and duplicate only minimal data in the new HTTP client test to avoid broad refactoring. Do not restructure unrelated tests.

- [ ] **Step 2: Add coordinator invalid-response test**

In `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeCoordinatorTests.cs`, add:

```csharp
[Test]
public void EnabledCoordinatorMapsInvalidSidecarResponseExceptionToInvalidFallback()
{
    ThrowingSidecarClient sidecar = new(new DataAgentGraphSidecarInvalidResponseException("invalid_response_schema"));
    DataAgentGraphHandshakeCoordinator coordinator = new(new DataAgentGraphHandshakeOptions(true), sidecar);

    DataAgentGraphHandshakeOutcome outcome = coordinator.TryHandshake(
        "owner",
        "Which gates failed?",
        AcceptedResult());

    Assert.Multiple(() =>
    {
        Assert.That(outcome.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Invalid));
        Assert.That(outcome.ReasonCode, Is.EqualTo("invalid_response_schema"));
        Assert.That(outcome.FallbackRequired, Is.True);
        Assert.That(outcome.Response, Is.Null);
    });
}
```

Update the local `ThrowingSidecarClient` helper if needed so it can accept a specific exception instance:

```csharp
sealed class ThrowingSidecarClient(Exception exception) : IDataAgentGraphSidecarClient
{
    public DataAgentGraphHandshakeResponse TryHandshake(DataAgentGraphHandshakeRequest request)
    {
        throw exception;
    }
}
```

- [ ] **Step 3: Run HTTP client and coordinator tests and verify they fail**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeHttpClientTests|FullyQualifiedName~EnabledCoordinatorMapsInvalidSidecarResponseExceptionToInvalidFallback" -v:minimal
```

Expected: FAIL because the HTTP client and invalid response exception do not exist.

- [ ] **Step 4: Add invalid response exception and HTTP client**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeHttpClient.cs`:

```csharp
using System.Net.Http.Json;
using System.Text.Json;

namespace Alife.Function.DataAgent;

public sealed class DataAgentGraphSidecarInvalidResponseException : Exception
{
    public DataAgentGraphSidecarInvalidResponseException(string message) : base(message)
    {
    }

    public DataAgentGraphSidecarInvalidResponseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class DataAgentGraphHandshakeHttpClient : IDataAgentGraphSidecarClient
{
    static readonly JsonSerializerOptions JsonOptions = new();

    readonly HttpClient httpClient;
    readonly DataAgentGraphHandshakeHttpOptions options;

    public DataAgentGraphHandshakeHttpClient(HttpClient httpClient, DataAgentGraphHandshakeHttpOptions options)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        if (options.Configured == false || options.Endpoint is null)
            throw new ArgumentException("Graph handshake HTTP endpoint is not configured.", nameof(options));
    }

    public DataAgentGraphHandshakeResponse TryHandshake(DataAgentGraphHandshakeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        using CancellationTokenSource cancellation = new(options.Timeout);
        try
        {
            using HttpRequestMessage message = new(HttpMethod.Post, options.Endpoint)
            {
                Content = JsonContent.Create(request, options: JsonOptions)
            };
            using HttpResponseMessage response = httpClient.SendAsync(message, cancellation.Token)
                .GetAwaiter()
                .GetResult();

            if (response.IsSuccessStatusCode == false)
                throw new InvalidOperationException("sidecar_unavailable");

            DataAgentGraphHandshakeResponse? handshakeResponse = response.Content
                .ReadFromJsonAsync<DataAgentGraphHandshakeResponse>(JsonOptions, cancellation.Token)
                .GetAwaiter()
                .GetResult();

            return handshakeResponse ?? throw new DataAgentGraphSidecarInvalidResponseException("invalid_response_schema");
        }
        catch (DataAgentGraphSidecarInvalidResponseException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new DataAgentGraphSidecarInvalidResponseException("invalid_response_schema", exception);
        }
        catch (TaskCanceledException exception)
        {
            throw new TimeoutException("sidecar_timeout", exception);
        }
    }
}
```

The client intentionally uses default `System.Text.Json` property naming so the
wire shape remains compatible with the existing PascalCase C# record property
names and the V3.1 Python dev stub.

- [ ] **Step 5: Map malformed response exception in coordinator**

In `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeCoordinator.cs`, add this catch before `catch (TimeoutException)`:

```csharp
catch (DataAgentGraphSidecarInvalidResponseException)
{
    return Outcome(DataAgentGraphHandshakeStatus.Invalid, "invalid_response_schema", fallbackRequired: true, request);
}
```

- [ ] **Step 6: Run HTTP client and coordinator tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeHttpClientTests|FullyQualifiedName~DataAgentGraphHandshakeCoordinatorTests" -v:minimal
```

Expected: PASS with `Failed: 0`.

- [ ] **Step 7: Commit Task 2**

Run:

```powershell
git add sources\Alife.Function\Alife.Function.DataAgent\DataAgentGraphHandshakeHttpClient.cs sources\Alife.Function\Alife.Function.DataAgent\DataAgentGraphHandshakeCoordinator.cs Tests\Alife.Test.DataAgent\DataAgentGraphHandshakeHttpClientTests.cs Tests\Alife.Test.DataAgent\DataAgentGraphHandshakeCoordinatorTests.cs
git commit -m "Add DataAgent graph handshake HTTP client"
```

---

### Task 3: Wire Dev HTTP Adapter Without Starting Runtime

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentModuleServiceTests.cs`

- [ ] **Step 1: Add failing module wiring tests**

In `Tests/Alife.Test.DataAgent/DataAgentModuleServiceTests.cs`, add:

```csharp
[Test]
public void AwakeWiresGraphHandshakeHttpClientThroughLoopbackOptionsWithoutStartingRuntime()
{
    string source = ReadModuleSource();

    Assert.Multiple(() =>
    {
        Assert.That(source, Does.Contain("DataAgentGraphHandshakeHttpOptions.FromEnvironment"));
        Assert.That(source, Does.Contain("CreateGraphHandshakeSidecarClient"));
        Assert.That(source, Does.Contain("DataAgentGraphHandshakeHttpClient"));
        Assert.That(source, Does.Contain("DisabledDataAgentGraphSidecarClient.Instance"));
        Assert.That(source, Does.Not.Contain("Process.Start"));
        Assert.That(source, Does.Not.Contain("uvicorn"));
        Assert.That(source, Does.Not.Contain("FastAPI"));
    });
}

[Test]
public void GraphHandshakeSidecarFactoryKeepsDefaultDisabledClient()
{
    MethodInfo method = typeof(DataAgentModuleService).GetMethod(
        "CreateGraphHandshakeSidecarClient",
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;

    object client = method.Invoke(null,
        [DataAgentGraphHandshakeOptions.Disabled, DataAgentGraphHandshakeHttpOptions.Disabled])!;

    Assert.That(client, Is.SameAs(DisabledDataAgentGraphSidecarClient.Instance));
}

[Test]
public void GraphHandshakeSidecarFactoryCreatesHttpClientOnlyForEnabledLoopbackEndpoint()
{
    MethodInfo method = typeof(DataAgentModuleService).GetMethod(
        "CreateGraphHandshakeSidecarClient",
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
    DataAgentGraphHandshakeHttpOptions options =
        DataAgentGraphHandshakeHttpOptions.FromValues("http://127.0.0.1:8765/handshake", "800");

    object client = method.Invoke(null, [new DataAgentGraphHandshakeOptions(true), options])!;

    Assert.That(client, Is.TypeOf<DataAgentGraphHandshakeHttpClient>());
}
```

- [ ] **Step 2: Run module tests and verify they fail**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~AwakeWiresGraphHandshakeHttpClientThroughLoopbackOptionsWithoutStartingRuntime|FullyQualifiedName~GraphHandshakeSidecarFactory" -v:minimal
```

Expected: FAIL because module wiring does not yet reference HTTP options/client.

- [ ] **Step 3: Add sidecar factory and module wiring**

In `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`, add:

```csharp
internal static IDataAgentGraphSidecarClient CreateGraphHandshakeSidecarClient(
    DataAgentGraphHandshakeOptions graphOptions,
    DataAgentGraphHandshakeHttpOptions httpOptions)
{
    if (graphOptions.Enabled == false ||
        httpOptions.Configured == false ||
        httpOptions.Endpoint is null)
    {
        return DisabledDataAgentGraphSidecarClient.Instance;
    }

    return new DataAgentGraphHandshakeHttpClient(new HttpClient(), httpOptions);
}
```

Then replace the current graph coordinator wiring:

```csharp
DataAgentGraphHandshakeCoordinator graphHandshakeCoordinator = new(
    DataAgentGraphHandshakeOptions.FromEnvironment(),
    DisabledDataAgentGraphSidecarClient.Instance);
```

with:

```csharp
DataAgentGraphHandshakeOptions graphHandshakeOptions = DataAgentGraphHandshakeOptions.FromEnvironment();
DataAgentGraphHandshakeHttpOptions graphHandshakeHttpOptions = DataAgentGraphHandshakeHttpOptions.FromEnvironment();
DataAgentGraphHandshakeCoordinator graphHandshakeCoordinator = new(
    graphHandshakeOptions,
    CreateGraphHandshakeSidecarClient(graphHandshakeOptions, graphHandshakeHttpOptions));
```

Do not add process startup, Python invocation, or QChat dependencies.

- [ ] **Step 4: Run module tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentModuleServiceTests" -v:minimal
```

Expected: PASS with `Failed: 0`.

- [ ] **Step 5: Commit Task 3**

Run:

```powershell
git add sources\Alife.Function\Alife.Function.DataAgent\DataAgentModuleService.cs Tests\Alife.Test.DataAgent\DataAgentModuleServiceTests.cs
git commit -m "Wire DataAgent graph handshake dev HTTP adapter"
```

---

### Task 4: Add V3.1 Readiness Gates

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
- Modify: `tools/check-dataagent-readiness.ps1`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV210ReadinessTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV216ReadinessTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV30ReadinessTests.cs`
- Modify: `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`
- Modify: `tools/check-qchat-engineering-map.ps1`

- [ ] **Step 1: Add failing V3.1 readiness tests**

In `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`, update `CoreReadinessChecksAllPass` expected count:

```csharp
Assert.That(checks, Has.Count.EqualTo(73));
```

Add assertions near the graph handshake readiness checks:

```csharp
Assert.That(checks.Select(check => check.Name), Does.Contain("GraphHandshakeDevSidecarAdapterPresent"));
DataAgentReadinessCheck graphHandshakeDevSidecarCheck = checks.Single(check => check.Name == "GraphHandshakeDevSidecarAdapterPresent");
Assert.That(graphHandshakeDevSidecarCheck.Detail, Does.Contain("default_enabled=false"));
Assert.That(graphHandshakeDevSidecarCheck.Detail, Does.Contain("dev_http_adapter_present=true"));
Assert.That(graphHandshakeDevSidecarCheck.Detail, Does.Contain("runtime_started=false"));
Assert.That(graphHandshakeDevSidecarCheck.Detail, Does.Contain("endpoint_required=true"));
Assert.That(graphHandshakeDevSidecarCheck.Detail, Does.Contain("loopback_only=true"));
Assert.That(graphHandshakeDevSidecarCheck.Detail, Does.Contain("fallback=true"));
Assert.That(graphHandshakeDevSidecarCheck.Detail, Does.Contain("validator=true"));
Assert.That(graphHandshakeDevSidecarCheck.Detail, Does.Contain("no_sql_authority=true"));
```

Update script summary expectations from:

```text
Summary: 86 required passed, 0 required missing
```

to:

```text
Summary: 87 required passed, 0 required missing
```

Add a static script marker test:

```csharp
[Test]
public void StaticReadinessScriptContainsV31DevSidecarAdapterMarkers()
{
    string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
    string script = File.ReadAllText(Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1"));
    string declaration = FindNewCheckDeclaration(script, "GraphHandshakeDevSidecarAdapterPresent");

    Assert.Multiple(() =>
    {
        Assert.That(declaration, Does.Contain("DataAgentGraphHandshakeHttpOptions"));
        Assert.That(declaration, Does.Contain("DataAgentGraphHandshakeHttpClient"));
        Assert.That(declaration, Does.Contain("ALIFE_DATAAGENT_GRAPH_HANDSHAKE_ENDPOINT"));
        Assert.That(declaration, Does.Contain("ALIFE_DATAAGENT_GRAPH_HANDSHAKE_TIMEOUT_MS"));
        Assert.That(declaration, Does.Contain("loopback"));
        Assert.That(declaration, Does.Contain("runtime_started=false"));
    });
}
```

If `FindNewCheckDeclaration` is not available in this class scope, reuse the existing helper pattern from nearby static readiness tests.

In `Tests/Alife.Test.DataAgent/DataAgentV210ReadinessTests.cs`, `DataAgentV216ReadinessTests.cs`, and `DataAgentV30ReadinessTests.cs`, update expected static count references:

```csharp
Assert.That(dataAgentScript, Does.Contain("$expectedRequired = 87"));
```

- [ ] **Step 2: Run readiness tests and verify they fail**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests|FullyQualifiedName~DataAgentV210ReadinessTests|FullyQualifiedName~DataAgentV216ReadinessTests|FullyQualifiedName~DataAgentV30ReadinessTests" -v:minimal
```

Expected: FAIL because V3.1 readiness is not yet wired and `$expectedRequired` is still `86`.

- [ ] **Step 3: Add dynamic V3.1 readiness**

In `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`, after `GraphHandshakeBoundaryPresent`, add:

```csharp
DataAgentGraphHandshakeHttpOptions graphHandshakeDefaultHttpOptions =
    DataAgentGraphHandshakeHttpOptions.FromValues(null, null);
DataAgentGraphHandshakeHttpOptions graphHandshakeLoopbackHttpOptions =
    DataAgentGraphHandshakeHttpOptions.FromValues("http://127.0.0.1:8765/handshake", "800");
DataAgentGraphHandshakeHttpOptions graphHandshakeRemoteHttpOptions =
    DataAgentGraphHandshakeHttpOptions.FromValues("http://example.com/handshake", "800");
bool graphHandshakeDevHttpAdapterPresent =
    typeof(DataAgentGraphHandshakeHttpClient).GetInterfaces().Contains(typeof(IDataAgentGraphSidecarClient));
bool graphHandshakeEndpointRequired =
    graphHandshakeDefaultHttpOptions.Configured == false &&
    graphHandshakeLoopbackHttpOptions.Configured &&
    graphHandshakeRemoteHttpOptions.Configured == false;
bool graphHandshakeNoRuntimeStarted =
    graphHandshakeDefaultHttpOptions.RuntimeStarted == false &&
    graphHandshakeLoopbackHttpOptions.RuntimeStarted == false;
bool graphHandshakeLoopbackOnly =
    graphHandshakeLoopbackHttpOptions.Endpoint?.IsLoopback == true &&
    graphHandshakeRemoteHttpOptions.Endpoint is null;
bool graphHandshakeDevFallback =
    graphHandshakeDisabledOutcome.FallbackRequired &&
    string.Equals(graphHandshakeDisabledOutcome.ReasonCode, "sidecar_disabled", StringComparison.Ordinal);
bool graphHandshakeDevReady =
    graphHandshakeDefaultOptions.Enabled == false &&
    graphHandshakeDevHttpAdapterPresent &&
    graphHandshakeNoRuntimeStarted &&
    graphHandshakeEndpointRequired &&
    graphHandshakeLoopbackOnly &&
    graphHandshakeDevFallback &&
    graphHandshakeSafeValidation.Accepted &&
    graphHandshakeNoSqlAuthority;
checks.Add(graphHandshakeDevReady
    ? Pass("GraphHandshakeDevSidecarAdapterPresent", "default_enabled=false;dev_http_adapter_present=true;runtime_started=false;endpoint_required=true;loopback_only=true;fallback=true;validator=true;no_sql_authority=true")
    : Fail("GraphHandshakeDevSidecarAdapterPresent", $"default_enabled={LowerBool(graphHandshakeDefaultOptions.Enabled)};dev_http_adapter_present={LowerBool(graphHandshakeDevHttpAdapterPresent)};runtime_started={LowerBool(graphHandshakeNoRuntimeStarted == false)};endpoint_required={LowerBool(graphHandshakeEndpointRequired)};loopback_only={LowerBool(graphHandshakeLoopbackOnly)};fallback={LowerBool(graphHandshakeDevFallback)};validator={LowerBool(graphHandshakeSafeValidation.Accepted)};no_sql_authority={LowerBool(graphHandshakeNoSqlAuthority)}"));
```

If this creates a compile error due to variable scope, move the existing V3.0 graph-handshake readiness local variables into a nearby block so V3.1 can reuse only safe booleans, or recompute minimal equivalents in the V3.1 block. Keep detail boolean-only.

- [ ] **Step 4: Add static readiness script marker**

In `tools/check-dataagent-readiness.ps1`, after `GraphHandshakeBoundaryPresent`, add:

```powershell
    New-Check -Group "Store" -Name "GraphHandshakeDevSidecarAdapterPresent" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeHttpOptions.cs" @("DataAgentGraphHandshakeHttpOptions", "ALIFE_DATAAGENT_GRAPH_HANDSHAKE_ENDPOINT", "ALIFE_DATAAGENT_GRAPH_HANDSHAKE_TIMEOUT_MS", "loopback", "RuntimeStarted")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeHttpClient.cs" @("DataAgentGraphHandshakeHttpClient", "IDataAgentGraphSidecarClient", "HttpClient", "invalid_response_schema", "sidecar_timeout")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs" @("CreateGraphHandshakeSidecarClient", "DataAgentGraphHandshakeHttpOptions.FromEnvironment", "DisabledDataAgentGraphSidecarClient.Instance")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("GraphHandshakeDevSidecarAdapterPresent", "default_enabled=false", "dev_http_adapter_present=true", "runtime_started=false", "endpoint_required=true", "loopback_only=true", "fallback=true", "validator=true", "no_sql_authority=true"))) -Detail "V3.1 graph handshake dev sidecar adapter markers default_enabled=false dev_http_adapter_present=true runtime_started=false endpoint_required=true loopback_only=true fallback=true validator=true no_sql_authority=true"
```

Change:

```powershell
$expectedRequired = 86
```

to:

```powershell
$expectedRequired = 87
```

- [ ] **Step 5: Keep QChat boundary count unchanged**

In `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`, ensure the forbidden markers include both graph families:

```csharp
"DataAgentDataQueryGraph",
"DataAgentGraphHandshake",
"DataAgentGraphSidecar"
```

In `tools/check-qchat-engineering-map.ps1`, keep:

```powershell
$expectedRequired = 63
```

Ensure the DataAgent diagnostics command contract omit patterns include:

```powershell
-OmitPatterns @("DataAgentDataQueryGraph", "DataAgentGraphHandshake", "DataAgentGraphSidecar")
```

If `DataAgentGraphSidecar` is already covered elsewhere, do not add a new required check or increment count.

- [ ] **Step 6: Run readiness and QChat boundary tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests|FullyQualifiedName~DataAgentV210ReadinessTests|FullyQualifiedName~DataAgentV216ReadinessTests|FullyQualifiedName~DataAgentV30ReadinessTests" -v:minimal
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatDoesNotDirectlyImportDataAgentBoundaryTypes|FullyQualifiedName~DataAgentDiagnosticsCommandContractCheckRequiresSharedParserAndQChatBoundary|FullyQualifiedName~QChatEngineeringMapScriptProtectsRequiredCheckCount" -v:minimal
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected:

- DataAgent readiness tests PASS with `Failed: 0`.
- QChat targeted tests PASS with `Failed: 0`.
- DataAgent readiness script includes `GraphHandshakeDevSidecarAdapterPresent` and `Summary: 87 required passed, 0 required missing`.
- QChat engineering map remains `Summary: 63 required passed, 0 required missing, 0 optional present, 0 optional missing`.

- [ ] **Step 7: Commit Task 4**

Run:

```powershell
git add sources\Alife.Function\Alife.Function.DataAgent\DataAgentReadiness.cs tools\check-dataagent-readiness.ps1 tools\check-qchat-engineering-map.ps1 Tests\Alife.Test.DataAgent\DataAgentReadinessTests.cs Tests\Alife.Test.DataAgent\DataAgentV210ReadinessTests.cs Tests\Alife.Test.DataAgent\DataAgentV216ReadinessTests.cs Tests\Alife.Test.DataAgent\DataAgentV30ReadinessTests.cs Tests\Alife.Test.QChat\QChatEngineeringMapRequiredV2Tests.cs
git commit -m "Add DataAgent V3.1 dev sidecar readiness"
```

---

### Task 5: Add Optional Python Dev Sidecar Stub And Docs

**Files:**
- Create: `tools/dataagent-graph-sidecar/app.py`
- Create: `tools/dataagent-graph-sidecar/requirements.txt`
- Create: `tools/dataagent-graph-sidecar/README.md`
- Create: `docs/dataagent/dataagent-v3.1-dev-sidecar-adapter.md`
- Create: `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeDevSidecarStubTests.cs`

- [ ] **Step 1: Write failing static stub tests**

Create `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeDevSidecarStubTests.cs`:

```csharp
namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentGraphHandshakeDevSidecarStubTests
{
    [Test]
    public void PythonDevStubExposesOnlyHandshakeAndHealthEndpoints()
    {
        string root = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string app = File.ReadAllText(Path.Combine(root, "tools", "dataagent-graph-sidecar", "app.py"));

        Assert.Multiple(() =>
        {
            Assert.That(app, Does.Contain("@app.post(\"/handshake\")"));
            Assert.That(app, Does.Contain("@app.get(\"/health\")"));
            Assert.That(app, Does.Contain("NoSqlAuthority"));
            Assert.That(app, Does.Contain("ReadOnly"));
            Assert.That(app, Does.Contain("RequestsCheckpointMutation"));
            Assert.That(app, Does.Contain("RequestsVisibleText"));
            Assert.That(app, Does.Not.Contain("sqlite"));
            Assert.That(app, Does.Not.Contain("postgres"));
            Assert.That(app, Does.Not.Contain("qchat"));
            Assert.That(app, Does.Not.Contain("qq"));
            Assert.That(app, Does.Not.Contain("browser"));
            Assert.That(app, Does.Not.Contain("checkpoint.write"));
            Assert.That(app, Does.Not.Contain("subprocess"));
            Assert.That(app, Does.Not.Contain("open("));
        });
    }

    [Test]
    public void PythonDevStubReadmeDeclaresManualLocalOnlyNonProductionBoundary()
    {
        string root = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string readme = File.ReadAllText(Path.Combine(root, "tools", "dataagent-graph-sidecar", "README.md"));

        Assert.Multiple(() =>
        {
            Assert.That(readme, Does.Contain("optional"));
            Assert.That(readme, Does.Contain("local-only"));
            Assert.That(readme, Does.Contain("manual"));
            Assert.That(readme, Does.Contain("not a production runtime"));
            Assert.That(readme, Does.Contain("ALIFE_DATAAGENT_GRAPH_HANDSHAKE_ENDPOINT"));
            Assert.That(readme, Does.Contain("ALIFE_DATAAGENT_GRAPH_HANDSHAKE_ENABLED"));
        });
    }

    [Test]
    public void DeveloperNoteDocumentsV31BoundaryAndV32Handoff()
    {
        string root = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string doc = File.ReadAllText(Path.Combine(root, "docs", "dataagent", "dataagent-v3.1-dev-sidecar-adapter.md"));

        Assert.Multiple(() =>
        {
            Assert.That(doc, Does.Contain("dev HTTP adapter"));
            Assert.That(doc, Does.Contain("not a production sidecar runtime"));
            Assert.That(doc, Does.Contain("default tests do not require Python"));
            Assert.That(doc, Does.Contain("C# keeps SQL"));
            Assert.That(doc, Does.Contain("Tool Broker"));
            Assert.That(doc, Does.Contain("QChat"));
            Assert.That(doc, Does.Contain("V3.2"));
            Assert.That(doc, Does.Contain("streaming progress"));
        });
    }

    static string FindRepoRoot(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tools")) &&
                Directory.Exists(Path.Combine(directory.FullName, "docs")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
```

- [ ] **Step 2: Run stub tests and verify they fail**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeDevSidecarStubTests" -v:minimal
```

Expected: FAIL because the stub and doc do not exist.

- [ ] **Step 3: Add FastAPI dev stub**

Create `tools/dataagent-graph-sidecar/app.py`:

```python
from __future__ import annotations

from typing import Any

from fastapi import FastAPI
from pydantic import BaseModel, Field


app = FastAPI(title="DataAgent Graph Dev Sidecar", version="0.1.0")


class GraphHandshakeProgress(BaseModel):
    NodeName: str
    Status: str
    ReasonCode: str


class GraphHandshakeRequest(BaseModel):
    RequestId: str
    SessionId: str
    TurnId: str
    CallerId: str
    GoalOrQuestion: str
    ScenarioContextSummary: str
    RouteScope: str
    QueryConstraints: str
    NodeManifests: list[dict[str, Any]] = Field(default_factory=list)
    NoSqlAuthority: bool
    ReadOnly: bool
    FallbackAvailable: bool
    TraceBudgetChars: int
    ProgressBudget: int


class GraphHandshakeResponse(BaseModel):
    RequestId: str
    Accepted: bool
    ReasonCode: str
    SelectedNodes: list[str]
    NodeProgress: list[GraphHandshakeProgress]
    TraceSummary: str
    ContextContribution: str
    FallbackRequired: bool
    NoSqlAuthority: bool
    ReadOnly: bool
    RequestedToolNames: list[str]
    RequestsCheckpointMutation: bool
    RequestsVisibleText: bool


@app.get("/health")
def health() -> dict[str, str]:
    return {"status": "ok", "runtime": "dev_sidecar"}


@app.post("/handshake", response_model=GraphHandshakeResponse)
def handshake(request: GraphHandshakeRequest) -> GraphHandshakeResponse:
    selected_nodes = ["scenario_context", "query_planner", "diagnostics_router"]
    return GraphHandshakeResponse(
        RequestId=request.RequestId,
        Accepted=True,
        ReasonCode="dev_sidecar_accepted",
        SelectedNodes=selected_nodes,
        NodeProgress=[
            GraphHandshakeProgress(
                NodeName="scenario_context",
                Status="Completed",
                ReasonCode="scenario_context_ready",
            ),
            GraphHandshakeProgress(
                NodeName="query_planner",
                Status="Completed",
                ReasonCode="planner_suggested",
            ),
            GraphHandshakeProgress(
                NodeName="diagnostics_router",
                Status="Completed",
                ReasonCode="diagnostics_ready",
            ),
        ],
        TraceSummary="ScenarioKnowledge:Completed>QueryPlanner:Completed>DiagnosticsRouter:Completed",
        ContextContribution="graph_handshake_dev_sidecar=accepted",
        FallbackRequired=False,
        NoSqlAuthority=True,
        ReadOnly=True,
        RequestedToolNames=["dataagent.query_plan.propose", "dataagent.diagnostics.progress.read"],
        RequestsCheckpointMutation=False,
        RequestsVisibleText=False,
    )
```

If the C# JSON serializer emits camelCase instead of PascalCase, add pydantic aliases in a follow-up test-driven patch. Do not make the Python stub permissive enough to accept or emit unsafe fields.

- [ ] **Step 4: Add optional Python requirements**

Create `tools/dataagent-graph-sidecar/requirements.txt`:

```text
fastapi==0.116.1
uvicorn==0.35.0
pydantic==2.11.7
```

- [ ] **Step 5: Add sidecar README**

Create `tools/dataagent-graph-sidecar/README.md`:

```markdown
# DataAgent Graph Dev Sidecar

This is an optional local-only development stub for DataAgent V3.1.

It is not a production runtime, not a LangGraph runtime, and not started by
the C# application. It exists so developers can manually exercise the V3.1
HTTP sidecar adapter while C# remains the authority boundary.

## Run Manually

```powershell
cd tools\dataagent-graph-sidecar
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
.\.venv\Scripts\python.exe -m uvicorn app:app --host 127.0.0.1 --port 8765
```

Then configure DataAgent separately:

```powershell
$env:ALIFE_DATAAGENT_GRAPH_HANDSHAKE_ENABLED = "true"
$env:ALIFE_DATAAGENT_GRAPH_HANDSHAKE_ENDPOINT = "http://127.0.0.1:8765/handshake"
$env:ALIFE_DATAAGENT_GRAPH_HANDSHAKE_TIMEOUT_MS = "800"
```

## Boundary

The stub returns graph-handshake suggestions only. It does not read project
files, connect to databases, execute SQL, call Tool Broker tools, mutate
checkpoints, send QChat text, own QQ ingress, control browser state, control
desktop pet state, or manage external RAG sources.

All sidecar output remains untrusted and must pass the C#
`DataAgentGraphHandshakeValidator`.
```

- [ ] **Step 6: Add V3.1 developer note**

Create `docs/dataagent/dataagent-v3.1-dev-sidecar-adapter.md`:

```markdown
# DataAgent V3.1 Dev Sidecar Adapter

V3.1 adds a dev HTTP adapter behind the existing V3.0 graph handshake boundary.

It does not add a production sidecar runtime, automatic Python process
management, live LangGraph runtime behavior, SQL execution, Tool Broker
authority, checkpoint ownership, QChat graph ownership, or QQ ingress.

## What It Adds

- Loopback-only HTTP endpoint options.
- A short-timeout C# HTTP client implementing `IDataAgentGraphSidecarClient`.
- Safe fallback for missing endpoint, unavailable endpoint, timeout, malformed
  JSON, and invalid sidecar responses.
- A manually run optional Python/FastAPI stub under `tools/`.
- Readiness markers proving the adapter exists while runtime startup remains
  disabled by default.

## Authority Boundary

C# keeps SQL, QueryPlan validation, SQL Safety Validator, read-only execution,
Tool Broker route state, checkpoint persistence, evidence, trace, progress,
diagnostics, QChat, and QQ authority.

The sidecar can return a bounded graph-handshake suggestion. It cannot
authorize datasets, fields, operators, limits, tools, checkpoint mutation,
visible text, SQL, SQL execution, or plugin actions.

## Testing Boundary

Default tests do not require Python, FastAPI, uvicorn, network access,
PostgreSQL, live QChat, live model calls, browser automation, or QQ.

C# tests use fake HTTP handlers. The Python stub is checked statically and can
be run manually for local demos.

## V3.2 Handoff

V3.2 can add streaming progress after V3.1 proves the sidecar request/response
transport is optional, local-only, bounded, validated, and safe to ignore.
```

- [ ] **Step 7: Run static stub/doc tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeDevSidecarStubTests" -v:minimal
```

Expected: PASS with `Failed: 0`.

- [ ] **Step 8: Commit Task 5**

Run:

```powershell
git add tools\dataagent-graph-sidecar\app.py tools\dataagent-graph-sidecar\requirements.txt tools\dataagent-graph-sidecar\README.md docs\dataagent\dataagent-v3.1-dev-sidecar-adapter.md Tests\Alife.Test.DataAgent\DataAgentGraphHandshakeDevSidecarStubTests.cs
git commit -m "Add DataAgent graph dev sidecar stub"
```

If `docs\dataagent\dataagent-v3.1-dev-sidecar-adapter.md` is ignored, use `git add -f` for that file only.

---

### Task 6: Final Verification

**Files:**
- Verify all files changed in Tasks 1-5.

- [ ] **Step 1: Check status and recent commits**

Run:

```powershell
git status --short --branch
git log --oneline -12
```

Expected:

- Working tree is clean after all task commits.
- Recent commits include V3.1 options, HTTP client, module wiring, readiness, stub, and docs.

- [ ] **Step 2: Run focused DataAgent V3.1 tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeHttpOptionsTests|FullyQualifiedName~DataAgentGraphHandshakeHttpClientTests|FullyQualifiedName~DataAgentGraphHandshakeCoordinatorTests|FullyQualifiedName~DataAgentGraphHandshakeDevSidecarStubTests|FullyQualifiedName~DataAgentReadinessTests|FullyQualifiedName~DataAgentV30ReadinessTests" -v:minimal
```

Expected: PASS with `Failed: 0`.

- [ ] **Step 3: Run DataAgent readiness script**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected output includes:

```text
GraphHandshakeDevSidecarAdapterPresent
Summary: 87 required passed, 0 required missing
```

- [ ] **Step 4: Run QChat graph model boundary scan**

Run:

```powershell
rg -n "DataAgentDataQueryGraph|DataAgentGraphHandshake|DataAgentGraphSidecar" sources\Alife.Function\Alife.Function.QChat
```

Expected: no matches. `rg` exit code `1` is acceptable for no matches.

- [ ] **Step 5: Run QChat engineering map**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected output includes:

```text
Summary: 63 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 6: Confirm default tests do not require Python**

Run:

```powershell
rg -n "uvicorn|fastapi|tools\\dataagent-graph-sidecar|ALIFE_DATAAGENT_GRAPH_HANDSHAKE_ENDPOINT" Tests\Alife.Test.DataAgent Tests\Alife.Test.QChat
```

Expected:

- Matches are allowed only in static stub/readiness tests and documentation/string marker tests.
- There must be no test command that starts `uvicorn`, `python`, or a live server.

- [ ] **Step 7: Run restore**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" restore Alife.slnx -v:minimal
```

Expected: restore completes with exit code `0`.

- [ ] **Step 8: Run build**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" build Alife.slnx --no-restore -v:minimal -m:1
```

Expected: build completes with exit code `0` and `0` errors. Existing `CS0067` warnings in QChat test fakes may remain.

- [ ] **Step 9: Run full solution tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore --no-build -v:minimal -m:1
```

Expected: PASS with `Failed: 0`.

- [ ] **Step 10: Record final status**

Run:

```powershell
git status --short --branch
git log --oneline -10
```

Expected:

- Working tree is clean.
- Recent commits include all V3.1 dev sidecar adapter commits.

---

## Self-Review

Spec coverage:

- Dev HTTP adapter behind V3.0 C# boundary: Tasks 1-3.
- Optional manually run Python/FastAPI stub: Task 5.
- Default tests avoid Python/network: Tasks 2, 5, and 6.
- Loopback-only endpoint policy: Tasks 1 and 4.
- Timeout and unavailable fallback: Task 2.
- Malformed JSON invalid fallback: Task 2.
- Existing validator remains authority: Tasks 2 and 4.
- No QChat graph ownership/imports: Tasks 4 and 6.
- V3.1 developer note: Task 5.
- Full verification: Task 6.

Completeness scan:

- The plan contains no unresolved implementation markers.
- The plan contains no empty future-work steps.
- Every created file has an exact path.
- Every test command includes an expected result.
- Every task ends with a commit.

Type consistency:

- Options type: `DataAgentGraphHandshakeHttpOptions`.
- HTTP client type: `DataAgentGraphHandshakeHttpClient`.
- Invalid response exception: `DataAgentGraphSidecarInvalidResponseException`.
- Existing sidecar client interface: `IDataAgentGraphSidecarClient`.
- Existing coordinator type: `DataAgentGraphHandshakeCoordinator`.
- Existing validator type: `DataAgentGraphHandshakeValidator`.
- Readiness check name: `GraphHandshakeDevSidecarAdapterPresent`.

Risk notes:

- The plan keeps `IDataAgentGraphSidecarClient` synchronous to fit V3.0 without broad refactoring. A future V3.x task may introduce async sidecar calls if needed.
- The Python stub is intentionally static-test-only by default. Live smoke tests are deferred to avoid making Python a default test dependency.
- `DataAgentModuleService` creates `HttpClient` directly for the dev adapter. If later production use appears, introduce an injectable factory then, not in V3.1.
