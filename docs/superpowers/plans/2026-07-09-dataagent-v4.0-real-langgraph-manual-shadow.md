# DataAgent V4.0 Real LangGraph Manual Shadow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the smallest V4.0 manual LangGraph shadow integration that proves advisory value for replay and harness review without changing default DataAgent runtime behavior.

**Architecture:** V4.0 stays in the V3.28 authority model: LangGraph suggests, harness executes, C# validates, artifacts record, readiness gates, and operator decides. The implementation adds a pure C# integration model, sanitized artifact writer, manual-only harness script, and readiness checks; it does not start LangGraph, install dependencies, call sidecars from default tests, execute SQL, write state, or publish QChat text.

**Tech Stack:** .NET 9, NUnit, C# records/static validators/formatters, PowerShell manual harness, existing DataAgent V3.24 advisory contract, V3.25 manual shadow provider, V3.26 diff gate, V3.28 readiness freeze baseline.

---

## File Structure

- Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentRealLangGraphManualShadowIntegration.cs`
  - Owns the V4.0 in-memory integration model, context layering, validation, result construction, and compact formatter.
- Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentRealLangGraphManualShadowArtifactWriter.cs`
  - Writes sanitized manual artifacts to an operator-provided output directory. It does not store SQL, secrets, hidden context, or absolute paths in the artifact body.
- Create `Tests/Alife.Test.DataAgent/DataAgentV40RealLangGraphManualShadowIntegrationTests.cs`
  - Covers accepted manual advisory, fallback cases, unsafe text rejection, context layering, formatter safety, and artifact writing.
- Create `docs/dataagent/dataagent-v4.0-real-langgraph-manual-shadow-integration.md`
  - Runtime-facing V4.0 checkpoint doc with machine markers.
- Create `tools/run-dataagent-v4-manual-shadow.ps1`
  - Manual operator harness. It validates a loopback endpoint, calls a manually started LangGraph-compatible endpoint only when the operator runs the script, and writes a sanitized artifact.
- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
  - Adds one dynamic readiness check named `GraphHandshakeRealLangGraphManualShadowIntegrationPresent`.
- Modify `tools/check-dataagent-readiness.ps1`
  - Adds one static readiness check and increments `$expectedRequired` from `111` to `112`.
- Modify readiness tests:
  - `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
  - `Tests/Alife.Test.DataAgent/DataAgentV210ReadinessTests.cs`
  - `Tests/Alife.Test.DataAgent/DataAgentV216ReadinessTests.cs`
  - `Tests/Alife.Test.DataAgent/DataAgentV30ReadinessTests.cs`
  - `Tests/Alife.Test.DataAgent/DataAgentV328FinalReadinessFreezeTests.cs`
  - Expected dynamic core count becomes `97`. Static required count becomes `112`. V3.28 frozen counts stay `110/95`.

---

### Task 1: V4.0 In-Memory Manual Shadow Integration Model

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentRealLangGraphManualShadowIntegration.cs`
- Test: `Tests/Alife.Test.DataAgent/DataAgentV40RealLangGraphManualShadowIntegrationTests.cs`

- [ ] **Step 1: Write failing tests for accepted advisory, fallback, unsafe text, and formatter safety**

Add this test file:

