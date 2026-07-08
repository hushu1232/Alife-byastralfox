# DataAgent V3.3 NDJSON Streaming Transport Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the default-disabled DataAgent V3.3 `/handshake-stream` NDJSON transport smoke so buffered sidecar progress is published only after the final graph handshake response is accepted by the existing C# validator.

**Architecture:** Add a small stream event envelope, stream options, and NDJSON HTTP client beside the existing V3.1 request/response adapter. `DataAgentGraphHandshakeCoordinator` chooses the stream path only when a stream client is explicitly wired, validates the final response first, and only then publishes buffered progress through the V3.2 `DataAgentGraphSidecarProgressBridge`; all invalid, rejected, timed out, unavailable, incomplete, or over-budget streams discard buffered progress.

**Tech Stack:** .NET 9, C# records/classes, `HttpClient`, `System.Text.Json`, NUnit, PowerShell readiness scripts, existing DataAgent graph handshake validator and progress bridge, optional FastAPI dev stub.

---

## CodeGraph Context Used

The local CodeGraph index for `D:\Alife` is healthy and up to date. These structural lookups were used before writing this plan:

```powershell
codegraph status D:\Alife
codegraph context "DataAgent V3.3 NDJSON stream plan DataAgentGraphHandshakeCoordinator HttpClient Options ModuleService tests readiness"
codegraph query DataAgentGraphHandshakeCoordinator
```

Important current entry points:

- `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeCoordinator.cs`
  - owns `IDataAgentGraphSidecarClient`, disabled fallback, final response validation, and V3.2 progress publishing.
- `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeHttpClient.cs`
  - existing `/handshake` request/response HTTP adapter and JSON options pattern.
- `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeHttpOptions.cs`
  - existing loopback-only endpoint and timeout parsing pattern.
- `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`
  - creates the sidecar client and coordinator during module wiring.
- `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
  - dynamic readiness checks; current core check count is `74`.
- `tools/check-dataagent-readiness.ps1`
  - static readiness checks; current required count is `88`.
- `tools/check-qchat-engineering-map.ps1`
  - QChat boundary map; required count must remain `63`.

## File Map

- Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeStreamModels.cs`
  - Defines `DataAgentGraphHandshakeStreamEventKind`, `DataAgentGraphHandshakeStreamEvent`, `DataAgentGraphHandshakeStreamResult`, `IDataAgentGraphHandshakeStreamClient`, and `DataAgentGraphSidecarInvalidStreamException`.
- Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeStreamOptions.cs`
  - Parses `ALIFE_DATAAGENT_GRAPH_HANDSHAKE_STREAM_ENABLED`, `ALIFE_DATAAGENT_GRAPH_HANDSHAKE_STREAM_ENDPOINT`, and `ALIFE_DATAAGENT_GRAPH_HANDSHAKE_STREAM_TIMEOUT_MS`.
  - Keeps stream transport disabled and unconfigured by default, with `RuntimeStarted=false`.
- Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeNdjsonStreamClient.cs`
  - Posts `DataAgentGraphHandshakeRequest` to `/handshake-stream`, reads bounded NDJSON events, validates event envelope schema, buffers progress, and returns a non-null final response result.
- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeCoordinator.cs`
  - Adds optional stream client dependency.
  - Uses stream client only when provided.
  - Validates final response before publishing buffered progress.
  - Maps stream failures to the confirmed reason codes.
- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`
  - Adds `CreateGraphHandshakeStreamClient`.
  - Wires stream options from environment and only passes a stream client when graph handshake and stream transport are both enabled and configured.
- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
  - Adds `GraphHandshakeDevSidecarStreamingTransportPresent` dynamic readiness marker.
- Create `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeStreamOptionsTests.cs`
  - Tests defaults, boolean enable parsing, loopback endpoint acceptance, remote endpoint rejection, and timeout bounds.
- Create `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeNdjsonStreamClientTests.cs`
  - Tests accepted NDJSON stream parsing and all stream schema/failure mappings.
- Create `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeStreamCoordinatorTests.cs`
  - Tests coordinator buffering, rejection, fallback, and existing `/handshake` path preservation.
- Modify `Tests/Alife.Test.DataAgent/DataAgentModuleServiceTests.cs`
  - Adds static wiring tests for stream options/client construction without starting Python.
- Modify `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
  - Updates dynamic count to `75`, static script summary to `89`, script count guard to `$expectedRequired = 89`, and adds a V3.3 static marker test.
- Modify `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeDevSidecarStubTests.cs`
  - Adds static assertions for `/handshake-stream`, NDJSON envelope shape, no SSE, no runtime dependency, and no reserved sidecar facts.
- Inspect `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`
  - Confirm existing QChat direct-import omit markers cover `DataAgentGraphHandshakeStream`; keep QChat required count unchanged.
- Modify `tools/check-dataagent-readiness.ps1`
  - Adds static V3.3 readiness check and increments required count from `88` to `89`.
- Inspect `tools/check-qchat-engineering-map.ps1`
  - Preferred change is to extend existing omit patterns with `DataAgentGraphHandshakeStream` while preserving `$expectedRequired = 63`.
- Modify `tools/dataagent-graph-sidecar/app.py`
  - Adds dev-only `/handshake-stream` endpoint returning `application/x-ndjson`.
- Modify `tools/dataagent-graph-sidecar/README.md`
  - Documents stream env vars, manual run, buffered progress, final response validation, and SSE deferral.
- Create `docs/dataagent/dataagent-v3.3-ndjson-streaming-transport.md`
  - Developer note for V3.3 scope, transport, failure semantics, tests, and future SSE handoff.

## Task 1: Stream Options And Models

**Files:**
- Create: `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeStreamOptionsTests.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeStreamModels.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeStreamOptions.cs`

- [ ] **Step 1: Add failing stream options tests**

Create `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeStreamOptionsTests.cs`:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentGraphHandshakeStreamOptionsTests
{
    const string ValidLoopbackEndpoint = "http://127.0.0.1:8765/handshake-stream";

    [Test]
    public void DefaultsAreDisabledUnconfiguredAndDoNotStartRuntime()
    {
        DataAgentGraphHandshakeStreamOptions options = DataAgentGraphHandshakeStreamOptions.FromValues(null, null, null);

        Assert.Multiple(() =>
        {
            Assert.That(options.Enabled, Is.False);
            Assert.That(options.Configured, Is.False);
            Assert.That(options.Endpoint, Is.Null);
            Assert.That(options.Timeout, Is.EqualTo(TimeSpan.FromMilliseconds(800)));
            Assert.That(options.RuntimeStarted, Is.False);
            Assert.That(DataAgentGraphHandshakeStreamOptions.EnabledEnvironmentVariable, Is.EqualTo("ALIFE_DATAAGENT_GRAPH_HANDSHAKE_STREAM_ENABLED"));
            Assert.That(DataAgentGraphHandshakeStreamOptions.EndpointEnvironmentVariable, Is.EqualTo("ALIFE_DATAAGENT_GRAPH_HANDSHAKE_STREAM_ENDPOINT"));
            Assert.That(DataAgentGraphHandshakeStreamOptions.TimeoutEnvironmentVariable, Is.EqualTo("ALIFE_DATAAGENT_GRAPH_HANDSHAKE_STREAM_TIMEOUT_MS"));
        });
    }

    [TestCase("true")]
    [TestCase("1")]
    [TestCase("yes")]
    public void EnabledLoopbackEndpointIsConfigured(string enabled)
    {
        DataAgentGraphHandshakeStreamOptions options = DataAgentGraphHandshakeStreamOptions.FromValues(
            enabled,
            ValidLoopbackEndpoint,
            "1200");

        Assert.Multiple(() =>
        {
            Assert.That(options.Enabled, Is.True);
            Assert.That(options.Configured, Is.True);
            Assert.That(options.Endpoint, Is.EqualTo(new Uri(ValidLoopbackEndpoint)));
            Assert.That(options.Timeout, Is.EqualTo(TimeSpan.FromMilliseconds(1200)));
            Assert.That(options.RuntimeStarted, Is.False);
        });
    }

    [TestCase("false")]
    [TestCase("0")]
    [TestCase("no")]
    [TestCase("maybe")]
    public void DisabledOrUnknownEnabledValueIsNotConfigured(string enabled)
    {
        DataAgentGraphHandshakeStreamOptions options = DataAgentGraphHandshakeStreamOptions.FromValues(
            enabled,
            ValidLoopbackEndpoint,
            "1200");

        Assert.Multiple(() =>
        {
            Assert.That(options.Enabled, Is.False);
            Assert.That(options.Configured, Is.False);
            Assert.That(options.Endpoint, Is.Null);
            Assert.That(options.Timeout, Is.EqualTo(TimeSpan.FromMilliseconds(1200)));
            Assert.That(options.RuntimeStarted, Is.False);
        });
    }

    [TestCase("http://localhost:8765/handshake-stream")]
    [TestCase("https://127.0.0.1:8765/handshake-stream")]
    public void LoopbackStreamEndpointsAreAccepted(string endpoint)
    {
        DataAgentGraphHandshakeStreamOptions options = DataAgentGraphHandshakeStreamOptions.FromValues("true", endpoint, "800");

        Assert.Multiple(() =>
        {
            Assert.That(options.Enabled, Is.True);
            Assert.That(options.Configured, Is.True);
            Assert.That(options.Endpoint, Is.EqualTo(new Uri(endpoint)));
        });
    }

    [TestCase("http://example.com/handshake-stream")]
    [TestCase("http://0.0.0.0:8765/handshake-stream")]
    [TestCase("file:///tmp/handshake-stream")]
    [TestCase("")]
    public void NonLoopbackOrBlankEndpointIsNotConfigured(string endpoint)
    {
        DataAgentGraphHandshakeStreamOptions options = DataAgentGraphHandshakeStreamOptions.FromValues("true", endpoint, "800");

        Assert.Multiple(() =>
        {
            Assert.That(options.Enabled, Is.True);
            Assert.That(options.Configured, Is.False);
            Assert.That(options.Endpoint, Is.Null);
            Assert.That(options.RuntimeStarted, Is.False);
        });
    }

    [TestCase(null, 800)]
    [TestCase("", 800)]
    [TestCase("99", 800)]
    [TestCase("100", 100)]
    [TestCase("5000", 5000)]
    [TestCase("5001", 800)]
    public void TimeoutParsingUsesSafeBounds(string? timeoutMs, int expectedMs)
    {
        DataAgentGraphHandshakeStreamOptions options = DataAgentGraphHandshakeStreamOptions.FromValues(
            "true",
            ValidLoopbackEndpoint,
            timeoutMs);

        Assert.That(options.Timeout, Is.EqualTo(TimeSpan.FromMilliseconds(expectedMs)));
    }
}
```

