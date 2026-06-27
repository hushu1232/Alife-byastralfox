using Alife.Function.Agent;
using NUnit.Framework;

namespace Alife.Test.Framework;

[TestFixture]
public sealed class AgentBrowserTaskPlannerTests
{
    [Test]
    public void PlanInitialAction_PublicUrl_ReturnsNavigateToFirstPublicUrl()
    {
        AgentBrowserAutomationAction action = AgentBrowserTaskPlanner.PlanInitialAction(
            "browse https://example.com/docs and summarize install steps https://example.com/api");

        Assert.Multiple(() =>
        {
            Assert.That(action.Kind, Is.EqualTo(AgentBrowserAutomationActionKind.NavigatePublicUrl));
            Assert.That(action.Target, Is.EqualTo("https://example.com/docs"));
        });
    }

    [Test]
    public void PlanInitialAction_IgnoresUnsafeUrlAndNavigatesFirstPublicUrl()
    {
        AgentBrowserAutomationAction action = AgentBrowserTaskPlanner.PlanInitialAction(
            "browse http://127.0.0.1:3000 and then https://example.com/readme");

        Assert.Multiple(() =>
        {
            Assert.That(action.Kind, Is.EqualTo(AgentBrowserAutomationActionKind.NavigatePublicUrl));
            Assert.That(action.Target, Is.EqualTo("https://example.com/readme"));
        });
    }

    [Test]
    public void PlanInitialAction_PublicIpv6Url_ReturnsNavigateWithoutTrimmingClosingBracket()
    {
        AgentBrowserAutomationAction action = AgentBrowserTaskPlanner.PlanInitialAction(
            "browse https://[2606:4700:4700::1111]");

        Assert.Multiple(() =>
        {
            Assert.That(action.Kind, Is.EqualTo(AgentBrowserAutomationActionKind.NavigatePublicUrl));
            Assert.That(action.Target, Is.EqualTo("https://[2606:4700:4700::1111]"));
        });
    }

    [Test]
    public void PlanInitialAction_NoPublicUrl_ReturnsSearchWithCompactNormalizedQuery()
    {
        AgentBrowserAutomationAction action = AgentBrowserTaskPlanner.PlanInitialAction(
            "  browse\r\n\tvercel    agent browser     docs  ");

        Assert.Multiple(() =>
        {
            Assert.That(action.Kind, Is.EqualTo(AgentBrowserAutomationActionKind.SearchPublicWeb));
            Assert.That(action.Target, Is.EqualTo("browse vercel agent browser docs"));
        });
    }

    [Test]
    public void PlanInitialAction_OnlyUnsafeUrl_ReturnsSearchWithoutUnsafeUrlText()
    {
        AgentBrowserAutomationAction action = AgentBrowserTaskPlanner.PlanInitialAction(
            "browse http://127.0.0.1:3000 admin docs");

        Assert.Multiple(() =>
        {
            Assert.That(action.Kind, Is.EqualTo(AgentBrowserAutomationActionKind.SearchPublicWeb));
            Assert.That(action.Target, Is.EqualTo("browse admin docs"));
            Assert.That(action.Target, Does.Not.Contain("127.0.0.1"));
        });
    }

    [Test]
    public void SelectNextLink_PrefersUsefulPublicLinks()
    {
        AgentBrowserSnapshotLink[] links =
        [
            new("Pricing", "https://example.com/pricing"),
            new("Getting Started", "https://example.com/docs/getting-started"),
            new("Contact", "mailto:test@example.com")
        ];

        AgentBrowserAutomationAction action = AgentBrowserTaskPlanner.SelectNextLink(
            "install steps",
            links,
            visitedUrls: ["https://example.com"]);

        Assert.Multiple(() =>
        {
            Assert.That(action.Kind, Is.EqualTo(AgentBrowserAutomationActionKind.ClickPublicLink));
            Assert.That(action.Target, Is.EqualTo("https://example.com/docs/getting-started"));
        });
    }

    [Test]
    public void SelectNextLink_SkipsUnsafeAndVisitedUrls()
    {
        AgentBrowserSnapshotLink[] links =
        [
            new("Docs", "http://localhost/docs"),
            new("Readme", "https://example.com/readme"),
            new("Install", "https://example.com/install")
        ];

        AgentBrowserAutomationAction action = AgentBrowserTaskPlanner.SelectNextLink(
            "install steps",
            links,
            visitedUrls: ["https://example.com/readme"]);

        Assert.Multiple(() =>
        {
            Assert.That(action.Kind, Is.EqualTo(AgentBrowserAutomationActionKind.ClickPublicLink));
            Assert.That(action.Target, Is.EqualTo("https://example.com/install"));
        });
    }

    [Test]
    public void SelectNextLink_NoSafeUsefulUnvisitedPublicLink_ReturnsStop()
    {
        AgentBrowserAutomationAction action = AgentBrowserTaskPlanner.SelectNextLink(
            "install steps",
            [
                new AgentBrowserSnapshotLink("Email", "mailto:test@example.com"),
                new AgentBrowserSnapshotLink("Docs", "https://example.com/docs")
            ],
            visitedUrls: ["https://example.com/docs"]);

        Assert.Multiple(() =>
        {
            Assert.That(action.Kind, Is.EqualTo(AgentBrowserAutomationActionKind.Stop));
            Assert.That(action.Target, Is.Empty);
        });
    }
}