```csharp
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV40RealLangGraphManualShadowIntegrationTests
{
    [Test]
    public void IntegrationAcceptsManualLangGraphAdvisoryThroughReplayDiffGate()
    {
        DataAgentRealLangGraphManualShadowResult result =
            DataAgentRealLangGraphManualShadowIntegration.Evaluate(NewInput());

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True, result.ReasonCode);
            Assert.That(result.ReasonCode, Is.EqualTo("real_langgraph_manual_shadow_integration_accepted"));
            Assert.That(result.SourceBaseline, Is.EqualTo("v3.28"));
            Assert.That(result.SourceReplayId, Is.EqualTo("v4.0-owner-readiness-analysis"));
            Assert.That(result.ContextLayerCount, Is.EqualTo(3));
            Assert.That(result.ManualOnly, Is.True);
            Assert.That(result.OperatorStartedRuntime, Is.True);
            Assert.That(result.LoopbackOnly, Is.True);
            Assert.That(result.AgentAdvisoryOnly, Is.True);
            Assert.That(result.HarnessExecutionAuthority, Is.True);
            Assert.That(result.CSharpValidationAuthority, Is.True);
            Assert.That(result.DefaultResultChanged, Is.False);
            Assert.That(result.FallbackRequired, Is.False);
            Assert.That(result.OperatorRequired, Is.False);
            Assert.That(result.StartsRuntime, Is.False);
            Assert.That(result.InstallsDependencies, Is.False);
            Assert.That(result.StoresSecrets, Is.False);
            Assert.That(result.StoresSql, Is.False);
            Assert.That(result.StoresHiddenContext, Is.False);
            Assert.That(result.ReasonCodes, Does.Contain("langgraph_manual_shadow_advisory_accepted"));
            Assert.That(result.ReasonCodes, Does.Contain("harness_replay_diff_gate_passed"));
        });
    }

    [Test]
    public void IntegrationFallsBackWhenManualRuntimeIsUnavailable()
    {
        DataAgentRealLangGraphManualShadowInput input = NewInput() with
        {
            OperatorStartedRuntime = false,
            ManualShadowResult = null,
            DiffGateResult = null
        };

        DataAgentRealLangGraphManualShadowResult result =
            DataAgentRealLangGraphManualShadowIntegration.Evaluate(input);

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("real_langgraph_manual_runtime_unavailable"));
            Assert.That(result.FallbackRequired, Is.True);
            Assert.That(result.OperatorRequired, Is.True);
            Assert.That(result.DefaultResultChanged, Is.False);
            Assert.That(result.StartsRuntime, Is.False);
            Assert.That(result.InstallsDependencies, Is.False);
        });
    }

    [Test]
    public void IntegrationRejectsUnsafeContextAndPreservesFallback()
    {
        DataAgentRealLangGraphManualShadowInput input = NewInput() with
        {
            ContextLayers =
            [
                new DataAgentRealLangGraphManualShadowContextLayer(
                    "layer_3_failure_excerpt",
                    "SELECT * FROM hidden_context WHERE bearer = secret")
            ]
        };

        DataAgentRealLangGraphManualShadowResult result =
            DataAgentRealLangGraphManualShadowIntegration.Evaluate(input);

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("real_langgraph_manual_shadow_unsafe_context"));
            Assert.That(result.FallbackRequired, Is.True);
            Assert.That(result.OperatorRequired, Is.True);
            Assert.That(result.DefaultResultChanged, Is.False);
            Assert.That(result.StoresSql, Is.False);
            Assert.That(result.StoresHiddenContext, Is.False);
        });
    }

    [Test]
    public void IntegrationFormatterEmitsCompactSafePacket()
    {
        DataAgentRealLangGraphManualShadowResult result =
            DataAgentRealLangGraphManualShadowIntegration.Evaluate(NewInput());

        string text = DataAgentRealLangGraphManualShadowFormatter.Format(result);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("real_langgraph_manual_shadow_integration=true"));
            Assert.That(text, Does.Contain("source_baseline=v3.28"));
            Assert.That(text, Does.Contain("manual_only=true"));
            Assert.That(text, Does.Contain("operator_started_runtime=true"));
            Assert.That(text, Does.Contain("loopback_only=true"));
            Assert.That(text, Does.Contain("agent_advisory_only=true"));
            Assert.That(text, Does.Contain("harness_execution_authority=true"));
            Assert.That(text, Does.Contain("csharp_validation_authority=true"));
            Assert.That(text, Does.Contain("default_result_changed=false"));
            Assert.That(text, Does.Contain("fallback_required=false"));
            Assert.That(text, Does.Contain("starts_runtime=false"));
            Assert.That(text, Does.Contain("installs_dependencies=false"));
            Assert.That(text, Does.Contain("stores_secrets=false"));
            Assert.That(text, Does.Contain("stores_sql=false"));
            Assert.That(text, Does.Contain("stores_hidden_context=false"));
            Assert.That(text, Does.Not.Contain("SELECT"));
            Assert.That(text, Does.Not.Contain("bearer"));
            Assert.That(text, Does.Not.Contain("hidden_context"));
        });
    }

    static DataAgentRealLangGraphManualShadowInput NewInput()
    {
        DataAgentLangGraphManualShadowResult advisory = AcceptedAdvisory("timeout_or_transport_failure");
        DataAgentHarnessReplayDiffGateResult diffGate =
            DataAgentHarnessReplayDiffGate.Evaluate(new DataAgentHarnessReplayDiffGateInput(NewReplayReport(), advisory));

        return new DataAgentRealLangGraphManualShadowInput(
            SourceReplayId: "v4.0-owner-readiness-analysis",
            OperatorStartedRuntime: true,
            LoopbackOnly: true,
            RuntimeStartedByAlife: false,
            DependenciesInstalledByAlife: false,
            SidecarCalledByAlife: false,
            ContextLayers:
            [
                new DataAgentRealLangGraphManualShadowContextLayer("layer_1_route", "fixture=v4.0-owner-readiness-analysis;route=allowed;node=plan"),
                new DataAgentRealLangGraphManualShadowContextLayer("layer_2_evidence", "reason_code=timeout_or_transport_failure;evidence_ref=replay_report:v3.20-shadow-replay-report"),
                new DataAgentRealLangGraphManualShadowContextLayer("layer_3_excerpt", "bounded_failure_excerpt=timeout_or_transport_failure")
            ],
            ManualShadowResult: advisory,
            DiffGateResult: diffGate);
    }

    static DataAgentGraphHandshakeReplayReport NewReplayReport()
    {
        DataAgentGraphHandshakeShadowComparison comparison = new(
            DataAgentGraphHandshakeShadowComparisonStatus.TimeoutOrTransportFailure,
            "timeout_or_transport_failure",
            "sidecar_disabled",
            "timeout",
            DataAgentGraphHandshakeStatus.Disabled,
            DataAgentGraphHandshakeStatus.Timeout,
            DeterministicFallbackRequired: true,
            SidecarFallbackRequired: true,
            DefaultResultChanged: false);

        DataAgentGraphHandshakeReplayFixtureResult fixture = new("timeout_fallback", comparison);
        return new DataAgentGraphHandshakeReplayReport(
            "v4.0-owner-readiness-analysis",
            [fixture],
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["timeout_or_transport_failure"] = 1
            },
            ComparisonCount: 1,
            DefaultResultChanged: false,
            Passed: true);
    }

    static DataAgentLangGraphManualShadowResult AcceptedAdvisory(string reasonCode)
    {
        return DataAgentLangGraphManualShadowProvider.Evaluate(NewRequest(reasonCode), NewPayload(NewResponse(reasonCode)));
    }

    static DataAgentAgentAdvisoryRequest NewRequest(string reasonCode)
    {
        return new DataAgentAgentAdvisoryRequest(
            ContractVersion: "v3.24",
            RunId: "v4.0-manual-shadow",
            Task: "summarize replay failure for operator review",
            CurrentState: "manual LangGraph runtime returned advisory packet",
            AllowedAdvisoryActions: ["explain_failure", "propose_manual_check", "summarize_artifact"],
            ForbiddenAuthorities: ["start_runtime", "execute_sql", "write_state", "publish_visible_answer", "override_readiness"],
            LastSuccessfulStep: "manual_shadow_capture",
            FailureCategory: reasonCode,
            EvidenceRefs: ["replay_report:v3.20-shadow-replay-report"],
            ArtifactIndexToken: "v3.23-manual-audit-bundle",
            ExpectedResponseSchema: "advisory_id,summary,reason_code,confidence,evidence_refs,proposed_next_steps,forbidden_authority_claims,requires_operator_action",
            AgentAdvisoryOnly: true,
            HarnessExecutionAuthority: true,
            CSharpValidationAuthority: true,
            DefaultResultChanged: false);
    }

    static DataAgentAgentAdvisoryResponse NewResponse(string reasonCode)
    {
        return new DataAgentAgentAdvisoryResponse(
            AdvisoryId: "lg-v40-manual",
            Summary: "manual LangGraph advisory matches replay evidence category",
            ReasonCode: reasonCode,
            Confidence: 0.81,
            EvidenceRefs: ["replay_report:v3.20-shadow-replay-report"],
            ProposedNextSteps: ["inspect_loopback", "review_replay_diff"],
            ForbiddenAuthorityClaims: [],
            RequiresOperatorAction: true,
            RequestsExecution: false,
            RequestsStateWrite: false,
            RequestsVisibleText: false,
            DefaultResultChanged: false);
    }

    static DataAgentLangGraphManualShadowPayload NewPayload(DataAgentAgentAdvisoryResponse response)
    {
        return new DataAgentLangGraphManualShadowPayload(
            ProviderName: "langgraph",
            CapturedByOperator: true,
            RuntimeStartedByAlife: false,
            DependenciesInstalledByAlife: false,
            SidecarCalledByAlife: false,
            Advisory: response);
    }
}
```

