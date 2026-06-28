using System.Collections.Generic;
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
            Assert.That(decision.Allows("DATAAGENT_ANALYSIS_CONTINUE"), Is.True);
            Assert.That(decision.Allows("browser_run_script"), Is.False);
            Assert.That(decision.Allows(null!), Is.False);
            Assert.That(decision.Allows("   "), Is.False);
        });
    }

    [Test]
    public void EmptyRouteStateFailsClosed()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ToolRouteState.Empty.HasActiveDataAgentSession, Is.False);
            Assert.That(ToolRouteState.Empty.IsTrustedRuntime, Is.False);
        });
    }

    [Test]
    public void RouteStateTreatsWhitespaceAndCaseInsensitiveActiveStatusAsActive()
    {
        ToolRouteState state = new(
            ActiveDataAgentSessionId: "analysis-1",
            ActiveDataAgentStatus: " active ",
            IsOwner: true,
            IsPrivateChat: true,
            IsTrustedRuntime: true);

        Assert.That(state.HasActiveDataAgentSession, Is.True);
    }

    [Test]
    public void RouteDecisionDefensivelyCopiesToolLists()
    {
        List<string> allowedTools = ["dataagent_analysis_continue"];
        List<ToolRouteDeniedTool> deniedTools = [new("browser_run_script", "domain_not_selected")];

        ToolRouteDecision decision = new(
            "route-1",
            ToolCapabilityDomain.DataAgent,
            "analysis_continue",
            allowedTools,
            deniedTools,
            ToolRouteState.Empty,
            "explicit_dataagent_analysis_continue");

        allowedTools.Clear();
        allowedTools.Add("browser_run_script");
        deniedTools.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(decision.Allows("dataagent_analysis_continue"), Is.True);
            Assert.That(decision.Allows("browser_run_script"), Is.False);
            Assert.That(decision.AllowedTools, Is.EqualTo(new[] { "dataagent_analysis_continue" }));
            Assert.That(decision.DeniedTools.Single().Name, Is.EqualTo("browser_run_script"));
        });
    }

    [Test]
    public void RouteDecisionTreatsNullToolListsAsEmpty()
    {
        ToolRouteDecision decision = new(
            "route-1",
            ToolCapabilityDomain.DataAgent,
            "analysis_continue",
            null!,
            null!,
            ToolRouteState.Empty,
            "explicit_dataagent_analysis_continue");

        Assert.Multiple(() =>
        {
            Assert.That(decision.AllowedTools, Is.EqualTo(System.Array.Empty<string>()));
            Assert.That(decision.DeniedTools, Is.EqualTo(System.Array.Empty<ToolRouteDeniedTool>()));
            Assert.That(decision.Allows("dataagent_analysis_continue"), Is.False);
        });
    }

    [Test]
    public void RouterAllowsStartAndQueryWhenNoDataAgentSessionExists()
    {
        ToolCapabilityRouter router = ToolCapabilityRouter.CreateDefault();
        ToolRouteState state = new("", "", IsOwner: true, IsPrivateChat: true, IsTrustedRuntime: true);

        ToolRouteDecision decision = router.Route(
            "分析一下我们离 V2 还差什么",
            state);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Domain, Is.EqualTo(ToolCapabilityDomain.DataAgent));
            Assert.That(decision.AllowedTools, Does.Contain("dataagent_query"));
            Assert.That(decision.AllowedTools, Does.Contain("dataagent_analysis_start"));
            Assert.That(decision.AllowedTools, Does.Not.Contain("dataagent_analysis_continue"));
            Assert.That(decision.AllowedTools, Does.Not.Contain("dataagent_analysis_summarize"));
            Assert.That(decision.AllowedTools, Does.Not.Contain("dataagent_analysis_end"));
        });
    }

    [Test]
    public void RouterAllowsContinueSummarizeAndEndOnlyForExplicitDataAgentAnalysisWithActiveSession()
    {
        ToolCapabilityRouter router = ToolCapabilityRouter.CreateDefault();
        ToolRouteState state = new("analysis-1", "Active", IsOwner: true, IsPrivateChat: true, IsTrustedRuntime: true);

        ToolRouteDecision decision = router.Route("继续刚才的 DataAgent 分析", state);

        Assert.Multiple(() =>
        {
            Assert.That(decision.AllowedTools, Does.Contain("dataagent_analysis_continue"));
            Assert.That(decision.AllowedTools, Does.Contain("dataagent_analysis_summarize"));
            Assert.That(decision.AllowedTools, Does.Contain("dataagent_analysis_end"));
        });
    }

    [Test]
    public void RouterDoesNotTreatOrdinaryContinueAsDataAgentAnalysis()
    {
        ToolCapabilityRouter router = ToolCapabilityRouter.CreateDefault();
        ToolRouteState state = new("analysis-1", "Active", IsOwner: true, IsPrivateChat: true, IsTrustedRuntime: true);

        ToolRouteDecision decision = router.Route("继续说", state);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Domain, Is.EqualTo(ToolCapabilityDomain.Chat));
            Assert.That(decision.AllowedTools, Does.Not.Contain("dataagent_analysis_continue"));
            Assert.That(decision.Reason, Is.EqualTo("ordinary_chat"));
        });
    }

    [Test]
    public void RouterDoesNotExposeDataAgentToolsWhenRouteStateIsUntrusted()
    {
        ToolCapabilityRouter router = ToolCapabilityRouter.CreateDefault();

        ToolRouteDecision decision = router.Route("分析一下我们离 V2 还差什么", ToolRouteState.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Domain, Is.EqualTo(ToolCapabilityDomain.DataAgent));
            Assert.That(decision.AllowedTools, Is.Empty);
            Assert.That(decision.DeniedTools.Select(tool => tool.Name), Does.Contain("dataagent_query"));
            Assert.That(decision.Reason, Is.EqualTo("route_state_not_trusted"));
        });
    }
}
