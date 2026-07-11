using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV328FinalReadinessFreezeTests
{
    [Test]
    public void FinalReadinessFreezeSummarizesFrozenV3ChecksWithoutRuntimeAuthority()
    {
        DataAgentV3FinalReadinessFreeze freeze =
            DataAgentV3FinalReadinessFreezeBuilder.Build(AcceptedClosure());

        Assert.Multiple(() =>
        {
            Assert.That(freeze.FreezeId, Is.EqualTo("v3.28-final-readiness-freeze"));
            Assert.That(freeze.FinalV3Version, Is.EqualTo("v3.28"));
            Assert.That(freeze.SourceVersions, Is.EqualTo("v3.0-v3.27"));
            Assert.That(freeze.FrozenRequiredCheckCount, Is.EqualTo(111));
            Assert.That(freeze.FrozenCoreCheckCount, Is.EqualTo(95));
            Assert.That(freeze.AllFrozenChecksPassed, Is.True);
            Assert.That(freeze.OperatorEvidencePackPresent, Is.True);
            Assert.That(freeze.ReadinessGatesFrozen, Is.True);
            Assert.That(freeze.OperatorDecides, Is.True);
            Assert.That(freeze.AgentAdvisoryOnly, Is.True);
            Assert.That(freeze.HarnessExecutionAuthority, Is.True);
            Assert.That(freeze.CSharpValidationAuthority, Is.True);
            Assert.That(freeze.DefaultResultChanged, Is.False);
            Assert.That(freeze.ManualOnly, Is.True);
            Assert.That(freeze.StartsRuntime, Is.False);
            Assert.That(freeze.InstallsDependencies, Is.False);
            Assert.That(freeze.CallsSidecar, Is.False);
            Assert.That(freeze.StoresSecrets, Is.False);
            Assert.That(freeze.StoresSql, Is.False);
            Assert.That(freeze.StoresHiddenContext, Is.False);
        });
    }

    [Test]
    public void FinalReadinessFreezeFailsClosedWhenRequiredCheckIsMissingOrFailed()
    {
        DataAgentV3FinalReadinessFreeze missingOperatorPack =
            DataAgentV3FinalReadinessFreezeBuilder.Build(AcceptedClosure() with
            {
                Accepted = false,
                OperatorEvidencePackPresent = false,
                MissingRequiredCheckNames = ["GraphHandshakeOperatorEvidencePackPresent"]
            });
        DataAgentV3FinalReadinessFreeze failedReadiness =
            DataAgentV3FinalReadinessFreezeBuilder.Build(AcceptedClosure() with
            {
                Accepted = false,
                OperatorEvidencePackPresent = false,
                FailedRequiredCheckNames = ["GraphHandshakeOperatorEvidencePackPresent"]
            });

        Assert.Multiple(() =>
        {
            Assert.That(missingOperatorPack.AllFrozenChecksPassed, Is.False);
            Assert.That(missingOperatorPack.OperatorEvidencePackPresent, Is.False);
            Assert.That(missingOperatorPack.ReadinessGatesFrozen, Is.False);
            Assert.That(missingOperatorPack.FallbackRequired, Is.True);
            Assert.That(missingOperatorPack.OperatorRequired, Is.True);
            Assert.That(failedReadiness.AllFrozenChecksPassed, Is.False);
            Assert.That(failedReadiness.OperatorEvidencePackPresent, Is.False);
            Assert.That(failedReadiness.ReadinessGatesFrozen, Is.False);
            Assert.That(failedReadiness.FallbackRequired, Is.True);
            Assert.That(failedReadiness.OperatorRequired, Is.True);
            Assert.That(failedReadiness.DefaultResultChanged, Is.False);
            Assert.That(failedReadiness.StartsRuntime, Is.False);
        });
    }

    [Test]
    public void FinalReadinessFreezeFormatsCompactSafePacket()
    {
        DataAgentV3FinalReadinessFreeze freeze =
            DataAgentV3FinalReadinessFreezeBuilder.Build(AcceptedClosure());

        string text = DataAgentV3FinalReadinessFreezeFormatter.Format(freeze);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("v3_final_readiness_freeze=true"));
            Assert.That(text, Does.Contain("final_v3_version=v3.28"));
            Assert.That(text, Does.Contain("source_versions=v3.0-v3.27"));
            Assert.That(text, Does.Contain("frozen_required_check_count=111"));
            Assert.That(text, Does.Contain("frozen_core_check_count=95"));
            Assert.That(text, Does.Contain("all_frozen_checks_passed=true"));
            Assert.That(text, Does.Contain("operator_evidence_pack_present=true"));
            Assert.That(text, Does.Contain("readiness_gates_frozen=true"));
            Assert.That(text, Does.Contain("operator_decides=true"));
            Assert.That(text, Does.Contain("agent_advisory_only=true"));
            Assert.That(text, Does.Contain("harness_execution_authority=true"));
            Assert.That(text, Does.Contain("csharp_validation_authority=true"));
            Assert.That(text, Does.Contain("default_result_changed=false"));
            Assert.That(text, Does.Contain("manual_only=true"));
            Assert.That(text, Does.Contain("starts_runtime=false"));
            Assert.That(text, Does.Contain("installs_dependencies=false"));
            Assert.That(text, Does.Contain("calls_sidecar=false"));
            Assert.That(text, Does.Contain("stores_secrets=false"));
            Assert.That(text, Does.Contain("stores_sql=false"));
            Assert.That(text, Does.Contain("stores_hidden_context=false"));
            Assert.That(text, Does.Not.Contain("SELECT"));
            Assert.That(text, Does.Not.Contain("bearer"));
            Assert.That(text, Does.Not.Contain("unsafe SELECT hidden_context bearer secret"));
        });
    }

    [Test]
    public void FinalReadinessFreezeFailsClosedWithoutLeakingClosureEvidence()
    {
        const string unsafeName = "DataAgentReplayRunbookPresent SELECT bearer C:\\secret private_context_payload";
        DataAgentV3FinalReadinessFreeze freeze = DataAgentV3FinalReadinessFreezeBuilder.Build(AcceptedClosure() with
        {
            Accepted = false,
            MissingRequiredCheckNames = [unsafeName]
        });

        string text = DataAgentV3FinalReadinessFreezeFormatter.Format(freeze);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.StartWith("v3_final_readiness_freeze=false"));
            Assert.That(text, Does.Contain("missing_required_check_count=1"));
            Assert.That(text, Does.Contain("fallback_required=true"));
            Assert.That(text, Does.Contain("operator_required=true"));
            Assert.That(text, Does.Not.Contain("DataAgentReplayRunbookPresent"));
            Assert.That(text, Does.Not.Contain("SELECT"));
            Assert.That(text, Does.Not.Contain("bearer"));
            Assert.That(text, Does.Not.Contain("C:\\secret"));
            Assert.That(text, Does.Not.Contain("private_context_payload"));
        });
    }

    [Test]
    public void V328DocumentDeclaresFinalReadinessFreezeBoundary()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string doc = File.ReadAllText(Path.Combine(repoRoot, "docs", "dataagent", "dataagent-v3.28-final-readiness-freeze.md"));

        Assert.Multiple(() =>
        {
            Assert.That(doc, Does.Contain("v3_final_readiness_freeze=true"));
            Assert.That(doc, Does.Contain("final_v3_version=v3.28"));
            Assert.That(doc, Does.Contain("source_versions=v3.0-v3.27"));
            Assert.That(doc, Does.Contain("frozen_required_check_count=111"));
            Assert.That(doc, Does.Contain("frozen_core_check_count=95"));
            Assert.That(doc, Does.Contain("operator_decides=true"));
            Assert.That(doc, Does.Contain("agent_advisory_only=true"));
            Assert.That(doc, Does.Contain("harness_execution_authority=true"));
            Assert.That(doc, Does.Contain("csharp_validation_authority=true"));
            Assert.That(doc, Does.Contain("default_result_changed=false"));
            Assert.That(doc, Does.Contain("manual_only=true"));
            Assert.That(doc, Does.Contain("starts_runtime=false"));
            Assert.That(doc, Does.Contain("installs_dependencies=false"));
            Assert.That(doc, Does.Contain("calls_sidecar=false"));
            Assert.That(doc, Does.Contain("stores_secrets=false"));
            Assert.That(doc, Does.Contain("stores_sql=false"));
            Assert.That(doc, Does.Contain("stores_hidden_context=false"));
        });
    }

    [Test]
    public void ReadinessScriptAndDynamicReadinessDeclareFinalFreeze()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string script = File.ReadAllText(Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1"));
        string source = File.ReadAllText(Path.Combine(
            repoRoot,
            "sources",
            "Alife.Function",
            "Alife.Function.DataAgent",
            "DataAgentReadiness.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("GraphHandshakeFinalV3ReadinessFreezePresent"));
            Assert.That(script, Does.Contain("$expectedRequired = 118"));
            Assert.That(source, Does.Contain("GraphHandshakeFinalV3ReadinessFreezePresent"));
            Assert.That(source, Does.Contain("DataAgentV3FinalReadinessFreezeBuilder.Build"));
            Assert.That(source, Does.Contain("frozen_required_check_count=111"));
            Assert.That(source, Does.Contain("frozen_core_check_count=95"));
        });
    }

    static DataAgentReadinessCheck[] NewFrozenChecks(string? failedCheckName = null, string? extraDetail = null)
    {
        string[] names =
        [
            "GraphHandshakeBoundaryPresent",
            "GraphHandshakeHarnessReplayDiffGatePresent",
            "GraphHandshakeOperatorEvidencePackPresent",
            "DataAgentSafetyCapabilitiesRemainDeterministic"
        ];

        return names
            .Select(name => new DataAgentReadinessCheck(
                name,
                string.Equals(name, failedCheckName, StringComparison.Ordinal) == false,
                string.Equals(name, "GraphHandshakeOperatorEvidencePackPresent", StringComparison.Ordinal)
                    ? $"operator_evidence_pack=true;operator_decides=true;{extraDetail ?? string.Empty}"
                    : $"ready=true;{extraDetail ?? string.Empty}"))
            .ToArray();
    }

    static DataAgentV3ClosureResult AcceptedClosure() => new(true, 111, 95, [], [], [], [], [], [], [], [], [], [], [], 0, true, true, true);

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
}