- [ ] **Step 2: Run the failing tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --filter "DataAgentV40RealLangGraphManualShadowIntegrationTests" -v:minimal
```

Expected: compile failure because `DataAgentRealLangGraphManualShadowInput`, `DataAgentRealLangGraphManualShadowContextLayer`, `DataAgentRealLangGraphManualShadowResult`, `DataAgentRealLangGraphManualShadowIntegration`, and `DataAgentRealLangGraphManualShadowFormatter` do not exist.

- [ ] **Step 3: Add the V4.0 integration implementation**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentRealLangGraphManualShadowIntegration.cs` with this complete content:

```csharp
namespace Alife.Function.DataAgent;

public sealed record DataAgentRealLangGraphManualShadowContextLayer(
    string Name,
    string Summary);

public sealed record DataAgentRealLangGraphManualShadowInput(
    string SourceReplayId,
    bool OperatorStartedRuntime,
    bool LoopbackOnly,
    bool RuntimeStartedByAlife,
    bool DependenciesInstalledByAlife,
    bool SidecarCalledByAlife,
    IReadOnlyList<DataAgentRealLangGraphManualShadowContextLayer> ContextLayers,
    DataAgentLangGraphManualShadowResult? ManualShadowResult,
    DataAgentHarnessReplayDiffGateResult? DiffGateResult);

public sealed record DataAgentRealLangGraphManualShadowResult(
    bool Accepted,
    string ReasonCode,
    string SourceBaseline,
    string SourceReplayId,
    int ContextLayerCount,
    IReadOnlyList<string> ReasonCodes,
    bool ManualOnly,
    bool OperatorStartedRuntime,
    bool LoopbackOnly,
    bool AgentAdvisoryOnly,
    bool HarnessExecutionAuthority,
    bool CSharpValidationAuthority,
    bool DefaultResultChanged,
    bool FallbackRequired,
    bool OperatorRequired,
    bool StartsRuntime,
    bool InstallsDependencies,
    bool CallsSidecar,
    bool StoresSecrets,
    bool StoresSql,
    bool StoresHiddenContext);

public static class DataAgentRealLangGraphManualShadowIntegration
{
    public const string SourceBaseline = "v3.28";

    public static DataAgentRealLangGraphManualShadowResult Evaluate(DataAgentRealLangGraphManualShadowInput? input)
    {
        if (input is null)
            return Reject("real_langgraph_manual_shadow_input_missing", "redacted", [], operatorStartedRuntime: false, loopbackOnly: false);

        if (HasSafeToken(input.SourceReplayId) == false)
            return Reject("real_langgraph_manual_shadow_replay_id_invalid", "redacted", [], input.OperatorStartedRuntime, input.LoopbackOnly);

        if (input.RuntimeStartedByAlife || input.DependenciesInstalledByAlife || input.SidecarCalledByAlife)
            return Reject("real_langgraph_manual_shadow_boundary_violation", input.SourceReplayId, [], input.OperatorStartedRuntime, input.LoopbackOnly);

        if (input.OperatorStartedRuntime == false || input.LoopbackOnly == false)
            return Reject("real_langgraph_manual_runtime_unavailable", input.SourceReplayId, [], input.OperatorStartedRuntime, input.LoopbackOnly);

        if (HasSafeContextLayers(input.ContextLayers) == false)
            return Reject("real_langgraph_manual_shadow_unsafe_context", input.SourceReplayId, [], input.OperatorStartedRuntime, input.LoopbackOnly);

        if (input.ManualShadowResult is null || input.DiffGateResult is null)
            return Reject("real_langgraph_manual_shadow_evidence_missing", input.SourceReplayId, [], input.OperatorStartedRuntime, input.LoopbackOnly);

        List<string> reasonCodes =
        [
            input.ManualShadowResult.ReasonCode,
            input.DiffGateResult.ReasonCode
        ];

        if (input.ManualShadowResult.Accepted == false)
            return Reject(input.ManualShadowResult.ReasonCode, input.SourceReplayId, reasonCodes, input.OperatorStartedRuntime, input.LoopbackOnly, input.ContextLayers.Count);

        if (input.DiffGateResult.GatePassed == false)
            return Reject(input.DiffGateResult.ReasonCode, input.SourceReplayId, reasonCodes, input.OperatorStartedRuntime, input.LoopbackOnly, input.ContextLayers.Count);

        return new DataAgentRealLangGraphManualShadowResult(
            Accepted: true,
            ReasonCode: "real_langgraph_manual_shadow_integration_accepted",
            SourceBaseline,
            input.SourceReplayId,
            input.ContextLayers.Count,
            reasonCodes,
            ManualOnly: true,
            OperatorStartedRuntime: true,
            LoopbackOnly: true,
            AgentAdvisoryOnly: true,
            HarnessExecutionAuthority: true,
            CSharpValidationAuthority: true,
            DefaultResultChanged: false,
            FallbackRequired: false,
            OperatorRequired: false,
            StartsRuntime: false,
            InstallsDependencies: false,
            CallsSidecar: false,
            StoresSecrets: false,
            StoresSql: false,
            StoresHiddenContext: false);
    }

    static DataAgentRealLangGraphManualShadowResult Reject(
        string reasonCode,
        string sourceReplayId,
        IReadOnlyList<string> reasonCodes,
        bool operatorStartedRuntime,
        bool loopbackOnly,
        int contextLayerCount = 0)
    {
        return new DataAgentRealLangGraphManualShadowResult(
            Accepted: false,
            ReasonCode: SafeToken(reasonCode),
            SourceBaseline,
            SourceReplayId: SafeToken(sourceReplayId),
            contextLayerCount,
            reasonCodes.Select(SafeToken).ToArray(),
            ManualOnly: true,
            OperatorStartedRuntime: operatorStartedRuntime,
            LoopbackOnly: loopbackOnly,
            AgentAdvisoryOnly: true,
            HarnessExecutionAuthority: true,
            CSharpValidationAuthority: true,
            DefaultResultChanged: false,
            FallbackRequired: true,
            OperatorRequired: true,
            StartsRuntime: false,
            InstallsDependencies: false,
            CallsSidecar: false,
            StoresSecrets: false,
            StoresSql: false,
            StoresHiddenContext: false);
    }

    static bool HasSafeContextLayers(IReadOnlyList<DataAgentRealLangGraphManualShadowContextLayer>? layers)
    {
        if (layers is null || layers.Count is < 1 or > 4)
            return false;

        return layers.All(layer =>
            HasSafeToken(layer.Name) &&
            string.IsNullOrWhiteSpace(layer.Summary) == false &&
            layer.Summary.Length <= 512 &&
            DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText(layer.Summary) == false);
    }

    static bool HasSafeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > DataAgentGraphHandshakeLimits.MaxReasonCodeLength)
            return false;

        if (DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText(value))
            return false;

        foreach (char current in value)
        {
            if (current is >= 'A' and <= 'Z'
                or >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '_'
                or '-'
                or '.')
            {
                continue;
            }

            return false;
        }

        return true;
    }

    static string SafeToken(string? value)
    {
        return HasSafeToken(value) ? value! : "redacted";
    }
}

public static class DataAgentRealLangGraphManualShadowFormatter
{
    public static string Format(DataAgentRealLangGraphManualShadowResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        List<string> lines =
        [
            "real_langgraph_manual_shadow_integration=true",
            $"source_baseline={SafeToken(result.SourceBaseline)}",
            $"source_replay_id={SafeToken(result.SourceReplayId)}",
            $"accepted={LowerBool(result.Accepted)}",
            $"reason_code={SafeToken(result.ReasonCode)}",
            $"context_layer_count={result.ContextLayerCount}",
            $"manual_only={LowerBool(result.ManualOnly)}",
            $"operator_started_runtime={LowerBool(result.OperatorStartedRuntime)}",
            $"loopback_only={LowerBool(result.LoopbackOnly)}",
            $"agent_advisory_only={LowerBool(result.AgentAdvisoryOnly)}",
            $"harness_execution_authority={LowerBool(result.HarnessExecutionAuthority)}",
            $"csharp_validation_authority={LowerBool(result.CSharpValidationAuthority)}",
            $"default_result_changed={LowerBool(result.DefaultResultChanged)}",
            $"fallback_required={LowerBool(result.FallbackRequired)}",
            $"operator_required={LowerBool(result.OperatorRequired)}",
            $"starts_runtime={LowerBool(result.StartsRuntime)}",
            $"installs_dependencies={LowerBool(result.InstallsDependencies)}",
            $"calls_sidecar={LowerBool(result.CallsSidecar)}",
            $"stores_secrets={LowerBool(result.StoresSecrets)}",
            $"stores_sql={LowerBool(result.StoresSql)}",
            $"stores_hidden_context={LowerBool(result.StoresHiddenContext)}"
        ];

        foreach (string reasonCode in result.ReasonCodes.Take(4))
            lines.Add($"reason_code_ref={SafeToken(reasonCode)}");

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    static string LowerBool(bool value) => value ? "true" : "false";

    static string SafeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > DataAgentGraphHandshakeLimits.MaxReasonCodeLength ||
            DataAgentGraphHandshakeUnsafeDiagnosticDetector.ContainsUnsafeText(value))
        {
            return "redacted";
        }

        foreach (char current in value)
        {
            if (current is >= 'A' and <= 'Z'
                or >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '_'
                or '-'
                or '.')
            {
                continue;
            }

            return "redacted";
        }

        return value;
    }
}
```