- [ ] **Step 2: Run the failing options tests**

Run from `D:\Alife`:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeStreamOptionsTests" -v:minimal
```

Expected: FAIL with missing type errors for `DataAgentGraphHandshakeStreamOptions`.

- [ ] **Step 3: Add stream models**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeStreamModels.cs`:

```csharp
namespace Alife.Function.DataAgent;

public enum DataAgentGraphHandshakeStreamEventKind
{
    Progress,
    FinalResponse
}

public sealed record DataAgentGraphHandshakeStreamEvent(
    DataAgentGraphHandshakeStreamEventKind Kind,
    DataAgentGraphHandshakeProgress? Progress = null,
    DataAgentGraphHandshakeResponse? Response = null);

public sealed record DataAgentGraphHandshakeStreamResult(
    DataAgentGraphHandshakeResponse Response,
    IReadOnlyList<DataAgentGraphHandshakeProgress> Progress);

public interface IDataAgentGraphHandshakeStreamClient
{
    DataAgentGraphHandshakeStreamResult TryHandshakeStream(DataAgentGraphHandshakeRequest request);
}

public sealed class DataAgentGraphSidecarInvalidStreamException : Exception
{
    public string ReasonCode { get; }

    public DataAgentGraphSidecarInvalidStreamException(string reasonCode)
        : base(reasonCode)
    {
        ReasonCode = reasonCode;
    }

    public DataAgentGraphSidecarInvalidStreamException(string reasonCode, Exception innerException)
        : base(reasonCode, innerException)
    {
        ReasonCode = reasonCode;
    }
}
```

- [ ] **Step 4: Add stream options implementation**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeStreamOptions.cs`:

```csharp
using System.Globalization;

namespace Alife.Function.DataAgent;

public sealed record DataAgentGraphHandshakeStreamOptions(
    Uri? Endpoint,
    TimeSpan Timeout,
    bool Configured,
    bool RuntimeStarted,
    bool Enabled)
{
    public const string EnabledEnvironmentVariable = "ALIFE_DATAAGENT_GRAPH_HANDSHAKE_STREAM_ENABLED";
    public const string EndpointEnvironmentVariable = "ALIFE_DATAAGENT_GRAPH_HANDSHAKE_STREAM_ENDPOINT";
    public const string TimeoutEnvironmentVariable = "ALIFE_DATAAGENT_GRAPH_HANDSHAKE_STREAM_TIMEOUT_MS";
    public const int DefaultTimeoutMs = 800;
    public const int MinTimeoutMs = 100;
    public const int MaxTimeoutMs = 5000;

    public static DataAgentGraphHandshakeStreamOptions Disabled { get; } = new(
        Endpoint: null,
        Timeout: TimeSpan.FromMilliseconds(DefaultTimeoutMs),
        Configured: false,
        RuntimeStarted: false,
        Enabled: false);

    public static DataAgentGraphHandshakeStreamOptions FromEnvironment()
    {
        return FromValues(
            Environment.GetEnvironmentVariable(EnabledEnvironmentVariable),
            Environment.GetEnvironmentVariable(EndpointEnvironmentVariable),
            Environment.GetEnvironmentVariable(TimeoutEnvironmentVariable));
    }

    public static DataAgentGraphHandshakeStreamOptions FromValues(string? enabled, string? endpoint, string? timeoutMs)
    {
        bool enabledValue = ParseEnabled(enabled);
        TimeSpan timeout = ParseTimeout(timeoutMs);
        if (enabledValue == false)
            return Disabled with { Timeout = timeout };

        if (TryParseLoopbackEndpoint(endpoint, out Uri? parsedEndpoint) == false)
            return Disabled with { Enabled = true, Timeout = timeout };

        return new DataAgentGraphHandshakeStreamOptions(
            parsedEndpoint,
            timeout,
            Configured: true,
            RuntimeStarted: false,
            Enabled: true);
    }

    static bool ParseEnabled(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "yes" => true,
            _ => false
        };
    }

    static TimeSpan ParseTimeout(string? timeoutMs)
    {
        if (int.TryParse(timeoutMs?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) &&
            parsed >= MinTimeoutMs &&
            parsed <= MaxTimeoutMs)
        {
            return TimeSpan.FromMilliseconds(parsed);
        }

        return TimeSpan.FromMilliseconds(DefaultTimeoutMs);
    }

    static bool TryParseLoopbackEndpoint(string? value, out Uri? endpoint)
    {
        endpoint = null;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? candidate) == false)
            return false;

        if (string.Equals(candidate.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) == false &&
            string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) == false)
            return false;

        if (string.Equals(candidate.Host, "0.0.0.0", StringComparison.Ordinal))
            return false;

        if (candidate.IsLoopback == false)
            return false;

        endpoint = candidate;
        return true;
    }
}
```

- [ ] **Step 5: Run options tests and commit**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeStreamOptionsTests" -v:minimal
```

Expected: PASS for `DataAgentGraphHandshakeStreamOptionsTests`.

```powershell
git add sources\Alife.Function\Alife.Function.DataAgent\DataAgentGraphHandshakeStreamModels.cs sources\Alife.Function\Alife.Function.DataAgent\DataAgentGraphHandshakeStreamOptions.cs Tests\Alife.Test.DataAgent\DataAgentGraphHandshakeStreamOptionsTests.cs
git commit -m "Add DataAgent graph handshake stream options"
```

## Task 2: NDJSON Stream Client

