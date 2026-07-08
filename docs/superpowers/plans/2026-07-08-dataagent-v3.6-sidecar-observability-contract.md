# DataAgent V3.6 Sidecar Observability Contract Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an offline C# observability contract for DataAgent graph sidecar disabled, not-configured, unavailable, rejected, accepted, and fallback states.

**Architecture:** Add a small DataAgent-owned observability model and reason-code vocabulary, attach deterministic snapshots to graph handshake outcomes, extend diagnostics formatting, and add readiness coverage. The sidecar remains advisory, default-disabled, no-SSE, no-runtime-startup, and no-QChat-production-coupling.

**Tech Stack:** C# records/enums, NUnit, existing DataAgent graph handshake coordinator, existing DataAgent readiness script, PowerShell readiness checks.

---

## File Structure

- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeModels.cs`
  - Add `DataAgentGraphSidecarObservabilityStatus`.
  - Add `DataAgentGraphSidecarObservabilityReasonCodes`.
  - Add `DataAgentGraphSidecarObservabilityContext`.
  - Add `DataAgentGraphSidecarObservabilitySnapshot`.
  - Add optional snapshot to `DataAgentGraphHandshakeOutcome`.
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeCoordinator.cs`
  - Accept optional observability context.
  - Map handshake outcomes to V3.6 observability snapshots.
  - Preserve existing `Status` and `ReasonCode` behavior.
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`
  - Pass endpoint/runtime observability context from HTTP/stream options into the coordinator.
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeDiagnosticsFormatter.cs`
  - Render observability snapshot fields when present.
  - Keep existing formatter output compatible for callers/tests.
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
  - Add dynamic V3.6 readiness check.
  - Increase dynamic DataAgent core readiness count from `75` to `76`.
- Modify: `tools/check-dataagent-readiness.ps1`
  - Add static V3.6 readiness marker.
  - Increase static expected required count from `90` to `91`.
- Modify: `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeCoordinatorTests.cs`
  - Add observability mapping tests.
- Modify: `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeDiagnosticsFormatterTests.cs`
  - Add formatter observability tests.
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
  - Assert dynamic count `76`.
  - Assert static summary `91`.
  - Add V3.6 readiness marker tests.

Do not modify:

- `sources/Alife.Function/Alife.Function.QChat/**`
- `tools/dataagent-graph-sidecar/**`
- `tools/run-dataagent-graph-sidecar-smoke.ps1`
- Python runtime files
- upload scripts

---

### Task 1: Add Observability Model And Reason Codes

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeModels.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeCoordinatorTests.cs`

- [ ] **Step 1: Write failing tests for stable reason codes and snapshot defaults**

Append these tests near the top of `DataAgentGraphHandshakeCoordinatorTests` after the existing disabled tests:

```csharp
[Test]
public void ObservabilityReasonCodesAreStableMachineTokens()
{
    string[] reasonCodes =
    [
        DataAgentGraphSidecarObservabilityReasonCodes.Disabled,
        DataAgentGraphSidecarObservabilityReasonCodes.NotConfigured,
        DataAgentGraphSidecarObservabilityReasonCodes.RuntimeUnavailable,
        DataAgentGraphSidecarObservabilityReasonCodes.ResponseRejected,
        DataAgentGraphSidecarObservabilityReasonCodes.ProgressRejected,
        DataAgentGraphSidecarObservabilityReasonCodes.Accepted,
        DataAgentGraphSidecarObservabilityReasonCodes.FallbackUsed,
        DataAgentGraphSidecarObservabilityReasonCodes.StreamFinalResponseMissing,
        DataAgentGraphSidecarObservabilityReasonCodes.StreamFinalResponseRejected
    ];

    Assert.Multiple(() =>
    {
        Assert.That(reasonCodes, Is.Unique);
        foreach (string reasonCode in reasonCodes)
        {
            Assert.That(reasonCode, Does.Match("^[a-z][a-z0-9_]*$"), reasonCode);
            Assert.That(reasonCode, Does.StartWith("graph_sidecar_"), reasonCode);
        }
    });
}

[Test]
public void ObservabilityContextDefaultsToOfflineAndNotConfigured()
{
    DataAgentGraphSidecarObservabilityContext context = DataAgentGraphSidecarObservabilityContext.Default;

    Assert.Multiple(() =>
    {
        Assert.That(context.EndpointConfigured, Is.False);
        Assert.That(context.RuntimeStartedByAlife, Is.False);
    });
}
```

- [ ] **Step 2: Run tests and verify they fail because types do not exist**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeCoordinatorTests.Observability" -v:minimal
```

Expected: compile failure mentioning missing `DataAgentGraphSidecarObservabilityReasonCodes` or `DataAgentGraphSidecarObservabilityContext`.

- [ ] **Step 3: Add model types and optional outcome snapshot**

In `DataAgentGraphHandshakeModels.cs`, insert these types after `DataAgentGraphHandshakeProgressStatus` and before `DataAgentGraphHandshakeToolNames`:

```csharp
public enum DataAgentGraphSidecarObservabilityStatus
{
    Disabled,
    NotConfigured,
    RuntimeUnavailable,
    Rejected,
    Accepted,
    Fallback
}

public static class DataAgentGraphSidecarObservabilityReasonCodes
{
    public const string Disabled = "graph_sidecar_disabled";
    public const string NotConfigured = "graph_sidecar_not_configured";
    public const string RuntimeUnavailable = "graph_sidecar_runtime_unavailable";
    public const string ResponseRejected = "graph_sidecar_response_rejected";
    public const string ProgressRejected = "graph_sidecar_progress_rejected";
    public const string Accepted = "graph_sidecar_accepted";
    public const string FallbackUsed = "graph_sidecar_fallback_used";
    public const string StreamFinalResponseMissing = "graph_sidecar_stream_final_response_missing";
    public const string StreamFinalResponseRejected = "graph_sidecar_stream_final_response_rejected";
}

public sealed record DataAgentGraphSidecarObservabilityContext(
    bool EndpointConfigured,
    bool RuntimeStartedByAlife)
{
    public static DataAgentGraphSidecarObservabilityContext Default { get; } = new(false, false);
}

public sealed record DataAgentGraphSidecarObservabilitySnapshot(
    string ReasonCode,
    DataAgentGraphSidecarObservabilityStatus Status,
    bool SidecarEnabled,
    bool EndpointConfigured,
    bool RuntimeStartedByAlife,
    bool NetworkAttempted,
    bool Accepted,
    bool FallbackUsed,
    string SafeSummary);
```

Then replace the existing `DataAgentGraphHandshakeOutcome` declaration with this optional-parameter version so existing constructor calls continue to compile:

```csharp
public sealed record DataAgentGraphHandshakeOutcome(
    DataAgentGraphHandshakeStatus Status,
    string ReasonCode,
    bool FallbackRequired,
    DataAgentGraphHandshakeRequest? Request,
    DataAgentGraphHandshakeResponse? Response,
    DataAgentGraphHandshakeValidationResult Validation,
    DataAgentGraphSidecarObservabilitySnapshot? Observability = null);
```

- [ ] **Step 4: Run focused tests and verify they pass**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeCoordinatorTests.ObservabilityReasonCodesAreStableMachineTokens|FullyQualifiedName~DataAgentGraphHandshakeCoordinatorTests.ObservabilityContextDefaultsToOfflineAndNotConfigured" -v:minimal
```

Expected: both tests pass.

- [ ] **Step 5: Commit model work**

Run:

```powershell
git add sources\Alife.Function\Alife.Function.DataAgent\DataAgentGraphHandshakeModels.cs Tests\Alife.Test.DataAgent\DataAgentGraphHandshakeCoordinatorTests.cs
git commit -m "Add DataAgent graph sidecar observability model"
```

---

### Task 2: Attach Observability Snapshots To Coordinator Outcomes

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeCoordinator.cs`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeCoordinatorTests.cs`

- [ ] **Step 1: Write failing coordinator observability tests**

Add these tests to `DataAgentGraphHandshakeCoordinatorTests` before `ConstructorRejectsNullOptions`:

```csharp
[Test]
public void DisabledCoordinatorEmitsDisabledObservabilitySnapshot()
{
    RecordingSidecarClient sidecar = new(NewAcceptedResponse);
    DataAgentGraphHandshakeCoordinator coordinator = new(DataAgentGraphHandshakeOptions.Disabled, sidecar);

    DataAgentGraphHandshakeOutcome outcome = coordinator.TryHandshake(
        "owner",
        "Which gates failed?",
        AcceptedResult());

    Assert.Multiple(() =>
    {
        Assert.That(outcome.Observability, Is.Not.Null);
        Assert.That(outcome.Observability!.Status, Is.EqualTo(DataAgentGraphSidecarObservabilityStatus.Disabled));
        Assert.That(outcome.Observability.ReasonCode, Is.EqualTo(DataAgentGraphSidecarObservabilityReasonCodes.Disabled));
        Assert.That(outcome.Observability.SidecarEnabled, Is.False);
        Assert.That(outcome.Observability.EndpointConfigured, Is.False);
        Assert.That(outcome.Observability.NetworkAttempted, Is.False);
        Assert.That(outcome.Observability.Accepted, Is.False);
        Assert.That(outcome.Observability.FallbackUsed, Is.True);
    });
}

[Test]
public void EnabledCoordinatorWithoutEndpointEmitsNotConfiguredObservabilitySnapshot()
{
    DataAgentGraphHandshakeCoordinator coordinator = new(
        new DataAgentGraphHandshakeOptions(true),
        DisabledDataAgentGraphSidecarClient.Instance,
        observabilityContext: DataAgentGraphSidecarObservabilityContext.Default);

    DataAgentGraphHandshakeOutcome outcome = coordinator.TryHandshake(
        "owner",
        "Which gates failed?",
        AcceptedResult());

    Assert.Multiple(() =>
    {
        Assert.That(outcome.Status, Is.EqualTo(DataAgentGraphHandshakeStatus.Unavailable));
        Assert.That(outcome.Observability, Is.Not.Null);
        Assert.That(outcome.Observability!.Status, Is.EqualTo(DataAgentGraphSidecarObservabilityStatus.NotConfigured));
        Assert.That(outcome.Observability.ReasonCode, Is.EqualTo(DataAgentGraphSidecarObservabilityReasonCodes.NotConfigured));
        Assert.That(outcome.Observability.SidecarEnabled, Is.True);
        Assert.That(outcome.Observability.EndpointConfigured, Is.False);
        Assert.That(outcome.Observability.NetworkAttempted, Is.False);
        Assert.That(outcome.Observability.FallbackUsed, Is.True);
    });
}