- [ ] **Step 4: Run the V4.0 integration tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --filter "DataAgentV40RealLangGraphManualShadowIntegrationTests" -v:minimal
```

Expected: `4 passed, 0 failed`.

- [ ] **Step 5: Commit Task 1**

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentRealLangGraphManualShadowIntegration.cs Tests/Alife.Test.DataAgent/DataAgentV40RealLangGraphManualShadowIntegrationTests.cs
git commit -m "feat(dataagent): add v4.0 manual langgraph shadow integration"
```

---

### Task 2: Sanitized Manual Artifact Writer

**Files:**
- Create: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentRealLangGraphManualShadowArtifactWriter.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV40RealLangGraphManualShadowIntegrationTests.cs`

- [ ] **Step 1: Add failing artifact writer tests**

Append these tests to `DataAgentV40RealLangGraphManualShadowIntegrationTests`:

```csharp
[Test]
public void ArtifactWriterWritesSanitizedManualPacketWithoutAbsolutePathInBody()
{
    string outputDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "v4-artifacts", Guid.NewGuid().ToString("N"));
    DataAgentRealLangGraphManualShadowResult result =
        DataAgentRealLangGraphManualShadowIntegration.Evaluate(NewInput());

    DataAgentRealLangGraphManualShadowArtifactWriteResult write =
        DataAgentRealLangGraphManualShadowArtifactWriter.Write(outputDirectory, result);

    string body = File.ReadAllText(write.FilePath);

    Assert.Multiple(() =>
    {
        Assert.That(write.Written, Is.True);
        Assert.That(write.FileName, Is.EqualTo("dataagent-v4.0-real-langgraph-manual-shadow.txt"));
        Assert.That(File.Exists(write.FilePath), Is.True);
        Assert.That(body, Does.Contain("real_langgraph_manual_shadow_integration=true"));
        Assert.That(body, Does.Contain("artifact_writer=true"));
        Assert.That(body, Does.Contain("manual_only=true"));
        Assert.That(body, Does.Not.Contain(outputDirectory));
        Assert.That(body, Does.Not.Contain("SELECT"));
        Assert.That(body, Does.Not.Contain("bearer"));
        Assert.That(body, Does.Not.Contain("hidden_context"));
    });
}