**Files:**
- Create: `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeNdjsonStreamClientTests.cs`
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeNdjsonStreamClient.cs`

- [ ] **Step 1: Add failing NDJSON client tests**

Create `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeNdjsonStreamClientTests.cs`:

```csharp
using System.Net;
using System.Text;
using System.Text.Json;
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentGraphHandshakeNdjsonStreamClientTests
{
    [Test]
    public void TryHandshakeStreamPostsRequestAndReturnsBufferedProgressWithFinalResponse()
    {
        DataAgentGraphHandshakeRequest request = NewRequest();
        HttpRequestMessage? capturedRequest = null;
        DataAgentGraphHandshakeRequest? capturedPayload = null;
        DataAgentGraphHandshakeNdjsonStreamClient client = NewClient(httpRequest =>
        {
            capturedRequest = httpRequest;
            capturedPayload = JsonSerializer.Deserialize<DataAgentGraphHandshakeRequest>(
                httpRequest.Content!.ReadAsStringAsync().GetAwaiter().GetResult());

            return NdjsonResponse(
                EventJson(new DataAgentGraphHandshakeStreamEvent(
                    DataAgentGraphHandshakeStreamEventKind.Progress,
                    Progress: new DataAgentGraphHandshakeProgress(
                        DataAgentWorkflowNodeNames.ScenarioKnowledge,
                        DataAgentGraphHandshakeProgressStatus.Completed,
                        "scenario_context_ready",
                        "scenario context ready",
                        new Dictionary<string, string> { ["stage"] = "scenario" }))),
                EventJson(new DataAgentGraphHandshakeStreamEvent(
                    DataAgentGraphHandshakeStreamEventKind.Progress,
                    Progress: new DataAgentGraphHandshakeProgress(
                        DataAgentWorkflowNodeNames.QueryPlanner,
                        DataAgentGraphHandshakeProgressStatus.Completed,
                        "planner_suggested",
                        "planner ready",
                        new Dictionary<string, string> { ["stage"] = "planner" }))),
                EventJson(new DataAgentGraphHandshakeStreamEvent(
                    DataAgentGraphHandshakeStreamEventKind.FinalResponse,
                    Response: NewResponse(capturedPayload!))));
        });

        DataAgentGraphHandshakeStreamResult result = client.TryHandshakeStream(request);
        DataAgentGraphHandshakeValidationResult validation = DataAgentGraphHandshakeValidator.Validate(request, result.Response);

        Assert.Multiple(() =>
        {
            Assert.That(capturedRequest, Is.Not.Null);
            Assert.That(capturedRequest!.RequestUri, Is.EqualTo(new Uri("http://localhost:32123/handshake-stream")));
            Assert.That(capturedPayload, Is.Not.Null);
            Assert.That(capturedPayload!.RequestId, Is.EqualTo(request.RequestId));
            Assert.That(result.Response.RequestId, Is.EqualTo(request.RequestId));
            Assert.That(result.Progress, Has.Count.EqualTo(2));
            Assert.That(result.Progress[0].Facts!["stage"], Is.EqualTo("scenario"));
            Assert.That(result.Progress[1].Facts!["stage"], Is.EqualTo("planner"));
            Assert.That(validation.Accepted, Is.True);
        });
    }

    [Test]
    public void MalformedJsonLineThrowsInvalidStreamSchema()
    {
        DataAgentGraphHandshakeNdjsonStreamClient client = NewClient(_ => NdjsonResponse("{not-json"));

        DataAgentGraphSidecarInvalidStreamException exception =
            Assert.Throws<DataAgentGraphSidecarInvalidStreamException>(() => client.TryHandshakeStream(NewRequest()))!;

        Assert.That(exception.ReasonCode, Is.EqualTo("invalid_stream_schema"));
    }

    [Test]
    public void UnknownEventKindThrowsInvalidStreamSchema()
    {
        DataAgentGraphHandshakeNdjsonStreamClient client = NewClient(_ => NdjsonResponse("""{"Kind":"Unknown","Progress":{}}"""));

        DataAgentGraphSidecarInvalidStreamException exception =
            Assert.Throws<DataAgentGraphSidecarInvalidStreamException>(() => client.TryHandshakeStream(NewRequest()))!;

        Assert.That(exception.ReasonCode, Is.EqualTo("invalid_stream_schema"));
    }

    [Test]
    public void ProgressEventWithoutProgressThrowsInvalidStreamSchema()
    {
        DataAgentGraphHandshakeNdjsonStreamClient client = NewClient(_ => NdjsonResponse("""{"Kind":"Progress"}"""));

        DataAgentGraphSidecarInvalidStreamException exception =
            Assert.Throws<DataAgentGraphSidecarInvalidStreamException>(() => client.TryHandshakeStream(NewRequest()))!;

        Assert.That(exception.ReasonCode, Is.EqualTo("invalid_stream_schema"));
    }

    [Test]
    public void FinalResponseEventWithoutResponseThrowsInvalidStreamSchema()
    {
        DataAgentGraphHandshakeNdjsonStreamClient client = NewClient(_ => NdjsonResponse("""{"Kind":"FinalResponse"}"""));

        DataAgentGraphSidecarInvalidStreamException exception =
            Assert.Throws<DataAgentGraphSidecarInvalidStreamException>(() => client.TryHandshakeStream(NewRequest()))!;

        Assert.That(exception.ReasonCode, Is.EqualTo("invalid_stream_schema"));
    }

    [Test]
    public void EventWithBothProgressAndResponseThrowsInvalidStreamSchema()
    {
        DataAgentGraphHandshakeRequest request = NewRequest();
        string line = EventJson(new DataAgentGraphHandshakeStreamEvent(
            DataAgentGraphHandshakeStreamEventKind.Progress,
            Progress: new DataAgentGraphHandshakeProgress(DataAgentWorkflowNodeNames.QueryPlanner, DataAgentGraphHandshakeProgressStatus.Completed, "planner_suggested"),
            Response: NewResponse(request)));
        DataAgentGraphHandshakeNdjsonStreamClient client = NewClient(_ => NdjsonResponse(line));

        DataAgentGraphSidecarInvalidStreamException exception =
            Assert.Throws<DataAgentGraphSidecarInvalidStreamException>(() => client.TryHandshakeStream(request))!;

        Assert.That(exception.ReasonCode, Is.EqualTo("invalid_stream_schema"));
    }

    [Test]
    public void StreamWithoutFinalResponseThrowsMissingFinalResponse()
    {
        string line = EventJson(new DataAgentGraphHandshakeStreamEvent(
            DataAgentGraphHandshakeStreamEventKind.Progress,
            Progress: new DataAgentGraphHandshakeProgress(DataAgentWorkflowNodeNames.QueryPlanner, DataAgentGraphHandshakeProgressStatus.Completed, "planner_suggested")));
        DataAgentGraphHandshakeNdjsonStreamClient client = NewClient(_ => NdjsonResponse(line));

        DataAgentGraphSidecarInvalidStreamException exception =
            Assert.Throws<DataAgentGraphSidecarInvalidStreamException>(() => client.TryHandshakeStream(NewRequest()))!;

        Assert.That(exception.ReasonCode, Is.EqualTo("missing_stream_final_response"));
    }

    [Test]
    public void ProgressOverBudgetThrowsStreamProgressOverBudget()
    {
        string[] lines = Enumerable.Range(0, DataAgentGraphHandshakeLimits.MaxProgressEvents + 1)
            .Select(_ => EventJson(new DataAgentGraphHandshakeStreamEvent(
                DataAgentGraphHandshakeStreamEventKind.Progress,
                Progress: new DataAgentGraphHandshakeProgress(DataAgentWorkflowNodeNames.QueryPlanner, DataAgentGraphHandshakeProgressStatus.Completed, "planner_suggested"))))
            .ToArray();
        DataAgentGraphHandshakeNdjsonStreamClient client = NewClient(_ => NdjsonResponse(lines));

        DataAgentGraphSidecarInvalidStreamException exception =
            Assert.Throws<DataAgentGraphSidecarInvalidStreamException>(() => client.TryHandshakeStream(NewRequest()))!;

        Assert.That(exception.ReasonCode, Is.EqualTo("stream_progress_over_budget"));
    }

    [Test]
    public void NonSuccessStatusThrowsUnavailable()
    {
        DataAgentGraphHandshakeNdjsonStreamClient client = NewClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => client.TryHandshakeStream(NewRequest()))!;

        Assert.That(exception.Message, Is.EqualTo("sidecar_unavailable"));
    }

    [Test]
    public void TimeoutThrowsTimeoutException()
    {
        DataAgentGraphHandshakeNdjsonStreamClient client = NewClient(_ => throw new TaskCanceledException("timed out"));

        TimeoutException exception = Assert.Throws<TimeoutException>(() => client.TryHandshakeStream(NewRequest()))!;

        Assert.That(exception.Message, Is.EqualTo("sidecar_timeout"));
    }

    static DataAgentGraphHandshakeNdjsonStreamClient NewClient(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        HttpClient httpClient = new(new DelegateHandler(responseFactory));
        DataAgentGraphHandshakeStreamOptions options = new(
            new Uri("http://localhost:32123/handshake-stream"),
            TimeSpan.FromMilliseconds(500),
            Configured: true,
            RuntimeStarted: false,
            Enabled: true);

        return new DataAgentGraphHandshakeNdjsonStreamClient(httpClient, options);
    }

    static HttpResponseMessage NdjsonResponse(params string[] lines)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Join("\n", lines) + "\n", Encoding.UTF8, "application/x-ndjson")
        };
    }

    static string EventJson(DataAgentGraphHandshakeStreamEvent value)
    {
        return JsonSerializer.Serialize(value, new JsonSerializerOptions
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
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
            "status=Active;executed_sql=true;terminal=false",
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
            ReasonCode: "handshake_accepted",
            SelectedNodes: [DataAgentWorkflowNodeNames.ScenarioKnowledge, DataAgentWorkflowNodeNames.QueryPlanner],
            NodeProgress: [],
            TraceSummary: "ScenarioKnowledge:Completed>QueryPlanner:Completed",
            ContextContribution: "graph_handshake=accepted",
            FallbackRequired: false,
            NoSqlAuthority: true,
            ReadOnly: true,
            RequestedToolNames: [DataAgentGraphHandshakeToolNames.ProposeQueryPlan],
            RequestsCheckpointMutation: false,
            RequestsVisibleText: false);
    }

    sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responseFactory(request));
        }
    }
}
```

- [ ] **Step 2: Run the failing NDJSON tests**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeNdjsonStreamClientTests" -v:minimal
```

Expected: FAIL with missing type errors for `DataAgentGraphHandshakeNdjsonStreamClient`.

- [ ] **Step 3: Add NDJSON stream client**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeNdjsonStreamClient.cs`:

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alife.Function.DataAgent;

public sealed class DataAgentGraphHandshakeNdjsonStreamClient : IDataAgentGraphHandshakeStreamClient
{
    const int MaxLineChars = 16384;
    static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    readonly HttpClient httpClient;
    readonly DataAgentGraphHandshakeStreamOptions options;

    public DataAgentGraphHandshakeNdjsonStreamClient(HttpClient httpClient, DataAgentGraphHandshakeStreamOptions options)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.options = options ?? throw new ArgumentNullException(nameof(options));

        if (options.Enabled == false || options.Configured == false || options.Endpoint is null)
            throw new ArgumentException("Graph handshake stream endpoint is not configured.", nameof(options));
    }

    public DataAgentGraphHandshakeStreamResult TryHandshakeStream(DataAgentGraphHandshakeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        using CancellationTokenSource cancellation = new(options.Timeout);

        try
        {
            using HttpResponseMessage response = httpClient.PostAsync(
                    options.Endpoint,
                    JsonContent.Create(request, options: JsonOptions),
                    cancellation.Token)
                .GetAwaiter()
                .GetResult();

            if (response.IsSuccessStatusCode == false)
                throw new InvalidOperationException("sidecar_unavailable");

            using Stream stream = response.Content.ReadAsStreamAsync(cancellation.Token)
                .GetAwaiter()
                .GetResult();
            using StreamReader reader = new(stream);

            List<DataAgentGraphHandshakeProgress> progress = [];
            DataAgentGraphHandshakeResponse? finalResponse = null;
            string? line;
            while ((line = reader.ReadLineAsync(cancellation.Token).GetAwaiter().GetResult()) is not null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    throw new DataAgentGraphSidecarInvalidStreamException("invalid_stream_schema");

                if (line.Length > MaxLineChars)
                    throw new DataAgentGraphSidecarInvalidStreamException("invalid_stream_schema");

                DataAgentGraphHandshakeStreamEvent streamEvent = DeserializeEvent(line);
                ValidateEnvelope(streamEvent);

                if (finalResponse is not null)
                    throw new DataAgentGraphSidecarInvalidStreamException("invalid_stream_schema");

                if (streamEvent.Kind == DataAgentGraphHandshakeStreamEventKind.Progress)
                {
                    progress.Add(streamEvent.Progress!);
                    if (progress.Count > DataAgentGraphHandshakeLimits.MaxProgressEvents)
                        throw new DataAgentGraphSidecarInvalidStreamException("stream_progress_over_budget");
                }
                else
                {
                    finalResponse = streamEvent.Response!;
                }
            }

            if (finalResponse is null)
                throw new DataAgentGraphSidecarInvalidStreamException("missing_stream_final_response");

            return new DataAgentGraphHandshakeStreamResult(finalResponse, progress);
        }
        catch (JsonException exception)
        {
            throw new DataAgentGraphSidecarInvalidStreamException("invalid_stream_schema", exception);
        }
        catch (TaskCanceledException exception)
        {
            throw new TimeoutException("sidecar_timeout", exception);
        }
    }

    static DataAgentGraphHandshakeStreamEvent DeserializeEvent(string line)
    {
        return JsonSerializer.Deserialize<DataAgentGraphHandshakeStreamEvent>(line, JsonOptions)
            ?? throw new DataAgentGraphSidecarInvalidStreamException("invalid_stream_schema");
    }

    static void ValidateEnvelope(DataAgentGraphHandshakeStreamEvent streamEvent)
    {
        if (Enum.IsDefined(streamEvent.Kind) == false)
            throw new DataAgentGraphSidecarInvalidStreamException("invalid_stream_schema");

        bool hasProgress = streamEvent.Progress is not null;
        bool hasResponse = streamEvent.Response is not null;

        if (streamEvent.Kind == DataAgentGraphHandshakeStreamEventKind.Progress && (hasProgress == false || hasResponse))
            throw new DataAgentGraphSidecarInvalidStreamException("invalid_stream_schema");

        if (streamEvent.Kind == DataAgentGraphHandshakeStreamEventKind.FinalResponse && (hasResponse == false || hasProgress))
            throw new DataAgentGraphSidecarInvalidStreamException("invalid_stream_schema");
    }

    static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new();
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
```

