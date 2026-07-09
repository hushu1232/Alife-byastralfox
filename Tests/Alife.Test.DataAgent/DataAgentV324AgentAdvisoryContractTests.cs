using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV324AgentAdvisoryContractTests
{
    [Test]
    public void AdvisoryRequestAndResponseAreBoundedEvidenceFirstAndAdvisoryOnly()
    {
        DataAgentAgentAdvisoryRequest request = NewRequest();
        DataAgentAgentAdvisoryResponse response = new(
            AdvisoryId: "adv-1",
            Summary: "sidecar timeout after loopback check; retry manual smoke with same fixture",
            ReasonCode: "timeout_or_transport_failure",
            Confidence: 0.74,
            EvidenceRefs: ["artifact_index:v3.23-manual-audit-bundle"],
            ProposedNextSteps: ["inspect_loopback", "rerun_manual_shadow"],
            ForbiddenAuthorityClaims: [],
            RequiresOperatorAction: true,
            RequestsExecution: false,
            RequestsStateWrite: false,
            RequestsVisibleText: false,
            DefaultResultChanged: false);

        DataAgentAgentAdvisoryValidationResult requestValidation =
            DataAgentAgentAdvisoryContract.ValidateRequest(request);
        DataAgentAgentAdvisoryValidationResult responseValidation =
            DataAgentAgentAdvisoryContract.ValidateResponse(request, response);
        string packet = DataAgentAgentAdvisoryFormatter.Format(request, response);

        Assert.Multiple(() =>
        {
            Assert.That(requestValidation.Accepted, Is.True, requestValidation.ReasonCode);
            Assert.That(responseValidation.Accepted, Is.True, responseValidation.ReasonCode);
            Assert.That(request.ContractVersion, Is.EqualTo("v3.24"));
            Assert.That(request.AgentAdvisoryOnly, Is.True);
            Assert.That(request.HarnessExecutionAuthority, Is.True);
            Assert.That(request.CSharpValidationAuthority, Is.True);
            Assert.That(request.DefaultResultChanged, Is.False);
            Assert.That(request.AllowedAdvisoryActions, Is.EquivalentTo(new[]
            {
                "explain_failure",
                "propose_manual_check",
                "summarize_artifact",
                "suggest_fixture",
                "compare_replay_diff"
            }));
            Assert.That(request.ForbiddenAuthorities, Does.Contain("execute_sql"));
            Assert.That(request.ForbiddenAuthorities, Does.Contain("write_state"));
            Assert.That(request.ForbiddenAuthorities, Does.Contain("publish_visible_answer"));
            Assert.That(packet, Does.Contain("agent_advisory_contract=true"));
            Assert.That(packet, Does.Contain("contract_version=v3.24"));
            Assert.That(packet, Does.Contain("agent_advisory_only=true"));
            Assert.That(packet, Does.Contain("harness_execution_authority=true"));
            Assert.That(packet, Does.Contain("csharp_validation_authority=true"));
            Assert.That(packet, Does.Contain("token_budget_context_layers=true"));
            Assert.That(packet, Does.Contain("evidence_first_response=true"));
            Assert.That(packet, Does.Contain("default_result_changed=false"));
            Assert.That(packet, Does.Contain("reason_code=timeout_or_transport_failure"));
            Assert.That(packet, Does.Contain("evidence_ref=artifact_index:v3.23-manual-audit-bundle"));
            Assert.That(packet, Does.Not.Contain("SELECT"));
            Assert.That(packet, Does.Not.Contain("bearer"));
            Assert.That(packet, Does.Not.Contain("hidden_context"));
        });
    }

    [Test]
    public void ValidatorRejectsForbiddenAuthorityUnsafeTextAndEvidenceFreeResponses()
    {
        DataAgentAgentAdvisoryRequest request = NewRequest();
        DataAgentAgentAdvisoryResponse safe = NewResponse();
        DataAgentAgentAdvisoryResponse authorityClaim = safe with
        {
            ForbiddenAuthorityClaims = ["execute_sql"],
            RequestsExecution = true
        };
        DataAgentAgentAdvisoryResponse unsafeSummary = safe with
        {
            Summary = "unsafe SELECT hidden_context bearer secret"
        };
        DataAgentAgentAdvisoryResponse noEvidence = safe with
        {
            EvidenceRefs = []
        };
        DataAgentAgentAdvisoryResponse defaultChanged = safe with
        {
            DefaultResultChanged = true
        };

        Assert.Multiple(() =>
        {
            Assert.That(DataAgentAgentAdvisoryContract.ValidateResponse(request, safe).Accepted, Is.True);
            Assert.That(DataAgentAgentAdvisoryContract.ValidateResponse(request, authorityClaim).ReasonCode, Is.EqualTo("advisory_forbidden_authority_claimed"));
            Assert.That(DataAgentAgentAdvisoryContract.ValidateResponse(request, unsafeSummary).ReasonCode, Is.EqualTo("advisory_unsafe_text"));
            Assert.That(DataAgentAgentAdvisoryContract.ValidateResponse(request, noEvidence).ReasonCode, Is.EqualTo("advisory_missing_evidence"));
            Assert.That(DataAgentAgentAdvisoryContract.ValidateResponse(request, defaultChanged).ReasonCode, Is.EqualTo("advisory_default_result_changed"));
        });
    }

    [Test]
    public void FormatterRedactsUnsafeTokensAndKeepsPacketCompact()
    {
        DataAgentAgentAdvisoryRequest request = NewRequest() with
        {
            RunId = "unsafe SELECT hidden_context bearer secret",
            ArtifactIndexToken = "unsafe SELECT hidden_context bearer secret"
        };
        DataAgentAgentAdvisoryResponse response = NewResponse() with
        {
            AdvisoryId = "unsafe SELECT hidden_context bearer secret",
            EvidenceRefs = ["unsafe SELECT hidden_context bearer secret"]
        };

        string packet = DataAgentAgentAdvisoryFormatter.Format(request, response);

        Assert.Multiple(() =>
        {
            Assert.That(packet.Length, Is.LessThanOrEqualTo(1600));
            Assert.That(packet, Does.Contain("run_id=redacted"));
            Assert.That(packet, Does.Contain("artifact_index_token=redacted"));
            Assert.That(packet, Does.Contain("advisory_id=redacted"));
            Assert.That(packet, Does.Contain("evidence_ref=redacted"));
            Assert.That(packet, Does.Not.Contain("SELECT"));
            Assert.That(packet, Does.Not.Contain("bearer"));
            Assert.That(packet, Does.Not.Contain("hidden_context"));
            Assert.That(packet, Does.Not.Contain("unsafe SELECT hidden_context bearer secret"));
        });
    }

    [Test]
    public void V324DocumentDeclaresAgentAdvisoryContractBoundary()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string doc = File.ReadAllText(Path.Combine(repoRoot, "docs", "dataagent", "dataagent-v3.24-agent-advisory-contract.md"));

        Assert.Multiple(() =>
        {
            Assert.That(doc, Does.Contain("agent_advisory_contract=true"));
            Assert.That(doc, Does.Contain("contract_version=v3.24"));
            Assert.That(doc, Does.Contain("token_budget_context_layers=true"));
            Assert.That(doc, Does.Contain("evidence_first_response=true"));
            Assert.That(doc, Does.Contain("agent_advisory_only=true"));
            Assert.That(doc, Does.Contain("harness_execution_authority=true"));
            Assert.That(doc, Does.Contain("csharp_validation_authority=true"));
            Assert.That(doc, Does.Contain("langgraph_provider_only=true"));
            Assert.That(doc, Does.Contain("starts_runtime=false"));
            Assert.That(doc, Does.Contain("installs_dependencies=false"));
            Assert.That(doc, Does.Contain("default_result_changed=false"));
            Assert.That(doc, Does.Contain("stores_secrets=false"));
            Assert.That(doc, Does.Contain("stores_sql=false"));
            Assert.That(doc, Does.Contain("stores_hidden_context=false"));
        });
    }

    static DataAgentAgentAdvisoryRequest NewRequest()
    {
        return new DataAgentAgentAdvisoryRequest(
            ContractVersion: "v3.24",
            RunId: "v3.24-contract-smoke",
            Task: "explain classified harness failure",
            CurrentState: "manual shadow run failed after loopback check",
            AllowedAdvisoryActions:
            [
                "explain_failure",
                "propose_manual_check",
                "summarize_artifact",
                "suggest_fixture",
                "compare_replay_diff"
            ],
            ForbiddenAuthorities:
            [
                "start_runtime",
                "execute_sql",
                "write_state",
                "write_secret",
                "publish_visible_answer",
                "decide_tool_permission",
                "override_readiness"
            ],
            LastSuccessfulStep: "loopback_check",
            FailureCategory: "timeout_or_transport_failure",
            EvidenceRefs: ["artifact_index:v3.23-manual-audit-bundle"],
            ArtifactIndexToken: "v3.23-manual-audit-bundle",
            ExpectedResponseSchema: "advisory_id,summary,reason_code,confidence,evidence_refs,proposed_next_steps,forbidden_authority_claims,requires_operator_action",
            AgentAdvisoryOnly: true,
            HarnessExecutionAuthority: true,
            CSharpValidationAuthority: true,
            DefaultResultChanged: false);
    }

    static DataAgentAgentAdvisoryResponse NewResponse()
    {
        return new DataAgentAgentAdvisoryResponse(
            AdvisoryId: "adv-1",
            Summary: "classified timeout can be retried manually after checking loopback health",
            ReasonCode: "timeout_or_transport_failure",
            Confidence: 0.8,
            EvidenceRefs: ["artifact_index:v3.23-manual-audit-bundle"],
            ProposedNextSteps: ["inspect_loopback"],
            ForbiddenAuthorityClaims: [],
            RequiresOperatorAction: true,
            RequestsExecution: false,
            RequestsStateWrite: false,
            RequestsVisibleText: false,
            DefaultResultChanged: false);
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
}