[Test]
public void ArtifactWriterRejectsMissingOutputDirectory()
{
    DataAgentRealLangGraphManualShadowArtifactWriteResult write =
        DataAgentRealLangGraphManualShadowArtifactWriter.Write(string.Empty, DataAgentRealLangGraphManualShadowIntegration.Evaluate(NewInput()));

    Assert.Multiple(() =>
    {
        Assert.That(write.Written, Is.False);
        Assert.That(write.ReasonCode, Is.EqualTo("artifact_output_directory_missing"));
        Assert.That(write.FileName, Is.EqualTo("redacted"));
    });
}
```

- [ ] **Step 2: Run the artifact tests and verify failure**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --filter "ArtifactWriterWritesSanitizedManualPacketWithoutAbsolutePathInBody|ArtifactWriterRejectsMissingOutputDirectory" -v:minimal
```

Expected: compile failure because `DataAgentRealLangGraphManualShadowArtifactWriter` and `DataAgentRealLangGraphManualShadowArtifactWriteResult` do not exist.

- [ ] **Step 3: Add the artifact writer implementation**

Create `sources/Alife.Function/Alife.Function.DataAgent/DataAgentRealLangGraphManualShadowArtifactWriter.cs`:

```csharp
namespace Alife.Function.DataAgent;

public sealed record DataAgentRealLangGraphManualShadowArtifactWriteResult(
    bool Written,
    string ReasonCode,
    string FileName,
    string FilePath);

public static class DataAgentRealLangGraphManualShadowArtifactWriter
{
    public const string FileName = "dataagent-v4.0-real-langgraph-manual-shadow.txt";

    public static DataAgentRealLangGraphManualShadowArtifactWriteResult Write(
        string outputDirectory,
        DataAgentRealLangGraphManualShadowResult? result)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return new DataAgentRealLangGraphManualShadowArtifactWriteResult(
                Written: false,
                ReasonCode: "artifact_output_directory_missing",
                FileName: "redacted",
                FilePath: string.Empty);
        }

        if (result is null)
        {
            return new DataAgentRealLangGraphManualShadowArtifactWriteResult(
                Written: false,
                ReasonCode: "artifact_result_missing",
                FileName: "redacted",
                FilePath: string.Empty);
        }

        Directory.CreateDirectory(outputDirectory);
        string filePath = Path.Combine(outputDirectory, FileName);
        string body = string.Join(
            Environment.NewLine,
            "artifact_writer=true",
            "artifact_name=dataagent-v4.0-real-langgraph-manual-shadow",
            DataAgentRealLangGraphManualShadowFormatter.Format(result).TrimEnd(),
            string.Empty);

        File.WriteAllText(filePath, body);

        return new DataAgentRealLangGraphManualShadowArtifactWriteResult(
            Written: true,
            ReasonCode: "artifact_written",
            FileName,
            filePath);
    }
}
```

- [ ] **Step 4: Run the artifact tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --filter "DataAgentV40RealLangGraphManualShadowIntegrationTests" -v:minimal
```

Expected: all V4.0 integration tests pass.

- [ ] **Step 5: Commit Task 2**

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentRealLangGraphManualShadowArtifactWriter.cs Tests/Alife.Test.DataAgent/DataAgentV40RealLangGraphManualShadowIntegrationTests.cs
git commit -m "feat(dataagent): write v4.0 manual shadow artifacts"
```

---

### Task 3: Manual-Only Harness Script