- [ ] **Step 4: Run NDJSON tests and commit**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeNdjsonStreamClientTests" -v:minimal
```

Expected: PASS for `DataAgentGraphHandshakeNdjsonStreamClientTests`.

```powershell
git add sources\Alife.Function\Alife.Function.DataAgent\DataAgentGraphHandshakeNdjsonStreamClient.cs Tests\Alife.Test.DataAgent\DataAgentGraphHandshakeNdjsonStreamClientTests.cs
git commit -m "Add DataAgent NDJSON handshake stream client"
```

## Task 3: Coordinator And Module Wiring

**Files:**
- Create: `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeStreamCoordinatorTests.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeCoordinator.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentModuleServiceTests.cs`

- [ ] **Step 1: Add failing coordinator stream tests**

Create `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeStreamCoordinatorTests.cs`:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentGraphHandshakeStreamCoordinatorTests
{
    [Test]
    public void AcceptedStreamPublishesBufferedProgressAfterFinalResponseValidation()
    {
        RecordingSidecarClient sidecar = new(NewAcceptedResponse);
        RecordingStreamClient stream = new(request => new DataAgentGraphHandshakeStreamResult(
            NewAcceptedResponse(request) with { NodeProgress = [] },
            [
                new DataAgentGraphHandshakeProgress(
                    DataAgentWorkflowNodeNames.QueryPlanner,
                    DataAgentGraphHandshakeProgressStatus.Completed,
                    "planner_suggested",
                    "planner ready",
                    new Dictionary<string, string> { ["stage"] = "planner" })
            ]));
        RecordingProgressSink progressSink = new();
        DataAgentGraphHandshakeCoordinator coordinator = new(
            new DataAgentGraphHandshakeOptions(true),
            sidecar,
            new DataAgentGraphSidecarProgressBridge(progressSink, Now),
            stream);

        DataAgentGraphHandshakeOutcome outcome = coordinator.TryHandshake("owner", "Which gates failed?", AcceptedResult());

        DataAgentProgressEvent progress = progressSink.Events.Single();
        Assert.Multiple(() =>
        {
            Assert.That(outcome.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Accepted));
            Assert.That(stream.Requests, Has.Count.EqualTo(1));
            Assert.That(sidecar.Requests, Is.Empty);
            Assert.That(progress.ReasonCode, Is.EqualTo("planner_suggested"));
            Assert.That(progress.Facts["stage"], Is.EqualTo("planner"));
            Assert.That(progress.Facts["source"], Is.EqualTo("graph_sidecar"));
        });
    }

    [Test]
    public void RejectedFinalResponseDoesNotPublishBufferedProgress()
    {
        RecordingStreamClient stream = new(request => new DataAgentGraphHandshakeStreamResult(
            NewAcceptedResponse(request) with { NoSqlAuthority = false },
            [
                new DataAgentGraphHandshakeProgress(
                    DataAgentWorkflowNodeNames.QueryPlanner,
                    DataAgentGraphHandshakeProgressStatus.Completed,
                    "planner_suggested",
                    "planner ready")
            ]));
        RecordingProgressSink progressSink = new();
        DataAgentGraphHandshakeCoordinator coordinator = new(
            new DataAgentGraphHandshakeOptions(true),
            new RecordingSidecarClient(NewAcceptedResponse),
            new DataAgentGraphSidecarProgressBridge(progressSink, Now),
            stream);

        DataAgentGraphHandshakeOutcome outcome = coordinator.TryHandshake("owner", "Which gates failed?", AcceptedResult());

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Rejected));
            Assert.That(outcome.ReasonCode, Is.EqualTo("sql_authority_requested"));
            Assert.That(outcome.Response, Is.Null);
            Assert.That(progressSink.Events, Is.Empty);
        });
    }

    [TestCase("invalid_stream_schema", DataAgentGraphHandshakeStatus.Invalid)]
    [TestCase("missing_stream_final_response", DataAgentGraphHandshakeStatus.Invalid)]
    [TestCase("stream_progress_over_budget", DataAgentGraphHandshakeStatus.Invalid)]
    public void InvalidStreamFailuresDiscardProgressAndReturnReason(string reasonCode, DataAgentGraphHandshakeStatus expectedStatus)
    {
        RecordingProgressSink progressSink = new();
        ThrowingStreamClient stream = new(new DataAgentGraphSidecarInvalidStreamException(reasonCode));
        DataAgentGraphHandshakeCoordinator coordinator = new(
            new DataAgentGraphHandshakeOptions(true),
            new RecordingSidecarClient(NewAcceptedResponse),
            new DataAgentGraphSidecarProgressBridge(progressSink, Now),
            stream);

        DataAgentGraphHandshakeOutcome outcome = coordinator.TryHandshake("owner", "Which gates failed?", AcceptedResult());

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Status, Is.EqualTo(expectedStatus));
            Assert.That(outcome.ReasonCode, Is.EqualTo(reasonCode));
            Assert.That(outcome.FallbackRequired, Is.True);
            Assert.That(progressSink.Events, Is.Empty);
        });
    }

    [Test]
    public void StreamTimeoutAndUnavailableDiscardProgress()
    {
        RecordingProgressSink timeoutSink = new();
        DataAgentGraphHandshakeCoordinator timeoutCoordinator = new(
            new DataAgentGraphHandshakeOptions(true),
            new RecordingSidecarClient(NewAcceptedResponse),
            new DataAgentGraphSidecarProgressBridge(timeoutSink, Now),
            new ThrowingStreamClient(new TimeoutException("sidecar_timeout")));
        RecordingProgressSink unavailableSink = new();
        DataAgentGraphHandshakeCoordinator unavailableCoordinator = new(
            new DataAgentGraphHandshakeOptions(true),
            new RecordingSidecarClient(NewAcceptedResponse),
            new DataAgentGraphSidecarProgressBridge(unavailableSink, Now),
            new ThrowingStreamClient(new InvalidOperationException("sidecar_unavailable")));

        DataAgentGraphHandshakeOutcome timeout = timeoutCoordinator.TryHandshake("owner", "Which gates failed?", AcceptedResult());
        DataAgentGraphHandshakeOutcome unavailable = unavailableCoordinator.TryHandshake("owner", "Which gates failed?", AcceptedResult());

        Assert.Multiple(() =>
        {
            Assert.That(timeout.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Timeout));
            Assert.That(timeout.ReasonCode, Is.EqualTo("sidecar_timeout"));
            Assert.That(timeoutSink.Events, Is.Empty);
            Assert.That(unavailable.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Unavailable));
            Assert.That(unavailable.ReasonCode, Is.EqualTo("sidecar_unavailable"));
            Assert.That(unavailableSink.Events, Is.Empty);
        });
    }

    [Test]
    public void DisabledCoordinatorDoesNotCallStreamClient()
    {
        RecordingStreamClient stream = new(request => new DataAgentGraphHandshakeStreamResult(NewAcceptedResponse(request), []));
        RecordingProgressSink progressSink = new();
        DataAgentGraphHandshakeCoordinator coordinator = new(
            DataAgentGraphHandshakeOptions.Disabled,
            new RecordingSidecarClient(NewAcceptedResponse),
            new DataAgentGraphSidecarProgressBridge(progressSink, Now),
            stream);

        DataAgentGraphHandshakeOutcome outcome = coordinator.TryHandshake("owner", "Which gates failed?", AcceptedResult());

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Disabled));
            Assert.That(stream.Requests, Is.Empty);
            Assert.That(progressSink.Events, Is.Empty);
        });
    }

    [Test]
    public void MissingStreamClientPreservesExistingRequestResponsePath()
    {
        RecordingSidecarClient sidecar = new(NewAcceptedResponse);
        RecordingProgressSink progressSink = new();
        DataAgentGraphHandshakeCoordinator coordinator = new(
            new DataAgentGraphHandshakeOptions(true),
            sidecar,
            new DataAgentGraphSidecarProgressBridge(progressSink, Now));

        DataAgentGraphHandshakeOutcome outcome = coordinator.TryHandshake("owner", "Which gates failed?", AcceptedResult());

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Accepted));
            Assert.That(sidecar.Requests, Has.Count.EqualTo(1));
            Assert.That(progressSink.Events, Has.Count.EqualTo(1));
        });
    }

    static DataAgentGraphHandshakeResponse NewAcceptedResponse(DataAgentGraphHandshakeRequest request)
    {
        return new DataAgentGraphHandshakeResponse(
            request.RequestId,
            Accepted: true,
            ReasonCode: "handshake_accepted",
            SelectedNodes: [DataAgentWorkflowNodeNames.ScenarioKnowledge, DataAgentWorkflowNodeNames.QueryPlanner],
            NodeProgress:
            [
                new DataAgentGraphHandshakeProgress(DataAgentWorkflowNodeNames.QueryPlanner, DataAgentGraphHandshakeProgressStatus.Completed, "planner_suggested")
            ],
            TraceSummary: "ScenarioKnowledge:Completed>QueryPlanner:Completed",
            ContextContribution: "graph_handshake=accepted",
            FallbackRequired: false,
            NoSqlAuthority: true,
            ReadOnly: true,
            RequestedToolNames: [DataAgentGraphHandshakeToolNames.ProposeQueryPlan],
            RequestsCheckpointMutation: false,
            RequestsVisibleText: false);
    }

    static DataAgentOrchestrationResult AcceptedResult()
    {
        DataAgentAnswer answer = new(
            "document_index",
            "SELECT path FROM document_index LIMIT 20",
            2,
            "Found DataAgent documentation.",
            "[data_agent_context]\nsql_status=validated\n[/data_agent_context]",
            true,
            string.Empty,
            new DataAgentPlannerExplanation("TestPlanner", "find_documents", "document_index", "high", ["test"], "test accepted answer"));
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
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.SchemaContext, DataAgentOrchestrationStepStatus.Succeeded, "schema_ready", ExecutedSql: false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Plan, DataAgentOrchestrationStepStatus.Succeeded, "plan_ready", ExecutedSql: false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Validate, DataAgentOrchestrationStepStatus.Succeeded, "validated", ExecutedSql: false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Execute, DataAgentOrchestrationStepStatus.Succeeded, "executed", ExecutedSql: true),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Explain, DataAgentOrchestrationStepStatus.Succeeded, "explained", ExecutedSql: false),
                new DataAgentOrchestrationStep(DataAgentOrchestrationNodeKind.Checkpoint, DataAgentOrchestrationStepStatus.Succeeded, "checkpoint_saved", ExecutedSql: false)
            ],
            checkpoint,
            response,
            routeContext);
    }

    static DateTimeOffset Now()
    {
        return new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);
    }

    sealed class RecordingStreamClient(Func<DataAgentGraphHandshakeRequest, DataAgentGraphHandshakeStreamResult> responseFactory)
        : IDataAgentGraphHandshakeStreamClient
    {
        readonly List<DataAgentGraphHandshakeRequest> requests = [];
        public IReadOnlyList<DataAgentGraphHandshakeRequest> Requests => requests;

        public DataAgentGraphHandshakeStreamResult TryHandshakeStream(DataAgentGraphHandshakeRequest request)
        {
            requests.Add(request);
            return responseFactory(request);
        }
    }

    sealed class ThrowingStreamClient(Exception exception) : IDataAgentGraphHandshakeStreamClient
    {
        public DataAgentGraphHandshakeStreamResult TryHandshakeStream(DataAgentGraphHandshakeRequest request)
        {
            throw exception;
        }
    }

    sealed class RecordingSidecarClient(Func<DataAgentGraphHandshakeRequest, DataAgentGraphHandshakeResponse> responseFactory)
        : IDataAgentGraphSidecarClient
    {
        readonly List<DataAgentGraphHandshakeRequest> requests = [];
        public IReadOnlyList<DataAgentGraphHandshakeRequest> Requests => requests;

        public DataAgentGraphHandshakeResponse TryHandshake(DataAgentGraphHandshakeRequest request)
        {
            requests.Add(request);
            return responseFactory(request);
        }
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

- [ ] **Step 2: Run failing coordinator stream tests**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeStreamCoordinatorTests" -v:minimal
```

