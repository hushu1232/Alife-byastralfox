using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV40RealLangGraphManualShadowIntegrationTests
{
    static readonly string[] V47RequestFields =
    [
        "RequestId", "SessionId", "TurnId", "CallerId", "GoalOrQuestion",
        "ScenarioContextSummary", "RouteScope", "QueryConstraints",
        "NodeManifests", "NoSqlAuthority", "ReadOnly", "FallbackAvailable",
        "TraceBudgetChars", "ProgressBudget"
    ];

    static readonly string[] V47ManifestFields =
    [
        "NodeName", "Purpose", "AllowedToolNames", "DeniedCapabilityMarkers",
        "InputShape", "OutputShape", "BusinessTerms", "SafetyNotes"
    ];

    static readonly string[] V47ResponseFields =
    [
        "RequestId", "Accepted", "ReasonCode", "SelectedNodes", "NodeProgress",
        "TraceSummary", "ContextContribution", "FallbackRequired", "NoSqlAuthority",
        "ReadOnly", "RequestedToolNames", "RequestsCheckpointMutation", "RequestsVisibleText"
    ];

    static readonly string[] V47HealthFields =
    [
        "ok", "ready", "runtimeMode", "langGraphLoaded", "langGraphVersion",
        "graphCompiled", "contractVersion", "graphVersion", "runtimeInstanceId",
        "configurationFingerprint", "startedAtUnixSeconds"
    ];

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
            Assert.That(result.CallsSidecar, Is.False);
            Assert.That(result.StoresSecrets, Is.False);
            Assert.That(result.StoresSql, Is.False);
            Assert.That(result.StoresHiddenContext, Is.False);
            Assert.That(result.ReasonCodes, Does.Contain("langgraph_manual_shadow_advisory_accepted"));
            Assert.That(result.ReasonCodes, Does.Contain("harness_replay_diff_gate_passed"));
        });
    }

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
            Assert.That(script, Does.Contain("calls_sidecar=false"));
            Assert.That(script, Does.Contain("Assert-LoopbackBaseUri"));
            Assert.That(script, Does.Contain("Invoke-WebRequest"));
            Assert.That(script, Does.Not.Contain("Start-Process"));
            Assert.That(script, Does.Not.Contain("pip install"));
            Assert.That(script, Does.Not.Contain("python -m venv"));
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
    public void IntegrationFallsBackWhenContextLayersAreMissing()
    {
        DataAgentRealLangGraphManualShadowInput input = NewInput() with
        {
            ContextLayers = null!
        };

        DataAgentRealLangGraphManualShadowResult result =
            DataAgentRealLangGraphManualShadowIntegration.Evaluate(input);

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("real_langgraph_manual_shadow_context_missing"));
            Assert.That(result.ContextLayerCount, Is.EqualTo(0));
            Assert.That(result.FallbackRequired, Is.True);
            Assert.That(result.OperatorRequired, Is.True);
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
    public void IntegrationRejectsManualShadowResultStorageAuthorityViolation()
    {
        DataAgentRealLangGraphManualShadowInput baseline = NewInput();
        DataAgentRealLangGraphManualShadowInput input = baseline with
        {
            ManualShadowResult = baseline.ManualShadowResult! with
            {
                StoresSql = true
            }
        };

        DataAgentRealLangGraphManualShadowResult result =
            DataAgentRealLangGraphManualShadowIntegration.Evaluate(input);

        AssertBoundaryViolationFallback(result);
        Assert.That(result.ReasonCodes, Does.Contain("manual_shadow_stores_sql_violation"));
    }

    [Test]
    public void IntegrationRejectsDiffGateDefaultResultViolation()
    {
        DataAgentRealLangGraphManualShadowInput baseline = NewInput();
        DataAgentRealLangGraphManualShadowInput input = baseline with
        {
            DiffGateResult = baseline.DiffGateResult! with
            {
                DefaultResultChanged = true
            }
        };

        DataAgentRealLangGraphManualShadowResult result =
            DataAgentRealLangGraphManualShadowIntegration.Evaluate(input);

        AssertBoundaryViolationFallback(result);
        Assert.That(result.ReasonCodes, Does.Contain("diff_gate_default_result_changed_violation"));
    }

    [Test]
    public void IntegrationRejectsDiffGateValidationAuthorityViolation()
    {
        DataAgentRealLangGraphManualShadowInput baseline = NewInput();
        DataAgentRealLangGraphManualShadowInput input = baseline with
        {
            DiffGateResult = baseline.DiffGateResult! with
            {
                CSharpValidationAuthority = false
            }
        };

        DataAgentRealLangGraphManualShadowResult result =
            DataAgentRealLangGraphManualShadowIntegration.Evaluate(input);

        AssertBoundaryViolationFallback(result);
        Assert.That(result.ReasonCodes, Does.Contain("diff_gate_csharp_validation_authority_missing"));
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
            Assert.That(text, Does.Contain("calls_sidecar=false"));
            Assert.That(text, Does.Contain("stores_secrets=false"));
            Assert.That(text, Does.Contain("stores_sql=false"));
            Assert.That(text, Does.Contain("stores_hidden_context=false"));
            Assert.That(text, Does.Not.Contain("SELECT"));
            Assert.That(text, Does.Not.Contain("bearer"));
            Assert.That(text, Does.Not.Contain("SELECT * FROM hidden_context"));
        });
    }

    [Test]
    public void IntegrationFormatterRedactsUnsafeDirectResultTokens()
    {
        DataAgentRealLangGraphManualShadowResult result = NewDirectResult(
            reasonCode: "SELECT_hidden_context_bearer",
            sourceReplayId: "source\nreplay",
            reasonCodes:
            [
                "SELECT",
                "bearer",
                "hidden_context",
                "bad\ncontrol"
            ]);

        string text = DataAgentRealLangGraphManualShadowFormatter.Format(result);
        string reasonCodesLine = LineStartingWith(text, "reason_codes=");

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("reason_code=redacted"));
            Assert.That(text, Does.Contain("source_replay_id=redacted"));
            Assert.That(text, Does.Contain("reason_codes=redacted,redacted,redacted,redacted"));
            Assert.That(text, Does.Not.Contain("SELECT"));
            Assert.That(text, Does.Not.Contain("bearer"));
            Assert.That(reasonCodesLine, Does.Not.Contain("hidden_context"));
            Assert.That(text, Does.Not.Contain("bad\ncontrol"));
        });
    }

    [Test]
    public void IntegrationFormatterRedactsNullReasonCodes()
    {
        DataAgentRealLangGraphManualShadowResult result = NewDirectResult(useNullReasonCodes: true);

        string text = DataAgentRealLangGraphManualShadowFormatter.Format(result);

        Assert.That(text, Does.Contain("reason_codes=redacted"));
    }

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
            Assert.That(body, Does.Contain("calls_sidecar=false"));
            Assert.That(body, Does.Not.Contain(outputDirectory));
            Assert.That(body, Does.Not.Contain("SELECT"));
            Assert.That(body, Does.Not.Contain("bearer"));
            Assert.That(body, Does.Not.Contain("SELECT * FROM hidden_context"));
        });
    }

    [Test]
    [NonParallelizable]
    public void ArtifactWriterPersistsCSharpDerivedAggregateThroughConfiguredSqliteStoreWithoutChangingManualResult()
    {
        string databasePath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "v4-artifact-store", Guid.NewGuid().ToString("N"), "dataagent.sqlite");
        string outputDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "v4-artifacts", Guid.NewGuid().ToString("N"));
        string? previousProvider = Environment.GetEnvironmentVariable("ALIFE_DATAAGENT_STORE_PROVIDER");
        string? previousSqlitePath = Environment.GetEnvironmentVariable("ALIFE_DATAAGENT_SQLITE_PATH");
        DataAgentSchemaInitializer.Initialize(databasePath);
        IDataAgentStore store = new SqliteDataAgentStore(databasePath);
        DataAgentRealLangGraphManualShadowResult result =
            DataAgentRealLangGraphManualShadowIntegration.Evaluate(NewInput());

        try
        {
            Environment.SetEnvironmentVariable("ALIFE_DATAAGENT_STORE_PROVIDER", "sqlite");
            Environment.SetEnvironmentVariable("ALIFE_DATAAGENT_SQLITE_PATH", databasePath);

            DataAgentRealLangGraphManualShadowArtifactWriteResult write =
                DataAgentRealLangGraphManualShadowArtifactWriter.Write(outputDirectory, result);
            DataAgentLangGraphShadowArtifactReadResult read =
                store.ReadLangGraphShadowArtifactAggregate(DateTimeOffset.UtcNow);
            DataAgentLangGraphShadowArtifactAggregate aggregate = read.Aggregate!;

            Assert.Multiple(() =>
            {
                Assert.That(read.Available, Is.True);
                Assert.That(write.Written, Is.True);
                Assert.That(result.Accepted, Is.True);
                Assert.That(result.DefaultResultChanged, Is.False);
                Assert.That(aggregate.Total, Is.EqualTo(1));
                Assert.That(aggregate.Accepted, Is.EqualTo(1));
                Assert.That(aggregate.LatestReasonCode, Is.EqualTo("real_langgraph_manual_shadow_integration_accepted"));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("ALIFE_DATAAGENT_STORE_PROVIDER", previousProvider);
            Environment.SetEnvironmentVariable("ALIFE_DATAAGENT_SQLITE_PATH", previousSqlitePath);
        }
    }

    [Test]
    [NonParallelizable]
    public void RuntimeProviderClassifiesCSharpResultsAndReturnsExactUnavailableAggregateWithoutSqlite()
    {
        string databasePath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "v4-runtime-provider", Guid.NewGuid().ToString("N"), "dataagent.sqlite");
        string? previousProvider = Environment.GetEnvironmentVariable("ALIFE_DATAAGENT_STORE_PROVIDER");
        string? previousSqlitePath = Environment.GetEnvironmentVariable("ALIFE_DATAAGENT_SQLITE_PATH");
        DataAgentSchemaInitializer.Initialize(databasePath);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        try
        {
            Environment.SetEnvironmentVariable("ALIFE_DATAAGENT_STORE_PROVIDER", "sqlite");
            Environment.SetEnvironmentVariable("ALIFE_DATAAGENT_SQLITE_PATH", databasePath);

            DataAgentLangGraphShadowArtifactRuntimeProvider.RecordManualShadowResult(
                DataAgentRealLangGraphManualShadowIntegration.Evaluate(NewInput()), now);
            DataAgentLangGraphShadowArtifactRuntimeProvider.RecordManualShadowResult(
                NewDirectResult(reasonCode: "manual_shadow_rejected"), now);
            DataAgentLangGraphShadowArtifactRuntimeProvider.RecordManualShadowResult(
                NewDirectResult(reasonCode: "protocol_failure"), now);
            DataAgentLangGraphShadowArtifactRuntimeProvider.RecordManualShadowResult(
                NewDirectResult(reasonCode: "timeout_or_transport_failure"), now);
            DataAgentLangGraphShadowArtifactRuntimeProvider.RecordManualShadowResult(
                NewDirectResult(reasonCode: "manual_shadow_fallback"), now);

            string aggregate = DataAgentLangGraphShadowArtifactRuntimeProvider.ReadConfiguredAggregate(now);
            Environment.SetEnvironmentVariable("ALIFE_DATAAGENT_STORE_PROVIDER", "postgres");
            string unavailable = DataAgentLangGraphShadowArtifactRuntimeProvider.ReadConfiguredAggregate(now);
            Environment.SetEnvironmentVariable("ALIFE_DATAAGENT_STORE_PROVIDER", "sqlite");
            Environment.SetEnvironmentVariable("ALIFE_DATAAGENT_SQLITE_PATH", Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.sqlite"));
            string unavailableWithoutStore = DataAgentLangGraphShadowArtifactRuntimeProvider.ReadConfiguredAggregate(now);

            Assert.Multiple(() =>
            {
                Assert.That(aggregate, Does.Contain("total=5"));
                Assert.That(aggregate, Does.Contain("accepted=1"));
                Assert.That(aggregate, Does.Contain("gate_rejected=1"));
                Assert.That(aggregate, Does.Contain("protocol_rejected=1"));
                Assert.That(aggregate, Does.Contain("timeout=1"));
                Assert.That(aggregate, Does.Contain("fallback=1"));
                Assert.That(unavailable, Is.EqualTo(DataAgentLangGraphShadowArtifactRuntimeProvider.UnavailableAggregate));
                Assert.That(unavailableWithoutStore, Is.EqualTo(DataAgentLangGraphShadowArtifactRuntimeProvider.UnavailableAggregate));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("ALIFE_DATAAGENT_STORE_PROVIDER", previousProvider);
            Environment.SetEnvironmentVariable("ALIFE_DATAAGENT_SQLITE_PATH", previousSqlitePath);
        }
    }

    [Test]
    [NonParallelizable]
    public void ShadowArtifactBridgePersistsAcceptedBoundedInvocationThroughConfiguredSqlite()
    {
        string databasePath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "shadow-artifact-bridge", Guid.NewGuid().ToString("N"), "dataagent.sqlite");
        DataAgentSchemaInitializer.Initialize(databasePath);

        ScriptResult result = RunShadowArtifactBridge(
            databasePath,
            "--reason-code", "manual_shadow_accepted",
            "--context-layers", "3",
            "--outcome", "accepted",
            "--handshake-status", "200",
            "--health-status", "200");

        IDataAgentStore store = new SqliteDataAgentStore(databasePath);
        DataAgentLangGraphShadowArtifactReadResult read =
            store.ReadLangGraphShadowArtifactAggregate(DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            Assert.That(result.StandardOutput, Is.EqualTo("artifact_persisted=true"));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(read.Available, Is.True);
            Assert.That(read.Aggregate!.Total, Is.EqualTo(1));
            Assert.That(read.Aggregate.Accepted, Is.EqualTo(1));
            Assert.That(read.Aggregate.Fallback, Is.EqualTo(0));
        });
    }

    [Test]
    [NonParallelizable]
    public void ShadowArtifactBridgeRejectsUnknownRawJsonOptionWithoutWritingArtifact()
    {
        string databasePath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "shadow-artifact-bridge", Guid.NewGuid().ToString("N"), "dataagent.sqlite");
        DataAgentSchemaInitializer.Initialize(databasePath);

        ScriptResult result = RunShadowArtifactBridge(
            databasePath,
            "--outcome", "accepted",
            "--reason-code", "manual_shadow_accepted",
            "--health-status", "200",
            "--handshake-status", "200",
            "--context-layers", "3",
            "--raw-json", "{}");

        IDataAgentStore store = new SqliteDataAgentStore(databasePath);
        DataAgentLangGraphShadowArtifactReadResult read =
            store.ReadLangGraphShadowArtifactAggregate(DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Not.EqualTo(0));
            Assert.That(result.StandardOutput, Is.EqualTo("artifact_persisted=false"));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(read.Available, Is.True);
            Assert.That(read.Aggregate!.Total, Is.EqualTo(0));
        });
    }

    [Test]
    [NonParallelizable]
    public void ShadowArtifactBridgeRejectsNonClosedProtocolOutcomeWithoutWritingArtifact()
    {
        string databasePath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "shadow-artifact-bridge", Guid.NewGuid().ToString("N"), "dataagent.sqlite");
        DataAgentSchemaInitializer.Initialize(databasePath);

        ScriptResult result = RunShadowArtifactBridge(
            databasePath,
            "--outcome", "protocol_rejected",
            "--reason-code", "manual_shadow_protocol_rejected",
            "--health-status", "200",
            "--handshake-status", "200",
            "--context-layers", "3");

        IDataAgentStore store = new SqliteDataAgentStore(databasePath);
        DataAgentLangGraphShadowArtifactReadResult read =
            store.ReadLangGraphShadowArtifactAggregate(DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Not.EqualTo(0));
            Assert.That(result.StandardOutput, Is.EqualTo("artifact_persisted=false"));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(read.Available, Is.True);
            Assert.That(read.Aggregate!.Total, Is.EqualTo(0));
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

    [Test]
    public void ManualHarnessLoopbackValidationAcceptsIpv4LocalhostAndIpv6LoopbackOnly()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "run-dataagent-v4-manual-shadow.ps1");
        string harness = BuildPowerShellFunctionHarness(scriptPath, """
        $accepted = @(
            'http://127.0.0.1:8765',
            'http://localhost:8765',
            'http://[::1]:8765',
            'http://[0000:0000:0000:0000:0000:0000:0000:0001]:8765'
        )

        foreach ($value in $accepted) {
            $uri = Assert-LoopbackBaseUri $value
            if ($uri.IsLoopback -ne $true) {
                throw ("expected loopback for {0}" -f $value)
            }
            Write-Output ("ACCEPT {0}" -f $value)
        }

        $rejected = @(
            'http://user:pass@127.0.0.1:8765',
            'http://192.168.1.10:8765',
            'http://10.0.0.8:8765',
            'http://example.com:8765'
        )

        foreach ($value in $rejected) {
            try {
                Assert-LoopbackBaseUri $value | Out-Null
                throw ("accepted unsafe URI {0}" -f $value)
            }
            catch {
                if ($_.Exception.Message.StartsWith('accepted unsafe URI', [System.StringComparison]::Ordinal)) {
                    throw
                }
                Write-Output ("REJECT {0}" -f $value)
            }
        }
        """);

        ScriptResult result = RunPowerShellCommand(harness);
        string combinedOutput = result.StandardOutput + Environment.NewLine + result.StandardError;

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(0), combinedOutput);
            Assert.That(result.StandardOutput, Does.Contain("ACCEPT http://127.0.0.1:8765"));
            Assert.That(result.StandardOutput, Does.Contain("ACCEPT http://localhost:8765"));
            Assert.That(result.StandardOutput, Does.Contain("ACCEPT http://[::1]:8765"));
            Assert.That(result.StandardOutput, Does.Contain("ACCEPT http://[0000:0000:0000:0000:0000:0000:0000:0001]:8765"));
            Assert.That(result.StandardOutput, Does.Contain("REJECT http://user:pass@127.0.0.1:8765"));
            Assert.That(result.StandardOutput, Does.Contain("REJECT http://192.168.1.10:8765"));
            Assert.That(result.StandardOutput, Does.Contain("REJECT http://10.0.0.8:8765"));
            Assert.That(result.StandardOutput, Does.Contain("REJECT http://example.com:8765"));
        });
    }

    [Test]
    public void ManualHarnessArtifactOutputDoesNotLeakAbsoluteDirectoryAndJsonUsesMarkerSchema()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "run-dataagent-v4-manual-shadow.ps1");
        string outputDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "v40-manual-shadow", Guid.NewGuid().ToString("N"));
        string escapedOutputDirectory = EscapePowerShellSingleQuotedString(outputDirectory);
        string harness = BuildPowerShellFunctionHarness(scriptPath, $$"""
        Write-ManualShadowArtifact -OutputDirectory '{{escapedOutputDirectory}}' -HealthStatusCode 200 -HandshakeStatusCode 202
        """);

        ScriptResult result = RunPowerShellCommand(harness);
        string artifactPath = Path.Combine(outputDirectory, "dataagent-v4.0-real-langgraph-manual-shadow.json");
        string artifact = File.ReadAllText(artifactPath);

        using JsonDocument document = JsonDocument.Parse(artifact);
        string[] propertyNames = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("artifact_written=true"));
            Assert.That(result.StandardOutput, Does.Contain("artifact_file=dataagent-v4.0-real-langgraph-manual-shadow.json"));
            Assert.That(result.StandardOutput, Does.Not.Contain(outputDirectory));
            Assert.That(File.Exists(artifactPath), Is.True);
            Assert.That(propertyNames, Does.Not.Contain("source_baseline"));
            Assert.That(propertyNames, Does.Not.Contain("source_replay_id"));
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                bool validType = property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False ||
                    property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out _);
                Assert.That(validType, Is.True, $"{property.Name} must be bool or int.");
            }
        });
    }

    [Test]
    public void ManualHarnessFailureReasonIsBoundedAndSanitized()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "run-dataagent-v4-manual-shadow.ps1");
        string harness = BuildPowerShellFunctionHarness(scriptPath, """
        $message = "SELECT * FROM hidden_context WHERE bearer_token='abc123'`r`nC:\Users\hu shu\secret.txt"
        $reason = ConvertTo-ManualShadowFailureReason $message
        Write-Output ("reason={0}" -f $reason)
        if ($reason.Length -gt 80) {
            throw "reason too long"
        }
        if ($reason -match "[`r`n]") {
            throw "reason contains newline"
        }
        if ($reason -match "(?i)(SELECT|hidden_context|bearer|secret|token|C:\\|Users)") {
            throw ("reason leaked unsafe content: {0}" -f $reason)
        }
        """);

        ScriptResult result = RunPowerShellCommand(harness);
        string combinedOutput = result.StandardOutput + Environment.NewLine + result.StandardError;

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(0), combinedOutput);
            Assert.That(result.StandardOutput, Does.Contain("reason=manual_shadow_failed"));
        });
    }

    [Test]
    public void ManualHarnessFallbackOutputUsesSanitizedReason()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "run-dataagent-v4-manual-shadow.ps1");

        ScriptResult result = RunPowerShellFile(
            scriptPath,
            "-BaseUri",
            "http://user:secret@127.0.0.1:8765",
            "-TimeoutMs",
            "1");
        string fallbackLine = result.StandardOutput
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.StartsWith("FALLBACK manual_shadow ", StringComparison.Ordinal)) ?? string.Empty;

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(1), result.StandardOutput + result.StandardError);
            Assert.That(fallbackLine, Does.Contain("FALLBACK manual_shadow "));
            Assert.That(fallbackLine.Length, Is.LessThanOrEqualTo(120));
            Assert.That(fallbackLine, Does.Not.Contain("secret"));
            Assert.That(fallbackLine, Does.Not.Contain("user:"));
            Assert.That(fallbackLine, Does.Not.Contain("\r"));
            Assert.That(fallbackLine, Does.Not.Contain("\n"));
        });
    }

    [TestCase("")]
    [TestCase("{not-json")]
    [TestCase("[]")]
    [TestCase("""{"Accepted":true}""")]
    public void ManualHarnessRejectsInvalidV47HandshakeJsonOrSchema(string handshakeBody)
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "run-dataagent-v4-manual-shadow.ps1");
        string escapedHandshakeBody = EscapePowerShellSingleQuotedString(handshakeBody);
        string harness = BuildPowerShellFunctionHarness(scriptPath, $$"""
        $response = [pscustomobject]@{
            StatusCode = 200
            Content = '{{escapedHandshakeBody}}'
        }

        try {
            Assert-ManualShadowV47HandshakeResponse $response | Out-Null
            Write-Output "PASS manual_shadow"
            exit 0
        }
        catch {
            $reason = ConvertTo-ManualShadowFailureReason $_.Exception.Message
            Write-Output ("FALLBACK manual_shadow {0}" -f $reason)
            exit 1
        }
        """);

        ScriptResult result = RunPowerShellCommand(harness);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(1), result.StandardOutput + result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("FALLBACK manual_shadow manual_shadow_response_rejected"));
            Assert.That(result.StandardOutput, Does.Not.Contain("PASS manual_shadow"));
            if (string.IsNullOrEmpty(handshakeBody) == false)
                Assert.That(result.StandardOutput, Does.Not.Contain(handshakeBody));
        });
    }

    [Test]
    public void ManualHarnessRejectsV47HandshakeResponseWithoutContentPropertyUsingSanitizedFallback()
    {
        ScriptResult result = RunManualHarnessResponseValidation("""
        $response = [pscustomobject]@{
            StatusCode = 200
        }
        """);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(1), result.StandardOutput + result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("FALLBACK manual_shadow manual_shadow_response_rejected"));
            Assert.That(result.StandardOutput, Does.Not.Contain("PASS manual_shadow"));
            Assert.That(result.StandardOutput, Does.Not.Contain("property 'Content'"));
            Assert.That(result.StandardOutput, Does.Not.Contain("cannot be found"));
        });
    }

    [TestCase("$null")]
    [TestCase("123")]
    [TestCase("@{ accepted = $true }")]
    [TestCase("''")]
    public void ManualHarnessRejectsNullWrongTypeOrEmptyV47HandshakeContent(string contentExpression)
    {
        ScriptResult result = RunManualHarnessResponseValidation($$"""
        $response = [pscustomobject]@{
            StatusCode = 200
            Content = {{contentExpression}}
        }
        """);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(1), result.StandardOutput + result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("FALLBACK manual_shadow manual_shadow_response_rejected"));
            Assert.That(result.StandardOutput, Does.Not.Contain("PASS manual_shadow"));
            Assert.That(result.StandardOutput, Does.Not.Contain("property 'Content'"));
            Assert.That(result.StandardOutput, Does.Not.Contain("cannot be found"));
        });
    }

    [Test]
    public void ManualHarnessRejectsV47ResponseWithoutRequestedToolNames()
    {
        string handshakeBody = NewSafeManualHandshakeResponseJsonWithoutRequestedToolNames();
        ScriptResult result = RunManualHarnessHandshakeValidation(handshakeBody);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(1), result.StandardOutput + result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("FALLBACK manual_shadow manual_shadow_response_rejected"));
            Assert.That(result.StandardOutput, Does.Not.Contain("PASS manual_shadow"));
            Assert.That(result.StandardOutput, Does.Not.Contain(handshakeBody));
        });
    }

    [Test]
    public void ManualHarnessRejectsV47ResponseWithExtraV40Marker()
    {
        string handshakeBody = NewSafeManualHandshakeResponseJson(("ContextBudget", new { max = 1200 }));
        ScriptResult result = RunManualHarnessHandshakeValidation(handshakeBody);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(1), result.StandardOutput + result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("FALLBACK manual_shadow manual_shadow_response_rejected"));
            Assert.That(result.StandardOutput, Does.Not.Contain("PASS manual_shadow"));
            Assert.That(result.StandardOutput, Does.Not.Contain(handshakeBody));
        });
    }

    [TestCase("RequestedToolNames", new[] { "sql.execute" })]
    [TestCase("RequestsCheckpointMutation", true)]
    [TestCase("RequestsVisibleText", true)]
    [TestCase("Accepted", false)]
    [TestCase("FallbackRequired", true)]
    [TestCase("NoSqlAuthority", false)]
    [TestCase("ReadOnly", false)]
    public void ManualHarnessRejectsUnsafeV47ResponseAuthority(string propertyName, object value)
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "run-dataagent-v4-manual-shadow.ps1");
        string handshakeBody = NewSafeManualHandshakeResponseJson((propertyName, value));
        string escapedHandshakeBody = EscapePowerShellSingleQuotedString(handshakeBody);
        string harness = BuildPowerShellFunctionHarness(scriptPath, $$"""
        $response = [pscustomobject]@{
            StatusCode = 200
            Content = '{{escapedHandshakeBody}}'
        }

        try {
            Assert-ManualShadowV47HandshakeResponse $response | Out-Null
            Write-Output "PASS manual_shadow"
            exit 0
        }
        catch {
            $reason = ConvertTo-ManualShadowFailureReason $_.Exception.Message
            Write-Output ("FALLBACK manual_shadow {0}" -f $reason)
            exit 1
        }
        """);

        ScriptResult result = RunPowerShellCommand(harness);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(1), result.StandardOutput + result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("FALLBACK manual_shadow manual_shadow_response_rejected"));
            Assert.That(result.StandardOutput, Does.Not.Contain("PASS manual_shadow"));
            Assert.That(result.StandardOutput, Does.Not.Contain(propertyName));
            Assert.That(result.StandardOutput, Does.Not.Contain(handshakeBody));
        });
    }

    [Test]
    [NonParallelizable]
    public void ManualHarnessBridgeCleanupFailureDoesNotEscapeOrChangeAcceptedTerminalResult()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "run-dataagent-v4-manual-shadow.ps1");
        string bridgePath = ResolveShadowArtifactBridgePath(repoRoot);
        string escapedBridgePath = EscapePowerShellSingleQuotedString(bridgePath);
        string harness = BuildPowerShellFunctionHarness(scriptPath, $$"""
        $ArtifactBridgePath = '{{escapedBridgePath}}'

        $failingTask = [pscustomobject]@{}
        $failingTask | Add-Member -MemberType ScriptMethod -Name Wait -Value {
            param([int]$Milliseconds)
            throw "cleanup_task_failed"
        }

        $failingStream = [pscustomobject]@{ Task = $failingTask }
        $failingStream | Add-Member -MemberType ScriptMethod -Name ReadToEndAsync -Value {
            return $this.Task
        }

        $fakeProcess = [pscustomobject]@{
            StartInfo = $null
            StandardOutput = $failingStream
            StandardError = $failingStream
            ExitCode = 0
        }
        $fakeProcess | Add-Member -MemberType ScriptMethod -Name Start -Value { return $true }
        $fakeProcess | Add-Member -MemberType ScriptMethod -Name WaitForExit -Value {
            param([int]$Milliseconds)
            return $true
        }
        $fakeProcess | Add-Member -MemberType ScriptMethod -Name Dispose -Value {
            throw "cleanup_dispose_failed"
        }

        function New-Object {
            param(
                [Parameter(Position = 0)][string]$TypeName,
                [Parameter(ValueFromRemainingArguments = $true)][object[]]$Remaining
            )

            if ($TypeName -eq "System.Diagnostics.Process") {
                return $fakeProcess
            }

            return Microsoft.PowerShell.Utility\New-Object $TypeName @Remaining
        }

        try {
            Invoke-ManualShadowArtifactBridge `
                -Outcome "accepted" `
                -ReasonCode "manual_shadow_handshake_accepted" `
                -HealthStatusCode 200 `
                -HandshakeStatusCode 200
            Write-Output "after_bridge=true"
        }
        catch {
            Write-Output "bridge_escaped=true"
        }
        """);

        ScriptResult result = RunPowerShellCommand(harness);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardOutput + result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("artifact_persisted=true"));
            Assert.That(result.StandardOutput, Does.Contain("after_bridge=true"));
            Assert.That(result.StandardOutput, Does.Not.Contain("bridge_escaped=true"));
            Assert.That(result.StandardOutput, Does.Not.Contain("cleanup_task_failed"));
            Assert.That(result.StandardOutput, Does.Not.Contain("cleanup_dispose_failed"));
            Assert.That(result.StandardError, Is.Empty);
        });
    }

    [Test]
    [NonParallelizable]
    public void ManualHarnessScriptRecordsOnlyFallbackWhenLegacyArtifactWriteFails()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "run-dataagent-v4-manual-shadow.ps1");
        string bridgePath = ResolveShadowArtifactBridgePath(repoRoot);
        string databasePath = CreateManualShadowArtifactDatabasePath();
        string outputDirectory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "manual-shadow-legacy-artifact-failure",
            Guid.NewGuid().ToString("N"),
            "not-a-directory");
        Directory.CreateDirectory(Path.GetDirectoryName(outputDirectory)!);
        File.WriteAllText(outputDirectory, "fixture");

        using ManualShadowLoopbackServer server = new(NewSafeManualHandshakeResponseJson());
        ScriptResult result = RunPowerShellFile(
            scriptPath,
            CreateSqliteEnvironment(databasePath),
            "-BaseUri", server.BaseUri,
            "-ArtifactBridgePath", bridgePath,
            "-OutputDirectory", outputDirectory,
            "-TimeoutMs", "5000");

        DataAgentLangGraphShadowArtifactReadResult read = ReadManualShadowArtifactAggregate(databasePath);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(1), result.StandardOutput + result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("FALLBACK manual_shadow"));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(read.Available, Is.True);
            Assert.That(read.Aggregate!.Accepted, Is.EqualTo(0));
            Assert.That(read.Aggregate.Fallback, Is.EqualTo(1));
        });
    }

    [Test]
    [NonParallelizable]
    public void ManualHarnessScriptPersistsAcceptedOutcomeThroughDefaultArtifactBridgePath()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "run-dataagent-v4-manual-shadow.ps1");
        string databasePath = CreateManualShadowArtifactDatabasePath();

        using ManualShadowLoopbackServer server = new(NewSafeManualHandshakeResponseJson());
        ScriptResult result = RunPowerShellFile(
            scriptPath,
            CreateSqliteEnvironment(databasePath),
            "-BaseUri", server.BaseUri,
            "-TimeoutMs", "5000");

        DataAgentLangGraphShadowArtifactReadResult read = ReadManualShadowArtifactAggregate(databasePath);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardOutput + result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("PASS manual_shadow"));
            Assert.That(result.StandardOutput, Does.Contain("artifact_persisted=true"));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(server.HandshakeRequestPropertyNames, Is.EquivalentTo(V47RequestFields));
            Assert.That(read.Available, Is.True);
            Assert.That(read.Aggregate!.Accepted, Is.EqualTo(1));
            Assert.That(read.Aggregate.Fallback, Is.EqualTo(0));
            Assert.That(read.Aggregate.LatestReasonCode, Is.EqualTo("manual_shadow_handshake_accepted"));
        });
    }

    [Test]
    [NonParallelizable]
    public void ManualHarnessScriptPersistsAcceptedOutcomeThroughExplicitArtifactBridge()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "run-dataagent-v4-manual-shadow.ps1");
        string bridgePath = ResolveShadowArtifactBridgePath(repoRoot);
        string databasePath = CreateManualShadowArtifactDatabasePath();

        using ManualShadowLoopbackServer server = new(NewSafeManualHandshakeResponseJson());
        ScriptResult result = RunPowerShellFile(
            scriptPath,
            CreateSqliteEnvironment(databasePath),
            "-BaseUri", server.BaseUri,
            "-ArtifactBridgePath", bridgePath,
            "-TimeoutMs", "5000");

        DataAgentLangGraphShadowArtifactReadResult read = ReadManualShadowArtifactAggregate(databasePath);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardOutput + result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("PASS manual_shadow"));
            Assert.That(result.StandardOutput, Does.Contain("artifact_persisted=true"));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(read.Available, Is.True);
            Assert.That(read.Aggregate!.Accepted, Is.EqualTo(1));
            Assert.That(read.Aggregate.Fallback, Is.EqualTo(0));
            Assert.That(read.Aggregate.LatestReasonCode, Is.EqualTo("manual_shadow_handshake_accepted"));
        });
    }

    [Test]
    [NonParallelizable]
    public void ManualHarnessScriptPersistsRejectedHandshakeAsFallbackThroughExplicitArtifactBridge()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "run-dataagent-v4-manual-shadow.ps1");
        string bridgePath = ResolveShadowArtifactBridgePath(repoRoot);
        string databasePath = CreateManualShadowArtifactDatabasePath();
        const string unsafeHandshakeBody = "{\"accepted\":false,\"secret\":\"must_not_reach_bridge\"}";

        using ManualShadowLoopbackServer server = new(unsafeHandshakeBody);
        ScriptResult result = RunPowerShellFile(
            scriptPath,
            CreateSqliteEnvironment(databasePath),
            "-BaseUri", server.BaseUri,
            "-ArtifactBridgePath", bridgePath,
            "-TimeoutMs", "5000");

        DataAgentLangGraphShadowArtifactReadResult read = ReadManualShadowArtifactAggregate(databasePath);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(1), result.StandardOutput + result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("FALLBACK manual_shadow manual_shadow_response_rejected"));
            Assert.That(result.StandardOutput, Does.Contain("artifact_persisted=true"));
            Assert.That(result.StandardOutput, Does.Not.Contain("must_not_reach_bridge"));
            Assert.That(result.StandardOutput, Does.Not.Contain("\"secret\""));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(read.Available, Is.True);
            Assert.That(read.Aggregate!.Accepted, Is.EqualTo(0));
            Assert.That(read.Aggregate.Fallback, Is.EqualTo(1));
            Assert.That(read.Aggregate.LatestReasonCode, Is.EqualTo("manual_shadow_response_rejected"));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    [NonParallelizable]
    public void ManualHarnessScriptCompletesWithOriginalExitContractWhenArtifactBridgeHangs(bool acceptedHandshake)
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "run-dataagent-v4-manual-shadow.ps1");
        string hangingBridgePath = ResolveHangingShadowArtifactBridgePath(repoRoot);
        string databasePath = CreateManualShadowArtifactDatabasePath();
        string handshakeBody = acceptedHandshake
            ? NewSafeManualHandshakeResponseJson()
            : "{\"accepted\":false,\"secret\":\"must_not_reach_bridge\"}";

        using ManualShadowLoopbackServer server = new(handshakeBody);
        TimedScriptResult timed = RunPowerShellFileWithTimeout(
            scriptPath,
            timeoutMilliseconds: 5000,
            CreateSqliteEnvironment(databasePath),
            "-BaseUri", server.BaseUri,
            "-ArtifactBridgePath", hangingBridgePath,
            "-TimeoutMs", "5000");

        Assert.Multiple(() =>
        {
            Assert.That(timed.Completed, Is.True, timed.Result.StandardOutput + timed.Result.StandardError);
            Assert.That(timed.Result.ExitCode, Is.EqualTo(acceptedHandshake ? 0 : 1));
            Assert.That(timed.Result.StandardOutput, Does.Contain("artifact_persisted=false"));
            Assert.That(timed.Result.StandardOutput, Does.Contain(acceptedHandshake
                ? "PASS manual_shadow"
                : "FALLBACK manual_shadow manual_shadow_response_rejected"));
            Assert.That(timed.Result.StandardOutput, Does.Not.Contain("must_not_reach_bridge"));
            Assert.That(timed.Result.StandardError, Is.Empty);
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    [NonParallelizable]
    public void ManualHarnessScriptKeepsOriginalExitContractWhenArtifactBridgeIsUnavailable(bool acceptedHandshake)
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "run-dataagent-v4-manual-shadow.ps1");
        string missingBridgePath = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"), "missing.dll");
        string databasePath = CreateManualShadowArtifactDatabasePath();
        string handshakeBody = acceptedHandshake
            ? NewSafeManualHandshakeResponseJson()
            : "{\"accepted\":false,\"secret\":\"must_not_reach_bridge\"}";

        using ManualShadowLoopbackServer server = new(handshakeBody);
        ScriptResult result = RunPowerShellFile(
            scriptPath,
            CreateSqliteEnvironment(databasePath),
            "-BaseUri", server.BaseUri,
            "-ArtifactBridgePath", missingBridgePath,
            "-TimeoutMs", "5000");

        DataAgentLangGraphShadowArtifactReadResult read = ReadManualShadowArtifactAggregate(databasePath);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(acceptedHandshake ? 0 : 1), result.StandardOutput + result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("artifact_persisted=false"));
            Assert.That(result.StandardOutput, Does.Contain(acceptedHandshake
                ? "PASS manual_shadow"
                : "FALLBACK manual_shadow manual_shadow_response_rejected"));
            Assert.That(result.StandardOutput, Does.Not.Contain("must_not_reach_bridge"));
            Assert.That(result.StandardError, Is.Empty);
            Assert.That(read.Available, Is.True);
            Assert.That(read.Aggregate!.Total, Is.EqualTo(0));
        });
    }

    [Test]
    public void ManualHarnessV47ScriptPersistsAcceptedOutcomeAndSendsExactRequestEnvelope()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "run-dataagent-v4-manual-shadow.ps1");
        string outputDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "v40-manual-shadow", Guid.NewGuid().ToString("N"));

        using ManualShadowLoopbackServer server = new(NewSafeManualHandshakeResponseJson());
        ScriptResult result = RunPowerShellFile(
            scriptPath,
            "-BaseUri",
            server.BaseUri,
            "-OutputDirectory",
            outputDirectory,
            "-TimeoutMs",
            "5000");

        string artifactPath = Path.Combine(outputDirectory, "dataagent-v4.0-real-langgraph-manual-shadow.json");
        Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardOutput + result.StandardError);
        Assert.That(File.Exists(artifactPath), Is.True, result.StandardOutput + result.StandardError);

        string artifact = File.ReadAllText(artifactPath);
        using JsonDocument document = JsonDocument.Parse(artifact);
        string[] propertyNames = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(result.StandardOutput, Does.Contain("PASS manual_shadow"));
            Assert.That(result.StandardOutput, Does.Contain("handshake_validated=true"));
            Assert.That(result.StandardOutput, Does.Not.Contain("accepted"));
            Assert.That(result.StandardOutput, Does.Not.Contain(outputDirectory));
            Assert.That(server.HandshakeRequestContentLength, Is.GreaterThan(0));
            Assert.That(server.HandshakeRequestBodyBytesDrained, Is.EqualTo(server.HandshakeRequestContentLength));
            Assert.That(server.HandshakeRequestPropertyNames, Is.EquivalentTo(V47RequestFields));
            Assert.That(File.Exists(artifactPath), Is.True);
            Assert.That(propertyNames, Does.Contain("handshake_validated"));
            Assert.That(propertyNames, Does.Not.Contain("source_baseline"));
            Assert.That(propertyNames, Does.Not.Contain("source_replay_id"));
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                bool validType = property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False ||
                    property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out _);
                Assert.That(validType, Is.True, $"{property.Name} must be bool or int.");
            }
        });
    }

    [Test]
    public void ManualHarnessBuildsExactV47RequestWithoutV40ContextEnvelope()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "run-dataagent-v4-manual-shadow.ps1");
        string harness = BuildPowerShellFunctionHarness(scriptPath, """
        $request = New-V47HandshakeRequest
        $json = $request | ConvertTo-Json -Depth 16 -Compress
        Write-Output $json
        """);

        ScriptResult result = RunPowerShellCommand(harness);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            using JsonDocument document = JsonDocument.Parse(result.StandardOutput);
            JsonElement request = document.RootElement;
            string[] propertyNames = request.EnumerateObject().Select(property => property.Name).ToArray();
            Assert.That(propertyNames, Is.EquivalentTo(V47RequestFields));
            Assert.That(propertyNames, Does.Not.Contain("ContextBudget"));
            Assert.That(propertyNames, Does.Not.Contain("ContextLayers"));
            Assert.That(request.GetProperty("RequestId").GetString(), Is.EqualTo("v4-manual-shadow-operator-run"));
            Assert.That(request.GetProperty("SessionId").GetString(), Is.EqualTo("v4-manual-shadow"));
            Assert.That(request.GetProperty("TurnId").GetString(), Is.EqualTo("manual-shadow-1"));
            Assert.That(request.GetProperty("CallerId").GetString(), Is.EqualTo("operator"));
            Assert.That(request.GetProperty("GoalOrQuestion").GetString(), Is.EqualTo("Summarize replay evidence for operator review."));
            Assert.That(request.GetProperty("ScenarioContextSummary").GetString(), Is.EqualTo("scenario_context=manual_shadow;source_baseline=v3.28"));
            Assert.That(request.GetProperty("RouteScope").GetString(), Is.EqualTo("route_present=true;route_allows_query=true"));
            Assert.That(request.GetProperty("QueryConstraints").GetString(), Is.EqualTo("default_result_changed=false;execute_sql=false"));
            Assert.That(request.GetProperty("NoSqlAuthority").GetBoolean(), Is.True);
            Assert.That(request.GetProperty("ReadOnly").GetBoolean(), Is.True);
            Assert.That(request.GetProperty("FallbackAvailable").GetBoolean(), Is.True);
            Assert.That(request.GetProperty("TraceBudgetChars").GetInt32(), Is.EqualTo(1200));
            Assert.That(request.GetProperty("ProgressBudget").GetInt32(), Is.EqualTo(8));

            JsonElement manifests = request.GetProperty("NodeManifests");
            Assert.That(manifests.GetArrayLength(), Is.EqualTo(1));
            JsonElement manifest = manifests[0];
            Assert.That(manifest.EnumerateObject().Select(property => property.Name), Is.EquivalentTo(V47ManifestFields));
            Assert.That(manifest.GetProperty("NodeName").GetString(), Is.EqualTo("diagnostics_router"));
            Assert.That(manifest.GetProperty("Purpose").GetString(), Is.EqualTo("Summarize replay evidence"));
            Assert.That(manifest.GetProperty("AllowedToolNames").EnumerateArray().Select(value => value.GetString()),
                Is.EquivalentTo(new[] { "dataagent.diagnostics.progress.read" }));
            Assert.That(manifest.GetProperty("DeniedCapabilityMarkers").EnumerateArray().Select(value => value.GetString()),
                Is.EquivalentTo(new[] { "sql.execute", "checkpoint.write", "qchat.visible_text", "tool.execute" }));
            Assert.That(manifest.GetProperty("InputShape").GetString(), Is.EqualTo("replay_evidence"));
            Assert.That(manifest.GetProperty("OutputShape").GetString(), Is.EqualTo("advisory_summary"));
            Assert.That(manifest.GetProperty("BusinessTerms").EnumerateArray().Select(value => value.GetString()),
                Is.EquivalentTo(new[] { "replay", "diagnostics", "operator" }));
            Assert.That(manifest.GetProperty("SafetyNotes").GetString(), Is.EqualTo("No execution or persistence authority"));
            Assert.That(result.StandardOutput, Does.Not.Contain("SELECT"));
            Assert.That(result.StandardOutput, Does.Not.Contain("hidden_context"));
            Assert.That(result.StandardOutput, Does.Not.Contain("bearer"));
            Assert.That(result.StandardOutput, Does.Not.Contain("password"));
        });
    }

    [Test]
    public void ManualHarnessRejectsV47ResponseWithArbitraryExtraField()
    {
        const string unsafeValue = "must_not_be_accepted";
        string handshakeBody = NewSafeManualHandshakeResponseJson(("unexpected_response_field", unsafeValue));
        ScriptResult result = RunManualHarnessHandshakeValidation(handshakeBody);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(1), result.StandardOutput + result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("FALLBACK manual_shadow manual_shadow_response_rejected"));
            Assert.That(result.StandardOutput, Does.Not.Contain("PASS manual_shadow"));
            Assert.That(result.StandardOutput, Does.Not.Contain(unsafeValue));
            Assert.That(result.StandardOutput, Does.Not.Contain(handshakeBody));
        });
    }

    [Test]
    public void ManualHarnessAcceptsExactV47HealthAndHandshakeContracts()
    {
        string healthBody = NewSafeManualV47HealthJson();
        string handshakeBody = NewSafeManualHandshakeResponseJson();
        string escapedHealthBody = EscapePowerShellSingleQuotedString(healthBody);
        string escapedHandshakeBody = EscapePowerShellSingleQuotedString(handshakeBody);
        string harness = BuildPowerShellFunctionHarness(
            Path.Combine(FindRepoRoot(TestContext.CurrentContext.TestDirectory), "tools", "run-dataagent-v4-manual-shadow.ps1"),
            $$"""
            $health = [pscustomobject]@{ StatusCode = 200; Content = '{{escapedHealthBody}}' }
            $handshake = [pscustomobject]@{ StatusCode = 200; Content = '{{escapedHandshakeBody}}' }
            Assert-ManualShadowV47HealthResponse $health | Out-Null
            Assert-ManualShadowV47HandshakeResponse $handshake | Out-Null
            Write-Output "PASS manual_shadow"
            """);

        ScriptResult result = RunPowerShellCommand(harness);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardOutput + result.StandardError);
            Assert.That(result.StandardOutput, Is.EqualTo("PASS manual_shadow" + Environment.NewLine));
            Assert.That(JsonDocument.Parse(healthBody).RootElement.EnumerateObject().Select(property => property.Name), Is.EquivalentTo(V47HealthFields));
            Assert.That(JsonDocument.Parse(handshakeBody).RootElement.EnumerateObject().Select(property => property.Name), Is.EquivalentTo(V47ResponseFields));
        });
    }

    [Test]
    public void ManualHarnessRejectsV47HealthWithNonOkStatusWithoutLeakingBody()
    {
        string healthBody = NewSafeManualV47HealthJson();
        string escapedHealthBody = EscapePowerShellSingleQuotedString(healthBody);
        ScriptResult result = RunPowerShellCommand(BuildPowerShellFunctionHarness(
            Path.Combine(FindRepoRoot(TestContext.CurrentContext.TestDirectory), "tools", "run-dataagent-v4-manual-shadow.ps1"),
            $$"""
            $response = [pscustomobject]@{ StatusCode = 202; Content = '{{escapedHealthBody}}' }
            try {
                Assert-ManualShadowV47HealthResponse $response | Out-Null
                Write-Output "PASS manual_shadow"
                exit 0
            }
            catch {
                $reason = ConvertTo-ManualShadowFailureReason $_.Exception.Message
                Write-Output ("FALLBACK manual_shadow {0}" -f $reason)
                exit 1
            }
            """));

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(1), result.StandardOutput + result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("FALLBACK manual_shadow manual_shadow_response_rejected"));
            Assert.That(result.StandardOutput, Does.Not.Contain("PASS manual_shadow"));
            Assert.That(result.StandardOutput, Does.Not.Contain(healthBody));
        });
    }

    [TestCase("contractVersion", "v4.0")]
    [TestCase("ready", false)]
    [TestCase("runtimeMode", "deterministic-stub")]
    [TestCase("langGraphLoaded", false)]
    [TestCase("langGraphVersion", "0.3.33")]
    [TestCase("graphCompiled", false)]
    [TestCase("graphVersion", "dataagent-advisory-v0")]
    [TestCase("unsafe_extra", "must_not_be_accepted")]
    public void ManualHarnessRejectsUnsafeOrNonExactV47HealthWithoutLeakingBody(string propertyName, object value)
    {
        string healthBody = NewSafeManualV47HealthJson((propertyName, value));
        string escapedHealthBody = EscapePowerShellSingleQuotedString(healthBody);
        ScriptResult result = RunPowerShellCommand(BuildPowerShellFunctionHarness(
            Path.Combine(FindRepoRoot(TestContext.CurrentContext.TestDirectory), "tools", "run-dataagent-v4-manual-shadow.ps1"),
            $$"""
            $response = [pscustomobject]@{ StatusCode = 200; Content = '{{escapedHealthBody}}' }
            try {
                Assert-ManualShadowV47HealthResponse $response | Out-Null
                Write-Output "PASS manual_shadow"
                exit 0
            }
            catch {
                $reason = ConvertTo-ManualShadowFailureReason $_.Exception.Message
                Write-Output ("FALLBACK manual_shadow {0}" -f $reason)
                exit 1
            }
            """));

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(1), result.StandardOutput + result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("FALLBACK manual_shadow manual_shadow_response_rejected"));
            Assert.That(result.StandardOutput, Does.Not.Contain("PASS manual_shadow"));
            Assert.That(result.StandardOutput, Does.Not.Contain("must_not_be_accepted"));
            Assert.That(result.StandardOutput, Does.Not.Contain(healthBody));
        });
    }

    static void AssertBoundaryViolationFallback(DataAgentRealLangGraphManualShadowResult result)
    {
        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("real_langgraph_manual_shadow_boundary_violation"));
            Assert.That(result.FallbackRequired, Is.True);
            Assert.That(result.OperatorRequired, Is.True);
            Assert.That(result.DefaultResultChanged, Is.False);
        });
    }

    static string LineStartingWith(string text, string prefix)
    {
        foreach (string line in text.Split(Environment.NewLine))
        {
            if (line.StartsWith(prefix, StringComparison.Ordinal))
                return line;
        }

        return string.Empty;
    }

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

    static string BuildPowerShellFunctionHarness(string scriptPath, string body)
    {
        string escapedScriptPath = EscapePowerShellSingleQuotedString(scriptPath);
        return $$"""
        $ErrorActionPreference = "Stop"
        Set-StrictMode -Version 2.0

        $scriptPath = '{{escapedScriptPath}}'
        $source = Get-Content -LiteralPath $scriptPath -Raw
        $tokens = $null
        $parseErrors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$parseErrors)
        if ($parseErrors -and $parseErrors.Count -gt 0) {
            throw ("Manual shadow script parse errors: {0}" -f ($parseErrors | ForEach-Object { $_.Message } | Out-String))
        }

        $functions = $ast.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst]
        }, $true)

        foreach ($function in $functions) {
            Invoke-Expression $function.Extent.Text
        }

        {{body}}
        """;
    }

    static ScriptResult RunPowerShellCommand(string command)
    {
        ProcessStartInfo startInfo = NewPowerShellStartInfo();
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);

        return RunPowerShell(startInfo);
    }

    static ScriptResult RunPowerShellFile(string scriptPath, params string[] arguments) =>
        RunPowerShellFile(scriptPath, environment: null, arguments);

    static ScriptResult RunPowerShellFile(
        string scriptPath,
        IReadOnlyDictionary<string, string>? environment,
        params string[] arguments)
    {
        ProcessStartInfo startInfo = NewPowerShellStartInfo();
        if (environment is not null)
        {
            foreach ((string name, string value) in environment)
                startInfo.Environment[name] = value;
        }

        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return RunPowerShell(startInfo);
    }

    static TimedScriptResult RunPowerShellFileWithTimeout(
        string scriptPath,
        int timeoutMilliseconds,
        IReadOnlyDictionary<string, string> environment,
        params string[] arguments)
    {
        ProcessStartInfo startInfo = NewPowerShellStartInfo();
        foreach ((string name, string value) in environment)
            startInfo.Environment[name] = value;

        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start PowerShell.");
        bool completed = process.WaitForExit(timeoutMilliseconds);
        if (completed == false)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }

        return new TimedScriptResult(
            completed,
            new ScriptResult(
                process.ExitCode,
                process.StandardOutput.ReadToEnd(),
                process.StandardError.ReadToEnd()));
    }

    static string ResolveShadowArtifactBridgePath(string repoRoot)
    {
        string bridgePath = Path.Combine(
            repoRoot,
            "Outputs",
            "Alife.Tools.DataAgentShadowArtifact",
            "Alife.Tools.DataAgentShadowArtifact.dll");
        Assert.That(File.Exists(bridgePath), Is.True, "Build the shadow artifact bridge before script integration tests.");
        return bridgePath;
    }

    static string ResolveHangingShadowArtifactBridgePath(string repoRoot)
    {
        string bridgePath = Path.Combine(
            repoRoot,
            "Outputs",
            "Alife.Test.DataAgentShadowArtifactHang",
            "Alife.Test.DataAgentShadowArtifactHang.dll");
        Assert.That(File.Exists(bridgePath), Is.True, "Build the hanging shadow artifact bridge fixture before script integration tests.");
        return bridgePath;
    }

    static string CreateManualShadowArtifactDatabasePath()
    {
        string databasePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "manual-shadow-script-bridge",
            Guid.NewGuid().ToString("N"),
            "dataagent.sqlite");
        DataAgentSchemaInitializer.Initialize(databasePath);
        return databasePath;
    }

    static IReadOnlyDictionary<string, string> CreateSqliteEnvironment(string databasePath) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ALIFE_DATAAGENT_STORE_PROVIDER"] = "sqlite",
            ["ALIFE_DATAAGENT_SQLITE_PATH"] = databasePath
        };

    static DataAgentLangGraphShadowArtifactReadResult ReadManualShadowArtifactAggregate(string databasePath) =>
        new SqliteDataAgentStore(databasePath).ReadLangGraphShadowArtifactAggregate(DateTimeOffset.UtcNow);

    static ScriptResult RunManualHarnessHandshakeValidation(string handshakeBody)
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(repoRoot, "tools", "run-dataagent-v4-manual-shadow.ps1");
        string escapedHandshakeBody = EscapePowerShellSingleQuotedString(handshakeBody);

        return RunManualHarnessResponseValidation($$"""
        $response = [pscustomobject]@{
            StatusCode = 200
            Content = '{{escapedHandshakeBody}}'
        }
        """, scriptPath);
    }

    static ScriptResult RunManualHarnessResponseValidation(string responseSetup, string? scriptPath = null)
    {
        string resolvedScriptPath = scriptPath ??
            Path.Combine(FindRepoRoot(TestContext.CurrentContext.TestDirectory), "tools", "run-dataagent-v4-manual-shadow.ps1");
        string harness = BuildPowerShellFunctionHarness(resolvedScriptPath, $$"""
        {{responseSetup}}

        try {
            Assert-ManualShadowV47HandshakeResponse $response | Out-Null
            Write-Output "PASS manual_shadow"
            exit 0
        }
        catch {
            $reason = ConvertTo-ManualShadowFailureReason $_.Exception.Message
            Write-Output ("FALLBACK manual_shadow {0}" -f $reason)
            exit 1
        }
        """);

        return RunPowerShellCommand(harness);
    }

    static ScriptResult RunShadowArtifactBridge(string databasePath, params string[] arguments)
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string projectPath = Path.Combine(repoRoot, "tools", "dataagent-shadow-artifact", "Alife.Tools.DataAgentShadowArtifact.csproj");
        Assert.That(File.Exists(projectPath), Is.True, "The shadow artifact bridge project must exist.");

        string dotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "dotnet.exe");
        string bridgeDll = Path.Combine(
            repoRoot,
            "Outputs",
            "Alife.Tools.DataAgentShadowArtifact",
            "Alife.Tools.DataAgentShadowArtifact.dll");
        Assert.That(File.Exists(bridgeDll), Is.True, "Shadow artifact bridge DLL must exist after build.");

        ProcessStartInfo startInfo = new()
        {
            FileName = dotnet,
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.Environment["ALIFE_DATAAGENT_STORE_PROVIDER"] = "sqlite";
        startInfo.Environment["ALIFE_DATAAGENT_SQLITE_PATH"] = databasePath;
        startInfo.ArgumentList.Add(bridgeDll);
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start shadow artifact bridge.");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        if (process.WaitForExit(15000) == false)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new TimeoutException("Shadow artifact bridge did not exit within 15 seconds.");
        }

        return new ScriptResult(process.ExitCode, stdout, stderr);
    }

    static ProcessStartInfo NewPowerShellStartInfo()
    {
        string powerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        ProcessStartInfo startInfo = new()
        {
            FileName = powerShell,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");

        return startInfo;
    }

    static ScriptResult RunPowerShell(ProcessStartInfo startInfo)
    {
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start PowerShell.");

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        if (process.WaitForExit(15000) == false)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new TimeoutException("Manual shadow script harness did not exit within 15 seconds.");
        }

        return new ScriptResult(process.ExitCode, stdout, stderr);
    }

    static string EscapePowerShellSingleQuotedString(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    static string NewSafeManualV47HealthJson(params (string Name, object Value)[] overrides)
    {
        Dictionary<string, object> health = new(StringComparer.Ordinal)
        {
            ["ok"] = true,
            ["ready"] = true,
            ["runtimeMode"] = "langgraph",
            ["langGraphLoaded"] = true,
            ["langGraphVersion"] = "0.3.34",
            ["graphCompiled"] = true,
            ["contractVersion"] = "v4.7",
            ["graphVersion"] = "dataagent-advisory-v1",
            ["runtimeInstanceId"] = "8d7674fd-89a9-4d1c-a864-64e5d5960489",
            ["configurationFingerprint"] = new string('a', 64),
            ["startedAtUnixSeconds"] = 1_784_225_600
        };

        foreach ((string name, object value) in overrides)
        {
            health[name] = value;
        }

        return JsonSerializer.Serialize(health);
    }

    static string NewSafeManualHandshakeResponseJson(params (string Name, object Value)[] overrides)
    {
        Dictionary<string, object> response = new(StringComparer.Ordinal)
        {
            ["RequestId"] = "v4-manual-shadow-operator-run",
            ["Accepted"] = true,
            ["ReasonCode"] = "langgraph_advisory_accepted",
            ["SelectedNodes"] = new[] { "diagnostics_router" },
            ["NodeProgress"] = new[]
            {
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["NodeName"] = "diagnostics_router",
                    ["Status"] = "Completed",
                    ["ReasonCode"] = "advisory_only",
                    ["Message"] = "Advisory graph completed without authority transfer.",
                    ["Facts"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["authority"] = "csharp" }
                }
            },
            ["TraceSummary"] = "Advisory graph completed; C# remains authority.",
            ["ContextContribution"] = "sidecar=langgraph;authority=csharp",
            ["FallbackRequired"] = false,
            ["NoSqlAuthority"] = true,
            ["ReadOnly"] = true,
            ["RequestedToolNames"] = Array.Empty<string>(),
            ["RequestsCheckpointMutation"] = false,
            ["RequestsVisibleText"] = false
        };

        foreach ((string name, object value) in overrides)
        {
            response[name] = value;
        }

        return JsonSerializer.Serialize(response);
    }

    static string NewSafeManualHandshakeResponseJsonWithoutRequestedToolNames()
    {
        using JsonDocument document = JsonDocument.Parse(NewSafeManualHandshakeResponseJson());
        Dictionary<string, JsonElement> response = document.RootElement
            .EnumerateObject()
            .Where(property => property.Name != "RequestedToolNames")
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
        return JsonSerializer.Serialize(response);
    }

    static DataAgentRealLangGraphManualShadowResult NewDirectResult(
        string reasonCode = "safe_reason",
        string sourceReplayId = "safe_replay",
        IReadOnlyList<string>? reasonCodes = null,
        bool useNullReasonCodes = false)
    {
        return new DataAgentRealLangGraphManualShadowResult(
            Accepted: false,
            reasonCode,
            SourceBaseline: "v3.28",
            sourceReplayId,
            ContextLayerCount: 0,
            ManualOnly: true,
            OperatorStartedRuntime: true,
            LoopbackOnly: true,
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
            StoresHiddenContext: false,
            useNullReasonCodes ? null! : reasonCodes ?? ["safe_reason"]);
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

    sealed class ManualShadowLoopbackServer : IDisposable
    {
        readonly TcpListener listener;
        readonly string handshakeBody;
        readonly string healthBody;
        readonly Task serverTask;
        int handshakeRequestContentLength;
        int handshakeRequestBodyBytesDrained;
        string? handshakeRequestBody;
        bool disposed;

        public ManualShadowLoopbackServer(string handshakeBody, string? healthBody = null)
        {
            this.handshakeBody = handshakeBody;
            this.healthBody = healthBody ?? NewSafeManualV47HealthJson();
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            BaseUri = $"http://127.0.0.1:{port}";
            serverTask = Task.Run(ServeAsync);
        }

        public string BaseUri { get; }

        public int HandshakeRequestContentLength =>
            System.Threading.Volatile.Read(ref handshakeRequestContentLength);

        public int HandshakeRequestBodyBytesDrained =>
            System.Threading.Volatile.Read(ref handshakeRequestBodyBytesDrained);

        public string[] HandshakeRequestPropertyNames
        {
            get
            {
                string? body = System.Threading.Volatile.Read(ref handshakeRequestBody);
                if (string.IsNullOrWhiteSpace(body))
                    return [];

                using JsonDocument document = JsonDocument.Parse(body);
                return document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            listener.Stop();
            try
            {
                serverTask.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
            }
        }

        async Task ServeAsync()
        {
            while (disposed == false)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync();
                }
                catch (SocketException) when (disposed)
                {
                    return;
                }
                catch (ObjectDisposedException) when (disposed)
                {
                    return;
                }

                _ = Task.Run(async () =>
                {
                    using (client)
                    using (NetworkStream stream = client.GetStream())
                    {
                        RequestInfo request = await ReadRequestAsync(stream);
                        bool isHandshakeRequest = request.Headers.Contains(" /handshake ", StringComparison.Ordinal);
                        if (isHandshakeRequest)
                        {
                            System.Threading.Volatile.Write(
                                ref handshakeRequestContentLength,
                                request.ContentLength);
                            System.Threading.Volatile.Write(
                                ref handshakeRequestBodyBytesDrained,
                                request.BodyBytesDrained);
                            System.Threading.Volatile.Write(ref handshakeRequestBody, request.Body);
                        }

                        string body = isHandshakeRequest
                            ? handshakeBody
                            : healthBody;

                        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
                        byte[] headerBytes = Encoding.ASCII.GetBytes(
                            "HTTP/1.1 200 OK\r\n" +
                            "Content-Type: application/json\r\n" +
                            $"Content-Length: {bodyBytes.Length}\r\n" +
                            "Connection: close\r\n" +
                            "\r\n");

                        await stream.WriteAsync(headerBytes);
                        await stream.WriteAsync(bodyBytes);
                    }
                });
            }
        }

        static async Task<RequestInfo> ReadRequestAsync(NetworkStream stream)
        {
            byte[] buffer = new byte[4096];
            List<byte> requestBytes = [];
            int headerEndIndex = -1;
            while (headerEndIndex < 0)
            {
                int read = await stream.ReadAsync(buffer);
                if (read <= 0)
                    break;

                for (int index = 0; index < read; index++)
                    requestBytes.Add(buffer[index]);

                headerEndIndex = IndexOfHeaderTerminator(requestBytes);
            }

            if (headerEndIndex < 0)
                return new RequestInfo(Encoding.ASCII.GetString(requestBytes.ToArray()), 0, 0, string.Empty);

            int headerByteCount = headerEndIndex + 4;
            string headers = Encoding.ASCII.GetString(requestBytes.GetRange(0, headerByteCount).ToArray());
            int contentLength = ParseContentLength(headers);
            int bodyBytesAlreadyRead = Math.Min(contentLength, Math.Max(0, requestBytes.Count - headerByteCount));
            int bodyBytesDrained = bodyBytesAlreadyRead;
            List<byte> bodyBytes = requestBytes
                .Skip(headerByteCount)
                .Take(bodyBytesAlreadyRead)
                .ToList();

            while (bodyBytesDrained < contentLength)
            {
                int remaining = contentLength - bodyBytesDrained;
                int read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)));
                if (read <= 0)
                    break;

                bodyBytesDrained += read;
                bodyBytes.AddRange(buffer.AsSpan(0, read).ToArray());
            }

            string body = contentLength == bodyBytesDrained
                ? Encoding.UTF8.GetString(bodyBytes.ToArray())
                : string.Empty;
            return new RequestInfo(headers, contentLength, bodyBytesDrained, body);
        }

        static int IndexOfHeaderTerminator(IReadOnlyList<byte> bytes)
        {
            for (int index = 0; index <= bytes.Count - 4; index++)
            {
                if (bytes[index] == '\r' &&
                    bytes[index + 1] == '\n' &&
                    bytes[index + 2] == '\r' &&
                    bytes[index + 3] == '\n')
                {
                    return index;
                }
            }

            return -1;
        }

        static int ParseContentLength(string headers)
        {
            foreach (string line in headers.Split("\r\n", StringSplitOptions.None))
            {
                const string prefix = "Content-Length:";
                if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(line[prefix.Length..].Trim(), out int contentLength) &&
                    contentLength > 0)
                {
                    return contentLength;
                }
            }

            return 0;
        }

        readonly record struct RequestInfo(string Headers, int ContentLength, int BodyBytesDrained, string Body);
    }

    readonly record struct ScriptResult(int ExitCode, string StandardOutput, string StandardError);

    readonly record struct TimedScriptResult(bool Completed, ScriptResult Result);
}