[Test]
public void UnavailableTimeoutRejectedAndAcceptedOutcomesEmitObservabilitySnapshots()
{
    DataAgentGraphSidecarObservabilityContext configured = new(EndpointConfigured: true, RuntimeStartedByAlife: false);
    DataAgentGraphHandshakeCoordinator unavailableCoordinator = new(
        new DataAgentGraphHandshakeOptions(true),
        new ThrowingSidecarClient(new InvalidOperationException("sidecar offline")),
        observabilityContext: configured);
    DataAgentGraphHandshakeCoordinator timeoutCoordinator = new(
        new DataAgentGraphHandshakeOptions(true),
        new ThrowingSidecarClient(new TimeoutException("sidecar timeout")),
        observabilityContext: configured);
    DataAgentGraphHandshakeCoordinator rejectedCoordinator = new(
        new DataAgentGraphHandshakeOptions(true),
        new RecordingSidecarClient(request => NewAcceptedResponse(request) with { NoSqlAuthority = false }),
        observabilityContext: configured);
    DataAgentGraphHandshakeCoordinator acceptedCoordinator = new(
        new DataAgentGraphHandshakeOptions(true),
        new RecordingSidecarClient(NewAcceptedResponse),
        observabilityContext: configured);

    DataAgentGraphHandshakeOutcome unavailable = unavailableCoordinator.TryHandshake("owner", "Which gates failed?", AcceptedResult());
    DataAgentGraphHandshakeOutcome timeout = timeoutCoordinator.TryHandshake("owner", "Which gates failed?", AcceptedResult());
    DataAgentGraphHandshakeOutcome rejected = rejectedCoordinator.TryHandshake("owner", "Which gates failed?", AcceptedResult());
    DataAgentGraphHandshakeOutcome accepted = acceptedCoordinator.TryHandshake("owner", "Which gates failed?", AcceptedResult());

    Assert.Multiple(() =>
    {
        Assert.That(unavailable.Observability!.Status, Is.EqualTo(DataAgentGraphSidecarObservabilityStatus.RuntimeUnavailable));
        Assert.That(unavailable.Observability.ReasonCode, Is.EqualTo(DataAgentGraphSidecarObservabilityReasonCodes.RuntimeUnavailable));
        Assert.That(unavailable.Observability.NetworkAttempted, Is.True);
        Assert.That(timeout.Observability!.Status, Is.EqualTo(DataAgentGraphSidecarObservabilityStatus.RuntimeUnavailable));
        Assert.That(timeout.Observability.ReasonCode, Is.EqualTo(DataAgentGraphSidecarObservabilityReasonCodes.RuntimeUnavailable));
        Assert.That(rejected.Observability!.Status, Is.EqualTo(DataAgentGraphSidecarObservabilityStatus.Rejected));
        Assert.That(rejected.Observability.ReasonCode, Is.EqualTo(DataAgentGraphSidecarObservabilityReasonCodes.ResponseRejected));
        Assert.That(rejected.Observability.Accepted, Is.False);
        Assert.That(rejected.Observability.FallbackUsed, Is.True);
        Assert.That(accepted.Observability!.Status, Is.EqualTo(DataAgentGraphSidecarObservabilityStatus.Accepted));
        Assert.That(accepted.Observability.ReasonCode, Is.EqualTo(DataAgentGraphSidecarObservabilityReasonCodes.Accepted));
        Assert.That(accepted.Observability.Accepted, Is.True);
        Assert.That(accepted.Observability.FallbackUsed, Is.False);
    });
}
```

- [ ] **Step 2: Run tests and verify they fail on missing constructor/observability mapping**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeCoordinatorTests.DisabledCoordinatorEmitsDisabledObservabilitySnapshot|FullyQualifiedName~DataAgentGraphHandshakeCoordinatorTests.EnabledCoordinatorWithoutEndpointEmitsNotConfiguredObservabilitySnapshot|FullyQualifiedName~DataAgentGraphHandshakeCoordinatorTests.UnavailableTimeoutRejectedAndAcceptedOutcomesEmitObservabilitySnapshots" -v:minimal
```

Expected: fail because `DataAgentGraphHandshakeCoordinator` has no `observabilityContext` parameter or outcomes have null observability.

- [ ] **Step 3: Add coordinator observability context and mapping**

Change the coordinator primary constructor in `DataAgentGraphHandshakeCoordinator.cs` to:

```csharp
public sealed class DataAgentGraphHandshakeCoordinator(
    DataAgentGraphHandshakeOptions options,
    IDataAgentGraphSidecarClient? sidecarClient = null,
    DataAgentGraphSidecarProgressBridge? progressBridge = null,
    IDataAgentGraphHandshakeStreamClient? streamClient = null,
    DataAgentGraphSidecarObservabilityContext? observabilityContext = null)
```

Add this field near the existing readonly fields:

```csharp
readonly DataAgentGraphSidecarObservabilityContext observabilityContext =
    observabilityContext ?? InferObservabilityContext(sidecarClient, streamClient);
```

Add these helper methods near `Outcome`:

```csharp
static DataAgentGraphSidecarObservabilityContext InferObservabilityContext(
    IDataAgentGraphSidecarClient? sidecarClient,
    IDataAgentGraphHandshakeStreamClient? streamClient)
{
    return new DataAgentGraphSidecarObservabilityContext(
        EndpointConfigured: (sidecarClient is not null && sidecarClient is not DisabledDataAgentGraphSidecarClient) || streamClient is not null,
        RuntimeStartedByAlife: false);
}

DataAgentGraphHandshakeOutcome Outcome(
    DataAgentGraphHandshakeStatus status,
    string reasonCode,
    bool fallbackRequired,
    DataAgentGraphHandshakeRequest? request,
    DataAgentGraphHandshakeResponse? response = null,
    DataAgentGraphHandshakeValidationResult? validation = null,
    bool networkAttempted = false,
    string? observabilityReasonCode = null)
{
    DataAgentGraphSidecarObservabilitySnapshot observability = CreateObservabilitySnapshot(
        status,
        fallbackRequired,
        networkAttempted,
        observabilityReasonCode);

    return new DataAgentGraphHandshakeOutcome(
        status,
        reasonCode,
        fallbackRequired,
        request,
        response,
        validation ?? new DataAgentGraphHandshakeValidationResult(false, reasonCode),
        observability);
}

DataAgentGraphSidecarObservabilitySnapshot CreateObservabilitySnapshot(
    DataAgentGraphHandshakeStatus status,
    bool fallbackRequired,
    bool networkAttempted,
    string? reasonCodeOverride)
{
    bool sidecarEnabled = options.Enabled;
    DataAgentGraphSidecarObservabilityStatus observabilityStatus = MapObservabilityStatus(
        status,
        sidecarEnabled,
        observabilityContext.EndpointConfigured,
        fallbackRequired);
    string reasonCode = reasonCodeOverride ?? MapObservabilityReasonCode(observabilityStatus);
    bool accepted = observabilityStatus == DataAgentGraphSidecarObservabilityStatus.Accepted;

    return new DataAgentGraphSidecarObservabilitySnapshot(
        reasonCode,
        observabilityStatus,
        sidecarEnabled,
        observabilityContext.EndpointConfigured,
        observabilityContext.RuntimeStartedByAlife,
        networkAttempted,
        accepted,
        fallbackRequired,
        SafeSummary: reasonCode);
}

static DataAgentGraphSidecarObservabilityStatus MapObservabilityStatus(
    DataAgentGraphHandshakeStatus status,
    bool sidecarEnabled,
    bool endpointConfigured,
    bool fallbackRequired)
{
    if (sidecarEnabled == false)
        return DataAgentGraphSidecarObservabilityStatus.Disabled;

    if (endpointConfigured == false &&
        status is DataAgentGraphHandshakeStatus.Unavailable or DataAgentGraphHandshakeStatus.Disabled)
        return DataAgentGraphSidecarObservabilityStatus.NotConfigured;

    return status switch
    {
        DataAgentGraphHandshakeStatus.Accepted => DataAgentGraphSidecarObservabilityStatus.Accepted,
        DataAgentGraphHandshakeStatus.Rejected or DataAgentGraphHandshakeStatus.Invalid => DataAgentGraphSidecarObservabilityStatus.Rejected,
        DataAgentGraphHandshakeStatus.Timeout or DataAgentGraphHandshakeStatus.Unavailable => DataAgentGraphSidecarObservabilityStatus.RuntimeUnavailable,
        _ when fallbackRequired => DataAgentGraphSidecarObservabilityStatus.Fallback,
        _ => DataAgentGraphSidecarObservabilityStatus.Fallback
    };
}

static string MapObservabilityReasonCode(DataAgentGraphSidecarObservabilityStatus status)
{
    return status switch
    {
        DataAgentGraphSidecarObservabilityStatus.Disabled => DataAgentGraphSidecarObservabilityReasonCodes.Disabled,
        DataAgentGraphSidecarObservabilityStatus.NotConfigured => DataAgentGraphSidecarObservabilityReasonCodes.NotConfigured,
        DataAgentGraphSidecarObservabilityStatus.RuntimeUnavailable => DataAgentGraphSidecarObservabilityReasonCodes.RuntimeUnavailable,
        DataAgentGraphSidecarObservabilityStatus.Rejected => DataAgentGraphSidecarObservabilityReasonCodes.ResponseRejected,
        DataAgentGraphSidecarObservabilityStatus.Accepted => DataAgentGraphSidecarObservabilityReasonCodes.Accepted,
        _ => DataAgentGraphSidecarObservabilityReasonCodes.FallbackUsed
    };
}
```

Remove the old static `Outcome` helper. Update coordinator call sites as follows:

- Calls before the request is built stay `networkAttempted: false`.
- The normal sidecar accepted/rejected outcomes pass `networkAttempted: true`.
- `TimeoutException` and general exception catches pass `networkAttempted: observabilityContext.EndpointConfigured`.
- The enabled+not-configured `DisabledDataAgentGraphSidecarClient` path should return `Unavailable` as before but with `networkAttempted: false`.
- Stream missing/rejected exceptions pass `observabilityReasonCode` in Task 3 if needed.

For the general catch block, use:

```csharp
catch (Exception)
{
    return Outcome(
        DataAgentGraphHandshakeStatus.Unavailable,
        "sidecar_unavailable",
        fallbackRequired: true,
        request,
        networkAttempted: observabilityContext.EndpointConfigured);
}
```

- [ ] **Step 4: Pass observability context from module service**

In `DataAgentModuleService.cs`, update the coordinator construction:

```csharp
DataAgentGraphHandshakeCoordinator graphHandshakeCoordinator = new(
    graphHandshakeOptions,
    CreateGraphHandshakeSidecarClient(graphHandshakeOptions, graphHandshakeHttpOptions),
    new DataAgentGraphSidecarProgressBridge(progressSink),
    CreateGraphHandshakeStreamClient(graphHandshakeOptions, graphHandshakeStreamOptions),
    new DataAgentGraphSidecarObservabilityContext(
        graphHandshakeHttpOptions.Configured || graphHandshakeStreamOptions.Configured,
        graphHandshakeHttpOptions.RuntimeStarted || graphHandshakeStreamOptions.RuntimeStarted));
```

