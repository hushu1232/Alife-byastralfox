using System;
using System.Collections.Generic;
using System.Linq;
using Alife.Function.FunctionCaller;

namespace Alife.Test.Interpreter;

public sealed class ToolCapabilityRouterTests
{
    static readonly string[] DataAgentToolNames =
    [
        "dataagent_query",
        "dataagent_analysis_start",
        "dataagent_analysis_continue",
        "dataagent_analysis_summarize",
        "dataagent_analysis_end"
    ];

    static readonly string[] StartAllowedTools =
    [
        "dataagent_query",
        "dataagent_analysis_start"
    ];

    static readonly string[] StartDeniedTools =
    [
        "dataagent_analysis_continue",
        "dataagent_analysis_summarize",
        "dataagent_analysis_end"
    ];

    static readonly string[] ActiveAllowedTools =
    [
        "dataagent_query",
        "dataagent_analysis_continue",
        "dataagent_analysis_summarize",
        "dataagent_analysis_end"
    ];

    [Test]
    public void DefaultRouterUsesSharedDataAgentManifestFactory()
    {
        ToolCapabilityRouter router = ToolCapabilityRouter.CreateDefault();
        IReadOnlyList<ToolCapabilityManifest> manifests = DataAgentToolCapabilityManifests.Create();

        Assert.Multiple(() =>
        {
            Assert.That(router.ToolNames, Is.EqualTo(manifests.Select(manifest => manifest.Name).ToArray()));
            Assert.That(manifests.Select(manifest => manifest.Name), Is.EqualTo(new[]
            {
                "dataagent_query",
                "dataagent_analysis_start",
                "dataagent_analysis_continue",
                "dataagent_analysis_summarize",
                "dataagent_analysis_end"
            }));
            Assert.That(manifests.Single(manifest => manifest.Name == "dataagent_query").StateEffect, Is.EqualTo(ToolStateEffect.ReadsData));
            Assert.That(manifests.Single(manifest => manifest.Name == "dataagent_analysis_end").StateEffect, Is.EqualTo(ToolStateEffect.EndsAnalysis));
        });
    }