Expected: FAIL because `DataAgentGraphHandshakeCoordinator` does not accept `IDataAgentGraphHandshakeStreamClient`.

- [ ] **Step 3: Modify coordinator constructor and stream branch**

In `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeCoordinator.cs`, change the primary constructor to:

```csharp
public sealed class DataAgentGraphHandshakeCoordinator(
    DataAgentGraphHandshakeOptions options,
    IDataAgentGraphSidecarClient? sidecarClient = null,
    DataAgentGraphSidecarProgressBridge? progressBridge = null,
    IDataAgentGraphHandshakeStreamClient? streamClient = null)
{
    readonly DataAgentGraphHandshakeOptions options = options ?? throw new ArgumentNullException(nameof(options));
    readonly IDataAgentGraphSidecarClient sidecarClient = sidecarClient ?? DisabledDataAgentGraphSidecarClient.Instance;
    readonly DataAgentGraphSidecarProgressBridge? progressBridge = progressBridge;
    readonly IDataAgentGraphHandshakeStreamClient? streamClient = streamClient;
```

Inside `TryHandshake`, immediately after request creation and before calling `sidecarClient.TryHandshake`, add:

```csharp
        if (streamClient is not null)
            return TryStreamHandshake(request!, result);
```

Add this instance method before `TryBuildRequest`:

```csharp
    DataAgentGraphHandshakeOutcome TryStreamHandshake(
        DataAgentGraphHandshakeRequest request,
        DataAgentOrchestrationResult result)
    {
        try
        {
            DataAgentGraphHandshakeStreamResult streamResult = streamClient!.TryHandshakeStream(request);
            DataAgentGraphHandshakeValidationResult validation = DataAgentGraphHandshakeValidator.Validate(request, streamResult.Response);
            if (validation.Accepted == false)
            {
                return Outcome(
                    DataAgentGraphHandshakeStatus.Rejected,
                    validation.ReasonCode,
                    fallbackRequired: true,
                    request,
                    response: null,
                    validation);
            }

            PublishProgressIfAvailable(request, result, streamResult.Progress);

            return Outcome(
                DataAgentGraphHandshakeStatus.Accepted,
                validation.ReasonCode,
                streamResult.Response.FallbackRequired,
                request,
                streamResult.Response,
                validation);
        }
        catch (DataAgentGraphSidecarInvalidStreamException exception)
        {
            return Outcome(DataAgentGraphHandshakeStatus.Invalid, exception.ReasonCode, fallbackRequired: true, request);
        }
        catch (TimeoutException)
        {
            return Outcome(DataAgentGraphHandshakeStatus.Timeout, "sidecar_timeout", fallbackRequired: true, request);
        }
        catch (Exception)
        {
            return Outcome(DataAgentGraphHandshakeStatus.Unavailable, "sidecar_unavailable", fallbackRequired: true, request);
        }
    }
```

Leave the existing request/response `sidecarClient.TryHandshake` branch unchanged so V3.1 behavior remains intact when `streamClient` is null.

- [ ] **Step 4: Add failing module wiring tests**

Append these tests to `Tests/Alife.Test.DataAgent/DataAgentModuleServiceTests.cs`:

```csharp
[Test]
public void CreateGraphHandshakeStreamClientReturnsNullUnlessGraphAndStreamAreConfigured()
{
    DataAgentGraphHandshakeStreamOptions configuredStream = DataAgentGraphHandshakeStreamOptions.FromValues(
        "true",
        "http://127.0.0.1:8765/handshake-stream",
        "800");

    Assert.Multiple(() =>
    {
        Assert.That(DataAgentModuleService.CreateGraphHandshakeStreamClient(DataAgentGraphHandshakeOptions.Disabled, configuredStream), Is.Null);
        Assert.That(DataAgentModuleService.CreateGraphHandshakeStreamClient(new DataAgentGraphHandshakeOptions(true), DataAgentGraphHandshakeStreamOptions.Disabled), Is.Null);
    });
}

[Test]
public void CreateGraphHandshakeStreamClientCreatesNdjsonClientForConfiguredLoopbackEndpoint()
{
    DataAgentGraphHandshakeStreamOptions configuredStream = DataAgentGraphHandshakeStreamOptions.FromValues(
        "true",
        "http://127.0.0.1:8765/handshake-stream",
        "800");

    IDataAgentGraphHandshakeStreamClient? client = DataAgentModuleService.CreateGraphHandshakeStreamClient(
        new DataAgentGraphHandshakeOptions(true),
        configuredStream);

    Assert.That(client, Is.TypeOf<DataAgentGraphHandshakeNdjsonStreamClient>());
}
```

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentModuleServiceTests" -v:minimal
```

Expected: FAIL because `CreateGraphHandshakeStreamClient` is missing.

- [ ] **Step 5: Wire stream options and client in module service**

In `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`, add this internal factory after `CreateGraphHandshakeSidecarClient`:

```csharp
    internal static IDataAgentGraphHandshakeStreamClient? CreateGraphHandshakeStreamClient(
        DataAgentGraphHandshakeOptions graphOptions,
        DataAgentGraphHandshakeStreamOptions streamOptions)
    {
        if (graphOptions.Enabled == false ||
            streamOptions.Enabled == false ||
            streamOptions.Configured == false ||
            streamOptions.Endpoint is null)
        {
            return null;
        }

        return new DataAgentGraphHandshakeNdjsonStreamClient(new HttpClient(), streamOptions);
    }
```

In `AwakeAsync`, after `graphHandshakeHttpOptions` is created, add:

```csharp
        DataAgentGraphHandshakeStreamOptions graphHandshakeStreamOptions =
            DataAgentGraphHandshakeStreamOptions.FromEnvironment();