**Files:**
- Create: `tools/run-dataagent-v4-manual-shadow.ps1`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV40RealLangGraphManualShadowIntegrationTests.cs`

- [ ] **Step 1: Add static script boundary test**

Append this test to `DataAgentV40RealLangGraphManualShadowIntegrationTests`:

```csharp
[Test]
public void ManualHarnessScriptDeclaresOperatorOnlyLoopbackBoundary()
{
    string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
    string script = File.ReadAllText(Path.Combine(repoRoot, "tools", "run-dataagent-v4-manual-shadow.ps1"));

    Assert.Multiple(() =>
    {
        Assert.That(script, Does.Contain("real_langgraph_manual_shadow_integration=true"));
        Assert.That(script, Does.Contain("manual_only=true"));
        Assert.That(script, Does.Contain("operator_started_runtime=true"));
        Assert.That(script, Does.Contain("loopback_only=true"));
        Assert.That(script, Does.Contain("starts_runtime=false"));
        Assert.That(script, Does.Contain("installs_dependencies=false"));
        Assert.That(script, Does.Contain("Assert-LoopbackBaseUri"));
        Assert.That(script, Does.Contain("Invoke-WebRequest"));
        Assert.That(script, Does.Not.Contain("Start-Process"));
        Assert.That(script, Does.Not.Contain("pip install"));
        Assert.That(script, Does.Not.Contain("python -m venv"));
    });
}
```

If the test file does not already have `FindRepoRoot`, add the helper from the V3.25 test file:

```csharp
static string FindRepoRoot(string startDirectory)
{
    DirectoryInfo? directory = new(startDirectory);
    while (directory != null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Alife.slnx")) &&
            Directory.Exists(Path.Combine(directory.FullName, "docs")) &&
            Directory.Exists(Path.Combine(directory.FullName, "tools")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Could not locate repository root.");
}
```

- [ ] **Step 2: Run the script boundary test and verify failure**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --filter "ManualHarnessScriptDeclaresOperatorOnlyLoopbackBoundary" -v:minimal
```

Expected: failure because `tools/run-dataagent-v4-manual-shadow.ps1` does not exist.

- [ ] **Step 3: Add the manual harness script**

Create `tools/run-dataagent-v4-manual-shadow.ps1`:

```powershell
param(
    [string]$BaseUri = "http://127.0.0.1:8765",
    [string]$OutputDirectory = "",
    [int]$TimeoutMs = 2000
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

# real_langgraph_manual_shadow_integration=true
# manual_only=true
# operator_started_runtime=true
# loopback_only=true
# starts_runtime=false
# installs_dependencies=false

function Assert-LoopbackBaseUri {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw "BaseUri is required."
    }

    $uri = $null
    if ([System.Uri]::TryCreate($Value.TrimEnd('/'), [System.UriKind]::Absolute, [ref]$uri) -eq $false) {
        throw "BaseUri must be an absolute URI."
    }

    if ($uri.Scheme -ne "http" -and $uri.Scheme -ne "https") {
        throw "BaseUri must use http or https."
    }

    if ([string]::IsNullOrEmpty($uri.UserInfo) -eq $false) {
        throw "BaseUri must not include user information."
    }

    $allowedHosts = @("127.0.0.1", "localhost", "::1")
    if ($allowedHosts -notcontains $uri.Host) {
        throw "BaseUri must target loopback host 127.0.0.1, localhost, or ::1."
    }

    return $uri
}

function Join-SidecarUri {
    param([System.Uri]$Base, [string]$Path)
    return (New-Object System.Uri($Base, $Path))
}

function Invoke-JsonRequest {
    param(
        [string]$Method,
        [System.Uri]$Uri,
        [object]$Body = $null,
        [int]$TimeoutSeconds
    )

    $parameters = @{
        Method = $Method
        Uri = $Uri
        TimeoutSec = $TimeoutSeconds
        UseBasicParsing = $true
    }

    if ($null -ne $Body) {
        $parameters.Body = ($Body | ConvertTo-Json -Depth 16 -Compress)
        $parameters.ContentType = "application/json"
    }

    Invoke-WebRequest @parameters
}

function New-V40HandshakeRequest {
    [ordered]@{
        RequestId = "v4-manual-shadow-operator-run"
        SessionId = "v4-manual-shadow"
        TurnId = "manual-shadow-1"
        CallerId = "operator"
        GoalOrQuestion = "Summarize replay evidence for operator review."
        ScenarioContextSummary = "source_baseline=v3.28;manual_only=true"
        RouteScope = "route_present=true;route_allows_query=true;route_reason_code=route_allowed"
        QueryConstraints = "default_result_changed=false;execute_sql=false"
        NodeManifests = @(
            [ordered]@{
                NodeName = "diagnostics_router"
                Purpose = "Summarize replay evidence"
                AllowedToolNames = @("dataagent.diagnostics.progress.read")
                DeniedCapabilityMarkers = @("sql.execute", "checkpoint.write", "qchat.visible_text", "tool.execute")
                InputShape = "replay_evidence"
                OutputShape = "advisory_summary"
                BusinessTerms = @("replay", "diagnostics", "operator")
                SafetyNotes = "No execution or persistence authority"
            }
        )
        NoSqlAuthority = $true
        ReadOnly = $true
        FallbackAvailable = $true
        TraceBudgetChars = 1200
        ProgressBudget = 8
    }
}

Write-Output "DataAgent V4.0 manual LangGraph shadow"

try {
    if ($TimeoutMs -le 0) {
        throw "TimeoutMs must be greater than zero."
    }

    $base = Assert-LoopbackBaseUri $BaseUri
    $timeoutSeconds = [Math]::Max(1, [int][Math]::Ceiling($TimeoutMs / 1000.0))
    $request = New-V40HandshakeRequest

    $healthResponse = Invoke-JsonRequest -Method "GET" -Uri (Join-SidecarUri $base "/health") -TimeoutSeconds $timeoutSeconds
    $handshakeResponse = Invoke-JsonRequest -Method "POST" -Uri (Join-SidecarUri $base "/handshake") -Body $request -TimeoutSeconds $timeoutSeconds

    $artifact = [ordered]@{
        real_langgraph_manual_shadow_integration = $true
        source_baseline = "v3.28"
        manual_only = $true
        operator_started_runtime = $true
        loopback_only = $true
        starts_runtime = $false
        installs_dependencies = $false
        default_result_changed = $false
        health_status_code = [int]$healthResponse.StatusCode
        handshake_status_code = [int]$handshakeResponse.StatusCode
    }

    if ([string]::IsNullOrWhiteSpace($OutputDirectory) -eq $false) {
        New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
        $artifactPath = Join-Path $OutputDirectory "dataagent-v4.0-manual-langgraph-shadow.json"
        $artifact | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $artifactPath -Encoding UTF8
        Write-Output ("artifact={0}" -f $artifactPath)
    }

    Write-Output "PASS manual_shadow"
    exit 0
}
catch {
    Write-Output ("FALLBACK manual_shadow {0}" -f $_.Exception.Message)
    exit 1
}
```

- [ ] **Step 4: Run the script boundary test**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --filter "ManualHarnessScriptDeclaresOperatorOnlyLoopbackBoundary" -v:minimal
```

Expected: `1 passed, 0 failed`.

- [ ] **Step 5: Commit Task 3**

```powershell
git add tools/run-dataagent-v4-manual-shadow.ps1 Tests/Alife.Test.DataAgent/DataAgentV40RealLangGraphManualShadowIntegrationTests.cs
git commit -m "feat(dataagent): add v4.0 manual langgraph shadow harness"
```

---

### Task 4: V4.0 Documentation And Readiness Gates

**Files:**
- Create: `docs/dataagent/dataagent-v4.0-real-langgraph-manual-shadow-integration.md`
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
- Modify: `tools/check-dataagent-readiness.ps1`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV210ReadinessTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV216ReadinessTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV30ReadinessTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV328FinalReadinessFreezeTests.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentV40RealLangGraphManualShadowIntegrationTests.cs`

- [ ] **Step 1: Add V4.0 implementation document**

Create `docs/dataagent/dataagent-v4.0-real-langgraph-manual-shadow-integration.md`:

```markdown
# DataAgent V4.0 Real LangGraph Manual Shadow Integration

V4.0 connects real LangGraph only through a manual shadow boundary. It is operator-started, loopback-only, advisory-only, and readiness-gated.

Markers:

```text
real_langgraph_manual_shadow_integration=true
source_baseline=v3.28
manual_only=true
operator_started_runtime=true
loopback_only=true
agent_advisory_only=true
harness_execution_authority=true
csharp_validation_authority=true
default_result_changed=false
fallback_required=true
starts_runtime=false
installs_dependencies=false
stores_secrets=false
stores_sql=false
stores_hidden_context=false
```

Boundary:

- LangGraph may describe, suggest, classify, and summarize.
- LangGraph may not execute, authorize, persist, route, or publish.
- Harness owns execution.
- C# owns validation.
- Readiness gates decide whether the integration boundary is present.
- Operator decides whether a manual advisory is useful.

Default tests do not start LangGraph, install dependencies, call a sidecar, bind a port, execute SQL, mutate state, or change the default DataAgent result.
```

- [ ] **Step 2: Add a document/readiness test**

Append this test to `DataAgentV40RealLangGraphManualShadowIntegrationTests`:

```csharp
[Test]
public void V40DocumentAndReadinessDeclareManualShadowIntegrationBoundary()
{
    string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
    string doc = File.ReadAllText(Path.Combine(repoRoot, "docs", "dataagent", "dataagent-v4.0-real-langgraph-manual-shadow-integration.md"));
    string script = File.ReadAllText(Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1"));
    string source = File.ReadAllText(Path.Combine(repoRoot, "sources", "Alife.Function", "Alife.Function.DataAgent", "DataAgentReadiness.cs"));

    Assert.Multiple(() =>
    {
        Assert.That(doc, Does.Contain("real_langgraph_manual_shadow_integration=true"));
        Assert.That(doc, Does.Contain("source_baseline=v3.28"));
        Assert.That(doc, Does.Contain("manual_only=true"));
        Assert.That(doc, Does.Contain("operator_started_runtime=true"));
        Assert.That(doc, Does.Contain("loopback_only=true"));
        Assert.That(doc, Does.Contain("agent_advisory_only=true"));
        Assert.That(doc, Does.Contain("harness_execution_authority=true"));
        Assert.That(doc, Does.Contain("csharp_validation_authority=true"));
        Assert.That(doc, Does.Contain("default_result_changed=false"));
        Assert.That(doc, Does.Contain("fallback_required=true"));
        Assert.That(doc, Does.Contain("starts_runtime=false"));
        Assert.That(doc, Does.Contain("installs_dependencies=false"));
        Assert.That(doc, Does.Contain("stores_secrets=false"));
        Assert.That(doc, Does.Contain("stores_sql=false"));
        Assert.That(doc, Does.Contain("stores_hidden_context=false"));
        Assert.That(script, Does.Contain("GraphHandshakeRealLangGraphManualShadowIntegrationPresent"));
        Assert.That(script, Does.Contain("$expectedRequired = 112"));
        Assert.That(source, Does.Contain("GraphHandshakeRealLangGraphManualShadowIntegrationPresent"));
        Assert.That(source, Does.Contain("DataAgentRealLangGraphManualShadowIntegration.Evaluate"));
    });
}
```

- [ ] **Step 3: Run the readiness-related tests and verify failure**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --filter "V40DocumentAndReadinessDeclareManualShadowIntegrationBoundary|CoreReadinessChecksAllPass|StaticReadinessScriptReportsAllRequiredChecks" -v:minimal
```

Expected: failure because readiness source/script do not yet include V4.0 and counts still reference `111`/`96`.

- [ ] **Step 4: Add dynamic readiness check**

In `DataAgentReadiness.CheckCore`, after `GraphHandshakeFinalV3ReadinessFreezePresent`, add a V4.0 check that:

- Builds an accepted `DataAgentRealLangGraphManualShadowInput`.
- Calls `DataAgentRealLangGraphManualShadowIntegration.Evaluate`.
- Formats it with `DataAgentRealLangGraphManualShadowFormatter.Format`.
- Requires the V4.0 implementation doc and manual harness script markers.

Use these exact readiness detail markers:

```text
real_langgraph_manual_shadow_integration=true
source_baseline=v3.28
manual_only=true
operator_started_runtime=true
loopback_only=true
agent_advisory_only=true
harness_execution_authority=true
csharp_validation_authority=true
default_result_changed=false
fallback_required=true
starts_runtime=false
installs_dependencies=false
stores_secrets=false
stores_sql=false
stores_hidden_context=false
```

The readiness result name must be:

```text
GraphHandshakeRealLangGraphManualShadowIntegrationPresent
```

- [ ] **Step 5: Add static readiness check**

In `tools/check-dataagent-readiness.ps1`, add:

```powershell
New-Check -Group "Store" -Name "GraphHandshakeRealLangGraphManualShadowIntegrationPresent" -Passed ((Test-FileMarker "docs/dataagent/dataagent-v4.0-real-langgraph-manual-shadow-integration.md" @("real_langgraph_manual_shadow_integration=true", "source_baseline=v3.28", "manual_only=true", "operator_started_runtime=true", "loopback_only=true", "agent_advisory_only=true", "harness_execution_authority=true", "csharp_validation_authority=true", "default_result_changed=false", "fallback_required=true", "starts_runtime=false", "installs_dependencies=false", "stores_secrets=false", "stores_sql=false", "stores_hidden_context=false")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentRealLangGraphManualShadowIntegration.cs" @("DataAgentRealLangGraphManualShadowIntegration", "DataAgentRealLangGraphManualShadowFormatter", "real_langgraph_manual_shadow_integration=true", "source_baseline=", "operator_started_runtime=", "loopback_only=", "agent_advisory_only=", "harness_execution_authority=", "csharp_validation_authority=", "default_result_changed=", "fallback_required=", "starts_runtime=", "installs_dependencies=", "stores_secrets=", "stores_sql=", "stores_hidden_context=")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentRealLangGraphManualShadowArtifactWriter.cs" @("DataAgentRealLangGraphManualShadowArtifactWriter", "artifact_writer=true", "dataagent-v4.0-real-langgraph-manual-shadow.txt")) -and (Test-FileMarker "tools/run-dataagent-v4-manual-shadow.ps1" @("real_langgraph_manual_shadow_integration=true", "manual_only=true", "operator_started_runtime=true", "loopback_only=true", "starts_runtime=false", "installs_dependencies=false", "Assert-LoopbackBaseUri")) -and (Test-FileMarker "Tests/Alife.Test.DataAgent/DataAgentV40RealLangGraphManualShadowIntegrationTests.cs" @("IntegrationAcceptsManualLangGraphAdvisoryThroughReplayDiffGate", "IntegrationFallsBackWhenManualRuntimeIsUnavailable", "IntegrationRejectsUnsafeContextAndPreservesFallback", "ArtifactWriterWritesSanitizedManualPacketWithoutAbsolutePathInBody", "ManualHarnessScriptDeclaresOperatorOnlyLoopbackBoundary", "V40DocumentAndReadinessDeclareManualShadowIntegrationBoundary")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs" @("GraphHandshakeRealLangGraphManualShadowIntegrationPresent", "DataAgentRealLangGraphManualShadowIntegration.Evaluate", "real_langgraph_manual_shadow_integration=true", "source_baseline=v3.28"))) -Detail "V4.0 real LangGraph manual shadow integration markers real_langgraph_manual_shadow_integration=true source_baseline=v3.28 manual_only=true operator_started_runtime=true loopback_only=true agent_advisory_only=true harness_execution_authority=true csharp_validation_authority=true default_result_changed=false fallback_required=true starts_runtime=false installs_dependencies=false stores_secrets=false stores_sql=false stores_hidden_context=false"
```

Change:

```powershell
$expectedRequired = 111
```

to:

```powershell
$expectedRequired = 112
```

- [ ] **Step 6: Update readiness test counts**

Update these expectations:

```text
DataAgentReadinessTests.CoreReadinessChecksAllPass: Has.Count.EqualTo(97)
DataAgentReadinessTests.StaticReadinessScriptReportsAllRequiredChecks: "  Summary: 112 required passed, 0 required missing"
DataAgentReadinessTests.StaticReadinessScriptHasFailClosedCoverageForRuntimeRouteDecision: "$expectedRequired = 112"
DataAgentV210ReadinessTests.StaticReadinessScriptContainsV210Markers: "$expectedRequired = 112"
DataAgentV216ReadinessTests.StaticReadinessScriptIncludesV216OwnerDiagnosticsBridge: "$expectedRequired = 112"
DataAgentV30ReadinessTests.StaticReadinessScriptIncludesV30GraphHandshakeBoundary: "$expectedRequired = 112"
DataAgentV328FinalReadinessFreezeTests.ReadinessScriptAndDynamicReadinessDeclareFinalFreeze: "$expectedRequired = 112"
```

Keep V3.28 frozen markers unchanged:

```text
frozen_required_check_count=110
frozen_core_check_count=95
```

- [ ] **Step 7: Run focused readiness verification**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --filter "DataAgentV40RealLangGraphManualShadowIntegrationTests|DataAgentReadinessTests|DataAgentV210ReadinessTests|DataAgentV216ReadinessTests|DataAgentV30ReadinessTests|DataAgentV328FinalReadinessFreezeTests" -v:minimal
```

Expected: all selected tests pass.

- [ ] **Step 8: Run static readiness script**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected:

```text
Summary: 112 required passed, 0 required missing
```

- [ ] **Step 9: Commit Task 4**

```powershell
git add docs/dataagent/dataagent-v4.0-real-langgraph-manual-shadow-integration.md sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs tools/check-dataagent-readiness.ps1 Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs Tests/Alife.Test.DataAgent/DataAgentV210ReadinessTests.cs Tests/Alife.Test.DataAgent/DataAgentV216ReadinessTests.cs Tests/Alife.Test.DataAgent/DataAgentV30ReadinessTests.cs Tests/Alife.Test.DataAgent/DataAgentV328FinalReadinessFreezeTests.cs Tests/Alife.Test.DataAgent/DataAgentV40RealLangGraphManualShadowIntegrationTests.cs
git commit -m "feat(dataagent): add v4.0 manual shadow readiness"
```

---

### Task 5: Final Verification And Upload

**Files:**
- No source file creation.
- Verify all V4.0 files and push only to `alife-byastralfox/master`.

- [ ] **Step 1: Run DataAgent test project**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
```

Expected: all DataAgent tests pass. Live PostgreSQL tests may remain skipped when `ALIFE_DATAAGENT_POSTGRES_TEST_CONNECTION` is not set.

- [ ] **Step 2: Run static DataAgent readiness**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
```

Expected:

```text
Summary: 112 required passed, 0 required missing
```

- [ ] **Step 3: Run full solution tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore -v:minimal
```

Expected: all test projects pass. Existing live tests may remain skipped.

- [ ] **Step 4: Check whitespace and git status**

Run:

```powershell
git diff --check
git status --short --branch
```

Expected:

```text
git diff --check exits 0
local master is ahead of alife-byastralfox/master by one or more commits
```

No unstaged changes should remain after the Task 4 commit.

- [ ] **Step 5: Push to GitHub target only**

Run:

```powershell
git push alife-byastralfox HEAD:master
git ls-remote --heads alife-byastralfox master
```

Expected: remote `alife-byastralfox/master` points at the final V4.0 commit.

Do not push to `origin`. Do not touch `D:\FOXD`, `D:\FOXD\alife-service`, or ASRRAL-FOX.

---

## Self-Review

Spec coverage:

- Manual shadow only: Tasks 1, 3, and 4.
- Harness execution authority: Tasks 1, 3, and 4.
- C# validation authority: Tasks 1 and 4.
- Sanitized artifact recording: Task 2.
- Token discipline through context layers: Task 1.
- No default runtime changes: Tasks 1, 3, 4, and 5.
- Readiness gates: Task 4.
- Operator-only manual LangGraph runtime: Task 3.

Placeholder scan:

- No unfinished placeholder markers.
- No undefined task references.
- No code step depends on an unnamed type.

Type consistency:

- `DataAgentRealLangGraphManualShadowInput`, `DataAgentRealLangGraphManualShadowContextLayer`, `DataAgentRealLangGraphManualShadowResult`, `DataAgentRealLangGraphManualShadowIntegration`, `DataAgentRealLangGraphManualShadowFormatter`, `DataAgentRealLangGraphManualShadowArtifactWriter`, and `DataAgentRealLangGraphManualShadowArtifactWriteResult` are introduced before they are used by readiness.
- Static readiness count is `112`.
- Dynamic core readiness count is `97`.
- V3.28 frozen counts remain `110/95`.