    [Test]
    public void SharedDataAgentManifestsReturnIndependentReadOnlyInstances()
    {
        IReadOnlyList<ToolCapabilityManifest> first = DataAgentToolCapabilityManifests.Create();
        IReadOnlyList<ToolCapabilityManifest> second = DataAgentToolCapabilityManifests.Create();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.SameAs(second));
            Assert.That(first[0], Is.Not.SameAs(second[0]));
            Assert.That(first[0].Preconditions, Is.Not.SameAs(second[0].Preconditions));
            Assert.That(first[0].Surfaces, Is.Not.SameAs(second[0].Surfaces));
            Assert.Throws<NotSupportedException>(() => ((IList<ToolCapabilityManifest>)first).Add(first[0]));
            Assert.Throws<NotSupportedException>(() => ((IList<ToolCapabilityPrecondition>)first[0].Preconditions).Add(ToolCapabilityPrecondition.None));
            Assert.Throws<NotSupportedException>(() => ((IList<ToolCapabilitySurface>)first[0].Surfaces).Add(ToolCapabilitySurface.PublicGroup));
        });
    }

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


    [TestCase("AwaitingClarification")]
    [TestCase("ReadyToSummarize")]
    [TestCase("Summarized")]
    public void RouteStateTreatsLiveAnalysisStatusesAsActive(string status)
    {
        ToolRouteState state = new(
            ActiveDataAgentSessionId: "analysis-1",
            ActiveDataAgentStatus: status,
            IsOwner: true,
            IsPrivateChat: true,
            IsTrustedRuntime: true);

        Assert.That(state.HasActiveDataAgentSession, Is.True);
    }

    [TestCase("Ended")]
    [TestCase("Rejected")]
    public void RouteStateTreatsTerminalAnalysisStatusesAsInactive(string status)
    {
        ToolRouteState state = new(
            ActiveDataAgentSessionId: "analysis-1",
            ActiveDataAgentStatus: status,
            IsOwner: true,
            IsPrivateChat: true,
            IsTrustedRuntime: true);

        Assert.That(state.HasActiveDataAgentSession, Is.False);
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
            Assert.That(decision.AllowedTools, Is.EqualTo(Array.Empty<string>()));
            Assert.That(decision.DeniedTools, Is.EqualTo(Array.Empty<ToolRouteDeniedTool>()));
            Assert.That(decision.Allows("dataagent_analysis_continue"), Is.False);
        });
    }

    [Test]
    public void RouterAllowsStartAndQueryWhenNoDataAgentSessionExists()
    {
        ToolCapabilityRouter router = ToolCapabilityRouter.CreateDefault();
        ToolRouteState state = TrustedOwnerPrivateState(activeSession: false);

        ToolRouteDecision decision = router.Route(
            "分析一下我们离 V2 还差什么",
            state);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Domain, Is.EqualTo(ToolCapabilityDomain.DataAgent));
            Assert.That(decision.Intent, Is.EqualTo("analysis_start"));
            Assert.That(decision.Reason, Is.EqualTo("explicit_dataagent_analysis_start"));
            Assert.That(decision.AllowedTools, Is.EqualTo(StartAllowedTools));
            AssertDeniedTools(decision, StartDeniedTools, "tool_not_allowed_in_current_route");
        });
    }

    [TestCase("analyze project readiness for V1.5")]
    [TestCase("analyze V2 readiness")]
    [TestCase("project readiness analysis")]
    public void RouterAllowsEnglishProjectReadinessAnalysisWhenNoDataAgentSessionExists(string utterance)
    {
        ToolCapabilityRouter router = ToolCapabilityRouter.CreateDefault();
        ToolRouteState state = TrustedOwnerPrivateState(activeSession: false);

        ToolRouteDecision decision = router.Route(utterance, state);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Domain, Is.EqualTo(ToolCapabilityDomain.DataAgent));
            Assert.That(decision.Intent, Is.EqualTo("analysis_start"));
            Assert.That(decision.Reason, Is.EqualTo("explicit_dataagent_analysis_start"));
            Assert.That(decision.AllowedTools, Is.EqualTo(StartAllowedTools));
            AssertDeniedTools(decision, StartDeniedTools, "tool_not_allowed_in_current_route");
        });
    }

    [Test]
    public void RouteReturnsStableReasonCodeWhenDataAgentAnalysisSessionIsMissing()
    {
        ToolCapabilityRouter router = ToolCapabilityRouter.CreateDefault();
        ToolRouteState state = TrustedOwnerPrivateState(activeSession: false);

        ToolRouteDecision decision = router.Route("continue DataAgent analysis", state);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Domain, Is.EqualTo(ToolCapabilityDomain.DataAgent));
            Assert.That(decision.Intent, Is.EqualTo("analysis_continue"));
            Assert.That(decision.AllowedTools, Is.Empty);
            Assert.That(decision.ReasonCode, Is.EqualTo("dataagent_analysis_session_missing"));
            AssertDeniedTools(decision, DataAgentToolNames, "dataagent_analysis_session_missing");
        });
    }
    [Test]
    public void RouterAllowsContinueSummarizeAndEndOnlyForExplicitDataAgentAnalysisWithActiveSession()
    {
        ToolCapabilityRouter router = ToolCapabilityRouter.CreateDefault();
        ToolRouteState state = TrustedOwnerPrivateState(activeSession: true);

        ToolRouteDecision decision = router.Route("继续刚才的 DataAgent 分析", state);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Domain, Is.EqualTo(ToolCapabilityDomain.DataAgent));
            Assert.That(decision.Intent, Is.EqualTo("analysis_continue"));
            Assert.That(decision.Reason, Is.EqualTo("explicit_dataagent_analysis_continue"));
            Assert.That(decision.AllowedTools, Is.EqualTo(ActiveAllowedTools));
            AssertDeniedTools(decision, ["dataagent_analysis_start"], "tool_not_allowed_in_current_route");
        });
    }

    [Test]
    public void RouterRoutesExplicitDataAgentSummarizeWithActiveSession()
    {
        ToolCapabilityRouter router = ToolCapabilityRouter.CreateDefault();
        ToolRouteState state = TrustedOwnerPrivateState(activeSession: true);

        ToolRouteDecision decision = router.Route("总结这个 DataAgent 分析", state);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Domain, Is.EqualTo(ToolCapabilityDomain.DataAgent));
            Assert.That(decision.AllowedTools, Is.EqualTo(ActiveAllowedTools));
            AssertDeniedTools(decision, ["dataagent_analysis_start"], "tool_not_allowed_in_current_route");
        });
    }

    [Test]
    public void RouterDoesNotTreatOrdinaryContinueAsDataAgentAnalysis()
    {
        ToolCapabilityRouter router = ToolCapabilityRouter.CreateDefault();
        ToolRouteState state = TrustedOwnerPrivateState(activeSession: true);

        ToolRouteDecision decision = router.Route("继续说", state);

        Assert.Multiple(() =>
        {
            AssertOrdinaryTrustedChatDecision(decision);
            AssertDeniedTools(decision, DataAgentToolNames, "tool_not_allowed_in_current_route");
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
            Assert.That(decision.Reason, Is.EqualTo("route_state_not_trusted"));
            AssertDeniedTools(decision, DataAgentToolNames, "route_state_not_trusted");
        });
    }

    [Test]
    public void RouterDoesNotExposeDataAgentToolsForUntrustedOrdinaryText()
    {
        ToolCapabilityRouter router = ToolCapabilityRouter.CreateDefault();
        ToolRouteState state = new("analysis-1", "Active", IsOwner: true, IsPrivateChat: true, IsTrustedRuntime: false);

        ToolRouteDecision decision = router.Route("继续说", state);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Domain, Is.EqualTo(ToolCapabilityDomain.Chat));
            Assert.That(decision.Intent, Is.EqualTo("ordinary_chat"));
            Assert.That(decision.AllowedTools, Is.Empty);
            Assert.That(decision.Reason, Is.EqualTo("route_state_not_trusted"));
            AssertDeniedTools(decision, DataAgentToolNames, "route_state_not_trusted");
        });
    }

    [TestCase(false, true)]
    [TestCase(true, false)]
    public void RouterDoesNotExposeDataAgentToolsWhenTrustedRuntimeIsNotOwnerPrivate(bool isOwner, bool isPrivateChat)
    {
        ToolCapabilityRouter router = ToolCapabilityRouter.CreateDefault();
        ToolRouteState state = new("", "", isOwner, isPrivateChat, IsTrustedRuntime: true);

        ToolRouteDecision decision = router.Route("分析一下我们离 V2 还差什么", state);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Domain, Is.EqualTo(ToolCapabilityDomain.DataAgent));
            Assert.That(decision.AllowedTools, Is.Empty);
            Assert.That(decision.Reason, Is.EqualTo("surface_not_allowed"));
            AssertDeniedTools(decision, DataAgentToolNames, "surface_not_allowed");
        });
    }

    [TestCase("继续分析这个观点")]
    [TestCase("帮我分析这段话")]
    [TestCase("分析一下这个回复怎么写")]
    public void RouterDoesNotTreatOrdinaryChineseAnalysisAsDataAgentAnalysis(string utterance)
    {
        ToolCapabilityRouter router = ToolCapabilityRouter.CreateDefault();
        ToolRouteState state = TrustedOwnerPrivateState(activeSession: true);

        ToolRouteDecision decision = router.Route(utterance, state);

        Assert.Multiple(() =>
        {
            AssertOrdinaryTrustedChatDecision(decision);
            AssertDeniedTools(decision, DataAgentToolNames, "tool_not_allowed_in_current_route");
        });
    }

    [TestCase("analyze this paragraph")]
    [TestCase("analysis of this paragraph")]
    public void RouterDoesNotTreatGenericEnglishAnalysisAsDataAgentAnalysis(string utterance)
    {
        ToolCapabilityRouter router = ToolCapabilityRouter.CreateDefault();
        ToolRouteState state = TrustedOwnerPrivateState(activeSession: true);

        ToolRouteDecision decision = router.Route(utterance, state);

        Assert.Multiple(() =>
        {
            AssertOrdinaryTrustedChatDecision(decision);
            AssertDeniedTools(decision, DataAgentToolNames, "tool_not_allowed_in_current_route");
        });
    }

    [Test]
    public void RouterRejectsNullManifestEntries()
    {
        ToolCapabilityManifest nullManifest = null!;

        Assert.That(
            () => new ToolCapabilityRouter([nullManifest]),
            Throws.InstanceOf<ArgumentException>());
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void RouterTreatsNullEmptyAndWhitespaceUtterancesAsOrdinaryChat(string? utterance)
    {
        ToolCapabilityRouter router = ToolCapabilityRouter.CreateDefault();
        ToolRouteState state = TrustedOwnerPrivateState(activeSession: true);

        ToolRouteDecision decision = router.Route(utterance!, state);

        Assert.Multiple(() =>
        {
            AssertOrdinaryTrustedChatDecision(decision);
            AssertDeniedTools(decision, DataAgentToolNames, "tool_not_allowed_in_current_route");
        });
    }

    static ToolRouteState TrustedOwnerPrivateState(bool activeSession)
    {
        return activeSession
            ? new("analysis-1", "Active", IsOwner: true, IsPrivateChat: true, IsTrustedRuntime: true)
            : new("", "", IsOwner: true, IsPrivateChat: true, IsTrustedRuntime: true);
    }

    static void AssertOrdinaryTrustedChatDecision(ToolRouteDecision decision)
    {
        Assert.That(decision.Domain, Is.EqualTo(ToolCapabilityDomain.Chat));
        Assert.That(decision.Intent, Is.EqualTo("ordinary_chat"));
        Assert.That(decision.AllowedTools, Is.Empty);
        Assert.That(decision.Reason, Is.EqualTo("ordinary_chat"));
    }

    static void AssertDeniedTools(
        ToolRouteDecision decision,
        IReadOnlyCollection<string> expectedToolNames,
        string expectedReason)
    {
        Assert.That(decision.DeniedTools.Select(tool => tool.Name), Is.EqualTo(expectedToolNames));
        Assert.That(decision.DeniedTools.Select(tool => tool.Reason), Is.All.EqualTo(expectedReason));
    }
}