```

Change coordinator construction to:

```csharp
        DataAgentGraphHandshakeCoordinator graphHandshakeCoordinator = new(
            graphHandshakeOptions,
            CreateGraphHandshakeSidecarClient(graphHandshakeOptions, graphHandshakeHttpOptions),
            new DataAgentGraphSidecarProgressBridge(progressSink),
            CreateGraphHandshakeStreamClient(graphHandshakeOptions, graphHandshakeStreamOptions));
```

- [ ] **Step 6: Run coordinator and module tests, then commit**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeStreamCoordinatorTests|FullyQualifiedName~DataAgentGraphHandshakeCoordinatorTests|FullyQualifiedName~DataAgentModuleServiceTests" -v:minimal
```

Expected: PASS for stream coordinator, existing coordinator, and module service tests.

```powershell
git add sources\Alife.Function\Alife.Function.DataAgent\DataAgentGraphHandshakeCoordinator.cs sources\Alife.Function\Alife.Function.DataAgent\DataAgentModuleService.cs Tests\Alife.Test.DataAgent\DataAgentGraphHandshakeStreamCoordinatorTests.cs Tests\Alife.Test.DataAgent\DataAgentModuleServiceTests.cs
git commit -m "Wire DataAgent NDJSON handshake stream coordinator"
```

## Task 4: Dev Stub And Developer Documentation

**Files:**
- Modify: `tools/dataagent-graph-sidecar/app.py`
- Modify: `tools/dataagent-graph-sidecar/README.md`
- Create: `docs/dataagent/dataagent-v3.3-ndjson-streaming-transport.md`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeDevSidecarStubTests.cs`

- [ ] **Step 1: Add failing static stub and doc tests**

Append this test to `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeDevSidecarStubTests.cs`:

```csharp
[Test]
public void PythonDevStubDocumentsV33NdjsonStreamWithoutRuntimeDependency()
{
    string root = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
    string app = File.ReadAllText(Path.Combine(root, "tools", "dataagent-graph-sidecar", "app.py"));
    string readme = File.ReadAllText(Path.Combine(root, "tools", "dataagent-graph-sidecar", "README.md"));
    string doc = File.ReadAllText(Path.Combine(root, "docs", "dataagent", "dataagent-v3.3-ndjson-streaming-transport.md"));

    Assert.Multiple(() =>
    {
        Assert.That(app, Does.Contain("@app.post(\"/handshake-stream\")"));
        Assert.That(app, Does.Contain("StreamingResponse"));
        Assert.That(app, Does.Contain("application/x-ndjson"));
        Assert.That(app, Does.Contain("\"Kind\": \"Progress\""));
        Assert.That(app, Does.Contain("\"Kind\": \"FinalResponse\""));
        Assert.That(app, Does.Contain("\"Progress\""));
        Assert.That(app, Does.Contain("\"Response\""));
        Assert.That(app, Does.Contain("\"stage\": \"planner\""));
        Assert.That(app, Does.Not.Contain("\"source\": \"graph_sidecar\""));
        Assert.That(app, Does.Not.Contain("\"node\":"));
        Assert.That(app, Does.Not.Contain("\"request_id\":"));
        Assert.That(app, Does.Not.Contain("EventSource"));
        Assert.That(app, Does.Not.Contain("text/event-stream"));
        Assert.That(app, Does.Not.Contain("subprocess"));
        Assert.That(app, Does.Not.Contain("sqlite"));
        Assert.That(app, Does.Not.Contain("postgres"));
        Assert.That(readme, Does.Contain("V3.3"));
        Assert.That(readme, Does.Contain("/handshake-stream"));
        Assert.That(readme, Does.Contain("NDJSON"));
        Assert.That(readme, Does.Contain("SSE is deferred"));
        Assert.That(readme, Does.Contain("buffered until the final response is accepted"));
        Assert.That(readme, Does.Contain("default tests do not require Python"));
        Assert.That(doc, Does.Contain("DataAgent V3.3"));
        Assert.That(doc, Does.Contain("NDJSON streaming transport smoke"));
        Assert.That(doc, Does.Contain("invalid_stream_schema"));
        Assert.That(doc, Does.Contain("missing_stream_final_response"));
        Assert.That(doc, Does.Contain("stream_progress_over_budget"));
        Assert.That(doc, Does.Contain("SSE is deferred"));
    });
}
```

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeDevSidecarStubTests" -v:minimal
```

Expected: FAIL because `/handshake-stream` and V3.3 docs do not exist.

- [ ] **Step 2: Add `/handshake-stream` to the Python stub**

In `tools/dataagent-graph-sidecar/app.py`, add imports:

```python
import json
from collections.abc import Iterable
from fastapi.responses import StreamingResponse
```

Add helper functions after `health`:

```python
def build_handshake_response(request: GraphHandshakeRequest) -> GraphHandshakeResponse:
    selected_nodes = ["scenario_knowledge", "query_planner", "diagnostics_router"]
    return GraphHandshakeResponse(
        RequestId=request.RequestId,
        Accepted=True,
        ReasonCode="dev_sidecar_accepted",
        SelectedNodes=selected_nodes,
        NodeProgress=[
            GraphHandshakeProgress(
                NodeName="scenario_knowledge",
                Status="Completed",
                ReasonCode="scenario_context_ready",
                Message="scenario context ready",
                Facts={"stage": "scenario"},
            ),
            GraphHandshakeProgress(
                NodeName="query_planner",
                Status="Completed",
                ReasonCode="planner_suggested",
                Message="planner ready",
                Facts={"stage": "planner"},
            ),
            GraphHandshakeProgress(
                NodeName="diagnostics_router",
                Status="Completed",
                ReasonCode="diagnostics_ready",
                Message="diagnostics ready",
                Facts={"stage": "diagnostics"},
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


def stream_handshake_events(response: GraphHandshakeResponse) -> Iterable[str]:
    for progress in response.NodeProgress:
        yield json.dumps({"Kind": "Progress", "Progress": progress.model_dump()}) + "\n"
    yield json.dumps({"Kind": "FinalResponse", "Response": response.model_dump()}) + "\n"
```

Replace the body of existing `handshake` with:

```python
    return build_handshake_response(request)
```

Add the stream endpoint:

```python
@app.post("/handshake-stream")
def handshake_stream(request: GraphHandshakeRequest) -> StreamingResponse:
    response = build_handshake_response(request)
    return StreamingResponse(
        stream_handshake_events(response),
        media_type="application/x-ndjson",
    )
```

- [ ] **Step 3: Update sidecar README**

Append to `tools/dataagent-graph-sidecar/README.md`:

````markdown
## V3.3 NDJSON Stream

V3.3 adds an optional `/handshake-stream` endpoint for NDJSON streaming
transport smoke. Configure it separately from the V3.1 request/response path:

```powershell
$env:ALIFE_DATAAGENT_GRAPH_HANDSHAKE_STREAM_ENABLED = "true"
$env:ALIFE_DATAAGENT_GRAPH_HANDSHAKE_STREAM_ENDPOINT = "http://127.0.0.1:8765/handshake-stream"
$env:ALIFE_DATAAGENT_GRAPH_HANDSHAKE_STREAM_TIMEOUT_MS = "800"
```

The stream emits one JSON object per line. Progress events are buffered by C#
until the final response is accepted by `DataAgentGraphHandshakeValidator`.
Only then can C# publish safe progress through
`DataAgentGraphSidecarProgressBridge`.

SSE is deferred. The dev stub does not expose `text/event-stream`, event ids,
heartbeats, reconnect behavior, or browser-facing stream behavior.

Default tests do not require Python, FastAPI, uvicorn, a live port, network
access, QChat, QQ, PostgreSQL, browser automation, model calls, or a live
sidecar.
````

- [ ] **Step 4: Add V3.3 developer note**

Create `docs/dataagent/dataagent-v3.3-ndjson-streaming-transport.md`:

````markdown
# DataAgent V3.3 NDJSON Streaming Transport

DataAgent V3.3 adds an optional NDJSON streaming transport smoke for the graph
handshake dev sidecar. It is not a production LangGraph runtime and it does
not start Python automatically.

## Stream Contract

C# posts the existing `DataAgentGraphHandshakeRequest` to `/handshake-stream`.
The dev sidecar returns newline-delimited JSON events:

```json
{"Kind":"Progress","Progress":{"NodeName":"query_planner","Status":"Completed","ReasonCode":"planner_suggested","Message":"planner ready","Facts":{"stage":"planner"}}}
{"Kind":"FinalResponse","Response":{"RequestId":"graph-handshake-session-1-turn-1","Accepted":true,"ReasonCode":"handshake_accepted","SelectedNodes":["scenario_knowledge","query_planner"],"NodeProgress":[],"TraceSummary":"ScenarioKnowledge:Completed>QueryPlanner:Completed","ContextContribution":"graph_handshake=accepted","FallbackRequired":false,"NoSqlAuthority":true,"ReadOnly":true,"RequestedToolNames":["dataagent.query_plan.propose"],"RequestsCheckpointMutation":false,"RequestsVisibleText":false}}
```

Progress is untrusted. The NDJSON client buffers progress events in memory and
requires exactly one final response. `DataAgentGraphHandshakeValidator`
validates the final response. `DataAgentGraphSidecarProgressBridge` publishes
buffered progress only after that final response is accepted.

## Failure Semantics

Malformed JSON, unknown event kinds, missing event bodies, dual-body events,
oversized lines, or trailing events after the final response fail with
`invalid_stream_schema`.

A stream that ends without a final response fails with
`missing_stream_final_response`.

More than `DataAgentGraphHandshakeLimits.MaxProgressEvents` progress events
fails with `stream_progress_over_budget`.