- [ ] **Step 5: Run coordinator tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeCoordinatorTests" -v:minimal
```

Expected: pass.

- [ ] **Step 6: Commit coordinator integration**

Run:

```powershell
git add sources\Alife.Function\Alife.Function.DataAgent\DataAgentGraphHandshakeCoordinator.cs sources\Alife.Function\Alife.Function.DataAgent\DataAgentModuleService.cs Tests\Alife.Test.DataAgent\DataAgentGraphHandshakeCoordinatorTests.cs
git commit -m "Attach graph sidecar observability to handshake outcomes"
```

---

### Task 3: Render Observability In Diagnostics Formatter

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeDiagnosticsFormatter.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeDiagnosticsFormatterTests.cs`

- [ ] **Step 1: Write failing formatter tests**

Add these tests to `DataAgentGraphHandshakeDiagnosticsFormatterTests` after `FormatDisabledOutcomeEmitsFallbackAndNoSqlAuthority`:

```csharp
[Test]
public void FormatOutcomeWithObservabilityEmitsStableSidecarFields()
{
    DataAgentGraphHandshakeRequest request = NewRequest();
    DataAgentGraphHandshakeOutcome outcome = new(
        DataAgentGraphHandshakeStatus.Rejected,
        "sql_authority_requested",
        FallbackRequired: true,
        request,
        Response: null,
        new DataAgentGraphHandshakeValidationResult(false, "sql_authority_requested"),
        new DataAgentGraphSidecarObservabilitySnapshot(
            DataAgentGraphSidecarObservabilityReasonCodes.ResponseRejected,
            DataAgentGraphSidecarObservabilityStatus.Rejected,
            SidecarEnabled: true,
            EndpointConfigured: true,
            RuntimeStartedByAlife: false,
            NetworkAttempted: true,
            Accepted: false,
            FallbackUsed: true,
            SafeSummary: "graph_sidecar_response_rejected"));

    string formatted = DataAgentGraphHandshakeDiagnosticsFormatter.Format(outcome);

    Assert.Multiple(() =>
    {
        Assert.That(formatted, Does.Contain("graph_sidecar status=rejected"));
        Assert.That(formatted, Does.Contain("reason=graph_sidecar_response_rejected"));
        Assert.That(formatted, Does.Contain("enabled=true"));
        Assert.That(formatted, Does.Contain("endpoint_configured=true"));
        Assert.That(formatted, Does.Contain("runtime_started_by_alife=false"));
        Assert.That(formatted, Does.Contain("network_attempted=true"));
        Assert.That(formatted, Does.Contain("accepted=false"));
        Assert.That(formatted, Does.Contain("fallback=true"));
    });
}

[Test]
public void FormatOutcomeWithObservabilityRedactsUnsafeSummary()
{
    DataAgentGraphHandshakeRequest request = NewRequest();
    DataAgentGraphHandshakeOutcome outcome = new(
        DataAgentGraphHandshakeStatus.Rejected,
        "unsafe_trace",
        FallbackRequired: true,
        request,
        Response: null,
        new DataAgentGraphHandshakeValidationResult(false, "unsafe_trace"),
        new DataAgentGraphSidecarObservabilitySnapshot(
            DataAgentGraphSidecarObservabilityReasonCodes.ResponseRejected,
            DataAgentGraphSidecarObservabilityStatus.Rejected,
            SidecarEnabled: true,
            EndpointConfigured: true,
            RuntimeStartedByAlife: false,
            NetworkAttempted: true,
            Accepted: false,
            FallbackUsed: true,
            SafeSummary: "SELECT * FROM document_index"));

    string formatted = DataAgentGraphHandshakeDiagnosticsFormatter.Format(outcome);

    Assert.Multiple(() =>
    {
        Assert.That(formatted, Does.Contain("summary=redacted"));
        Assert.That(formatted, Does.Not.Contain("SELECT"));
        Assert.That(formatted, Does.Not.Contain("document_index"));
    });
}
```

- [ ] **Step 2: Run formatter tests and verify they fail**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeDiagnosticsFormatterTests.FormatOutcomeWithObservability" -v:minimal
```

Expected: fail because formatter does not render `graph_sidecar` observability fields.

- [ ] **Step 3: Extend formatter**

In `DataAgentGraphHandshakeDiagnosticsFormatter.Format`, after the existing `runtime_required=false` line, add:

```csharp
if (outcome.Observability is not null)
{
    builder.AppendLine(FormatObservability(outcome.Observability));
}
```

Add this helper near `FormatTokens`:

```csharp
static string FormatObservability(DataAgentGraphSidecarObservabilitySnapshot snapshot)
{
    return string.Join(' ',
        "graph_sidecar",
        $"status={SafeToken(snapshot.Status.ToString().ToLowerInvariant())}",
        $"reason={SafeToken(snapshot.ReasonCode)}",
        $"enabled={Bool(snapshot.SidecarEnabled)}",
        $"endpoint_configured={Bool(snapshot.EndpointConfigured)}",
        $"runtime_started_by_alife={Bool(snapshot.RuntimeStartedByAlife)}",
        $"network_attempted={Bool(snapshot.NetworkAttempted)}",
        $"accepted={Bool(snapshot.Accepted)}",
        $"fallback={Bool(snapshot.FallbackUsed)}",
        $"summary={SafeDiagnosticText(snapshot.SafeSummary, DataAgentGraphHandshakeLimits.MaxReasonCodeLength)}");
}
```

- [ ] **Step 4: Run formatter tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeDiagnosticsFormatterTests" -v:minimal
```

Expected: pass.

- [ ] **Step 5: Commit formatter work**

Run:

```powershell
git add sources\Alife.Function\Alife.Function.DataAgent\DataAgentGraphHandshakeDiagnosticsFormatter.cs Tests\Alife.Test.DataAgent\DataAgentGraphHandshakeDiagnosticsFormatterTests.cs
git commit -m "Render graph sidecar observability diagnostics"
```

