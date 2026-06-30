using Alife.Function.DataAgent;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentToolRouteContextAccessorTests
{
    [Test]
    public void MissingAccessorReturnsFailClosedContext()
    {
        DataAgentToolRouteContext context = MissingDataAgentToolRouteContextAccessor.Instance.Get(
            "dataagent_analysis_start",
            null);

        Assert.Multiple(() =>
        {
            Assert.That(context.Present, Is.False);
            Assert.That(context.ToolName, Is.EqualTo("dataagent_analysis_start"));
            Assert.That(context.AllowsTool, Is.False);
            Assert.That(context.AllowsQuery, Is.False);
            Assert.That(context.RouteId, Is.Empty);
            Assert.That(context.Intent, Is.Empty);
            Assert.That(context.ReasonCode, Is.EqualTo("tool_route_required"));
            Assert.That(context.RouteSessionId, Is.Empty);
        });
    }

    [Test]
    public void XmlPolicyAccessorReturnsAllowedContextForCurrentRoute()
    {
        XmlFunctionExecutionPolicy policy = new();
        policy.CurrentRoute = new ToolRouteDecision(
            "route-1",
            ToolCapabilityDomain.DataAgent,
            "analysis_start",
            ["dataagent_analysis_start"],
            [],
            new ToolRouteState(string.Empty, string.Empty, true, true, true),
            "route_allowed",
            "route_allowed");
        XmlPolicyDataAgentToolRouteContextAccessor accessor = new(policy);

        DataAgentToolRouteContext context = accessor.Get("dataagent_analysis_start", null);

        Assert.Multiple(() =>
        {
            Assert.That(context.Present, Is.True);
            Assert.That(context.ToolName, Is.EqualTo("dataagent_analysis_start"));
            Assert.That(context.AllowsTool, Is.True);
            Assert.That(context.AllowsQuery, Is.True);
            Assert.That(context.RouteId, Is.EqualTo("route-1"));
            Assert.That(context.Intent, Is.EqualTo("analysis_start"));
            Assert.That(context.ReasonCode, Is.EqualTo("route_allowed"));
            Assert.That(context.RouteSessionId, Is.Empty);
        });
    }

    [Test]
    public void XmlPolicyAccessorReturnsFailClosedContextWhenRouteIsMissing()
    {
        XmlPolicyDataAgentToolRouteContextAccessor accessor = new(new XmlFunctionExecutionPolicy());

        DataAgentToolRouteContext context = accessor.Get("dataagent_analysis_continue", "session-1");

        Assert.Multiple(() =>
        {
            Assert.That(context.Present, Is.False);
            Assert.That(context.AllowsTool, Is.False);
            Assert.That(context.AllowsQuery, Is.False);
            Assert.That(context.ReasonCode, Is.EqualTo("tool_route_required"));
            Assert.That(context.RouteSessionId, Is.Empty);
        });
    }

    [Test]
    public void XmlPolicyAccessorRejectsSessionScopedMismatchForDefenseInDepth()
    {
        XmlFunctionExecutionPolicy policy = new();
        policy.CurrentRoute = new ToolRouteDecision(
            "route-2",
            ToolCapabilityDomain.DataAgent,
            "analysis_continue",
            ["dataagent_analysis_continue"],
            [],
            new ToolRouteState("session-allowed", "Active", true, true, true),
            "route_allowed",
            "route_allowed");
        XmlPolicyDataAgentToolRouteContextAccessor accessor = new(policy);

        DataAgentToolRouteContext context = accessor.Get("dataagent_analysis_continue", "session-other");

        Assert.Multiple(() =>
        {
            Assert.That(context.Present, Is.True);
            Assert.That(context.AllowsTool, Is.False);
            Assert.That(context.AllowsQuery, Is.False);
            Assert.That(context.RouteId, Is.EqualTo("route-2"));
            Assert.That(context.Intent, Is.EqualTo("analysis_continue"));
            Assert.That(context.ReasonCode, Is.EqualTo("tool_session_not_allowed_in_current_route"));
            Assert.That(context.RouteSessionId, Is.EqualTo("session-allowed"));
        });
    }
}
