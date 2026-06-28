using Alife.Function.FunctionCaller;

namespace Alife.Test.Interpreter;

public sealed class ToolCapabilityRouterTests
{
    [Test]
    public void ManifestStoresDomainIntentAndPreconditions()
    {
        ToolCapabilityManifest manifest = new(
            "dataagent_analysis_continue",
            ToolCapabilityDomain.DataAgent,
            "analysis_continue",
            ToolCapabilityRisk.Low,
            [ToolCapabilityPrecondition.ActiveDataAgentAnalysisSession],
            [ToolCapabilitySurface.OwnerPrivate, ToolCapabilitySurface.TrustedRuntime],
            ToolStateEffect.AppendsAnalysisTurn);

        Assert.Multiple(() =>
        {
            Assert.That(manifest.Name, Is.EqualTo("dataagent_analysis_continue"));
            Assert.That(manifest.Domain, Is.EqualTo(ToolCapabilityDomain.DataAgent));
            Assert.That(manifest.Intent, Is.EqualTo("analysis_continue"));
            Assert.That(manifest.Preconditions, Does.Contain(ToolCapabilityPrecondition.ActiveDataAgentAnalysisSession));
            Assert.That(manifest.Surfaces, Does.Contain(ToolCapabilitySurface.OwnerPrivate));
            Assert.That(manifest.StateEffect, Is.EqualTo(ToolStateEffect.AppendsAnalysisTurn));
        });
    }

    [Test]
    public void RouteDecisionCapturesAllowedAndDeniedTools()
    {
        ToolRouteState state = new(
            ActiveDataAgentSessionId: "analysis-1",
            ActiveDataAgentStatus: "Active",
            IsOwner: true,
            IsPrivateChat: true,
            IsTrustedRuntime: true);

        ToolRouteDecision decision = new(
            "route-1",
            ToolCapabilityDomain.DataAgent,
            "analysis_continue",
            ["dataagent_analysis_continue"],
            [new ToolRouteDeniedTool("browser_run_script", "domain_not_selected")],
            state,
            "explicit_dataagent_analysis_continue");

        Assert.Multiple(() =>
        {
            Assert.That(decision.AllowedTools, Is.EqualTo(new[] { "dataagent_analysis_continue" }));
            Assert.That(decision.DeniedTools.Single().Name, Is.EqualTo("browser_run_script"));
            Assert.That(decision.State.ActiveDataAgentSessionId, Is.EqualTo("analysis-1"));
            Assert.That(decision.Reason, Is.EqualTo("explicit_dataagent_analysis_continue"));
        });
    }
}
