using Alife.Function.DataAgent;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;
using Alife.Function.QChat;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentEndToEndChainContractTests
{
    const string SidecarAuthorityBoundary = "sidecar_authority=false";
    const string DefaultTestsLiveRuntimeBoundary = "default_tests_live_runtime=false";

    static readonly string[] AllDataAgentTools =
    [
        "dataagent_query",
        "dataagent_analysis_start",
        "dataagent_analysis_continue",
        "dataagent_analysis_summarize",
        "dataagent_analysis_end"
    ];

    [Test]
    public void ToolBrokerRoutesDataAgentToolsOnlyForTrustedOwnerPrivateSurface()
    {
        ToolCapabilityRouter router = ToolCapabilityRouter.CreateDefault();

        ToolRouteDecision start = router.Route(
            "analyze project readiness for V2",
            RouteState());
        Assert.Multiple(() =>
        {
            Assert.That(start.Domain, Is.EqualTo(ToolCapabilityDomain.DataAgent));
            Assert.That(start.AllowedTools, Is.EqualTo(new[] { "dataagent_query", "dataagent_analysis_start" }));
            Assert.That(start.Intent, Is.EqualTo("analysis_start"));
            Assert.That(start.ReasonCode, Is.EqualTo("route_allowed"));
        });

        ToolRouteDecision active = router.Route(
            "continue DataAgent analysis",
            RouteState(sessionId: "session-a", status: "Active"));
        Assert.Multiple(() =>
        {
            Assert.That(active.Domain, Is.EqualTo(ToolCapabilityDomain.DataAgent));
            Assert.That(active.AllowedTools, Is.EqualTo(new[]
            {
                "dataagent_query",
                "dataagent_analysis_continue",
                "dataagent_analysis_summarize",
                "dataagent_analysis_end"
            }));
            Assert.That(active.Intent, Is.EqualTo("analysis_continue"));
            Assert.That(active.ReasonCode, Is.EqualTo("route_allowed"));
            Assert.That(active.State.ActiveDataAgentSessionId, Is.EqualTo("session-a"));
        });

        AssertDataAgentDenied(
            router.Route("analyze project readiness for V2", RouteState(isOwner: false)),
            "owner_private_required",
            "surface_not_allowed");
        AssertDataAgentDenied(
            router.Route("analyze project readiness for V2", RouteState(isPrivateChat: false)),
            "owner_private_required",
            "surface_not_allowed");
        AssertDataAgentDenied(
            router.Route("analyze project readiness for V2", RouteState(isTrustedRuntime: false)),
            "trusted_runtime_required",
            "route_state_not_trusted");

        ToolRouteDecision ordinaryChat = router.Route("hello, can you answer normally?", RouteState());
        Assert.Multiple(() =>
        {
            Assert.That(ordinaryChat.Domain, Is.EqualTo(ToolCapabilityDomain.Chat));
            Assert.That(ordinaryChat.AllowedTools, Is.Empty);
        });
        AssertDataAgentDenied(
            ordinaryChat,
            "intent_not_matched",
            "tool_not_allowed_in_current_route");
    }

    [Test]
    public void XmlExecutionPolicyEnforcesRouteAndSessionScopeForDataAgentTools()
    {
        ToolCapabilityRouter router = ToolCapabilityRouter.CreateDefault();
        XmlFunctionExecutionPolicy policy = new();
        policy.SetGovernedToolNames(router.ToolNames);

        XmlFunctionExecutionDecision noRouteStart = policy.TryConsume(Function("dataagent_analysis_start"));
        Assert.Multiple(() =>
        {
            Assert.That(noRouteStart.IsAllowed, Is.False);
            Assert.That(noRouteStart.Reason, Is.EqualTo("tool_route_required"));
        });

        policy.CurrentRoute = router.Route("analyze project readiness for V2", RouteState());
        XmlFunctionExecutionDecision startAllowed = policy.TryConsume(Function("dataagent_analysis_start"));
        XmlFunctionExecutionDecision continueDeniedOnStart = policy.TryConsume(Function("dataagent_analysis_continue"));
        Assert.Multiple(() =>
        {
            Assert.That(startAllowed.IsAllowed, Is.True);
            Assert.That(continueDeniedOnStart.IsAllowed, Is.False);
            Assert.That(continueDeniedOnStart.Reason, Is.EqualTo("tool_not_allowed_in_current_route"));
        });

        policy.CurrentRoute = router.Route(
            "continue DataAgent analysis",
            RouteState(sessionId: "session-a", status: "Active"));
        XmlFunctionExecutionDecision missingSession = policy.TryConsume(
            Function("dataagent_analysis_continue"),
            ContextWithSession(null));
        XmlFunctionExecutionDecision wrongSession = policy.TryConsume(
            Function("dataagent_analysis_continue"),
            ContextWithSession("session-b"));
        XmlFunctionExecutionDecision matchingSession = policy.TryConsume(
            Function("dataagent_analysis_continue"),
            ContextWithSession("session-a"));

        Assert.Multiple(() =>
        {
            Assert.That(missingSession.IsAllowed, Is.False);
            Assert.That(missingSession.Reason, Is.EqualTo("tool_session_not_allowed_in_current_route"));
            Assert.That(wrongSession.IsAllowed, Is.False);
            Assert.That(wrongSession.Reason, Is.EqualTo("tool_session_not_allowed_in_current_route"));
            Assert.That(matchingSession.IsAllowed, Is.True);
        });
    }

    [Test]
    public void OfflineBoundaryMarkersLockNoLiveRuntimeAndNoSidecarAuthority()
    {
        string repoRoot = FindRepoRoot();
        string testFile = Path.Combine(
            repoRoot,
            "Tests",
            "Alife.Test.DataAgent",
            nameof(DataAgentEndToEndChainContractTests) + ".cs");
        string source = File.ReadAllText(testFile);

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain(SidecarAuthorityBoundary));
            Assert.That(source, Does.Contain(DefaultTestsLiveRuntimeBoundary));
            Assert.That(source, Does.Not.Contain("Invoke-" + "WebRequest"));
            Assert.That(source, Does.Not.Contain("Start-" + "Process"));
            Assert.That(source, Does.Not.Contain("uvi" + "corn"));
            Assert.That(source, Does.Not.Contain("127.0.0." + "1:8765"));
            Assert.That(source, Does.Not.Contain("Event" + "Source"));
        });
    }

    static ToolRouteState RouteState(
        string sessionId = "",
        string status = "",
        bool isOwner = true,
        bool isPrivateChat = true,
        bool isTrustedRuntime = true)
    {
        return new ToolRouteState(
            sessionId,
            status,
            isOwner,
            isPrivateChat,
            isTrustedRuntime);
    }

    static void AssertDataAgentDenied(
        ToolRouteDecision decision,
        string expectedReasonCode,
        string expectedDeniedToolReason)
    {
        Assert.Multiple(() =>
        {
            Assert.That(decision.AllowedTools, Is.Empty);
            Assert.That(decision.ReasonCode, Is.EqualTo(expectedReasonCode));
            Assert.That(
                decision.DeniedTools.Select(tool => tool.Name),
                Is.EqualTo(AllDataAgentTools));
            Assert.That(
                decision.DeniedTools.Select(tool => tool.Reason),
                Is.All.EqualTo(expectedDeniedToolReason));
        });
    }

    static XmlFunction Function(string name)
    {
        return new XmlFunction
        {
            Name = name,
            Mode = FunctionMode.OneShot,
            Invoker = (_, _) => Task.CompletedTask,
        };
    }

    static XmlContext ContextWithSession(string? sessionId)
    {
        Dictionary<string, string> parameters = [];
        if (string.IsNullOrWhiteSpace(sessionId) == false)
            parameters["sessionid"] = sessionId;

        return new XmlContext
        {
            CallMode = CallMode.OneShot,
            Parameters = parameters,
        };
    }

    static string FindRepoRoot()
    {
        DirectoryInfo? current = new(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Alife.slnx")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not find repository root containing Alife.slnx.");
    }
}