Timeouts use `sidecar_timeout`. HTTP failures and transport failures use
`sidecar_unavailable`.

Rejected, invalid, timed out, unavailable, malformed, incomplete, and
over-budget streams publish no sidecar progress.

## Boundaries

The sidecar cannot execute SQL, authorize SQL, mutate checkpoints, decide Tool
Broker route state, send visible QChat text, own QQ ingress, read files,
control browser state, publish diagnostics, write evidence, or manage plugins.
C# keeps recorder, diagnostics, SQL, checkpoint, and QChat authority.

## Tests

Default tests use fake HTTP handlers and static stub checks. They do not start
Python, FastAPI, uvicorn, a live sidecar, QChat, QQ, PostgreSQL, browser
automation, network services, or model calls.

## Future

SSE is deferred. A later V3.x milestone can reuse the stream envelope after
NDJSON proves parsing, buffering, fallback, final-response validation, and the
V3.2 bridge boundary.
````

- [ ] **Step 5: Run stub/doc tests and commit**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeDevSidecarStubTests" -v:minimal
```

Expected: PASS for `DataAgentGraphHandshakeDevSidecarStubTests`.

```powershell
git add tools\dataagent-graph-sidecar\app.py tools\dataagent-graph-sidecar\README.md docs\dataagent\dataagent-v3.3-ndjson-streaming-transport.md Tests\Alife.Test.DataAgent\DataAgentGraphHandshakeDevSidecarStubTests.cs
git commit -m "Document DataAgent V3.3 NDJSON stream stub"
```

## Task 5: Readiness And QChat Boundary

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
- Modify: `tools/check-dataagent-readiness.ps1`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
- Inspect: `tools/check-qchat-engineering-map.ps1`
- Inspect: `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`

- [ ] **Step 1: Add failing readiness assertions**

In `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`:

Change the core check count assertion:

```csharp
Assert.That(checks, Has.Count.EqualTo(75));
```

Add near the existing graph handshake readiness assertions:

```csharp
Assert.That(checks.Select(check => check.Name), Does.Contain("GraphHandshakeDevSidecarStreamingTransportPresent"));
DataAgentReadinessCheck graphHandshakeStreamCheck = checks.Single(check => check.Name == "GraphHandshakeDevSidecarStreamingTransportPresent");
Assert.That(graphHandshakeStreamCheck.Detail, Does.Contain("default_enabled=false"));
Assert.That(graphHandshakeStreamCheck.Detail, Does.Contain("ndjson_stream=true"));
Assert.That(graphHandshakeStreamCheck.Detail, Does.Contain("buffer_until_accepted=true"));
Assert.That(graphHandshakeStreamCheck.Detail, Does.Contain("final_response_required=true"));
Assert.That(graphHandshakeStreamCheck.Detail, Does.Contain("sse_deferred=true"));
Assert.That(graphHandshakeStreamCheck.Detail, Does.Contain("csharp_bridge_authority=true"));
Assert.That(graphHandshakeStreamCheck.Detail, Does.Contain("qchat_boundary=true"));
Assert.That(graphHandshakeStreamCheck.Detail, Does.Contain("runtime_required=false"));
```

In `ReadinessScriptDefaultModeExitsZeroAndPrintsSummary`, change:

```csharp
"  Summary: 89 required passed, 0 required missing"
```

and add:

```csharp
Assert.That(result.StandardOutput, Does.Contain("GraphHandshakeDevSidecarStreamingTransportPresent"));
```

In `ReadinessScriptProtectsV23RouteGateContract`, change:

```csharp
Assert.That(script, Does.Contain("$expectedRequired = 89"));
```

Add this test after the V3.2 static readiness marker test:

```csharp
[Test]
public void StaticReadinessScriptContainsV33NdjsonStreamingTransportMarkers()
{
    string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
    string scriptPath = Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1");
    string script = File.ReadAllText(scriptPath);

    string declaration = FindNewCheckDeclaration(script, "GraphHandshakeDevSidecarStreamingTransportPresent");

    Assert.Multiple(() =>
    {
        Assert.That(declaration, Does.Contain("DataAgentGraphHandshakeStreamModels.cs"));
        Assert.That(declaration, Does.Contain("DataAgentGraphHandshakeStreamOptions.cs"));
        Assert.That(declaration, Does.Contain("DataAgentGraphHandshakeNdjsonStreamClient.cs"));
        Assert.That(declaration, Does.Contain("DataAgentGraphHandshakeStreamEvent"));
        Assert.That(declaration, Does.Contain("IDataAgentGraphHandshakeStreamClient"));
        Assert.That(declaration, Does.Contain("DataAgentGraphSidecarInvalidStreamException"));
        Assert.That(declaration, Does.Contain("invalid_stream_schema"));
        Assert.That(declaration, Does.Contain("missing_stream_final_response"));
        Assert.That(declaration, Does.Contain("stream_progress_over_budget"));
        Assert.That(declaration, Does.Contain("DataAgentGraphSidecarProgressBridge"));
        Assert.That(declaration, Does.Contain("buffer_until_accepted=true"));
        Assert.That(declaration, Does.Contain("sse_deferred=true"));
    });
}
```

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests" -v:minimal
```

Expected: FAIL because dynamic and static readiness checks still report V3.2 counts.

- [ ] **Step 2: Add dynamic readiness marker**

In `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`, add after the V3.2 `GraphHandshakeDevSidecarProgressBridgePresent` block:

```csharp
DataAgentGraphHandshakeStreamOptions graphHandshakeDefaultStreamOptions =
    DataAgentGraphHandshakeStreamOptions.FromValues(null, null, null);
DataAgentGraphHandshakeStreamOptions graphHandshakeLoopbackStreamOptions =
    DataAgentGraphHandshakeStreamOptions.FromValues("true", "http://127.0.0.1:8765/handshake-stream", "800");
DataAgentGraphHandshakeStreamOptions graphHandshakeRemoteStreamOptions =
    DataAgentGraphHandshakeStreamOptions.FromValues("true", "http://example.com/handshake-stream", "800");
DataAgentGraphHandshakeStreamResult graphHandshakeStreamResult = new(
    graphHandshakeSafeResponse with { NodeProgress = [] },
    [
        new DataAgentGraphHandshakeProgress(
            DataAgentWorkflowNodeNames.QueryPlanner,
            DataAgentGraphHandshakeProgressStatus.Completed,
            "planner_suggested",
            "planner ready",
            new Dictionary<string, string> { ["stage"] = "planner" })
    ]);
DataAgentProgressRecorder graphHandshakeStreamRecorder = new();
DataAgentGraphSidecarProgressBridge graphHandshakeStreamBridge = new(
    new DataAgentProgressDiagnosticsPublisher(graphHandshakeStreamRecorder),
    () => graphSidecarProgressNow);
DataAgentGraphHandshakeValidationResult graphHandshakeStreamValidation =
    DataAgentGraphHandshakeValidator.Validate(graphHandshakeRequest, graphHandshakeStreamResult.Response);
DataAgentGraphSidecarProgressBridgeResult graphHandshakeStreamPublished = graphHandshakeStreamValidation.Accepted
    ? graphHandshakeStreamBridge.PublishHandshakeProgress(graphHandshakeRequest, graphSidecarProgressResult, graphHandshakeStreamResult.Progress)
    : new DataAgentGraphSidecarProgressBridgeResult(0, graphHandshakeStreamResult.Progress.Count);
IReadOnlyList<DataAgentProgressEvent> graphHandshakeStreamEvents = graphHandshakeStreamRecorder.GetRecent(
    graphHandshakeRequest.SessionId,
    graphSidecarProgressNow);
bool graphHandshakeStreamDefaultDisabled =
    graphHandshakeDefaultStreamOptions.Enabled == false &&
    graphHandshakeDefaultStreamOptions.Configured == false &&
    graphHandshakeDefaultStreamOptions.RuntimeStarted == false;
bool graphHandshakeStreamLoopbackOnly =
    graphHandshakeLoopbackStreamOptions.Enabled &&
    graphHandshakeLoopbackStreamOptions.Configured &&
    graphHandshakeRemoteStreamOptions.Configured == false;
bool graphHandshakeStreamEnvelopeReady =
    typeof(DataAgentGraphHandshakeStreamEvent).IsClass &&
    typeof(IDataAgentGraphHandshakeStreamClient).IsInterface &&
    typeof(DataAgentGraphHandshakeNdjsonStreamClient).GetInterfaces().Contains(typeof(IDataAgentGraphHandshakeStreamClient)) &&
    typeof(DataAgentGraphSidecarInvalidStreamException).IsClass;
bool graphHandshakeStreamBufferedUntilAccepted =
    graphHandshakeStreamValidation.Accepted &&
    graphHandshakeStreamPublished.AcceptedCount == 1 &&
    graphHandshakeStreamEvents.Count == 1 &&
    string.Equals(graphHandshakeStreamEvents.Single().Facts["source"], "graph_sidecar", StringComparison.Ordinal);
bool graphHandshakeStreamQChatBoundary =
    string.Equals(typeof(DataAgentGraphHandshakeStreamEvent).Namespace, "Alife.Function.DataAgent", StringComparison.Ordinal) &&
    typeof(DataAgentGraphHandshakeStreamEvent).Assembly.GetName().Name?.Contains("QChat", StringComparison.OrdinalIgnoreCase) == false;
bool graphHandshakeStreamReady =
    graphHandshakeStreamDefaultDisabled &&
    graphHandshakeStreamLoopbackOnly &&
    graphHandshakeStreamEnvelopeReady &&
    graphHandshakeStreamBufferedUntilAccepted &&
    graphHandshakeStreamQChatBoundary;
checks.Add(graphHandshakeStreamReady
    ? Pass("GraphHandshakeDevSidecarStreamingTransportPresent", "default_enabled=false;ndjson_stream=true;buffer_until_accepted=true;final_response_required=true;sse_deferred=true;csharp_bridge_authority=true;qchat_boundary=true;runtime_required=false")
    : Fail("GraphHandshakeDevSidecarStreamingTransportPresent", $"default_enabled={LowerBool(graphHandshakeStreamDefaultDisabled)};ndjson_stream={LowerBool(graphHandshakeStreamEnvelopeReady)};buffer_until_accepted={LowerBool(graphHandshakeStreamBufferedUntilAccepted)};final_response_required=true;sse_deferred=true;csharp_bridge_authority={LowerBool(graphHandshakeStreamEvents.Count == 1)};qchat_boundary={LowerBool(graphHandshakeStreamQChatBoundary)};runtime_required={LowerBool(graphHandshakeDefaultStreamOptions.RuntimeStarted)}"));
```

