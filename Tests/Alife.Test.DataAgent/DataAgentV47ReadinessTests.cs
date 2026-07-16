using System.Diagnostics;
using System.Text;
using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

public sealed class DataAgentV47ReadinessTests
{
    [Test]
    public void DynamicReadinessIncludesAcceptedV47LiveCanaryClosure()
    {
        string databasePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory, $"dataagent-v47-readiness-{Guid.NewGuid():N}.sqlite");
        IReadOnlyList<DataAgentReadinessCheck> checks = DataAgentReadiness.CheckCore(databasePath);
        DataAgentReadinessCheck check = checks.Single(item =>
            item.Name == "GraphHandshakeV47LiveCanaryClosurePresent");

        Assert.Multiple(() =>
        {
            Assert.That(checks, Has.Count.EqualTo(104));
            Assert.That(check.Passed, Is.True);
            Assert.That(check.Detail, Does.Contain("live_canary_closure=v4.7"));
            Assert.That(check.Detail, Does.Contain("minimum_observations=20"));
            Assert.That(check.Detail, Does.Contain("fault_drill_count=7"));
            Assert.That(check.Detail, Does.Contain("runtime_identity_stable=true"));
            Assert.That(check.Detail, Does.Contain("kill_switch_restored=true"));
            Assert.That(check.Detail, Does.Contain("production_shadow_restored_disabled=true"));
            Assert.That(check.Detail, Does.Contain("artifact_verifier=tools/verify-dataagent-v47-live-canary.ps1"));
        });
    }

    [Test]
    public void StaticReadinessAndManifestIncludeV47WithoutChangingV3Freeze()
    {
        string root = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string script = File.ReadAllText(Path.Combine(root, "tools", "check-dataagent-readiness.ps1"));
        string runbook = File.ReadAllText(Path.Combine(
            root, "docs", "dataagent", "dataagent-v4.7-live-canary-closure.md"));
        IReadOnlyList<string> names = DataAgentV3ClosureManifest.ParseStaticCheckNames(script);

        Assert.Multiple(() =>
        {
            Assert.That(names, Has.Count.EqualTo(120));
            Assert.That(names, Does.Contain("GraphHandshakeV47LiveCanaryClosurePresent"));
            Assert.That(script, Does.Contain("verify-dataagent-v47-live-canary.ps1"));
            Assert.That(script, Does.Contain("DataAgentV47LiveCanaryArtifactWriter.cs"));
            Assert.That(script, Does.Contain("local artifact existence is verified separately"));
            Assert.That(script, Does.Not.Contain("Test-Path -LiteralPath \"Outputs/dataagent-v4.7-live-canary"));
            Assert.That(script, Does.Contain("$expectedRequired = 120"));
            Assert.That(runbook, Does.Contain(
                "powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/verify-dataagent-v47-live-canary.ps1"));
            Assert.That(runbook, Does.Contain(
                "-ArtifactPath Outputs/dataagent-v4.7-live-canary/dataagent-v4.7-live-canary-closure.txt"));
            Assert.That(DataAgentV3ClosureManifest.V4OnlyCheckNames,
                Does.Contain("GraphHandshakeV47LiveCanaryClosurePresent"));
            Assert.That(DataAgentV3ClosureManifest.PostV3StaticCheckNames,
                Does.Contain("GraphHandshakeV47LiveCanaryClosurePresent"));
            Assert.That(DataAgentV3ClosureManifest.ExpectedFrozenStaticRequiredCount, Is.EqualTo(111));
            Assert.That(DataAgentV3ClosureManifest.ExpectedFrozenCoreCount, Is.EqualTo(95));
        });
    }

    [Test]
    public void ArtifactVerifierAcceptsValidFixedSchemaWithOnlySafeSuccessMarker()
    {
        VerifierResult result = RunVerifier(ValidArtifactLines());

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, result.CombinedOutput);
            Assert.That(Normalize(result.StandardOutput), Is.EqualTo("artifact_verified=true"));
            Assert.That(result.StandardError, Is.Empty);
            AssertNoArtifactDisclosure(result);
        });
    }

    [Test]
    public void ArtifactVerifierSanitizesFilesystemReadFailuresWithoutDisclosingPathOrPayload()
    {
        VerifierResult result = RunVerifier(ValidArtifactLines(), lockArtifact: true);
        AssertRejected(result, "artifact_read_failed");
    }

    [Test]
    public void ArtifactVerifierRejectsDuplicateUnknownMissingAndWrongCaseKeys()
    {
        string[] valid = ValidArtifactLines();
        AssertRejected(RunVerifier([.. valid, "accepted=true"]), "duplicate_artifact_key");
        AssertRejected(RunVerifier([.. valid, "unexpected_key=true"]), "unknown_artifact_key");
        AssertRejected(RunVerifier(valid.Where(line =>
            line.StartsWith("accepted_count=", StringComparison.Ordinal) == false)), "artifact_key_missing");
        AssertRejected(RunVerifier(ReplaceKey(valid, "accepted", "Accepted")), "unknown_artifact_key");
    }

    [TestCase("live_canary_closure", "v4.8")]
    [TestCase("source_baseline", "v4.5")]
    [TestCase("accepted", "false")]
    [TestCase("reason_code", "v4_7_observation_window_incomplete")]
    [TestCase("reason_codes", "v4_7_live_canary_closure_accepted,unexpected")]
    public void ArtifactVerifierRejectsChangedClosureIdentityOrReasonCodes(string key, string value)
    {
        AssertRejected(RunVerifier(ReplaceValue(ValidArtifactLines(), key, value)), "artifact_value_invalid");
    }

    [TestCase("runtime_instance_id", "not-a-uuid", "runtime_identity_invalid")]
    [TestCase("runtime_instance_id", "12345678-1234-5678-9234-56781234567A", "runtime_identity_invalid")]
    [TestCase("configuration_fingerprint", "abc", "configuration_fingerprint_invalid")]
    [TestCase("configuration_fingerprint", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "configuration_fingerprint_invalid")]
    [TestCase("started_at_unix_seconds", "not-a-number", "artifact_number_invalid")]
    [TestCase("started_at_unix_seconds", "0", "artifact_number_invalid")]
    [TestCase("started_at_unix_seconds", "253402300800", "artifact_number_invalid")]
    public void ArtifactVerifierRejectsMalformedRuntimeIdentity(
        string key, string value, string expectedError)
    {
        AssertRejected(RunVerifier(ReplaceValue(ValidArtifactLines(), key, value)), expectedError);
    }

    [TestCase("observation_capacity", "255")]
    [TestCase("observation_window_minutes", "14")]
    [TestCase("observation_count", "19")]
    [TestCase("observation_count", "257")]
    [TestCase("accepted_count", "-1")]
    [TestCase("accepted_count", "21")]
    [TestCase("rejected_count", "-1")]
    [TestCase("fallback_count", "-1")]
    [TestCase("timeout_count", "-1")]
    [TestCase("unavailable_count", "-1")]
    [TestCase("busy_count", "-1")]
    [TestCase("circuit_open_count", "-1")]
    [TestCase("average_latency_ms", "-1")]
    [TestCase("average_latency_ms", "300001")]
    [TestCase("p95_latency_ms", "-1")]
    [TestCase("p95_latency_ms", "2001")]
    [TestCase("fallback_ratio_basis_points", "-1")]
    [TestCase("fallback_ratio_basis_points", "2501")]
    [TestCase("max_observations_per_minute", "-1")]
    [TestCase("max_observations_per_minute", "21")]
    [TestCase("runtime_restart_count", "-1")]
    [TestCase("runtime_restart_count", "2")]
    [TestCase("fault_drill_count", "6")]
    [TestCase("fault_drill_count", "8")]
    [TestCase("network_attempt_count", "21")]
    [TestCase("observation_count", "020")]
    public void ArtifactVerifierRejectsEveryCountAndThresholdViolation(string key, string value)
    {
        AssertRejected(RunVerifier(ReplaceValue(ValidArtifactLines(), key, value)), "artifact_");
    }

    [TestCase("accepted_count", "19", "artifact_count_relation_invalid")]
    [TestCase("network_attempt_count", "19", "artifact_network_relation_invalid")]
    public void ArtifactVerifierRejectsIncoherentObservationRelations(
        string key, string value, string expectedError)
    {
        AssertRejected(RunVerifier(ReplaceValue(ValidArtifactLines(), key, value)), expectedError);
    }

    [Test]
    public void ArtifactVerifierRejectsOverfilledStatusBucketsEvenWhenEachBucketIsInRange()
    {
        string[] lines = ReplaceValue(ValidArtifactLines(), "rejected_count", "1");
        AssertRejected(RunVerifier(lines), "artifact_count_relation_invalid");
    }

    [TestCase("rejected_count")]
    [TestCase("fallback_count")]
    [TestCase("timeout_count")]
    [TestCase("unavailable_count")]
    [TestCase("busy_count")]
    [TestCase("circuit_open_count")]
    public void ArtifactVerifierRequiresEverySuccessfulWindowObservationToBeAccepted(
        string nonAcceptedBucket)
    {
        string[] lines = ReplaceValue(ValidArtifactLines(), "accepted_count", "19");
        lines = ReplaceValue(lines, nonAcceptedBucket, "1");
        AssertRejected(RunVerifier(lines), "artifact_success_window_invalid");
    }

    [Test]
    public void ArtifactVerifierRejectsFallbackRatioContradictingAllAcceptedWindow()
    {
        string[] lines = ReplaceValue(
            ValidArtifactLines(), "fallback_ratio_basis_points", "1");
        AssertRejected(RunVerifier(lines), "artifact_fallback_relation_invalid");
    }

    [TestCase("retry_storm_detected", "true")]
    [TestCase("identity_stable_across_window", "false")]
    [TestCase("kill_switch_restored", "false")]
    [TestCase("production_shadow_restored_disabled", "false")]
    [TestCase("agent_advisory_only", "false")]
    [TestCase("csharp_validation_authority", "false")]
    [TestCase("allows_execution", "true")]
    [TestCase("allows_state_write", "true")]
    [TestCase("allows_visible_text", "true")]
    [TestCase("stores_sensitive_data", "true")]
    public void ArtifactVerifierRejectsUnsafeAuthorityIdentityOrRestorationBoolean(string key, string value)
    {
        AssertRejected(RunVerifier(ReplaceValue(ValidArtifactLines(), key, value)), "artifact_value_invalid");
    }

    [TestCase("drill_runtime_unavailable")]
    [TestCase("drill_timeout")]
    [TestCase("drill_invalid_schema")]
    [TestCase("drill_unsafe_authority")]
    [TestCase("drill_concurrency_saturation")]
    [TestCase("drill_circuit_open_recovery")]
    [TestCase("drill_live_kill_switch")]
    public void ArtifactVerifierRequiresEachOfTheExactSevenAcceptedDrills(string key)
    {
        AssertRejected(RunVerifier(ReplaceValue(ValidArtifactLines(), key, "false")), "artifact_value_invalid");
    }

    static string[] ValidArtifactLines() =>
    [
        "live_canary_closure=v4.7",
        "source_baseline=v4.6",
        "accepted=true",
        "reason_code=v4_7_live_canary_closure_accepted",
        "observation_capacity=256",
        "observation_window_minutes=15",
        "observation_count=20",
        "accepted_count=20",
        "rejected_count=0",
        "fallback_count=0",
        "timeout_count=0",
        "unavailable_count=0",
        "busy_count=0",
        "circuit_open_count=0",
        "network_attempt_count=20",
        "average_latency_ms=10",
        "p95_latency_ms=20",
        "fallback_ratio_basis_points=0",
        "max_observations_per_minute=20",
        "retry_storm_detected=false",
        "runtime_instance_id=12345678-1234-5678-9234-567812345678",
        "configuration_fingerprint=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        "started_at_unix_seconds=1783820000",
        "identity_stable_across_window=true",
        "runtime_restart_count=0",
        "fault_drill_count=7",
        "drill_runtime_unavailable=true",
        "drill_timeout=true",
        "drill_invalid_schema=true",
        "drill_unsafe_authority=true",
        "drill_concurrency_saturation=true",
        "drill_circuit_open_recovery=true",
        "drill_live_kill_switch=true",
        "kill_switch_restored=true",
        "production_shadow_restored_disabled=true",
        "agent_advisory_only=true",
        "csharp_validation_authority=true",
        "allows_execution=false",
        "allows_state_write=false",
        "allows_visible_text=false",
        "stores_sensitive_data=false",
        "reason_codes=v4_7_live_canary_closure_accepted"
    ];

    static string[] ReplaceValue(IEnumerable<string> lines, string key, string value) =>
        lines.Select(line => line.StartsWith(key + "=", StringComparison.Ordinal)
            ? key + "=" + value
            : line).ToArray();

    static string[] ReplaceKey(IEnumerable<string> lines, string key, string replacement) =>
        lines.Select(line => line.StartsWith(key + "=", StringComparison.Ordinal)
            ? replacement + line[key.Length..]
            : line).ToArray();

    static VerifierResult RunVerifier(IEnumerable<string> lines, bool lockArtifact = false)
    {
        string root = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string scriptPath = Path.Combine(root, "tools", "verify-dataagent-v47-live-canary.ps1");
        string directory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            $"dataagent-v47-verifier-{Guid.NewGuid():N}");
        string artifactPath = Path.Combine(directory, "dataagent-v4.7-live-canary-closure.txt");
        Directory.CreateDirectory(directory);
        string payload = string.Join(Environment.NewLine, lines);
        File.WriteAllText(artifactPath, payload, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        FileStream? artifactLock = lockArtifact
            ? new FileStream(artifactPath, FileMode.Open, FileAccess.Read, FileShare.None)
            : null;

        string powerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
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
        foreach (string argument in new[]
        {
            "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath,
            "-ArtifactPath", artifactPath
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start PowerShell artifact verifier.");
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            if (process.WaitForExit(15000) == false)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                throw new TimeoutException("V4.7 artifact verifier did not exit within 15 seconds.");
            }
            return new(process.ExitCode, stdout, stderr, artifactPath, payload);
        }
        finally
        {
            artifactLock?.Dispose();
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    static void AssertRejected(VerifierResult result, string expectedError)
    {
        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Not.Zero, result.CombinedOutput);
            Assert.That(result.CombinedOutput, Does.Contain(expectedError));
            AssertNoArtifactDisclosure(result);
        });
    }

    static void AssertNoArtifactDisclosure(VerifierResult result)
    {
        Assert.Multiple(() =>
        {
            Assert.That(result.CombinedOutput, Does.Not.Contain(result.ArtifactPath));
            Assert.That(result.CombinedOutput, Does.Not.Contain(result.Payload));
            Assert.That(result.CombinedOutput, Does.Not.Contain("12345678-1234-5678-9234-567812345678"));
            Assert.That(result.CombinedOutput, Does.Not.Contain(new string('a', 64)));
            Assert.That(result.CombinedOutput, Does.Not.Contain("observation_count=20"));
            Assert.That(result.CombinedOutput, Does.Not.Contain("accepted=true"));
        });
    }

    static string Normalize(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');

    static string FindRepoRoot(string start)
    {
        DirectoryInfo? current = new(start);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Alife.slnx")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("repo_root_not_found");
    }

    readonly record struct VerifierResult(
        int ExitCode,
        string StandardOutput,
        string StandardError,
        string ArtifactPath,
        string Payload)
    {
        public string CombinedOutput => StandardOutput + Environment.NewLine + StandardError;
    }
}
