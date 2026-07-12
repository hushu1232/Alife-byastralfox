using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

public sealed class DataAgentV47LiveCanaryTests
{
    const string RuntimeId = "12345678-1234-5678-9234-567812345678";

    [Test]
    public void EvaluatorAcceptsOnlyCompleteSafeLiveEvidence()
    {
        DataAgentV47LiveCanaryResult result = DataAgentV47LiveCanaryClosureEvaluator.Evaluate(Input());

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.ReasonCode, Is.EqualTo("v4_7_live_canary_closure_accepted"));
            Assert.That(result.ContractVersion, Is.EqualTo("v4.7"));
            Assert.That(result.SourceBaseline, Is.EqualTo("v4.6"));
            Assert.That(result.AgentAdvisoryOnly, Is.True);
            Assert.That(result.CSharpValidationAuthority, Is.True);
            Assert.That(result.AllowsExecution, Is.False);
            Assert.That(result.AllowsStateWrite, Is.False);
            Assert.That(result.AllowsVisibleText, Is.False);
            Assert.That(result.StoresSensitiveData, Is.False);
        });
    }

    [TestCase("snapshot", "v4_7_observation_window_incomplete")]
    [TestCase("capacity", "v4_7_observation_window_incomplete")]
    [TestCase("aggregate", "v4_7_observation_window_incomplete")]
    [TestCase("fallback", "v4_7_fallback_ratio_exceeded")]
    [TestCase("latency", "v4_7_latency_budget_exceeded")]
    [TestCase("retry", "v4_7_retry_storm_detected")]
    [TestCase("restart", "v4_7_restart_budget_exceeded")]
    [TestCase("drill", "v4_7_fault_drill_failed")]
    [TestCase("identity", "v4_7_runtime_identity_invalid")]
    [TestCase("fingerprint", "v4_7_configuration_fingerprint_invalid")]
    [TestCase("start", "v4_7_runtime_start_time_invalid")]
    [TestCase("unstable", "v4_7_runtime_identity_unstable")]
    [TestCase("kill", "v4_7_kill_switch_not_restored")]
    [TestCase("shadow", "v4_7_production_shadow_not_restored_disabled")]
    public void EvaluatorFailsClosedForEveryHardGate(string scenario, string expectedReason)
    {
        DataAgentV47LiveCanaryInput input = Input();
        input = scenario switch
        {
            "snapshot" => input with { ObservationSnapshot = null },
            "capacity" => input with { ObservationSnapshot = input.ObservationSnapshot! with { Capacity = 128 } },
            "aggregate" => input with { ObservationSnapshot = input.ObservationSnapshot! with { AcceptedCount = 19 } },
            "fallback" => input with { ObservationSnapshot = input.ObservationSnapshot! with { FallbackRatioBasisPoints = 2501 } },
            "latency" => input with { ObservationSnapshot = input.ObservationSnapshot! with { P95LatencyMs = 2001 } },
            "retry" => input with { ObservationSnapshot = input.ObservationSnapshot! with { RetryStormDetected = true } },
            "restart" => input with { RuntimeRestartCount = 2 },
            "drill" => input with { FaultDrillResult = input.FaultDrillResult! with { Accepted = false } },
            "identity" => input with { RuntimeIdentity = input.RuntimeIdentity! with { RuntimeInstanceId = "not-a-uuid" } },
            "fingerprint" => input with { RuntimeIdentity = input.RuntimeIdentity! with { ConfigurationFingerprint = "ABC" } },
            "start" => input with { RuntimeIdentity = input.RuntimeIdentity! with { StartedAtUnixSeconds = 0 } },
            "unstable" => input with { RuntimeIdentity = input.RuntimeIdentity! with { StableAcrossWindow = false } },
            "kill" => input with { KillSwitchRestored = false },
            "shadow" => input with { ProductionShadowRestoredDisabled = false },
            _ => input
        };

        DataAgentV47LiveCanaryResult result = DataAgentV47LiveCanaryClosureEvaluator.Evaluate(input);

        Assert.That(result.Accepted, Is.False);
        Assert.That(result.ReasonCode, Is.EqualTo(expectedReason));
    }

    [Test]
    public void FormatterAndWriterPersistOnlyFixedSafeAcceptedEvidence()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"dataagent-v47-{Guid.NewGuid():N}");
        try
        {
            DataAgentV47LiveCanaryResult result = DataAgentV47LiveCanaryClosureEvaluator.Evaluate(Input());
            string formatted = DataAgentV47LiveCanaryClosureFormatter.Format(result);
            DataAgentV47LiveCanaryArtifactWriteResult written =
                DataAgentV47LiveCanaryArtifactWriter.Write(directory, result);
            string body = File.ReadAllText(written.FilePath);

            Assert.Multiple(() =>
            {
                Assert.That(written.Written, Is.True);
                Assert.That(written.FileName, Is.EqualTo("dataagent-v4.7-live-canary-closure.txt"));
                Assert.That(body, Is.EqualTo(formatted));
                Assert.That(body, Does.Contain("live_canary_closure=v4.7"));
                Assert.That(body, Does.Contain("runtime_instance_id=" + RuntimeId));
                Assert.That(body, Does.Contain("configuration_fingerprint=" + new string('a', 64)));
                Assert.That(body, Does.Contain("fault_drill_count=7"));
                Assert.That(body, Does.Contain("drill_live_kill_switch=true"));
                Assert.That(body, Does.Contain("kill_switch_restored=true"));
                Assert.That(body, Does.Contain("production_shadow_restored_disabled=true"));
                foreach (string forbidden in new[] { "request", "response", "endpoint", "sql", "token", "hidden_context", "exception", "path" })
                    Assert.That(body.ToLowerInvariant(), Does.Not.Contain(forbidden));
                Assert.That(body, Does.Not.Contain(directory));
            });
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void WriterRejectsRejectedOrMissingInputWithoutWriting()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"dataagent-v47-{Guid.NewGuid():N}");
        DataAgentV47LiveCanaryResult rejected = DataAgentV47LiveCanaryClosureEvaluator.Evaluate(
            Input() with { KillSwitchRestored = false });

        DataAgentV47LiveCanaryArtifactWriteResult write =
            DataAgentV47LiveCanaryArtifactWriter.Write(directory, rejected);

        Assert.Multiple(() =>
        {
            Assert.That(write.Written, Is.False);
            Assert.That(write.FileName, Is.EqualTo("redacted"));
            Assert.That(write.FilePath, Is.Empty);
            Assert.That(Directory.Exists(directory), Is.False);
        });
    }

    static DataAgentV47LiveCanaryInput Input() => new(
        HealthySnapshot(), PassedDrills(),
        new DataAgentV47RuntimeIdentityEvidence(RuntimeId, new string('a', 64), 1_783_820_000, true),
        RuntimeRestartCount: 0,
        KillSwitchRestored: true,
        ProductionShadowRestoredDisabled: true);

    static DataAgentV45ProductionObservationSnapshot HealthySnapshot() => new(
        256, 15, 20, 20, 0, 0, 0, 0, 0, 0, 20, 400, 900, 0, 4, false, false);

    static DataAgentV45ProductionFaultDrillResult PassedDrills() =>
        DataAgentV45ProductionFaultDrillEvaluator.Evaluate(
        [
            new(DataAgentV45FaultDrillKind.RuntimeUnavailable, true, "production_shadow_unavailable", true),
            new(DataAgentV45FaultDrillKind.Timeout, true, "production_shadow_timeout", true),
            new(DataAgentV45FaultDrillKind.InvalidSchema, true, "request_id_mismatch", true),
            new(DataAgentV45FaultDrillKind.UnsafeAuthority, true, "sql_authority_requested", true),
            new(DataAgentV45FaultDrillKind.ConcurrencySaturation, true, "production_shadow_busy", false),
            new(DataAgentV45FaultDrillKind.CircuitOpenRecovery, true, "production_shadow_circuit_open", false),
            new(DataAgentV45FaultDrillKind.LiveKillSwitch, true, "production_shadow_kill_switch_active", false)
        ]);
}