- [ ] **Step 3: Add static readiness script check**

In `tools/check-dataagent-readiness.ps1`, add immediately after `GraphHandshakeDevSidecarProgressBridgePresent`:

```powershell
New-Check -Group "Store" -Name "GraphHandshakeDevSidecarStreamingTransportPresent" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeStreamModels.cs" @("DataAgentGraphHandshakeStreamEvent", "DataAgentGraphHandshakeStreamEventKind", "DataAgentGraphHandshakeStreamResult", "IDataAgentGraphHandshakeStreamClient", "DataAgentGraphSidecarInvalidStreamException")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeStreamOptions.cs" @("DataAgentGraphHandshakeStreamOptions", "ALIFE_DATAAGENT_GRAPH_HANDSHAKE_STREAM_ENABLED", "ALIFE_DATAAGENT_GRAPH_HANDSHAKE_STREAM_ENDPOINT", "ALIFE_DATAAGENT_GRAPH_HANDSHAKE_STREAM_TIMEOUT_MS", "TryParseLoopbackEndpoint", "RuntimeStarted")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeNdjsonStreamClient.cs" @("DataAgentGraphHandshakeNdjsonStreamClient", "application/x-ndjson", "invalid_stream_schema", "missing_stream_final_response", "stream_progress_over_budget", "sidecar_timeout", "sidecar_unavailable")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeCoordinator.cs" @("IDataAgentGraphHandshakeStreamClient", "TryStreamHandshake", "DataAgentGraphHandshakeValidator.Validate", "PublishProgressIfAvailable(request, result, streamResult.Progress)")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs" @("CreateGraphHandshakeStreamClient", "DataAgentGraphHandshakeStreamOptions.FromEnvironment", "DataAgentGraphHandshakeNdjsonStreamClient")) -and (Test-FileMarker "tools/dataagent-graph-sidecar/app.py" @("@app.post(`"/handshake-stream`")", "StreamingResponse", "application/x-ndjson", "`"Kind`": `"Progress`"", "`"Kind`": `"FinalResponse`"")) -and (Test-FileMarker "docs/dataagent/dataagent-v3.3-ndjson-streaming-transport.md" @("DataAgent V3.3", "NDJSON streaming transport smoke", "SSE is deferred", "invalid_stream_schema", "missing_stream_final_response", "stream_progress_over_budget")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("GraphHandshakeDevSidecarStreamingTransportPresent", "default_enabled=false", "ndjson_stream=true", "buffer_until_accepted=true", "final_response_required=true", "sse_deferred=true", "csharp_bridge_authority=true", "qchat_boundary=true", "runtime_required=false"))) -Detail "V3.3 graph handshake dev sidecar NDJSON stream markers default_enabled=false ndjson_stream=true buffer_until_accepted=true final_response_required=true sse_deferred=true csharp_bridge_authority=true qchat_boundary=true runtime_required=false"
```

Change:

```powershell
$expectedRequired = 88
```

to:

```powershell
$expectedRequired = 89
```

- [ ] **Step 4: Preserve QChat engineering map count and direct-import boundary**

Inspect:

```powershell
rg -n "DataAgentGraphHandshake|DataAgentGraphSidecar|expectedRequired" tools\check-qchat-engineering-map.ps1 Tests\Alife.Test.QChat\QChatEngineeringMapRequiredV2Tests.cs
```

Preferred outcome:

- `tools/check-qchat-engineering-map.ps1` still has `$expectedRequired = 63`.
- Existing omit patterns already include `"DataAgentGraphHandshake"` and therefore cover `DataAgentGraphHandshakeStream*`.
- If `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs` uses an explicit boundary list, add `DataAgentGraphHandshakeStreamEvent`, `DataAgentGraphHandshakeStreamResult`, `DataAgentGraphHandshakeStreamOptions`, `DataAgentGraphHandshakeNdjsonStreamClient`, and `IDataAgentGraphHandshakeStreamClient` to that list.
- Do not add a new QChat required row for V3.3.

- [ ] **Step 5: Run readiness and QChat boundary checks**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests" -v:minimal
```

Expected: PASS for `DataAgentReadinessTests`.

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected output includes:

```text
PASS     GraphHandshakeDevSidecarStreamingTransportPresent
Summary: 89 required passed, 0 required missing
```

```powershell
rg -n "DataAgentGraphHandshakeStream|DataAgentGraphSidecarProgress|DataAgentGraphHandshake" sources\Alife.Function\Alife.Function.QChat
```

Expected: no matches and exit code `1`.

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected output includes:

```text
Summary: 63 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 6: Commit readiness and boundary work**

```powershell
git add sources\Alife.Function\Alife.Function.DataAgent\DataAgentReadiness.cs tools\check-dataagent-readiness.ps1 Tests\Alife.Test.DataAgent\DataAgentReadinessTests.cs Tests\Alife.Test.QChat\QChatEngineeringMapRequiredV2Tests.cs
git commit -m "Add DataAgent V3.3 stream readiness"
```

If `Tests\Alife.Test.QChat\QChatEngineeringMapRequiredV2Tests.cs` has no V3.3 diff, omit it from `git add`.

## Task 6: Final Verification

**Files:**
- Verify all changed files from Tasks 1-5.

- [ ] **Step 1: Run focused V3.3 DataAgent tests**

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeStreamOptionsTests|FullyQualifiedName~DataAgentGraphHandshakeNdjsonStreamClientTests|FullyQualifiedName~DataAgentGraphHandshakeStreamCoordinatorTests|FullyQualifiedName~DataAgentGraphHandshakeCoordinatorTests|FullyQualifiedName~DataAgentModuleServiceTests|FullyQualifiedName~DataAgentGraphHandshakeDevSidecarStubTests|FullyQualifiedName~DataAgentReadinessTests" -v:minimal
```

Expected: PASS with `0 failed` for the focused V3.3 set.

- [ ] **Step 2: Run DataAgent readiness script**

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected output includes:

```text
PASS     GraphHandshakeDevSidecarStreamingTransportPresent
Summary: 89 required passed, 0 required missing
```

- [ ] **Step 3: Run QChat source boundary scan**

```powershell
rg -n "DataAgentGraphHandshakeStream|DataAgentGraphSidecarProgress|DataAgentGraphHandshake" sources\Alife.Function\Alife.Function.QChat
```

Expected: no matches and exit code `1`.

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

Expected: restore exits `0`.

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" build Alife.slnx --no-restore -v:minimal
```

Expected: build exits `0` with `0 errors`. Existing QChat `CS0067` warnings may remain.

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore --no-build -m:1 -v:minimal
```

Expected: full solution tests exit `0` with `0 failed`.

- [ ] **Step 6: Review diff hygiene**

```powershell
git status --short --branch
git diff --check
```

Expected:

- `git diff --check` exits `0`.
- No generated `bin`, `obj`, `.codegraph`, `.worktrees`, `Outputs`, `Runtime`, `Storage`, or credentials are staged.

- [ ] **Step 7: Commit final verification changes only if a file changed during verification**

If verification caused no file edits, do not create a verification-only commit. If a legitimate source/test/doc fix was needed, commit the changed files with:

```powershell
git add <changed-source-test-doc-files>
git commit -m "Harden DataAgent V3.3 stream verification"
```

## Self-Review

- Spec coverage: The plan covers the NDJSON `/handshake-stream` endpoint, stream envelope, bounded parser, final-response requirement, buffered progress, validation-before-publish behavior, stream-specific failure reason codes, dev stub, docs, readiness, and QChat boundary.
- Non-goals preserved: The plan does not add SSE, browser-facing event streams, production LangGraph runtime behavior, automatic Python startup, live sidecar default tests, QChat production imports, SQL/checkpoint/Tool Broker/QChat/QQ/file/browser/plugin authority, or visible chat authority for the sidecar.
- Type consistency: `DataAgentGraphHandshakeStreamEvent`, `DataAgentGraphHandshakeStreamResult`, `IDataAgentGraphHandshakeStreamClient`, `DataAgentGraphHandshakeStreamOptions`, `DataAgentGraphHandshakeNdjsonStreamClient`, and `DataAgentGraphSidecarInvalidStreamException` are introduced before later tasks reference them.
- Failure consistency: `invalid_stream_schema`, `missing_stream_final_response`, `stream_progress_over_budget`, `sidecar_timeout`, and `sidecar_unavailable` are used consistently in tests, client, coordinator, readiness, and docs.
- Boundary consistency: Progress is buffered in the stream client, discarded on every failed stream outcome, and published only through `DataAgentGraphSidecarProgressBridge` after `DataAgentGraphHandshakeValidator` accepts the final response.
- Count consistency: DataAgent static readiness increases from `88` to `89`, DataAgent dynamic readiness increases from `74` to `75`, and QChat engineering map remains at `63`.
- Open-item scan: The plan contains concrete paths, code snippets, commands, expected outputs, and commit messages; it leaves no open-ended implementation work.
