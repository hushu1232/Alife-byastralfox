using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentV325LangGraphManualShadowProviderTests
{
    [Test]
    public void ManualShadowProviderAcceptsValidatedLangGraphAdvisoryWithoutRuntimeAuthority()
    {
        DataAgentAgentAdvisoryRequest request = NewRequest();
        DataAgentLangGraphManualShadowPayload payload = NewPayload(NewResponse());

        DataAgentLangGraphManualShadowResult result =
            DataAgentLangGraphManualShadowProvider.Evaluate(request, payload);

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True, result.ReasonCode);
            Assert.That(result.ReasonCode, Is.EqualTo("langgraph_manual_shadow_advisory_accepted"));
            Assert.That(result.ProviderName, Is.EqualTo("langgraph"));
            Assert.That(result.ManualShadowOnly, Is.True);
            Assert.That(result.StartsRuntime, Is.False);
            Assert.That(result.InstallsDependencies, Is.False);
            Assert.That(result.CallsSidecar, Is.False);
            Assert.That(result.DefaultResultChanged, Is.False);
            Assert.That(result.StoresSecrets, Is.False);
            Assert.That(result.StoresSql, Is.False);
            Assert.That(result.StoresHiddenContext, Is.False);
            Assert.That(result.Validation.Accepted, Is.True);
            Assert.That(result.Advisory, Is.Not.Null);
            Assert.That(result.Advisory?.ReasonCode, Is.EqualTo("timeout_or_transport_failure"));
        });
    }

    [Test]
    public void ManualShadowProviderRejectsForbiddenAuthorityAndRequiresFallback()
    {
        DataAgentAgentAdvisoryRequest request = NewRequest();
        DataAgentAgentAdvisoryResponse unsafeResponse = NewResponse() with
        {
            ForbiddenAuthorityClaims = ["execute_sql"],
            RequestsExecution = true
        };

        DataAgentLangGraphManualShadowResult result =
            DataAgentLangGraphManualShadowProvider.Evaluate(request, NewPayload(unsafeResponse));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("advisory_forbidden_authority_claimed"));
            Assert.That(result.FallbackRequired, Is.True);
            Assert.That(result.DefaultResultChanged, Is.False);
            Assert.That(result.Advisory, Is.Null);
        });
    }

    [Test]
    public void ManualShadowProviderTreatsMissingManualPayloadAsUnavailableFallback()
    {
        DataAgentLangGraphManualShadowResult result =
            DataAgentLangGraphManualShadowProvider.Evaluate(NewRequest(), null);

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("langgraph_manual_shadow_payload_missing"));
            Assert.That(result.FallbackRequired, Is.True);
            Assert.That(result.ManualShadowOnly, Is.True);
            Assert.That(result.StartsRuntime, Is.False);
            Assert.That(result.CallsSidecar, Is.False);
            Assert.That(result.DefaultResultChanged, Is.False);
        });
    }

    [Test]
    public void ManualShadowProviderFormatsCompactSafeAuditPacket()
    {
        DataAgentLangGraphManualShadowResult result =
            DataAgentLangGraphManualShadowProvider.Evaluate(NewRequest(), NewPayload(NewResponse()));

        string text = DataAgentLangGraphManualShadowFormatter.Format(result);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("real_langgraph_manual_shadow_provider=true"));
            Assert.That(text, Does.Contain("langgraph_provider_only=true"));
            Assert.That(text, Does.Contain("manual_shadow_only=true"));
            Assert.That(text, Does.Contain("agent_advisory_contract=v3.24"));
            Assert.That(text, Does.Contain("accepted=true"));
            Assert.That(text, Does.Contain("fallback_required=false"));
            Assert.That(text, Does.Contain("starts_runtime=false"));
            Assert.That(text, Does.Contain("installs_dependencies=false"));
            Assert.That(text, Does.Contain("calls_sidecar=false"));
            Assert.That(text, Does.Contain("default_result_changed=false"));
            Assert.That(text, Does.Contain("stores_secrets=false"));
            Assert.That(text, Does.Contain("stores_sql=false"));
            Assert.That(text, Does.Contain("stores_hidden_context=false"));
            Assert.That(text, Does.Not.Contain("SELECT"));
            Assert.That(text, Does.Not.Contain("bearer"));
            Assert.That(text, Does.Not.Contain("unsafe SELECT hidden_context bearer secret"));
        });
    }

    [Test]
    public void V325DocumentDeclaresRealLangGraphManualShadowBoundary()
    {
        string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
        string doc = File.ReadAllText(Path.Combine(repoRoot, "docs", "dataagent", "dataagent-v3.25-real-langgraph-manual-shadow-provider.md"));

        Assert.Multiple(() =>
        {
            Assert.That(doc, Does.Contain("real_langgraph_manual_shadow_provider=true"));
            Assert.That(doc, Does.Contain("langgraph_provider_only=true"));
            Assert.That(doc, Does.Contain("manual_shadow_only=true"));
            Assert.That(doc, Does.Contain("agent_advisory_contract=v3.24"));
            Assert.That(doc, Does.Contain("starts_runtime=false"));
            Assert.That(doc, Does.Contain("installs_dependencies=false"));
            Assert.That(doc, Does.Contain("calls_sidecar=false"));
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
            RunId: "v3.25-manual-shadow",
            Task: "explain classified harness failure",
            CurrentState: "manual LangGraph shadow response captured by operator",
            AllowedAdvisoryActions: ["explain_failure", "propose_manual_check", "summarize_artifact"],
            ForbiddenAuthorities: ["start_runtime", "execute_sql", "write_state", "publish_visible_answer", "override_readiness"],
            LastSuccessfulStep: "manual_shadow_capture",
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
            AdvisoryId: "lg-manual-1",
            Summary: "manual LangGraph advisory recommends checking loopback health before retry",
            ReasonCode: "timeout_or_transport_failure",
            Confidence: 0.72,
            EvidenceRefs: ["artifact_index:v3.23-manual-audit-bundle"],
            ProposedNextSteps: ["inspect_loopback"],
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