---

### Task 4: Add Readiness Marker And Static Boundary Coverage

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
- Modify: `tools/check-dataagent-readiness.ps1`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`

- [ ] **Step 1: Write failing readiness tests**

In `DataAgentReadinessTests.CoreReadinessChecksAllPass`, change:

```csharp
Assert.That(checks, Has.Count.EqualTo(75));
```

to:

```csharp
Assert.That(checks, Has.Count.EqualTo(76));
```

Add this assert inside the same `Assert.Multiple` block:

```csharp
Assert.That(checks.Select(check => check.Name), Does.Contain("GraphHandshakeDevSidecarObservabilityContractPresent"));
```

In `ReadinessScriptDefaultModeExitsZeroAndPrintsSummary`, change the expected summary string from:

```csharp
"  Summary: 90 required passed, 0 required missing"
```

to:

```csharp
"  Summary: 91 required passed, 0 required missing"
```

In `ReadinessScriptProtectsV26EvidenceDiagnosticsContract`, change:

```csharp
Assert.That(script, Does.Contain("$expectedRequired = 90"));
```

to:

```csharp
Assert.That(script, Does.Contain("$expectedRequired = 91"));
```

Add this new test after `StaticReadinessScriptContainsV34LiveSmokeHarnessMarkers`:

```csharp
[Test]
public void StaticReadinessScriptContainsV36SidecarObservabilityMarkers()
{
    string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
    string script = File.ReadAllText(Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1"));
    string declaration = FindNewCheckDeclaration(script, "GraphHandshakeDevSidecarObservabilityContractPresent");

    Assert.Multiple(() =>
    {
        Assert.That(declaration, Does.Contain("DataAgentGraphSidecarObservabilitySnapshot"));
        Assert.That(declaration, Does.Contain("DataAgentGraphSidecarObservabilityReasonCodes"));
        Assert.That(declaration, Does.Contain("graph_sidecar_disabled"));
        Assert.That(declaration, Does.Contain("graph_sidecar_not_configured"));
        Assert.That(declaration, Does.Contain("graph_sidecar_runtime_unavailable"));
        Assert.That(declaration, Does.Contain("graph_sidecar_response_rejected"));
        Assert.That(declaration, Does.Contain("graph_sidecar_accepted"));
        Assert.That(declaration, Does.Contain("sse_deferred=true"));
        Assert.That(declaration, Does.Contain("qchat_boundary=true"));
        Assert.That(declaration, Does.Contain("default_tests_live_runtime=false"));
    });
}
```

- [ ] **Step 2: Run readiness tests and verify they fail**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests" -v:minimal
```

Expected: fail because readiness count and V3.6 marker are not yet implemented.

- [ ] **Step 3: Add dynamic readiness check**

In `DataAgentReadiness.cs`, after the existing `GraphHandshakeDevSidecarLiveSmokeHarnessPresent` dynamic readiness area or immediately before `DataQueryGraphOwnerDiagnosticsPresent`, add a V3.6 check that constructs representative snapshots and verifies formatter output:

```csharp
DataAgentGraphSidecarObservabilitySnapshot graphSidecarDisabledObservability =
    graphHandshakeDisabledOutcome.Observability!;
DataAgentGraphHandshakeOutcome graphSidecarRejectedObservabilityOutcome =
    new DataAgentGraphHandshakeCoordinator(
            new DataAgentGraphHandshakeOptions(true),
            new FixedGraphSidecarClient(request => graphHandshakeSafeResponse with
            {
                RequestId = request.RequestId,
                NoSqlAuthority = false
            }),
            observabilityContext: new DataAgentGraphSidecarObservabilityContext(
                EndpointConfigured: true,
                RuntimeStartedByAlife: false))
        .TryHandshake("owner", "Which required gates failed?", CreateReadinessDataQueryGraphAcceptedResult());
DataAgentGraphHandshakeOutcome graphSidecarAcceptedObservabilityOutcome =
    new DataAgentGraphHandshakeCoordinator(
            new DataAgentGraphHandshakeOptions(true),
            new FixedGraphSidecarClient(request => graphHandshakeSafeResponse with
            {
                RequestId = request.RequestId
            }),
            observabilityContext: new DataAgentGraphSidecarObservabilityContext(
                EndpointConfigured: true,
                RuntimeStartedByAlife: false))
        .TryHandshake("owner", "Which required gates failed?", CreateReadinessDataQueryGraphAcceptedResult());
string graphSidecarRejectedObservabilityDiagnostics =
    DataAgentGraphHandshakeDiagnosticsFormatter.Format(graphSidecarRejectedObservabilityOutcome);
bool graphSidecarObservabilityModelReady =
    typeof(DataAgentGraphSidecarObservabilitySnapshot).IsClass &&
    Enum.IsDefined(typeof(DataAgentGraphSidecarObservabilityStatus), DataAgentGraphSidecarObservabilityStatus.Disabled);
bool graphSidecarObservabilityReasonCodesReady =
    string.Equals(DataAgentGraphSidecarObservabilityReasonCodes.Disabled, "graph_sidecar_disabled", StringComparison.Ordinal) &&
    string.Equals(DataAgentGraphSidecarObservabilityReasonCodes.NotConfigured, "graph_sidecar_not_configured", StringComparison.Ordinal) &&
    string.Equals(DataAgentGraphSidecarObservabilityReasonCodes.RuntimeUnavailable, "graph_sidecar_runtime_unavailable", StringComparison.Ordinal) &&
    string.Equals(DataAgentGraphSidecarObservabilityReasonCodes.ResponseRejected, "graph_sidecar_response_rejected", StringComparison.Ordinal) &&
    string.Equals(DataAgentGraphSidecarObservabilityReasonCodes.Accepted, "graph_sidecar_accepted", StringComparison.Ordinal);
bool graphSidecarObservabilityDisabledReady =
    graphSidecarDisabledObservability.Status == DataAgentGraphSidecarObservabilityStatus.Disabled &&
    string.Equals(graphSidecarDisabledObservability.ReasonCode, DataAgentGraphSidecarObservabilityReasonCodes.Disabled, StringComparison.Ordinal) &&
    graphSidecarDisabledObservability.NetworkAttempted == false &&
    graphSidecarDisabledObservability.FallbackUsed;
bool graphSidecarObservabilityRejectedReady =
    graphSidecarRejectedObservabilityOutcome.Observability?.Status == DataAgentGraphSidecarObservabilityStatus.Rejected &&
    string.Equals(graphSidecarRejectedObservabilityOutcome.Observability.ReasonCode, DataAgentGraphSidecarObservabilityReasonCodes.ResponseRejected, StringComparison.Ordinal) &&
    graphSidecarRejectedObservabilityDiagnostics.Contains("graph_sidecar status=rejected", StringComparison.Ordinal) &&
    graphSidecarRejectedObservabilityDiagnostics.Contains("fallback=true", StringComparison.Ordinal);
bool graphSidecarObservabilityAcceptedReady =
    graphSidecarAcceptedObservabilityOutcome.Observability?.Status == DataAgentGraphSidecarObservabilityStatus.Accepted &&
    string.Equals(graphSidecarAcceptedObservabilityOutcome.Observability.ReasonCode, DataAgentGraphSidecarObservabilityReasonCodes.Accepted, StringComparison.Ordinal) &&
    graphSidecarAcceptedObservabilityOutcome.Observability.Accepted &&
    graphSidecarAcceptedObservabilityOutcome.Observability.FallbackUsed == false;
bool graphSidecarObservabilityQChatBoundary =
    string.Equals(typeof(DataAgentGraphSidecarObservabilitySnapshot).Namespace, "Alife.Function.DataAgent", StringComparison.Ordinal) &&
    typeof(DataAgentGraphSidecarObservabilitySnapshot).Assembly.GetReferencedAssemblies().Any(assemblyName =>
        string.Equals(assemblyName.Name, "Alife.Function.QChat", StringComparison.Ordinal)) == false;
bool graphSidecarObservabilityReady =
    graphHandshakeDefaultOptions.Enabled == false &&
    graphSidecarObservabilityModelReady &&
    graphSidecarObservabilityReasonCodesReady &&
    graphSidecarObservabilityDisabledReady &&
    graphSidecarObservabilityRejectedReady &&
    graphSidecarObservabilityAcceptedReady &&
    graphSidecarObservabilityQChatBoundary;
checks.Add(graphSidecarObservabilityReady
    ? Pass("GraphHandshakeDevSidecarObservabilityContractPresent", "default_enabled=false;observability_model=true;reason_codes=true;fallback_reason=true;unsafe_diagnostics_redacted=true;sse_deferred=true;qchat_boundary=true;default_tests_live_runtime=false")
    : Fail("GraphHandshakeDevSidecarObservabilityContractPresent", $"default_enabled={LowerBool(graphHandshakeDefaultOptions.Enabled)};observability_model={LowerBool(graphSidecarObservabilityModelReady)};reason_codes={LowerBool(graphSidecarObservabilityReasonCodesReady)};disabled={LowerBool(graphSidecarObservabilityDisabledReady)};rejected={LowerBool(graphSidecarObservabilityRejectedReady)};accepted={LowerBool(graphSidecarObservabilityAcceptedReady)};qchat_boundary={LowerBool(graphSidecarObservabilityQChatBoundary)};runtime_required=false"));
```

If `FixedGraphSidecarClient` does not exist in `DataAgentReadiness.cs`, add this private nested helper near the existing readiness fake stream/client helpers:

```csharp
sealed class FixedGraphSidecarClient(Func<DataAgentGraphHandshakeRequest, DataAgentGraphHandshakeResponse> responseFactory)
    : IDataAgentGraphSidecarClient
{
    public DataAgentGraphHandshakeResponse TryHandshake(DataAgentGraphHandshakeRequest request)
    {
        return responseFactory(request);
    }
}
```

- [ ] **Step 4: Add static readiness marker**

In `tools/check-dataagent-readiness.ps1`, insert a new check directly after `GraphHandshakeDevSidecarLiveSmokeHarnessPresent`:

```powershell
    New-Check -Group "Store" -Name "GraphHandshakeDevSidecarObservabilityContractPresent" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeModels.cs" @("DataAgentGraphSidecarObservabilitySnapshot", "DataAgentGraphSidecarObservabilityStatus", "DataAgentGraphSidecarObservabilityReasonCodes", "graph_sidecar_disabled", "graph_sidecar_not_configured", "graph_sidecar_runtime_unavailable", "graph_sidecar_response_rejected", "graph_sidecar_accepted")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeCoordinator.cs" @("DataAgentGraphSidecarObservabilityContext", "CreateObservabilitySnapshot", "NetworkAttempted", "RuntimeStartedByAlife")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentGraphHandshakeDiagnosticsFormatter.cs" @("FormatObservability", "graph_sidecar", "endpoint_configured", "network_attempted", "summary=")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeCoordinatorTests.cs" @("DisabledCoordinatorEmitsDisabledObservabilitySnapshot", "EnabledCoordinatorWithoutEndpointEmitsNotConfiguredObservabilitySnapshot", "UnavailableTimeoutRejectedAndAcceptedOutcomesEmitObservabilitySnapshots")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentGraphHandshakeDiagnosticsFormatterTests.cs" @("FormatOutcomeWithObservabilityEmitsStableSidecarFields", "FormatOutcomeWithObservabilityRedactsUnsafeSummary")) -and (Test-FileOmitsMarker "sources/Alife.Function/Alife.Function.QChat/QChatService.cs" @("DataAgentGraphSidecarObservabilitySnapshot", "DataAgentGraphSidecarObservabilityStatus"))) -Detail "V3.6 graph handshake sidecar observability contract markers default_enabled=false observability_model=true reason_codes=true fallback_reason=true unsafe_diagnostics_redacted=true sse_deferred=true qchat_boundary=true default_tests_live_runtime=false"
```

Change:

```powershell
$expectedRequired = 90
```

to:

```powershell
$expectedRequired = 91
```

- [ ] **Step 5: Run readiness checks**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests" -v:minimal
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected:

```text
Summary: 91 required passed, 0 required missing
```

- [ ] **Step 6: Commit readiness work**

Run:

```powershell
git add sources\Alife.Function\Alife.Function.DataAgent\DataAgentReadiness.cs tools\check-dataagent-readiness.ps1 Tests\Alife.Test.DataAgent\DataAgentReadinessTests.cs
git commit -m "Add DataAgent V3.6 sidecar observability readiness"
```

---

### Task 5: Verify Boundaries And Full DataAgent Tests

**Files:**
- Read only unless verification finds a V3.6 regression.

- [ ] **Step 1: Run focused V3.6 tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentGraphHandshakeCoordinatorTests|FullyQualifiedName~DataAgentGraphHandshakeDiagnosticsFormatterTests|FullyQualifiedName~DataAgentReadinessTests" -v:minimal
```

Expected: pass.

- [ ] **Step 2: Run DataAgent readiness script**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected:

```text
PASS     GraphHandshakeDevSidecarObservabilityContractPresent
Summary: 91 required passed, 0 required missing
```

- [ ] **Step 3: Run QChat engineering map**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected:

```text
Summary: 63 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 4: Confirm QChat production source boundary**

Run:

```powershell
rg --no-ignore -n "DataAgentGraphSidecarObservabilitySnapshot|DataAgentGraphSidecarObservabilityStatus|DataAgentGraphHandshakeStream|DataAgentGraphSidecarProgress|DataAgentGraphHandshake" sources\Alife.Function\Alife.Function.QChat
```

Expected: no output, exit code `1`.

- [ ] **Step 5: Confirm no SSE/runtime startup expansion**

Run:

```powershell
rg --no-ignore -n "EventSource|text/event-stream|uvicorn app:app|Start-Process|python -m venv|pip install" sources\Alife.Function\Alife.Function.DataAgent Tests\Alife.Test.DataAgent tools\check-dataagent-readiness.ps1
```

Expected:

- No production DataAgent runtime startup or SSE implementation.
- Existing negative static assertions may mention forbidden strings only inside `Does.Not.Contain`, `Test-FileOmitsMarker`, or split string literals.
- No new V3.6 test starts Python, uvicorn, venv, pip, or SSE.

- [ ] **Step 6: Run diff hygiene**

Run:

```powershell
git diff --check
```

Expected: exit code `0`.

- [ ] **Step 7: Run full DataAgent test project**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
```

Expected: pass. Existing live PostgreSQL tests may be skipped when the environment variable is absent.

- [ ] **Step 8: Commit only if verification required fixes**

If Task 5 requires no changes, do not create a commit. If a small V3.6 verification correction is required, run:

```powershell
git add sources\Alife.Function\Alife.Function.DataAgent\DataAgentGraphHandshakeModels.cs sources\Alife.Function\Alife.Function.DataAgent\DataAgentGraphHandshakeCoordinator.cs sources\Alife.Function\Alife.Function.DataAgent\DataAgentModuleService.cs sources\Alife.Function\Alife.Function.DataAgent\DataAgentGraphHandshakeDiagnosticsFormatter.cs sources\Alife.Function\Alife.Function.DataAgent\DataAgentReadiness.cs tools\check-dataagent-readiness.ps1 Tests\Alife.Test.DataAgent\DataAgentGraphHandshakeCoordinatorTests.cs Tests\Alife.Test.DataAgent\DataAgentGraphHandshakeDiagnosticsFormatterTests.cs Tests\Alife.Test.DataAgent\DataAgentReadinessTests.cs
git commit -m "Harden DataAgent V3.6 sidecar observability verification"
```

---

## Self-Review

- Spec coverage: the plan implements the V3.6 model, reason codes, coordinator snapshots, formatter output, readiness marker, count changes, no-network tests, QChat boundary scan, no-SSE/no-runtime checks, and full DataAgent verification.
- Scope check: the plan does not touch QChat production source, Python sidecar runtime, live smoke script, upload scripts, or SSE transport.
- Count consistency: dynamic DataAgent readiness moves from `75` to `76`; static DataAgent readiness moves from `90` to `91`; QChat engineering map remains `63`.
- TDD discipline: each implementation task starts with failing tests, then minimal code, then focused verification, then commit.
- Risk note: adding an optional `Observability` parameter to `DataAgentGraphHandshakeOutcome` is chosen to avoid rewriting existing call sites while still exposing deterministic snapshots.
